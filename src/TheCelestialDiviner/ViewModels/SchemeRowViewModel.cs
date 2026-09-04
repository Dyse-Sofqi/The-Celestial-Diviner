using System.ComponentModel;
using System.Windows.Input;
using TheCelestialDiviner.Helpers;
using TheCelestialDiviner.Models;

namespace TheCelestialDiviner.ViewModels;

/// <summary>
/// 方案面板中的单行视图模型：一个目标键自身的连发方案
/// （目标键 = 触发键，按所选模式连发自身）；双宏开关为两行一体。
/// 启用勾选与删除通过回调回写主视图模型（配置 → 调度器 → 落盘）。
/// </summary>
public sealed class SchemeRowViewModel : INotifyPropertyChanged
{
    private bool _enabled;
    private bool _visualEnabled = true;   // 键位可视化显示开关（默认开启）
    private string _intervalEdit;
    private int _intervalMs;

    public SchemeRowViewModel(InputSource source, string keyName, string modeName,
        int intervalMs, bool enabled, Action<bool> onEnabledChanged, Action onDelete,
        Action<int>? onIntervalChanged = null,
        bool isDual = false, string? secondKeyName = null,
        bool visualEnabled = true, Action<bool>? onVisualChanged = null)
    {
        Source = source;
        KeyName = keyName;
        ModeName = modeName;
        _intervalMs = Compat.Clamp(intervalMs, Constants.MinIntervalMs, Constants.MaxIntervalMs);
        _intervalEdit = _intervalMs.ToString();
        _enabled = enabled;
        OnEnabledChanged = onEnabledChanged;
        OnIntervalChanged = onIntervalChanged ?? (_ => { });
        DeleteCommand = RelayCommand.Create(onDelete);
        ToggleVisualCommand = RelayCommand.Create(() =>
        {
            VisualEnabled = !VisualEnabled;   // 属性 setter 负责回调 OnVisualChanged → VM 落盘
        });
        IsDual = isDual;
        SecondKeyName = secondKeyName ?? "";
        _visualEnabled = visualEnabled;
        OnVisualChanged = onVisualChanged ?? (_ => { });
    }

    /// <summary>目标输入源（与触发键相同）。</summary>
    public InputSource Source { get; }

    /// <summary>目标键显示名（如 "F6" / "侧键1"）。</summary>
    public string KeyName { get; }

    /// <summary>模式显示名（"开关" / "按压"）。</summary>
    public string ModeName { get; }

    /// <summary>连发间隔可编辑文本（毫秒；非法输入在提交时回退上次有效值，越界自动钳制到 10~100）。</summary>
    public string IntervalEdit
    {
        get => _intervalEdit;
        set
        {
            var text = value?.Trim() ?? "";
            if (int.TryParse(text, out var ms) && ms >= Constants.MinIntervalMs && ms <= Constants.MaxIntervalMs)
            {
                _intervalMs = ms;
                OnIntervalChanged(ms);
            }
            else
            {
                ms = _intervalMs;
            }
            _intervalEdit = ms.ToString();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IntervalEdit)));
        }
    }

    /// <summary>是否双宏开关（两行一体：第二行仅显示辅键名，占位对齐）。</summary>
    public bool IsDual { get; }

    /// <summary>双宏辅键显示名（仅 IsDual 为 true 时有效）。</summary>
    public string SecondKeyName { get; }

    /// <summary>方案是否启用（勾选框双向）。</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Enabled)));
            OnEnabledChanged(value);
        }
    }

    /// <summary>键位可视化显示开关（eye / eye-off 切换；仅控制悬浮层显示，不影响连发）。</summary>
    public bool VisualEnabled
    {
        get => _visualEnabled;
        set
        {
            if (_visualEnabled == value) return;
            _visualEnabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VisualEnabled)));
            OnVisualChanged(value);
        }
    }

    private Action<bool> OnEnabledChanged { get; }

    private Action<int> OnIntervalChanged { get; }

    private Action<bool> OnVisualChanged { get; }

    /// <summary>删除该方案。</summary>
    public ICommand DeleteCommand { get; }

    /// <summary>切换键位可视化显示（eye / eye-off）。</summary>
    public ICommand ToggleVisualCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;
}
