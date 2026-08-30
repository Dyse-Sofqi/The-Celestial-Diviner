using System.Runtime.InteropServices;
using TheCelestialDiviner.Helpers;
using TheCelestialDiviner.Models;

namespace TheCelestialDiviner.Services;

/// <summary>
/// 输入模拟执行器：通过 SendInput 发送键盘按下/抬起、鼠标按下/抬起、滚轮事件。
/// 所有注入事件均携带 <see cref="InputHookService.InjectMagic"/> 标记（dwExtraInfo），
/// 低级钩子据此过滤本程序自身注入的事件，避免目标键再次触发注册源造成反馈循环。
/// SendInput 本身为原子注入，服务线程安全。
/// </summary>
public static class InputSimulatorService
{
    /// <summary>
    /// 键盘注入模式：0 = SendInput（虚拟键码 + 扫描码，普通程序），
    /// 1 = SendInput + KEYEVENTF_SCANCODE（DirectInput / RawInput 游戏），
    /// 2 = PostMessage 窗口消息（直投目标窗口 WM_KEYDOWN/WM_KEYUP，
    /// 不进入系统输入流、无 LLKHF_INJECTED 标记，可绕过基于注入标记的过滤；
    /// 仅对读取窗口消息的游戏有效）。运行时热切换。
    /// </summary>
    public static int KeyboardMode { get; set; }

    /// <summary>兼容旧配置字段：true 等价于 KeyboardMode = 1。</summary>
    public static bool UseScanCodes
    {
        get => KeyboardMode == 1;
        set => KeyboardMode = value ? 1 : 0;
    }

    /// <summary>发送一次键盘按下事件。</summary>
    public static bool KeyDown(int vk, bool extended = false)
    {
        if (KeyboardMode == 2) return PostKey(vk, up: false, extended);

        // 兼容性策略：wVk 与 wScan 双字段同填。
        // DirectInput / RawInput 游戏读扫描码字段，普通程序读虚拟键码——双填两端都兼容。
        // 仅当开启扫描码模式时才置 KEYEVENTF_SCANCODE 标志（此时系统以 wScan 为准重建事件）。
        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            U = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = (ushort)vk,
                    wScan = MapVirtualKeyToScan(vk),
                    dwFlags = extended ? NativeMethods.KEYEVENTF_EXTENDEDKEY : NativeMethods.KEYEVENTF_KEYDOWN,
                    time = 0,
                    dwExtraInfo = InputHookService.InjectMagic
                }
            }
        };
        if (KeyboardMode == 1) input.U.ki.dwFlags |= NativeMethods.KEYEVENTF_SCANCODE;
        return Send(ref input);
    }

    /// <summary>发送一次键盘抬起事件。</summary>
    public static bool KeyUp(int vk, bool extended = false)
    {
        if (KeyboardMode == 2) return PostKey(vk, up: true, extended);

        // 同 KeyDown：wVk + wScan 双字段同填，扫描码模式下加 KEYEVENTF_SCANCODE。
        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            U = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = (ushort)vk,
                    wScan = MapVirtualKeyToScan(vk),
                    dwFlags = NativeMethods.KEYEVENTF_KEYUP |
                              (extended ? NativeMethods.KEYEVENTF_EXTENDEDKEY : 0),
                    time = 0,
                    dwExtraInfo = InputHookService.InjectMagic
                }
            }
        };
        if (KeyboardMode == 1) input.U.ki.dwFlags |= NativeMethods.KEYEVENTF_SCANCODE;
        return Send(ref input);
    }

    /// <summary>
    /// 消息模式：向当前前台窗口 PostMessage 键盘消息。
    /// 注意：连发线程调用时游戏应处于前台；游戏切后台后消息会投到别的窗口，
    /// 因此本模式仅建议“游戏前台 + 连发目标键”场景使用。
    /// </summary>
    private static bool PostKey(int vk, bool up, bool extended)
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;

        var scan = MapVirtualKeyToScan(vk);
        // lParam 布局（Win32）：0-15 扫描码 | 24 扩展键 | 30 前次状态（按下时置 1 表示重复）
        // | 31 过渡状态（抬起时置 1）。重复计数 0-15 位不使用，保持 1 即可。
        var lParam = scan & 0xFF;
        if (extended) lParam |= 0x1000000;               // bit 24：扩展键标志
        if (up) lParam |= unchecked((int)0xC0000000);    // bit 30+31：释放事件
        else lParam |= 0x0;                              // 按下：首次按下，前次状态 0

        var msg = up ? NativeMethods.WM_KEYUP : NativeMethods.WM_KEYDOWN;
        // wParam = 虚拟键码；dwExtraInfo 魔数无法随窗口消息传递，
        // 本程序低级钩子对“无 InjectMagic 的键盘事件”会误判为物理输入，
        // 但消息模式的事件不进入系统输入流，低级钩子本就看不到，故无反馈循环风险。
        var ok = NativeMethods.PostMessage(hwnd, msg, (IntPtr)vk, (IntPtr)lParam);
        if (!ok) Logger.Error($"PostMessage 投递失败（vk=0x{vk:X2}，last error = {Marshal.GetLastWin32Error()}）。");
        return ok;
    }

    /// <summary>虚拟键码 → 扫描码（扩展键先映射再加 0xE0 前缀语义由 EXTENDEDKEY 标志表达）。</summary>
    private static ushort MapVirtualKeyToScan(int vk)
    {
        // MAPVK_VK_TO_VSC：返回不区分左右修饰键的基础扫描码；
        // 扩展键（方向键 / 小键盘等）在 SendInput 侧由 EXTENDEDKEY 标志补足 0xE0 语义。
        var scan = NativeMethods.MapVirtualKey((uint)vk, NativeMethods.MAPVK_VK_TO_VSC);
        return (ushort)(scan & 0xFF);
    }

    /// <summary>发送一次鼠标按下事件（左 / 右 / 中 / 侧键）。</summary>
    public static bool MouseDown(MouseInput button)
    {
        var flag = ButtonDownFlag(button);
        if (flag is null) return false;

        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            U = new NativeMethods.InputUnion
            {
                mi = new NativeMethods.MOUSEINPUT
                {
                    dx = 0, dy = 0,
                    mouseData = flag.Value.xData,
                    dwFlags = flag.Value.flag,
                    time = 0,
                    dwExtraInfo = InputHookService.InjectMagic
                }
            }
        };
        return Send(ref input);
    }

    /// <summary>发送一次鼠标抬起事件（左 / 右 / 中 / 侧键）。</summary>
    public static bool MouseUp(MouseInput button)
    {
        var flag = ButtonUpFlag(button);
        if (flag is null) return false;

        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            U = new NativeMethods.InputUnion
            {
                mi = new NativeMethods.MOUSEINPUT
                {
                    dx = 0, dy = 0,
                    mouseData = flag.Value.xData,
                    dwFlags = flag.Value.flag,
                    time = 0,
                    dwExtraInfo = InputHookService.InjectMagic
                }
            }
        };
        return Send(ref input);
    }

    /// <summary>发送一次滚轮事件（正值向上，负值向下；一格 = 120）。</summary>
    public static bool MouseWheel(int delta)
    {
        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            U = new NativeMethods.InputUnion
            {
                mi = new NativeMethods.MOUSEINPUT
                {
                    dx = 0, dy = 0,
                    mouseData = unchecked((uint)delta),
                    dwFlags = NativeMethods.MOUSEEVENTF_WHEEL,
                    time = 0,
                    dwExtraInfo = InputHookService.InjectMagic
                }
            }
        };
        return Send(ref input);
    }

    /// <summary>发送一次完整的“按下并抬起”（点击 / 滚动一格）。</summary>
    public static bool Click(TargetKeyConfig target)
    {
        return target.Kind switch
        {
            TargetKind.Keyboard => ClickKeyWithMods(target),
            TargetKind.Mouse => ClickButtonWithMods(target),
            TargetKind.Wheel => MouseWheel(target.Wheel == MouseInput.WheelUp
                ? NativeMethods.WHEEL_DELTA : -NativeMethods.WHEEL_DELTA),
            _ => false
        };
    }

    /// <summary>键盘目标点击：先按下勾选的修饰键 → 点击目标 → 按逆序抬起修饰键。</summary>
    private static bool ClickKeyWithMods(TargetKeyConfig target)
    {
        var ok = PressModsDown(target);
        ok &= PressKey(target.VirtualKey, target.Extended);
        ReleaseModsUp(target);
        return ok;
    }

    /// <summary>鼠标目标点击：先按下勾选的修饰键 → 鼠标点击 → 按逆序抬起修饰键。</summary>
    private static bool ClickButtonWithMods(TargetKeyConfig target)
    {
        var ok = PressModsDown(target);
        ok &= ClickButton(target.Mouse);
        ReleaseModsUp(target);
        return ok;
    }

    /// <summary>按下目标键配置勾选的修饰键（固定顺序 Ctrl → Shift → Alt）。</summary>
    private static bool PressModsDown(TargetKeyConfig target)
    {
        var ok = true;
        if (target.ModCtrl) ok &= KeyDown(0x11);
        if (target.ModShift) ok &= KeyDown(0x10);
        if (target.ModAlt) ok &= KeyDown(0x12);
        return ok;
    }

    /// <summary>抬起修饰键（按按下顺序的逆序 Alt → Shift → Ctrl，保证配对正确）。</summary>
    private static void ReleaseModsUp(TargetKeyConfig target)
    {
        if (target.ModAlt) KeyUp(0x12);
        if (target.ModShift) KeyUp(0x10);
        if (target.ModCtrl) KeyUp(0x11);
    }

    /// <summary>键盘键完整按下并抬起。</summary>
    private static bool PressKey(int vk, bool extended)
    {
        var ok = KeyDown(vk, extended);
        ok &= KeyUp(vk, extended);
        return ok;
    }

    /// <summary>鼠标键完整按下并抬起。</summary>
    private static bool ClickButton(MouseInput button)
    {
        var ok = MouseDown(button);
        ok &= MouseUp(button);
        return ok;
    }

    /// <summary>调用 SendInput 注入。</summary>
    private static bool Send(ref NativeMethods.INPUT input)
    {
        var inputs = new[] { input };
        var sent = NativeMethods.SendInput(1, inputs, NativeMethods.INPUT.Size);
        if (sent != 1)
        {
            // 注入失败（如被 UIPI 拦截），记录错误但不抛异常——由任务循环继续。
            Logger.Error($"SendInput 注入失败（last error = {Marshal.GetLastWin32Error()}）。");
            return false;
        }
        return true;
    }

    /// <summary>鼠标按下标志映射。</summary>
    private static (uint flag, uint xData)? ButtonDownFlag(MouseInput b) => b switch
    {
        MouseInput.Left => (NativeMethods.MOUSEEVENTF_LEFTDOWN, 0),
        MouseInput.Right => (NativeMethods.MOUSEEVENTF_RIGHTDOWN, 0),
        MouseInput.Middle => (NativeMethods.MOUSEEVENTF_MIDDLEDOWN, 0),
        MouseInput.XButton1 => (NativeMethods.MOUSEEVENTF_XDOWN, NativeMethods.XBUTTON1),
        MouseInput.XButton2 => (NativeMethods.MOUSEEVENTF_XDOWN, NativeMethods.XBUTTON2),
        _ => null // WheelUp / WheelDown 不是“按下”语义
    };

    /// <summary>鼠标抬起标志映射。</summary>
    private static (uint flag, uint xData)? ButtonUpFlag(MouseInput b) => b switch
    {
        MouseInput.Left => (NativeMethods.MOUSEEVENTF_LEFTUP, 0),
        MouseInput.Right => (NativeMethods.MOUSEEVENTF_RIGHTUP, 0),
        MouseInput.Middle => (NativeMethods.MOUSEEVENTF_MIDDLEUP, 0),
        MouseInput.XButton1 => (NativeMethods.MOUSEEVENTF_XUP, NativeMethods.XBUTTON1),
        MouseInput.XButton2 => (NativeMethods.MOUSEEVENTF_XUP, NativeMethods.XBUTTON2),
        _ => null
    };
}
