using System.ComponentModel;
using System.Windows.Input;
using TheCelestialDiviner.Helpers;

namespace TheCelestialDiviner.ViewModels;

/// <summary>
/// 多宏方案行中的单个触发键位（附属键）行视图模型：键名 + 按压时长 / 连发间隔编辑框 + 可视化开关。
/// 时序编辑交互与首键行完全一致：有效值实时提交（含输入中间态）、无效 / 空输入仅暂存不回退，
/// 失焦 / 回车由 Commit* 统一回退到上次有效值。回调携带 targetIndex 回写主视图模型（配置 → 调度器 → 落盘）。
/// </summary>
public sealed class SchemeKeyRowViewModel : INotifyPropertyChanged
{
    private string _intervalEdit;         // 连发间隔编辑文本（含清空态 / 非法中间态）
    private string _holdEdit;             // 按压时长编辑文本（含清空态 / 非法中间态）
    private int _intervalMs;
    private int _holdMs;
    private bool _visualEnabled = true;

    public SchemeKeyRowViewModel(string keyName, int intervalMs, int holdMs, bool visualEnabled,
        Action<int> onIntervalChanged, Action<int> onHoldChanged, Action<bool> onVisualChanged)
    {
        KeyName = keyName;
        _intervalMs = Compat.Clamp(intervalMs, Constants.MinIntervalMs, Constants.MaxIntervalMs);
        _intervalEdit = _intervalMs.ToString();
        _holdMs = Compat.Clamp(holdMs, Constants.MinHoldMs, Constants.MaxHoldMs);
        _holdEdit = _holdMs.ToString();
        _visualEnabled = visualEnabled;
        OnIntervalChanged = onIntervalChanged;
        OnHoldChanged = onHoldChanged;
        OnVisualChanged = onVisualChanged;
        ToggleVisualCommand = RelayCommand.Create(() =>
        {
            VisualEnabled = !VisualEnabled;   // 属性 setter 负责回调 OnVisualChanged → VM 落盘
        });
    }

    /// <summary>触发键位显示名（如 "F6" / "侧键1"）。</summary>
    public string KeyName { get; }

    /// <summary>已提交的连发间隔（毫秒；间隔框虚影水印显示此值）。</summary>
    public int IntervalMs => _intervalMs;

    /// <summary>连发间隔编辑文本（UpdateSourceTrigger=PropertyChanged 实时同步；交互语义与首键行一致）。</summary>
    public string IntervalEdit
    {
        get => _intervalEdit;
        set
        {
            var text = value?.Trim() ?? "";
            if (_intervalEdit == text) return;
            _intervalEdit = text;
            if (int.TryParse(text, out var ms) && ms >= Constants.MinIntervalMs && ms <= Constants.MaxIntervalMs)
            {
                if (ms != _intervalMs)
                {
                    _intervalMs = ms;
                    OnIntervalChanged(ms);
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IntervalMs)));
                }
            }
            // 无效 / 空：不回退不提交，保持用户键入（提交时统一处理）。
        }
    }

    /// <summary>提交间隔编辑（回车 / 失焦调用）：有效值已实时提交；无效或空 → 回退上次有效值（虚影值）。</summary>
    public void CommitIntervalEdit()
    {
        if (!(int.TryParse(_intervalEdit, out var ms) && ms >= Constants.MinIntervalMs && ms <= Constants.MaxIntervalMs))
            ms = _intervalMs;
        _intervalEdit = ms.ToString();
        if (ms != _intervalMs)
        {
            _intervalMs = ms;
            OnIntervalChanged(ms);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IntervalMs)));
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IntervalEdit)));
    }

    /// <summary>已提交的按压时长（毫秒；按压框虚影水印显示此值）。</summary>
    public int HoldMs => _holdMs;

    /// <summary>按压时长编辑文本（交互语义与首键行完全一致，范围 5~100）。</summary>
    public string HoldEdit
    {
        get => _holdEdit;
        set
        {
            var text = value?.Trim() ?? "";
            if (_holdEdit == text) return;
            _holdEdit = text;
            if (int.TryParse(text, out var ms) && ms >= Constants.MinHoldMs && ms <= Constants.MaxHoldMs)
            {
                if (ms != _holdMs)
                {
                    _holdMs = ms;
                    OnHoldChanged(ms);
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HoldMs)));
                }
            }
            // 无效 / 空：不回退不提交，保持用户键入（提交时统一处理）。
        }
    }

    /// <summary>提交按压时长编辑（回车 / 失焦调用）：有效值已实时提交；无效或空 → 回退上次有效值（虚影值）。</summary>
    public void CommitHoldEdit()
    {
        if (!(int.TryParse(_holdEdit, out var ms) && ms >= Constants.MinHoldMs && ms <= Constants.MaxHoldMs))
            ms = _holdMs;
        _holdEdit = ms.ToString();
        if (ms != _holdMs)
        {
            _holdMs = ms;
            OnHoldChanged(ms);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HoldMs)));
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HoldEdit)));
    }

    /// <summary>触发键位可视化显示开关（eye / eye-off 切换；仅控制悬浮层显示，不影响连发）。</summary>
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

    private Action<int> OnIntervalChanged { get; }

    private Action<int> OnHoldChanged { get; }

    private Action<bool> OnVisualChanged { get; }

    /// <summary>切换触发键位可视化显示（eye / eye-off）。</summary>
    public ICommand ToggleVisualCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;
}
