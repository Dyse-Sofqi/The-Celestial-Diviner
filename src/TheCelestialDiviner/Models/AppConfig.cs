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

/// <summary>开关模式分区：决定开关方案的连发行为（仅 Mode = Toggle 的方案有效）。</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ToggleSection
{
    /// <summary>常规开关：正常的开关模式，单独启停互不影响。</summary>
    Normal,

    /// <summary>轮转开关：同分区互斥——开启其他轮转键时自动停止正在运行的轮转键。</summary>
    Rotate,

    /// <summary>双宏开关：一个开关键位控制两个键轮流触发（1-2-1-2…），再按一次一起停止。</summary>
    Dual
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
        => Compat.CombineHashCodes((int)Kind, VirtualKey, (int)Mouse, Extended ? 1 : 0);

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

    /// <summary>
    /// 按压时长（毫秒，10~200，默认 26）：按下到弹起的持续时间。
    /// 逐帧轮询输入的游戏每帧采样一次，按压须 ≥ 帧窗口（40fps ≈ 25ms）才能保证
    /// 按下状态必被采样到；实际注入时逐发独立抖动 ±20%。
    /// </summary>
    public int HoldMs { get; set; } = Constants.DefaultHoldMs;

    /// <summary>连发间隔（毫秒，10~100，默认 26）：弹起到下一次按下的间隔；
    /// 连发周期 = 按压时长 + 间隔，实际注入时逐发独立抖动 ±20%。</summary>
    public int IntervalMs { get; set; } = Constants.DefaultIntervalMs;

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
        HoldMs = HoldMs,
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

    /// <summary>开关模式分区（常规 / 轮转 / 双宏；仅 Mode = Toggle 的方案有意义，默认常规）。</summary>
    public ToggleSection Section { get; set; } = ToggleSection.Normal;

    /// <summary>方案是否启用（停用后该源不响应）。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>创建当前实例的深拷贝。</summary>
    public KeyScheme Clone() => new()
    {
        Enabled = Enabled,
        Section = Section,
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

/// <summary>切换方案热键配置（按下按顺序切换非空方案档位；默认未设置 = 缺省）。</summary>
public sealed class ProfileCycleConfig
{
    /// <summary>是否已设置切换方案热键（默认 false = 缺省未设置）。</summary>
    public bool HasKey { get; set; }

    /// <summary>切换方案热键虚拟键码（HasKey = true 时有效）。</summary>
    public int VirtualKey { get; set; }

    /// <summary>切换方案热键扩展键标志。</summary>
    public bool Extended { get; set; }

    /// <summary>创建当前实例的副本。</summary>
    public ProfileCycleConfig Clone() => new()
    {
        HasKey = HasKey,
        VirtualKey = VirtualKey,
        Extended = Extended
    };
}

/// <summary>应用配置根对象（持久化到 %APPDATA%\TheCelestialDiviner\config.json）。</summary>
public sealed class AppConfig
{
    /// <summary>键盘注入模式默认值：DD 驱动（物理级，无 LLKHF_INJECTED 标记）。</summary>
    public const int DefaultKeyboardMode = 3;

    /// <summary>配置文件当前版本（v2：总开关默认关闭 + 默认键 F9 + 提示语音音量；v3：开关模式分区；
    /// v4：方案三档位 ①②③ + 当前档位；v5：键位可视化开关表；v6：连发时序增加按压时长；
    /// v7：状态提醒开关；v8：状态提醒默认激活；v9：切换方案热键；v10：成为衍天高手语音按钮；
    /// v11：注释区公告内容缓存）。</summary>
    public const int CurrentVersion = 11;

    /// <summary>配置文件版本号（预留迁移能力）。</summary>
    public int Version { get; set; } = CurrentVersion;

    /// <summary>
    /// 方案档位列表（固定 4 套：①②③④，各存一套按键方案；默认选中①）。
    /// 运行期约定：<see cref="Schemes"/> 与 <see cref="Profiles"/>[ActiveProfile] 是同一字典实例
    /// （加载 / 导入 / 切换档位时对齐），所有方案编辑经 Schemes 直接落在活动档位。
    /// 序列化时 Schemes 为兼容镜像（旧版程序导入可读）。
    /// </summary>
    public List<Dictionary<string, KeyScheme>> Profiles { get; set; } = new()
    {
        new(StringComparer.Ordinal),
        new(StringComparer.Ordinal),
        new(StringComparer.Ordinal),
        new(StringComparer.Ordinal)
    };

    /// <summary>当前选中的方案档位（0~3 ↔ ①②③④，默认①；切换时实时落盘）。</summary>
    public int ActiveProfile { get; set; }

    /// <summary>键位可视化显示开关（键为输入源标识字符串；缺省视为开启可视化）。</summary>
    public Dictionary<string, bool> VisualKeys { get; set; } = new(StringComparer.Ordinal);

    /// <summary>键位可视化总开关（关闭时全部键位暂停显示；缺省 true）。</summary>
    public bool GlobalVisualEnabled { get; set; } = true;

    /// <summary>状态提醒开关（底栏按钮；开启后总开关开启时常驻应用图标键帽、连发期间隐藏；默认开启）。</summary>
    public bool StatusReminderEnabled { get; set; } = true;

    /// <summary>“成为衍天高手”按钮（底栏；激活时总开关启动的语音播报改为衍天高手启动音；默认关闭）。</summary>
    public bool DivinerVoiceEnabled { get; set; }

    /// <summary>键帽配色方案名（KeycapSchemes.All 之一；缺省 Pansy）。</summary>
    public string KeycapScheme { get; set; } = "Pansy";

    /// <summary>
    /// 注释区公告内容缓存（空 = 使用内嵌 Notice.md 默认公告）。
    /// 启动时远端（Gitee）公告与当前内容不同则覆盖并落盘，此后离线启动仍显示上次同步到的内容。
    /// </summary>
    public string NoticeContent { get; set; } = "";

    /// <summary>夜间模式：true 夜间深色 / false 白天浅色（底栏按钮切换）。</summary>
    public bool NightMode { get; set; }

    /// <summary>主题跟随系统变化：true 按 AppsUseLightTheme 判断，false 固定白天浅色。</summary>
    public bool ThemeFollowSystem { get; set; } = true;

    /// <summary>可视化悬浮窗保存位置 X（null = 默认屏幕右下角）。</summary>
    public double? VisualizerLeft { get; set; }

    /// <summary>可视化悬浮窗保存位置 Y（null = 默认屏幕右下角）。</summary>
    public double? VisualizerTop { get; set; }

    /// <summary>
    /// 可视化方案：0 全部键位 / 1 修饰键和自定义键 / 2 仅自定义键（方案内键位）。默认 0。
    /// </summary>
    public int VisualizerMode { get; set; }

    /// <summary>所有输入源方案（键为输入源标识字符串；镜像当前档位 Profiles[ActiveProfile]）。</summary>
    public Dictionary<string, KeyScheme> Schemes { get; set; } = new(StringComparer.Ordinal);

    /// <summary>全局开关键配置。</summary>
    public GlobalSwitchConfig GlobalSwitch { get; set; } = new();

    /// <summary>切换方案热键配置（按下按顺序切换非空方案档位；与全局开关键、连发方案互斥）。</summary>
    public ProfileCycleConfig ProfileCycle { get; set; } = new();

    /// <summary>全局开关提示语音音量（0~100，默认 70）。</summary>
    public double SoundVolume { get; set; } = Constants.DefaultSoundVolume;

    /// <summary>
    /// 扫描码兼容模式：键盘注入改用 KEYEVENTF_SCANCODE + 扫描码。
    /// 部分游戏（DirectInput 读扫描码）忽略虚拟键码事件时开启。
    /// </summary>
    public bool UseScanCodes { get; set; }

    /// <summary>
    /// 键盘注入模式：0 普通 SendInput / 1 扫描码 / 2 消息 / 3 DD 虚拟驱动（默认）。
    /// 旧配置无此字段时回退 <see cref="DefaultKeyboardMode"/>（默认 DD 驱动）。
    /// </summary>
    public int KeyboardMode { get; set; } = DefaultKeyboardMode;

    /// <summary>方案面板默认连发间隔（毫秒，添加连发键时录入该值），10~100。</summary>
    public int DefaultIntervalMs { get; set; } = Constants.DefaultIntervalMs;

    /// <summary>方案面板默认按压时长（毫秒，添加连发键时录入该值），10~200。</summary>
    public int DefaultHoldMs { get; set; } = Constants.DefaultHoldMs;

    /// <summary>连发时序档位：0 = 常规（26/26，40~100 帧零丢失）、1 = 极限（11/11，帧率 ≥90
    /// 上限 ~45 发/秒，低帧率丢发）。切换时把该档值应用到全部连发键并作为新方案默认。</summary>
    public int TimingPreset { get; set; }

    /// <summary>创建当前实例的深拷贝。</summary>
    public AppConfig Clone() => new()
    {
        Version = Version,
        Profiles = Profiles.Select(p => p.ToDictionary(kv => kv.Key, kv => kv.Value.Clone(), StringComparer.Ordinal)).ToList(),
        ActiveProfile = ActiveProfile,
        GlobalVisualEnabled = GlobalVisualEnabled,
        StatusReminderEnabled = StatusReminderEnabled,
        DivinerVoiceEnabled = DivinerVoiceEnabled,
        KeycapScheme = KeycapScheme,
        NoticeContent = NoticeContent,
        NightMode = NightMode,
        ThemeFollowSystem = ThemeFollowSystem,
        VisualizerLeft = VisualizerLeft,
        VisualizerTop = VisualizerTop,
        VisualizerMode = VisualizerMode,
        VisualKeys = new Dictionary<string, bool>(VisualKeys, StringComparer.Ordinal),
        Schemes = Schemes.ToDictionary(p => p.Key, p => p.Value.Clone(), StringComparer.Ordinal),
        GlobalSwitch = GlobalSwitch.Clone(),
        ProfileCycle = ProfileCycle.Clone(),
        SoundVolume = SoundVolume,
        UseScanCodes = UseScanCodes,
        KeyboardMode = KeyboardMode,
        DefaultIntervalMs = DefaultIntervalMs,
        DefaultHoldMs = DefaultHoldMs,
        TimingPreset = TimingPreset
    };
}
