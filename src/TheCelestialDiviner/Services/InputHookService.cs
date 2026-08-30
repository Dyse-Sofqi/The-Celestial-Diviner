using System.Runtime.InteropServices;
using TheCelestialDiviner.Helpers;
using TheCelestialDiviner.Models;

namespace TheCelestialDiviner.Services;

/// <summary>
/// 全局输入监听服务：WH_KEYBOARD_LL + WH_MOUSE_LL 低级钩子。
/// 钩子安装在专用线程上并通过消息泵驱动；回调中仅做轻量解析与事件分发，
/// 禁止任何耗时操作。委托引用由本类长期持有，防止被 GC 回收导致崩溃。
/// 通过 dwExtraInfo 魔数过滤自身 SendInput 注入的事件，避免自触发反馈循环。
/// </summary>
public sealed class InputHookService : IDisposable
{
    /// <summary>与 InputSimulatorService 约定的注入魔数：携带此标记的事件来自本程序。</summary>
    internal static readonly UIntPtr InjectMagic = new(0x1A2B3C4D);

    private readonly NativeMethods.LowLevelHookProc _keyboardProc;  // 保持引用防 GC
    private readonly NativeMethods.LowLevelHookProc _mouseProc;     // 保持引用防 GC

    private Thread? _hookThread;
    private volatile bool _running;
    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;
    private uint _win32ThreadId;
    private readonly ManualResetEventSlim _installResult = new(false);
    private volatile bool _installOk;

    /// <summary>输入源按下（键盘按下 / 鼠标按下 / 滚轮滚动）。</summary>
    public event Action<InputSource>? SourceDown;

    /// <summary>输入源释放（键盘释放 / 鼠标释放）。</summary>
    public event Action<InputSource>? SourceUp;

    /// <summary>钩子是否已成功安装并运行中。</summary>
    public bool IsInstalled => _running && _installOk;

    public InputHookService()
    {
        // 在构造函数中创建委托实例，确保生命周期与本服务一致（防 GC）。
        _keyboardProc = KeyboardHookCallback;
        _mouseProc = MouseHookCallback;
    }

    /// <summary>安装全局低级钩子。返回是否成功（失败通常意味着权限不足）。</summary>
    public bool Install()
    {
        if (_running) return _installOk;

        _installResult.Reset();
        _running = true;

        // 专用钩子线程：独立消息泵，避免 UI 线程繁忙导致钩子超时被系统移除。
        _hookThread = new Thread(HookThreadProc)
        {
            Name = "InputHookThread",
            IsBackground = true
        };
        _hookThread.SetApartmentState(ApartmentState.STA);
        _hookThread.Start();

        // 等待安装结果（最多 3 秒，防止异常卡死启动流程）。
        _installResult.Wait(3000);
        return _installOk;
    }

    /// <summary>钩子线程主过程：安装钩子 → 消息泵 → 退出时卸载。</summary>
    private void HookThreadProc()
    {
        // 记录 Win32 线程 ID，供 Dispose 时投递 WM_QUIT。
        _win32ThreadId = NativeMethods.GetCurrentThreadId();

        var hMod = NativeMethods.GetModuleHandle(null);
        _keyboardHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyboardProc, hMod, 0);
        _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseProc, hMod, 0);

        _installOk = _keyboardHook != IntPtr.Zero && _mouseHook != IntPtr.Zero;
        _installResult.Set();

        if (!_installOk)
        {
            Logger.Error($"低级钩子安装失败（keyboard=0x{_keyboardHook:X}, mouse=0x{_mouseHook:X}），通常需要管理员权限。");
            _running = false;
            return;
        }

        Logger.Info("低级钩子安装成功（WH_KEYBOARD_LL + WH_MOUSE_LL）。");

        // 消息泵：低级钩子回调依赖本线程持续取消息。收到 WM_QUIT 时 GetMessage 返回 0 退出。
        while (NativeMethods.GetMessage(out _, IntPtr.Zero, 0, 0) > 0)
        {
            // 无窗口消息需要分发，仅维持泵运转。
        }

        // 退出前在安装线程上卸载钩子。
        if (_keyboardHook != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(_keyboardHook);
        if (_mouseHook != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(_mouseHook);
        _keyboardHook = _mouseHook = IntPtr.Zero;
        _running = false;
        Logger.Info("低级钩子已卸载。");
    }

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

    /// <summary>鼠标低级钩子回调：仅做轻量解析与标记。</summary>
    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var info = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);

                if (info.dwExtraInfo != InjectMagic)
                {
                    var msg = wParam.ToInt32();
                    var mouseData = info.mouseData;

                    // 鼠标输入源解析（按下 / 释放 / 滚轮）。
                    InputSource? down = null, up = null;
                    switch (msg)
                    {
                        case NativeMethods.WM_LBUTTONDOWN: down = Src(MouseInput.Left); break;
                        case NativeMethods.WM_LBUTTONUP: up = Src(MouseInput.Left); break;
                        case NativeMethods.WM_RBUTTONDOWN: down = Src(MouseInput.Right); break;
                        case NativeMethods.WM_RBUTTONUP: up = Src(MouseInput.Right); break;
                        case NativeMethods.WM_MBUTTONDOWN: down = Src(MouseInput.Middle); break;
                        case NativeMethods.WM_MBUTTONUP: up = Src(MouseInput.Middle); break;
                        case NativeMethods.WM_XBUTTONDOWN:
                            down = Src(HiWord(mouseData) == NativeMethods.XBUTTON2 ? MouseInput.XButton2 : MouseInput.XButton1);
                            break;
                        case NativeMethods.WM_XBUTTONUP:
                            up = Src(HiWord(mouseData) == NativeMethods.XBUTTON2 ? MouseInput.XButton2 : MouseInput.XButton1);
                            break;
                        case NativeMethods.WM_MOUSEWHEEL:
                        {
                            // 滚轮：高字为 delta，正值向上，负值向下。滚轮为即时事件，无“释放”。
                            var delta = (short)HiWord(mouseData);
                            down = Src(delta > 0 ? MouseInput.WheelUp : MouseInput.WheelDown);
                            break;
                        }
                    }

                    if (down is not null) SourceDown?.Invoke(down);
                    if (up is not null) SourceUp?.Invoke(up);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("鼠标钩子回调异常。", ex);
            }
        }
        return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private static InputSource Src(MouseInput mouse) => new() { Kind = InputKind.Mouse, Mouse = mouse };

    /// <summary>取 32 位值的高 16 位（鼠标 wheel / XBUTTON 数据存于高字，按有符号短整型解释）。</summary>
    private static int HiWord(uint value) => unchecked((short)(value >> 16));

    /// <summary>卸载钩子并结束钩子线程。</summary>
    public void Dispose()
    {
        if (_hookThread is null) return;

        // 向钩子线程投递 WM_QUIT → 消息泵退出 → 线程自行卸载钩子后结束。
        if (_win32ThreadId != 0)
        {
            NativeMethods.PostThreadMessage(_win32ThreadId, NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }

        _hookThread.Join(2000); // 最多等待 2 秒；超时则作为后台线程随进程退出。
        _hookThread = null;
        _installResult.Dispose();
    }
}
