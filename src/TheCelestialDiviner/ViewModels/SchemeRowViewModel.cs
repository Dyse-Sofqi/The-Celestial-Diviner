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
    private bool _firstKeyFiring = true;  // 双宏首键位是否参与连发（仅 IsDual 有效）
    private string _intervalEdit;         // 连发间隔编辑文本（含清空态 / 非法中间态）
    private string _holdEdit;             // 按压时长编辑文本（含清空态 / 非法中间态）
    private int _intervalMs;
    private int _holdMs;

    public SchemeRowViewModel(InputSource source, string keyName, string modeName,
        int intervalMs, int holdMs, bool enabled, Action<bool> onEnabledChanged, Action onDelete,
        Action<int>? onIntervalChanged = null, Action<int>? onHoldChanged = null,
        bool isDual = false, string? secondKeyName = null,
        bool visualEnabled = true, Action<bool>? onVisualChanged = null,
        bool firstKeyFiring = true, Action<bool>? onFirstKeyFiringChanged = null)
    {
        Source = source;
        KeyName = keyName;
        ModeName = modeName;
        _intervalMs = Compat.Clamp(intervalMs, Constants.MinIntervalMs, Constants.MaxIntervalMs);
        _intervalEdit = _intervalMs.ToString();
        _holdMs = Compat.Clamp(holdMs, Constants.MinHoldMs, Constants.MaxHoldMs);
        _holdEdit = _holdMs.ToString();
        _enabled = enabled;
        OnEnabledChanged = onEnabledChanged;
        OnIntervalChanged = onIntervalChanged ?? (_ => { });
        OnHoldChanged = onHoldChanged ?? (_ => { });
        DeleteCommand = RelayCommand.Create(onDelete);
        ToggleVisualCommand = RelayCommand.Create(() =>
        {
            VisualEnabled = !VisualEnabled;   // 属性 setter 负责回调 OnVisualChanged → VM 落盘
        });
        IsDual = isDual;
        SecondKeyName = secondKeyName ?? "";
        _visualEnabled = visualEnabled;
        OnVisualChanged = onVisualChanged ?? (_ => { });
        _firstKeyFiring = firstKeyFiring;
        OnFirstKeyFiringChanged = onFirstKeyFiringChanged ?? (_ => { });
    }

    /// <summary>目标输入源（与触发键相同）。</summary>
    public InputSource Source { get; }

    /// <summary>目标键显示名（如 "F6" / "侧键1"）。</summary>
    public string KeyName { get; }

    /// <summary>模式显示名（"开关" / "按压"）。</summary>
    public string ModeName { get; }

    /// <summary>已提交的连发间隔（毫秒；间隔框虚影水印显示此值）。</summary>
    public int IntervalMs => _intervalMs;

    /// <summary>
    /// 连发间隔编辑文本（点击清空后可直接键入；UpdateSourceTrigger=PropertyChanged 实时同步）：
    /// 有效值实时提交（含输入中间态，如输入 "1" 暂时越界不提交、补到 "10" 时提交）；
    /// 无效 / 空输入仅暂存不回退（避免打断键入），失焦 / 回车时由 <see cref="CommitIntervalEdit"/> 统一回退。
    /// </summary>
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

    /// <summary>按压时长编辑文本（交互语义与 <see cref="IntervalEdit"/> 完全一致，范围 10~200）。</summary>
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

    /// <summary>
    /// 双宏首键位是否参与连发（第二勾选框，仅 IsDual 有效）：
    /// 勾选 = 首键与次键 1-2-1-2 交替（默认）；取消 = 首键仅作启停触发键，
    /// 按下首键启动 / 停止次键单独连发，等效于用首键位启停次键位的连发方案。
    /// </summary>
    public bool FirstKeyFiring
    {
        get => _firstKeyFiring;
        set
        {
            if (_firstKeyFiring == value) return;
            _firstKeyFiring = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FirstKeyFiring)));
            OnFirstKeyFiringChanged(value);
        }
    }

    private Action<bool> OnEnabledChanged { get; }

    private Action<int> OnIntervalChanged { get; }

    private Action<int> OnHoldChanged { get; }

    private Action<bool> OnVisualChanged { get; }

    private Action<bool> OnFirstKeyFiringChanged { get; }

    /// <summary>删除该方案。</summary>
    public ICommand DeleteCommand { get; }

    /// <summary>切换键位可视化显示（eye / eye-off）。</summary>
    public ICommand ToggleVisualCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;
}
