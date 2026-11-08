using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TheCelestialDiviner.Helpers;
using TheCelestialDiviner.Models;
using TheCelestialDiviner.Services;
using System.Windows.Media;

namespace TheCelestialDiviner.ViewModels;

/// <summary>输入源状态（仅注册状态，无运行态）。?。</summary>
public enum SourceRegistration
{
    /// <summary>空置（未注册方案）。?。</summary>
    Empty,

    /// <summary>已注册，显示模式取第一个目标键（开关模式）。?。</summary>
    RegisteredToggle,

    /// <summary>已注册，显示模式取第一个目标键（按压模式）。?。</summary>
    RegisteredHold
}

/// <summary>键盘 / 鼠标输入源控件视图模型（键盘区与鼠标区共用）。?。</summary>
public sealed class KeySourceViewModel : INotifyPropertyChanged
{
    private readonly InputSource _source;
    private string _name;
    private string _targetsSummary = "";
    private string _multiMarkSummary = "";   // 多宏触发键位标记（"⇄ 开关键位…"，非空 = 该键是某启用多宏方案的触发键位）
    private SourceRegistration _registration = SourceRegistration.Empty;
    private bool _schemeDisabled;            // 注册的方案已停用（Enabled=false）：键位显示虚线轮廓提醒，不隐身
    private bool _globallyDimmed;
    private bool _isMasterKey;
    private bool _isCycleKey;
    private bool _isCloseKey;
    private Brush _background = s_emptyBrush;
    private Brush _foreground = s_emptyForeground;

    private static bool s_isDark;
    private static Brush s_emptyBrush = MakeBrush("#F2F2F2");
    private static Brush s_emptyForeground = MakeBrush("#666666");
    // 图块强调色（色值唯一落点 Constants.cs / SectThemes.cs：按档位方案定制外观取三个槽位色；
    // UpdateTheme 时重建，便于切换档位（含主题预设变化）/ 切换日夜时全局换色）。
    private static Brush s_toggleBrush = MakeBrush(SectThemes.Default.AccentPrimaryHex);   // 开关模式
    private static Brush s_holdBrush = MakeBrush(SectThemes.Default.AccentGoldHex);        // 按压模式
    private static Brush s_multiBrush = MakeBrush(SectThemes.Default.AccentDualHex);       // 多宏触发键位标记
    private static readonly Brush s_registeredForeground = Brushes.White;

    /// <summary>更新主题静态刷子（深色 / 浅色 + 档位外观调色板），随后需对每个实例调 RefreshTheme。</summary>
    public static void UpdateTheme(bool isDark, Models.ResolvedAppearance look)
    {
        // 强调色与主题资源同源（档位外观解析色）；每次重建刷子，切换档位后重跑即全局生效。
        s_toggleBrush = MakeBrush(look.AccentPrimaryHex);
        s_holdBrush = MakeBrush(look.AccentGoldHex);
        s_multiBrush = MakeBrush(look.AccentDualHex);

        if (s_isDark == isDark) return;
        s_isDark = isDark;
        s_emptyBrush = MakeBrush(isDark ? "#2F2F2F" : "#F2F2F2");
        s_emptyForeground = MakeBrush(isDark ? "#AAAAAA" : "#666666");
    }

    /// <summary>按当前注册状态与主题刷新配色。</summary>
    public void RefreshTheme()
    {
        // 多宏触发键位标记仅是"提示"不占用键位：该键有自有方案时注册色优先（标记只进小字），
        // 无自有方案时才用标记色铺底——与旧双宏辅键的独占配色相反。
        if (Registration == SourceRegistration.Empty && _multiMarkSummary.Length > 0)
        {
            Background = s_multiBrush;
            Foreground = s_registeredForeground;
            return;
        }

        // 停用方案：背景回空置色、键名用模式色（开关紫 / 按压金）——配合 XAML 的虚线轮廓
        //（ShowDisabledOutline）表达"这里注册过方案，当前停用"；不用半透明实心底色，
        // 那会与录入态的全图块压暗（Opacity 0.45）撞车。
        if (_schemeDisabled && Registration != SourceRegistration.Empty)
        {
            Background = s_emptyBrush;
            Foreground = Registration switch
            {
                SourceRegistration.RegisteredToggle => s_toggleBrush,
                SourceRegistration.RegisteredHold => s_holdBrush,
                _ => s_emptyForeground
            };
            return;
        }

        Background = Registration switch
        {
            SourceRegistration.RegisteredToggle => s_toggleBrush,
            SourceRegistration.RegisteredHold => s_holdBrush,
            _ => s_emptyBrush
        };
        Foreground = Registration == SourceRegistration.Empty ? s_emptyForeground : s_registeredForeground;
    }

    /// <summary>创建输入源控件视图模型（isSpacer = true 时为布局占位空白；units 为真实键宽单位；
    /// row/col/rowSpan/colSpan 为小键盘区网格位置，普通行列布局取默认值）。</summary>
    public KeySourceViewModel(InputSource source, string name, string icon = "",
        bool isSpacer = false, double units = 4,
        int row = 0, int col = 0, int rowSpan = 1, int colSpan = 1)
    {
        _source = source;
        _name = name;
        Icon = icon;
        IsSpacer = isSpacer;
        WidthUnits = units;
        Row = row;
        Column = col;
        RowSpan = rowSpan;
        ColumnSpan = colSpan;
    }

    /// <summary>是否为布局占位空白（非真实输入源，不参与交互与配置）。?。</summary>
    public bool IsSpacer { get; }

    /// <summary>真实键盘宽度单位（普通键 = 4；供 KeyboardRowPanel 按比例排列）。</summary>
    public double WidthUnits { get; }

    /// <summary>小键盘区网格行位置（0 = 功能键行；普通布局取 0）。</summary>
    public int Row { get; }

    /// <summary>小键盘区网格列位置（0..3，N0 双倍宽时跨 0-1）。</summary>
    public int Column { get; }

    /// <summary>竖向跨越行数（+ 与 ENT 跨两行）。</summary>
    public int RowSpan { get; }

    /// <summary>横向跨越列数（N0 双倍宽）。</summary>
    public int ColumnSpan { get; }

    /// <summary>输入源标识（钩子分发与配置键共用）。?。</summary>
    public InputSource Source => _source;

    /// <summary>控件显示名（。?"A" / "鼠标左键"）。?。</summary>
    public string Name
    {
        get => _name;
        private set => Set(ref _name, value);
    }

    /// <summary>控件图标（鼠标键。?🖱，键盘键为空）。?。</summary>
    public string Icon { get; }

    /// <summary>绑定列表小字：自有方案（"→ A · B"）+ 多宏触发键位标记（"⇄ 开关键位…"，有则追加）。</summary>
    public string TargetsSummary
    {
        get => _targetsSummary;
        private set => Set(ref _targetsSummary, value);
    }

    /// <summary>多宏触发键位标记（非空 = 该键是某些启用多宏方案的触发键位；仅提示不占用键位）。</summary>
    public string MultiMarkSummary
    {
        get => _multiMarkSummary;
        private set => Set(ref _multiMarkSummary, value);
    }

    /// <summary>
    /// 标记 / 清除多宏触发键位状态（markSummary = "⇄ A · B"，多个方案共用同键时开关键位名合并；
    /// null / 空串清除）。**只追加小字不覆盖**：键位有自有方案时保留其 "→ 目标…" 小字，
    /// 标记接在后面；配色在 RefreshTheme 按注册态优先级决定。
    /// 调用序约束：ApplyScheme（设置自有方案小字）之后、每轮整体刷新先 ClearMultiMarks 再标记。
    /// </summary>
    public void MarkMultiMember(string? markSummary)
    {
        MultiMarkSummary = markSummary ?? "";
        TargetsSummary = string.IsNullOrEmpty(markSummary)
            ? TargetsSummary
            : (TargetsSummary.Length > 0 ? TargetsSummary + "  " : "") + markSummary;
        RefreshTheme();
    }

    /// <summary>清除全部多宏触发键位标记（每轮 RefreshAllButtons 先清后标；不触碰自有方案小字）。</summary>
    public void ClearMultiMarks()
    {
        if (MultiMarkSummary.Length == 0) return;
        MultiMarkSummary = "";
        RefreshTheme();
    }

    /// <summary>注册状态（决定配色）。?。</summary>
    public SourceRegistration Registration
    {
        get => _registration;
        private set
        {
            if (Set(ref _registration, value))
            {
                // 状态变化时同步刷新配色（背。?+ 前景）。?
                RefreshTheme();
            }
        }
    }

    /// <summary>
    /// 是否显示"停用方案"虚线轮廓：键位注册了方案但 Enabled=false。
    /// XAML 据此显示模式色虚线轮廓（配色见 RefreshTheme 停用分支）——
    /// 停用方案不再从键盘区隐身，避免用户忘记设置过。
    /// </summary>
    public bool ShowDisabledOutline => _schemeDisabled && _registration != SourceRegistration.Empty;

    /// <summary>更新停用标记并联动配色与轮廓可见性。</summary>
    private void SetSchemeDisabled(bool value)
    {
        if (_schemeDisabled == value) return;
        _schemeDisabled = value;
        RefreshTheme();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowDisabledOutline)));
    }

    /// <summary>总开关开启时控件暗淡（提示面板非设置时机；关闭时清晰便于编辑方案）。</summary>
    public bool GloballyDimmed
    {
        get => _globallyDimmed;
        set => Set(ref _globallyDimmed, value);
    }

    /// <summary>是否为全局总开关键（键盘 / 鼠标区标黑高亮）。</summary>
    public bool IsMasterKey
    {
        get => _isMasterKey;
        set => Set(ref _isMasterKey, value);
    }

    /// <summary>是否为切换方案热键（键鼠区标灰高亮：与总开关键的黑底作灰度区分，提示该键位已被占用）。</summary>
    public bool IsCycleKey
    {
        get => _isCycleKey;
        set => Set(ref _isCycleKey, value);
    }

    /// <summary>是否为独立关闭热键（键鼠区标灰高亮：同切换方案热键，与总开关键的黑底作灰度区分）。</summary>
    public bool IsCloseKey
    {
        get => _isCloseKey;
        set => Set(ref _isCloseKey, value);
    }

    /// <summary>控件背景（跟随注册状态与主题）。?。</summary>
    public Brush Background
    {
        get => _background;
        private set => Set(ref _background, value);
    }

    /// <summary>控件文字颜色。?。</summary>
    public Brush Foreground
    {
        get => _foreground;
        private set => Set(ref _foreground, value);
    }

    /// <summary>
    /// 应用方案（编辑确认 / 删除 / 清空后调用）。
    /// 显示模式取第一个目标键的模式；空方案回到未注册状态。
    /// **停用方案（Enabled=false）不再隐身**：键位保持注册态可见性——虚线轮廓 + 模式色键名
    /// （见 <see cref="ShowDisabledOutline"/>），避免用户忘记设置过。
    /// 多宏触发键位标记（MarkMultiMember）独立于注册态、只追加小字；
    /// 配色上自有方案注册色优先，标记色仅用于无自有方案的图块。
    /// </summary>
    public void ApplyScheme(KeyScheme? scheme)
    {
        if (scheme is null || scheme.Targets.Count == 0)
        {
            Registration = SourceRegistration.Empty;
            TargetsSummary = "";
            SetSchemeDisabled(false);
            return;
        }

        Registration = scheme.Targets[0].Mode == TriggerMode.Toggle
            ? SourceRegistration.RegisteredToggle
            : SourceRegistration.RegisteredHold;

        // 小字列出目标键名（最多 6 个，超出省略）。
        var names = scheme.Targets.Select(InputNameMapper.GetTargetName).Take(6);
        var suffix = scheme.Targets.Count > 6 ? " …" : "";
        TargetsSummary = "→ " + string.Join(" · ", names) + suffix;

        // 停用方案：键位仍显示注册过（虚线轮廓 + 模式色文字），不回退到空置态。
        SetSchemeDisabled(!scheme.Enabled);
    }

    /// <summary>冻结刷子工厂（避免重复创建）。?。</summary>
    private static SolidColorBrush MakeBrush(string hex)
    {
        var Brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        Brush.Freeze();
        return Brush;
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
