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
    /// 扫描码兼容模式：true 时键盘事件以 KEYEVENTF_SCANCODE + 扫描码注入。
    /// 部分游戏（DirectInput / RawInput 读扫描码）忽略虚拟键码事件，需开启此模式。
    /// 由主界面开关控制，运行时热切换。
    /// </summary>
    public static bool UseScanCodes { get; set; }

    /// <summary>发送一次键盘按下事件。</summary>
    public static bool KeyDown(int vk, bool extended = false)
    {
        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            U = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = UseScanCodes ? (ushort)0 : (ushort)vk,
                    wScan = UseScanCodes ? MapVirtualKeyToScan(vk) : (ushort)0,
                    dwFlags = extended ? NativeMethods.KEYEVENTF_EXTENDEDKEY : NativeMethods.KEYEVENTF_KEYDOWN,
                    time = 0,
                    dwExtraInfo = InputHookService.InjectMagic
                }
            }
        };
        if (UseScanCodes) input.U.ki.dwFlags |= NativeMethods.KEYEVENTF_SCANCODE;
        return Send(ref input);
    }

    /// <summary>发送一次键盘抬起事件。</summary>
    public static bool KeyUp(int vk, bool extended = false)
    {
        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            U = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = UseScanCodes ? (ushort)0 : (ushort)vk,
                    wScan = UseScanCodes ? MapVirtualKeyToScan(vk) : (ushort)0,
                    dwFlags = NativeMethods.KEYEVENTF_KEYUP |
                              (extended ? NativeMethods.KEYEVENTF_EXTENDEDKEY : 0),
                    time = 0,
                    dwExtraInfo = InputHookService.InjectMagic
                }
            }
        };
        if (UseScanCodes) input.U.ki.dwFlags |= NativeMethods.KEYEVENTF_SCANCODE;
        return Send(ref input);
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
            TargetKind.Keyboard => PressKey(target.VirtualKey, target.Extended),
            TargetKind.Mouse => ClickButton(target.Mouse),
            TargetKind.Wheel => MouseWheel(target.Wheel == MouseInput.WheelUp
                ? NativeMethods.WHEEL_DELTA : -NativeMethods.WHEEL_DELTA),
            _ => false
        };
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
