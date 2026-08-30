using TheCelestialDiviner.Models;

namespace TheCelestialDiviner.Services;

/// <summary>虚拟键码与输入源 / 目标键的中文友好名称映射。</summary>
public static class InputNameMapper
{
    /// <summary>将键盘虚拟键码映射为中文名称（如 0x41 → "A"，0x70 → "F1"）。</summary>
    public static string GetKeyName(int vk)
    {
        // 0x00-0xFF 范围内使用预置表；范围外显示十六进制码。
        return vk is >= 0 and <= 0xFF ? KeyNames[vk] : $"VK 0x{vk:X2}";
    }

    /// <summary>将鼠标输入映射为中文名称。</summary>
    public static string GetMouseName(MouseInput m) => m switch
    {
        MouseInput.Left => "鼠标左键",
        MouseInput.Right => "鼠标右键",
        MouseInput.Middle => "鼠标中键",
        MouseInput.WheelUp => "滚轮上",
        MouseInput.WheelDown => "滚轮下",
        MouseInput.XButton1 => "侧键1",
        MouseInput.XButton2 => "侧键2",
        _ => "未知鼠标输入"
    };

    /// <summary>获取输入源的显示名称。</summary>
    public static string GetSourceName(InputSource s) => s.Kind switch
    {
        InputKind.Keyboard => GetKeyName(s.VirtualKey),
        InputKind.Mouse => GetMouseName(s.Mouse),
        _ => "未知"
    };

    /// <summary>获取目标键的显示名称。</summary>
    public static string GetTargetName(TargetKeyConfig t) => t.Kind switch
    {
        TargetKind.Keyboard => GetKeyName(t.VirtualKey),
        TargetKind.Mouse => GetMouseName(t.Mouse),
        TargetKind.Wheel => t.Wheel == MouseInput.WheelUp ? "滚轮上" : "滚轮下",
        _ => "未知"
    };

    /// <summary>获取模式显示名。</summary>
    public static string GetModeName(TriggerMode mode) => mode switch
    {
        TriggerMode.Toggle => "开关",
        TriggerMode.Hold => "按压",
        _ => "未知"
    };

    /// <summary>0x00-0xFF 虚拟键码 → 中文名称表（Windows 常用 104 键布局）。</summary>
    private static readonly string[] KeyNames = BuildKeyNames();

    private static string[] BuildKeyNames()
    {
        var names = new string[256];
        for (var i = 0; i < 256; i++) names[i] = $"VK 0x{i:X2}";

        // 字母
        for (var i = 0x41; i <= 0x5A; i++) names[i] = ((char)i).ToString();

        // 数字（主键盘区）
        for (var i = 0x30; i <= 0x39; i++) names[i] = ((char)i).ToString();

        // F1-F24
        for (var i = 0; i < 24; i++) names[0x70 + i] = $"F{i + 1}";

        // 功能键
        names[0x08] = "Backspace";
        names[0x09] = "Tab";
        names[0x0C] = "Clear";
        names[0x0D] = "Enter";
        names[0x10] = "Shift";
        names[0x11] = "Ctrl";
        names[0x12] = "Alt";
        names[0x13] = "Pause";
        names[0x14] = "Caps Lock";
        names[0x1B] = "Esc";
        names[0x20] = "空格";
        names[0x21] = "Page Up";
        names[0x22] = "Page Down";
        names[0x23] = "End";
        names[0x24] = "Home";
        names[0x25] = "←";
        names[0x26] = "↑";
        names[0x27] = "→";
        names[0x28] = "↓";
        names[0x2C] = "Print Screen";
        names[0x2D] = "Insert";
        names[0x2E] = "Delete";
        names[0x2F] = "Help";

        // 数字小键盘
        names[0x60] = "小键盘 0"; names[0x61] = "小键盘 1"; names[0x62] = "小键盘 2";
        names[0x63] = "小键盘 3"; names[0x64] = "小键盘 4"; names[0x65] = "小键盘 5";
        names[0x66] = "小键盘 6"; names[0x67] = "小键盘 7"; names[0x68] = "小键盘 8";
        names[0x69] = "小键盘 9";
        names[0x6A] = "小键盘 *"; names[0x6B] = "小键盘 +";
        names[0x6D] = "小键盘 -"; names[0x6E] = "小键盘 ."; names[0x6F] = "小键盘 /";

        // 修饰键（左右区分）
        names[0xA0] = "左 Shift"; names[0xA1] = "右 Shift";
        names[0xA2] = "左 Ctrl"; names[0xA3] = "右 Ctrl";
        names[0xA4] = "左 Alt"; names[0xA5] = "右 Alt";

        // Windows 键 / 菜单键
        names[0x5B] = "左 Win"; names[0x5C] = "右 Win"; names[0x5D] = "菜单键";

        // 标点
        names[0xBA] = ";"; names[0xBB] = "="; names[0xBC] = ",";
        names[0xBD] = "-"; names[0xBE] = "."; names[0xBF] = "/";
        names[0xC0] = "`"; names[0xDB] = "["; names[0xDC] = "\\";
        names[0xDD] = "]"; names[0xDE] = "'";

        // OEM / 多媒体
        names[0xDF] = "OEM 8";
        names[0xE2] = "\\ (OEM 102)";

        return names;
    }
}
