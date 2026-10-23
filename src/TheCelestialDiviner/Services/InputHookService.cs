using System.Runtime.InteropServices;
using TheCelestialDiviner.Helpers;
using TheCelestialDiviner.Models;

namespace TheCelestialDiviner.Services;

/// <summary>
/// 全局输入监听服务。
///
/// **键盘**：WH_KEYBOARD_LL 低级钩子（装在专用线程 + 消息泵）。键盘事件频率低，
/// 钩子带来的同步开销可忽略；且需要"能拦下事件"的语义。
///
/// **鼠标**：改用 **Raw Input**（`RegisterRawInputDevices` + `RIDEV_INPUTSINK` + 消息专用窗口）。
/// 低级鼠标钩子会让系统把**每一个**鼠标事件同步送进本进程、等回调返回后才继续投递——
/// 实测把窗口收到的移动消息从 370~530 次/秒压到约 30 次/秒，拖动时窗口明显落在光标后面。
/// Raw Input 是并行通知：不插队、不节流系统输入，鼠标连发照常工作。
/// 本程序自己注入的鼠标事件靠 `RAWMOUSE.ulExtraInformation`（= SendInput 的 dwExtraInfo 魔数）
/// 过滤，避免自触发反馈循环。
/// </summary>
public sealed class InputHookService : IDisposable
{
    /// <summary>与 InputSimulatorService 约定的注入魔数：携带此标记的事件来自本程序。</summary>
    internal static readonly UIntPtr InjectMagic = new(0x1A2B3C4D);

    private readonly NativeMethods.LowLevelHookProc _keyboardProc;  // 保持引用防 GC

    private Thread? _hookThread;
    private volatile bool _running;
    private IntPtr _keyboardHook;
    private uint _win32ThreadId;
    private readonly ManualResetEventSlim _installResult = new(false);
    private volatile bool _installOk;
    private static int _lastLatencyLogTick;   // 键盘管线延迟告警限频（Environment.TickCount）

    // ---- 鼠标：Raw Input ----
    private const string RawWindowClass = "TCDFallbackRawInput";
    private delegate IntPtr RawWndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    private Thread? _rawThread;
    private IntPtr _rawHwnd;                   // 消息专用窗口（原生 Win32，不经 WPF）
    private uint _rawThreadId;
    private static RawWndProcDelegate? _rawWndProcKeepAlive;   // Win32 持有函数指针，防 GC
    private IntPtr _rawBuffer;                 // 复用的非托管缓冲（RAWINPUT 很小）
    private const int RawBufferSize = 256;
    private volatile bool _rawRegistered;
    private int _rawInputCount;                // 收到的 WM_INPUT 条数（诊断）
    private int _rawButtonCount;               // 其中带按钮 / 滚轮事件的条数（诊断）

    /// <summary>
    /// 诊断开关：跳过鼠标检测（命令行 `--no-mouse-hook`）。鼠标类方案将不可用，仅供排查。
    /// </summary>
    public static bool SuppressMouseHook { get; set; }

    /// <summary>键盘输入源按下（键盘按下 / 鼠标按下 / 滚轮滚动）。</summary>
    public event Action<InputSource>? SourceDown;

    /// <summary>输入源释放（键盘释放 / 鼠标释放）。</summary>
    public event Action<InputSource>? SourceUp;

    /// <summary>键盘钩子是否已成功安装并运行中。</summary>
    public bool IsInstalled => _running && _installOk;

    public InputHookService()
    {
        _keyboardProc = KeyboardHookCallback;
    }

    /// <summary>安装全局低级键盘钩子并启动鼠标原始输入。返回键盘钩子是否成功（失败通常意味着权限不足）。</summary>
    public bool Install()
    {
        if (_running) return _installOk;

        _installResult.Reset();
        _running = true;

        // 专用钩子线程：独立消息泵，避免 UI 线程繁忙导致钩子超时被系统移除。
        // **Highest**：整个系统键盘输入管线要等钩子回调返回，回调本身只做几次整数比较。
        _hookThread = new Thread(HookThreadProc)
        {
            Name = "InputHookThread",
            IsBackground = true,
            Priority = ThreadPriority.Highest
        };
        _hookThread.SetApartmentState(ApartmentState.STA);
        _hookThread.Start();

        // 等待安装结果（最多 3 秒，防止异常卡死启动流程）。
        _installResult.Wait(3000);

        // 鼠标：原始输入线程（与键盘钩子互不影响；失败只影响鼠标类方案）。
        if (SuppressMouseHook)
            Logger.Warn("诊断开关已生效：跳过鼠标检测（鼠标类方案不可用）。");
        else
            StartRawInput();

        return _installOk;
    }

    /// <summary>键盘钩子线程：安装钩子 → 消息泵 → 退出时卸载。</summary>
    private void HookThreadProc()
    {
        _win32ThreadId = NativeMethods.GetCurrentThreadId();

        var hMod = NativeMethods.GetModuleHandle(null);
        _keyboardHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyboardProc, hMod, 0);
        _installOk = _keyboardHook != IntPtr.Zero;
        _installResult.Set();

        if (!_installOk)
        {
            Logger.Error($"低级键盘钩子安装失败（0x{_keyboardHook:X}），通常需要管理员权限。");
            _running = false;
            return;
        }

        Logger.Info("低级键盘钩子安装成功（WH_KEYBOARD_LL）。");

        // 消息泵：低级钩子回调依赖本线程持续取消息。收到 WM_QUIT 时 GetMessage 返回 0 退出。
        while (NativeMethods.GetMessage(out _, IntPtr.Zero, 0, 0) > 0)
        {
            // 无窗口消息需要分发，仅维持泵运转。
        }

        if (_keyboardHook != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(_keyboardHook);
        _keyboardHook = IntPtr.Zero;
        _running = false;
        Logger.Info("低级键盘钩子已卸载。");
    }

    // ---------- 鼠标：Raw Input ----------

    /// <summary>启动原始输入线程（消息专用窗口 + 自己的 Dispatcher）。</summary>
    private void StartRawInput()
    {
        _rawBuffer = Marshal.AllocHGlobal(RawBufferSize);
        _rawThread = new Thread(RawInputThreadProc)
        {
            Name = "RawInputThread",
            IsBackground = true,
            Priority = ThreadPriority.Highest   // 与钩子线程同级：鼠标事件要尽快处理完
        };
        _rawThread.SetApartmentState(ApartmentState.STA);
        _rawThread.Start();
    }

    private void RawInputThreadProc()
    {
        try
        {
            _rawThreadId = NativeMethods.GetCurrentThreadId();
            _rawWndProcKeepAlive = RawWndProc;
            var hInstance = NativeMethods.GetModuleHandle(null);
            var wc = new NativeMethods.WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_rawWndProcKeepAlive),
                hInstance = hInstance,
                lpszClassName = RawWindowClass
            };
            NativeMethods.RegisterClassEx(ref wc);   // 已注册过（err=1410）无害

            // **隐藏的普通顶层窗口**（WS_POPUP，不 Show）：实测 HWND_MESSAGE 消息专用窗口
            // 在这台机器上收不到 WM_INPUT（注册成功但一条都不到），换成普通窗口后才有投递。
            _rawHwnd = NativeMethods.CreateWindowEx(0, RawWindowClass, "TCDFallbackRawInput",
                NativeMethods.WS_POPUP, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
            if (_rawHwnd == IntPtr.Zero)
            {
                Logger.Error($"Raw Input 窗口创建失败（err={Marshal.GetLastWin32Error()}）：鼠标类方案不可用。");
                return;
            }
            Logger.Info($"Raw Input 接收窗口已创建（hwnd=0x{_rawHwnd.ToInt64():X}）。");

            var device = new NativeMethods.RAWINPUTDEVICE
            {
                usUsagePage = NativeMethods.HID_USAGE_PAGE_GENERIC,
                usUsage = NativeMethods.HID_USAGE_GENERIC_MOUSE,
                dwFlags = NativeMethods.RIDEV_INPUTSINK,
                hwndTarget = _rawHwnd
            };
            _rawRegistered = NativeMethods.RegisterRawInputDevices(
                new[] { device }, 1, Marshal.SizeOf<NativeMethods.RAWINPUTDEVICE>());
            if (_rawRegistered)
                Logger.Info("鼠标检测已启用（Raw Input，不占用系统输入路径）。");
            else
                Logger.Error($"Raw Input 注册失败（err={Marshal.GetLastWin32Error()}）：鼠标类方案不可用。");

            while (NativeMethods.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                NativeMethods.TranslateMessage(ref msg);
                NativeMethods.DispatchMessage(ref msg);
            }
            Logger.Info($"Raw Input 消息循环结束（累计 WM_INPUT {_rawInputCount} 条、按钮事件 {_rawButtonCount} 条、"
                        + $"其中自注入已过滤 {_rawSelfFiltered} 条）。");

            if (_rawHwnd != IntPtr.Zero) NativeMethods.DestroyWindow(_rawHwnd);
            _rawHwnd = IntPtr.Zero;
        }
        catch (Exception ex)
        {
            Logger.Error("Raw Input 线程异常（鼠标类方案不可用）。", ex);
        }
    }

    /// <summary>消息专用窗口的过程：只处理 WM_INPUT，其余交回系统。</summary>
    private IntPtr RawWndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg != NativeMethods.WM_INPUT) return NativeMethods.DefWindowProc(hwnd, msg, wParam, lParam);
        try
        {
            HandleRawInput(lParam);
        }
        catch (Exception ex)
        {
            Logger.Error("Raw Input 处理异常。", ex);
        }
        return IntPtr.Zero;
    }

    /// <summary>解析一条 WM_INPUT：只关心按钮 / 滚轮，纯移动直接丢弃（不占处理时间）。</summary>
    private void HandleRawInput(IntPtr rawInputHandle)
    {
        if (_rawBuffer == IntPtr.Zero) return;

        var size = (uint)RawBufferSize;
        var headerSize = Marshal.SizeOf<NativeMethods.RAWINPUTHEADER>();
        var got = NativeMethods.GetRawInputData(rawInputHandle, NativeMethods.RID_INPUT, _rawBuffer,
            ref size, headerSize);
        if (got == 0 || got == uint.MaxValue)
        {
            if (_rawInputCount == 0)
                Logger.Warn($"GetRawInputData 失败（got={got}, err={Marshal.GetLastWin32Error()}）。");
            return;
        }

        _rawInputCount++;
        var header = Marshal.PtrToStructure<NativeMethods.RAWINPUTHEADER>(_rawBuffer);
        if (_rawInputCount == 1)
            Logger.Info($"Raw Input 首条 WM_INPUT 已到达（dwType={header.dwType}, size={header.dwSize}）。");
        if (header.dwType != NativeMethods.RIM_TYPEMOUSE) return;

        var mouse = Marshal.PtrToStructure<NativeMethods.RAWMOUSE>(_rawBuffer + headerSize);
        var flags = mouse.usButtonFlags;
        if (flags == 0) return;   // 纯移动：本程序不关心

        _rawButtonCount++;
        if (_rawButtonCount == 1)
            Logger.Info("Raw Input 首个按钮事件已到达（鼠标检测链路正常）。");

        // 自己注入的事件（SendInput 的 dwExtraInfo 会带到这里）：丢弃，避免自触发。
        // **只靠这个魔数判断**：曾经加过"40ms 时间窗"兜底，结果连发时（每 26ms 注入一次）
        // 时间窗被不断刷新，用户真实的松手永远落在窗内被吞掉 —— 按压方案一旦触发就停不下来。
        if (mouse.ulExtraInformation == InjectMagic.ToUInt32())
        {
            if (++_rawSelfFiltered == 1)
                Logger.Info("Raw Input 自注入过滤已生效（ulExtraInformation 魔数可用）。");
            return;
        }

        void Fire(MouseInput m, bool down)
        {
            if (down) SourceDown?.Invoke(Src(m));
            else SourceUp?.Invoke(Src(m));
        }

        if ((flags & NativeMethods.RI_MOUSE_LEFT_BUTTON_DOWN) != 0) Fire(MouseInput.Left, true);
        if ((flags & NativeMethods.RI_MOUSE_LEFT_BUTTON_UP) != 0) Fire(MouseInput.Left, false);
        if ((flags & NativeMethods.RI_MOUSE_RIGHT_BUTTON_DOWN) != 0) Fire(MouseInput.Right, true);
        if ((flags & NativeMethods.RI_MOUSE_RIGHT_BUTTON_UP) != 0) Fire(MouseInput.Right, false);
        if ((flags & NativeMethods.RI_MOUSE_MIDDLE_BUTTON_DOWN) != 0) Fire(MouseInput.Middle, true);
        if ((flags & NativeMethods.RI_MOUSE_MIDDLE_BUTTON_UP) != 0) Fire(MouseInput.Middle, false);
        if ((flags & NativeMethods.RI_MOUSE_BUTTON_4_DOWN) != 0) Fire(MouseInput.XButton1, true);
        if ((flags & NativeMethods.RI_MOUSE_BUTTON_4_UP) != 0) Fire(MouseInput.XButton1, false);
        if ((flags & NativeMethods.RI_MOUSE_BUTTON_5_DOWN) != 0) Fire(MouseInput.XButton2, true);
        if ((flags & NativeMethods.RI_MOUSE_BUTTON_5_UP) != 0) Fire(MouseInput.XButton2, false);

        // 滚轮：usButtonData 为带符号的 120 倍数；滚轮是即时事件，没有"释放"。
        if ((flags & NativeMethods.RI_MOUSE_WHEEL) != 0)
        {
            var delta = unchecked((short)mouse.usButtonData);
            if (delta != 0)
            {
                SourceDown?.Invoke(Src(delta > 0 ? MouseInput.WheelUp : MouseInput.WheelDown));
            }
        }
    }

    private static InputSource Src(MouseInput mouse) => new() { Kind = InputKind.Mouse, Mouse = mouse };

    // 自注入事件计数（诊断）：只靠 ulExtraInformation 魔数过滤，见 HandleRawInput。
    private int _rawSelfFiltered;

    // ---------- 键盘：低级钩子 ----------

    /// <summary>键盘低级钩子回调：仅做轻量解析与标记。</summary>
    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var info = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);

                // 忽略本程序注入的事件（避免目标键又触发注册源的反馈循环）。
                if (info.dwExtraInfo != InjectMagic)
                {
                    var msg = wParam.ToInt32();
                    var isDown = msg is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN;
                    var isUp = msg is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP;

                    if (isDown || isUp)
                    {
                        // 诊断埋点：事件生成（info.time）→ 本回调送达的管线延迟。
                        // >50ms 说明系统输入管线被挤压（钩子链 / DPC / RIT 饥饿），
                        // 与本程序内部处理无关——限频记录供定位。
                        var latency = unchecked(Environment.TickCount - (int)info.time);
                        if (latency > 50 && unchecked(Environment.TickCount - _lastLatencyLogTick) > 2000)
                        {
                            _lastLatencyLogTick = Environment.TickCount;
                            Logger.Warn($"键盘输入管线延迟 {latency}ms（vk=0x{info.vkCode:X2}, down={isDown}）。");
                        }

                        // DD 键盘模式：注入回环与物理输入在系统层不可区分
                        // （无 INJECTED 标记、设备归因相同），按注入时间线消除回环，
                        // 防止任务目标键再触发注册源造成 Toggle 被回环关断 / Hold 被打断。
                        if (InputSimulatorService.KeyboardMode == 3 &&
                            EchoGuard.TryConsume((int)info.vkCode, isDown, unchecked((int)info.time)))
                        {
                            return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
                        }

                        var source = new InputSource
                        {
                            Kind = InputKind.Keyboard,
                            VirtualKey = (int)info.vkCode,
                            Extended = (info.flags & NativeMethods.LLKHF_EXTENDED) != 0
                        };
                        if (isDown) SourceDown?.Invoke(source);
                        else SourceUp?.Invoke(source);
                    }
                }
            }
            catch (Exception ex)
            {
                // 回调内绝不允许异常逸出（会导致钩子被移除 / 进程崩溃）。
                Logger.Error("键盘钩子回调异常。", ex);
            }
        }
        return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    // ---------- 释放 ----------

    /// <summary>卸载键盘钩子、停止原始输入并结束相关线程。</summary>
    public void Dispose()
    {
        // 键盘：投递 WM_QUIT → 消息泵退出 → 线程自行卸载钩子。
        if (_hookThread is not null)
        {
            if (_win32ThreadId != 0)
                NativeMethods.PostThreadMessage(_win32ThreadId, NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            _hookThread.Join(2000);
            _hookThread = null;
            _installResult.Dispose();
        }

        // 鼠标：反注册原始输入并结束该线程的消息循环。
        try
        {
            if (_rawRegistered)
            {
                var off = new NativeMethods.RAWINPUTDEVICE
                {
                    usUsagePage = NativeMethods.HID_USAGE_PAGE_GENERIC,
                    usUsage = NativeMethods.HID_USAGE_GENERIC_MOUSE,
                    dwFlags = 0x00000001,   // RIDEV_REMOVE
                    hwndTarget = IntPtr.Zero
                };
                NativeMethods.RegisterRawInputDevices(new[] { off }, 1,
                    Marshal.SizeOf<NativeMethods.RAWINPUTDEVICE>());
                _rawRegistered = false;
            }
            if (_rawThreadId != 0)
                NativeMethods.PostThreadMessage(_rawThreadId, NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Logger.Warn($"停止 Raw Input 时出错（忽略）：{ex.Message}");
        }
        _rawThread?.Join(1000);
        _rawThread = null;
        if (_rawBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_rawBuffer);
            _rawBuffer = IntPtr.Zero;
        }
    }
}
