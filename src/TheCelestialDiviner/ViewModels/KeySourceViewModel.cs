using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
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
    private SourceRegistration _registration = SourceRegistration.Empty;
    private bool _globallyDimmed;
    private Brush _background = s_emptyBrush;
    private Brush _foreground = s_emptyForeground;

    private static bool s_isDark;
    private static Brush s_emptyBrush = MakeBrush("#F2F2F2");
    private static Brush s_emptyForeground = MakeBrush("#666666");
    private static readonly Brush s_toggleBrush = MakeBrush("#1E88E5");
    private static readonly Brush s_holdBrush = MakeBrush("#FB8C00");
    private static readonly Brush s_registeredForeground = Brushes.White;

    /// <summary>更新主题静态刷子（深色 / 浅色），随后需对每个实例调。?RefreshTheme。?。</summary>
    public static void UpdateTheme(bool isDark)
    {
        if (s_isDark == isDark) return;
        s_isDark = isDark;
        s_emptyBrush = MakeBrush(isDark ? "#2F2F2F" : "#F2F2F2");
        s_emptyForeground = MakeBrush(isDark ? "#AAAAAA" : "#666666");
    }

    /// <summary>按当前注册状态与主题刷新配色。?。</summary>
    public void RefreshTheme()
    {
        Background = Registration switch
        {
            SourceRegistration.RegisteredToggle => s_toggleBrush,
            SourceRegistration.RegisteredHold => s_holdBrush,
            _ => s_emptyBrush
        };
        Foreground = Registration == SourceRegistration.Empty ? s_emptyForeground : s_registeredForeground;
    }

    /// <summary>创建输入源控件视图模型（isSpacer = true 时为布局占位空白；units 为真实键宽单位）。</summary>
    public KeySourceViewModel(InputSource source, string name, string icon = "",
        bool isSpacer = false, double units = 4)
    {
        _source = source;
        _name = name;
        Icon = icon;
        IsSpacer = isSpacer;
        WidthUnits = units;
    }

    /// <summary>是否为布局占位空白（非真实输入源，不参与交互与配置）。?。</summary>
    public bool IsSpacer { get; }

    /// <summary>真实键盘宽度单位（普通键 = 4；供 KeyboardRowPanel 按比例排列）。</summary>
    public double WidthUnits { get; }

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

    /// <summary>绑定的目标键列表（小字显示，。?"。?A · B"）。?。</summary>
    public string TargetsSummary
    {
        get => _targetsSummary;
        private set => Set(ref _targetsSummary, value);
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

    /// <summary>总开关开启时控件暗淡（提示面板非设置时机；关闭时清晰便于编辑方案）。</summary>
    public bool GloballyDimmed
    {
        get => _globallyDimmed;
        set => Set(ref _globallyDimmed, value);
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
    /// 应用方案（编辑确。?/ 删除 / 清空后调用）。?
    /// 显示模式取第一个目标键的模式；空方案回到未注册状态。?
    /// <。</summary>
    public void ApplyScheme(KeyScheme? scheme)
    {
        if (scheme is null || !scheme.Enabled || scheme.Targets.Count == 0)
        {
            Registration = SourceRegistration.Empty;
            TargetsSummary = "";
            return;
        }

        Registration = scheme.Targets[0].Mode == TriggerMode.Toggle
            ? SourceRegistration.RegisteredToggle
            : SourceRegistration.RegisteredHold;

        // 小字列出目标键名（最多 6 个，超出省略）。
        var names = scheme.Targets.Select(InputNameMapper.GetTargetName).Take(6);
        var suffix = scheme.Targets.Count > 6 ? " …" : "";
        TargetsSummary = "→ " + string.Join(" · ", names) + suffix;
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
