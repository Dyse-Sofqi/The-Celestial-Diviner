using System.Text.Json.Serialization;
using TheCelestialDiviner.Helpers;

namespace TheCelestialDiviner.Models;

/// <summary>输入源类型：键盘按键或鼠标输入。</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum InputKind
{
    /// <summary>键盘虚拟键码。</summary>
    Keyboard,

    /// <summary>鼠标按键 / 滚轮 / 侧键。</summary>
    Mouse
}

/// <summary>鼠标输入的具体标识（含滚轮方向与侧键）。</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MouseInput
{
    /// <summary>鼠标左键。</summary>
    Left,

    /// <summary>鼠标右键。</summary>
    Right,

    /// <summary>鼠标中键。</summary>
    Middle,

    /// <summary>滚轮向上。</summary>
    WheelUp,

    /// <summary>滚轮向下。</summary>
    WheelDown,

    /// <summary>侧键 1（XBUTTON1，通常为“后退”）。</summary>
    XButton1,

    /// <summary>侧键 2（XBUTTON2，通常为“前进”）。</summary>
    XButton2
}

/// <summary>目标键类型。</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TargetKind
{
    /// <summary>键盘按键。</summary>
    Keyboard,

    /// <summary>鼠标按键。</summary>
    Mouse,

    /// <summary>滚轮（上 / 下）。</summary>
    Wheel
}

/// <summary>目标键触发模式。</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TriggerMode
{
    /// <summary>开关模式：按一次注册源启动连发，再按一次停止。</summary>
    Toggle,

    /// <summary>按压模式：按住注册源连发，松开即停。</summary>
    Hold
}

/// <summary>
/// 输入源标识：键盘虚拟键码或鼠标输入类型。
/// 作为字典键 / 比较依据时使用 <see cref="Equals(object)"/> 的值语义。
/// </summary>
public sealed class InputSource : IEquatable<InputSource>
{
    /// <summary>输入类型（键盘 / 鼠标）。</summary>
    public InputKind Kind { get; set; } = InputKind.Keyboard;

    /// <summary>键盘虚拟键码（Kind = Keyboard 时有效，如 0x41 = 'A'）。</summary>
    public int VirtualKey { get; set; }

    /// <summary>鼠标输入标识（Kind = Mouse 时有效）。</summary>
    public MouseInput Mouse { get; set; } = MouseInput.Left;

    /// <summary>键盘扩展键标志（如方向键、Numpad 区分主键盘区）。</summary>
    public bool Extended { get; set; }

    public bool Equals(InputSource? other) =>
        other is not null && Kind == other.Kind && VirtualKey == other.VirtualKey &&
        Mouse == other.Mouse && Extended == other.Extended;

    public override bool Equals(object? obj) => Equals(obj as InputSource);

    public override int GetHashCode()
        => HashCode.Combine((int)Kind, VirtualKey, (int)Mouse, Extended);

    /// <summary>创建当前实例的副本。</summary>
    public InputSource Clone() => new() { Kind = Kind, VirtualKey = VirtualKey, Mouse = Mouse, Extended = Extended };
}

/// <summary>单个目标键配置：连发什么、怎么连发。</summary>
public sealed class TargetKeyConfig
{
    /// <summary>配置项唯一 ID（用于列表操作定位）。</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>目标键类型（键盘 / 鼠标 / 滚轮）。</summary>
    public TargetKind Kind { get; set; } = TargetKind.Keyboard;

    /// <summary>键盘目标键虚拟键码（Kind = Keyboard 时有效）。</summary>
    public int VirtualKey { get; set; }

    /// <summary>键盘目标键扩展键标志。</summary>
    public bool Extended { get; set; }

    /// <summary>鼠标目标键（Kind = Mouse 时有效：左/右/中/侧键1/侧键2）。</summary>
    public MouseInput Mouse { get; set; } = MouseInput.Left;

    /// <summary>滚轮方向（Kind = Wheel 时有效：WheelUp / WheelDown）。</summary>
    public MouseInput Wheel { get; set; } = MouseInput.WheelUp;

    /// <summary>触发模式（Toggle 开关 / Hold 按压）。</summary>
    public TriggerMode Mode { get; set; } = TriggerMode.Toggle;

    /// <summary>连发间隔（毫秒，1~100，默认 5）。</summary>
    public int IntervalMs { get; set; } = 5;

    /// <summary>实验功能：连发时附带 Ctrl（注入修饰按下 → 目标 → 修饰抬起）。</summary>
    public bool ModCtrl { get; set; }

    /// <summary>实验功能：连发时附带 Shift。</summary>
    public bool ModShift { get; set; }

    /// <summary>实验功能：连发时附带 Alt。</summary>
    public bool ModAlt { get; set; }

    /// <summary>该目标键是否启用（停用后不参与触发）。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>创建当前实例的副本。</summary>
    public TargetKeyConfig Clone() => new()
    {
        Id = Id,
        Kind = Kind,
        VirtualKey = VirtualKey,
        Extended = Extended,
        Mouse = Mouse,
        Wheel = Wheel,
        Mode = Mode,
        IntervalMs = IntervalMs,
        ModCtrl = ModCtrl,
        ModShift = ModShift,
        ModAlt = ModAlt,
        Enabled = Enabled
    };
}

/// <summary>一个输入源的连发方案：源 + 若干目标键（一套方案，编辑即覆盖）。</summary>
public sealed class KeyScheme
{
    /// <summary>绑定的目标键列表（按添加顺序；首个目标键的模式决定该源的显示模式）。</summary>
    public List<TargetKeyConfig> Targets { get; set; } = new();

    /// <summary>方案是否启用（停用后该源不响应）。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>创建当前实例的深拷贝。</summary>
    public KeyScheme Clone() => new()
    {
        Enabled = Enabled,
        Targets = Targets.Select(t => t.Clone()).ToList()
    };
}

/// <summary>全局开关键配置。</summary>
public sealed class GlobalSwitchConfig
{
    /// <summary>是否已设置全局开关键（默认 F9，可在“设置全局开关”中自定义 / 清除）。</summary>
    public bool HasKey { get; set; } = true;

    /// <summary>全局开关键虚拟键码（HasKey = true 时有效，默认 F9 = 0x78）。</summary>
    public int VirtualKey { get; set; } = Constants.DefaultMasterKeyVk;

    /// <summary>全局开关键扩展键标志。</summary>
    public bool Extended { get; set; }

    /// <summary>总开关当前是否启用（默认关闭；开启后方案才响应输入，停用即停所有连发）。</summary>
    public bool Enabled { get; set; }

    /// <summary>创建当前实例的副本。</summary>
    public GlobalSwitchConfig Clone() => new()
    {
        HasKey = HasKey,
        VirtualKey = VirtualKey,
        Extended = Extended,
        Enabled = Enabled
    };
}

/// <summary>应用配置根对象（持久化到 %APPDATA%\TheCelestialDiviner\config.json）。</summary>
public sealed class AppConfig
{
    /// <summary>配置文件当前版本（v2：总开关默认关闭 + 默认键 F9 + 提示语音音量）。</summary>
    public const int CurrentVersion = 2;

    /// <summary>配置文件版本号（预留迁移能力）。</summary>
    public int Version { get; set; } = CurrentVersion;

    /// <summary>所有输入源方案（键为输入源标识字符串）。</summary>
    public Dictionary<string, KeyScheme> Schemes { get; set; } = new();

    /// <summary>全局开关键配置。</summary>
    public GlobalSwitchConfig GlobalSwitch { get; set; } = new();

    /// <summary>全局开关提示语音音量（0~100，默认 70）。</summary>
    public double SoundVolume { get; set; } = Constants.DefaultSoundVolume;

    /// <summary>
    /// 扫描码兼容模式：键盘注入改用 KEYEVENTF_SCANCODE + 扫描码。
    /// 部分游戏（DirectInput 读扫描码）忽略虚拟键码事件时开启。
    /// </summary>
    public bool UseScanCodes { get; set; }

    /// <summary>
    /// 键盘注入模式：0 普通 SendInput / 1 扫描码 / 2 消息 / 3 DD 虚拟驱动。
    /// 旧配置无此字段时回退 <see cref="UseScanCodes"/> 语义（true→1，false→0）。
    /// </summary>
    public int KeyboardMode { get; set; }

    /// <summary>创建当前实例的深拷贝。</summary>
    public AppConfig Clone() => new()
    {
        Version = Version,
        Schemes = Schemes.ToDictionary(p => p.Key, p => p.Value.Clone(), StringComparer.Ordinal),
        GlobalSwitch = GlobalSwitch.Clone(),
        SoundVolume = SoundVolume,
        UseScanCodes = UseScanCodes,
        KeyboardMode = KeyboardMode
    };
}
