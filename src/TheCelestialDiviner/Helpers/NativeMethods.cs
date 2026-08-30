using System.Runtime.InteropServices;

namespace TheCelestialDiviner.Helpers;

#pragma warning disable CA1707 // 原生 API 命名保持 Win32 习惯

/// <summary>
/// Win32 API P/Invoke 声明与相关结构体 / 常量。
/// 仅包含本应用所需的：低级钩子、SendInput、定时器分辨率、托盘图标辅助。
/// </summary>
public static class NativeMethods
{
    // ---------- 定时器分辨率（winmm.dll） ----------

    /// <summary>将系统定时器分辨率提升到 1ms，保障 5ms 级连发间隔的稳定性。</summary>
    [DllImport("winmm.dll", SetLastError = true)]
    public static extern uint timeBeginPeriod(uint msPeriod);

    /// <summary>恢复系统定时器分辨率（退出时调用）。</summary>
    [DllImport("winmm.dll", SetLastError = true)]
    public static extern uint timeEndPeriod(uint msPeriod);

    // ---------- 低级钩子（user32.dll） ----------

    /// <summary>WH_KEYBOARD_LL 低级键盘钩子 id。</summary>
    public const int WH_KEYBOARD_LL = 13;

    /// <summary>WH_MOUSE_LL 低级鼠标钩子 id。</summary>
    public const int WH_MOUSE_LL = 14;

    /// <summary>按键按下（low bit 置位表示释放后的过渡状态）。</summary>
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_SYSKEYDOWN = 0x0104;

    /// <summary>按键释放。</summary>
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYUP = 0x0105;

    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_RBUTTONDOWN = 0x0204;
    public const int WM_RBUTTONUP = 0x0205;
    public const int WM_MBUTTONDOWN = 0x0207;
    public const int WM_MBUTTONUP = 0x0208;
    public const int WM_MOUSEWHEEL = 0x020A;
    public const int WM_XBUTTONDOWN = 0x020B;
    public const int WM_XBUTTONUP = 0x020C;

    /// <summary>钩子回调返回 1 表示拦截该事件，不继续传递。</summary>
    public const int HC_ACTION = 0;

    /// <summary>LLKH 鼠标/键盘结构 flags：扩展键标志。</summary>
    public const uint LLKHF_EXTENDED = 0x01;

    /// <summary>退出消息泵的标准消息。</summary>
    public const uint WM_QUIT = 0x0012;

    /// <summary>安装钩子。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelHookProc lpfn, IntPtr hMod, uint dwThreadId);

    /// <summary>卸载钩子。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    /// <summary>调用钩子链中的下一个钩子（低级钩子固定传 0, 0）。</summary>
    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    /// <summary>获取指定模块句柄（传入本 exe 的 HMODULE）。</summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    /// <summary>低级钩子回调委托。引用必须长期持有，否则被 GC 回收会导致崩溃。</summary>
    public delegate IntPtr LowLevelHookProc(int nCode, IntPtr wParam, IntPtr lParam);

    // ---------- 键盘事件结构 ----------

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    // ---------- 鼠标事件结构 ----------

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    // ---------- SendInput（user32.dll） ----------

    public const uint INPUT_MOUSE = 0;
    public const uint INPUT_KEYBOARD = 1;

    public const uint KEYEVENTF_KEYDOWN = 0x0000; // 无标志 = 按下
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    public const uint KEYEVENTF_SCANCODE = 0x0008; // 以扫描码而非虚拟键码注入（DirectInput 游戏兼容）

    public const uint MAPVK_VK_TO_VSC = 0; // MapVirtualKey：虚拟键码 → 扫描码

    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP = 0x0004;
    public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    public const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    public const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    public const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    public const uint MOUSEEVENTF_WHEEL = 0x0800;
    public const uint MOUSEEVENTF_XDOWN = 0x0080;
    public const uint MOUSEEVENTF_XUP = 0x0100;

    public const uint XBUTTON1 = 0x0001;
    public const uint XBUTTON2 = 0x0002;

    public const int WHEEL_DELTA = 120;

    /// <summary>注入一组输入事件。返回实际成功注入的事件数（0 表示全部失败）。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    /// <summary>虚拟键码 → 扫描码转换（MAPVK_VK_TO_VSC）。</summary>
    [DllImport("user32.dll")]
    public static extern uint MapVirtualKey(uint uCode, uint uMapType);

    /// <summary>向指定窗口的消息队列投递消息（不等待处理完成）。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    /// <summary>获取当前前台窗口句柄。</summary>
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    /// <summary>获取窗口所在进程 ID。</summary>
    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public InputUnion U;
        public static int Size => Marshal.SizeOf<INPUT>();
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    // ---------- 消息泵（钩子线程专用） ----------

    /// <summary>从调用线程的消息队列取消息（返回 0 表示收到 WM_QUIT）。</summary>
    [DllImport("user32.dll")]
    public static extern int GetMessage(out NativeMessage lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    /// <summary>取调用线程的 Win32 线程 ID。</summary>
    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeMessage
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    // ---------- 通用工具 ----------

    /// <summary>向指定线程 / 全部线程投递消息，用于等待钩子线程处理（备用）。</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>WM_APP 自定义消息基址（内部线程通信用）。</summary>
    public const uint WM_APP_STOP_ALL = 0x8000 + 0x0001;
}
#pragma warning restore CA1707
