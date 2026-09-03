using System.ComponentModel;
using System.Runtime.CompilerServices;
using TheCelestialDiviner.Helpers;
using TheCelestialDiviner.Models;
using TheCelestialDiviner.Services;

namespace TheCelestialDiviner.ViewModels;

/// <summary>目标键列表行视图模型（方案设置对话框表格用）。</summary>
public sealed class TargetKeyViewModel : INotifyPropertyChanged
{
    private readonly TargetKeyConfig _config;
    private string _targetName;
    private string _modeName;
    private int _intervalMs;
    private bool _enabled;

    /// <summary>底层配置对象（确认时由此生成快照）。</summary>
    public TargetKeyConfig Config => _config;

    public TargetKeyViewModel(TargetKeyConfig config)
    {
        _config = config;
        _targetName = InputNameMapper.GetTargetName(config);
        _modeName = InputNameMapper.GetModeName(config.Mode);
        _intervalMs = config.IntervalMs;
        _enabled = config.Enabled;
    }

    /// <summary>目标键显示名。</summary>
    public string TargetName
    {
        get => _targetName;
        private set => Set(ref _targetName, value);
    }

    /// <summary>目标键类型（添加表单选择用）。</summary>
    public TargetKind Kind
    {
        get => _config.Kind;
        set
        {
            if (_config.Kind == value) return;
            _config.Kind = value;
            OnPropertyChanged();
            RefreshName();
        }
    }

    /// <summary>键盘目标键虚拟键码（录制捕获）。</summary>
    public int VirtualKey
    {
        get => _config.VirtualKey;
        set
        {
            if (_config.VirtualKey == value) return;
            _config.VirtualKey = value;
            OnPropertyChanged();
            RefreshName();
        }
    }

    /// <summary>键盘目标键扩展键标志。</summary>
    public bool Extended
    {
        get => _config.Extended;
        set
        {
            if (_config.Extended == value) return;
            _config.Extended = value;
            OnPropertyChanged();
        }
    }

    /// <summary>鼠标目标键。</summary>
    public MouseInput Mouse
    {
        get => _config.Mouse;
        set
        {
            if (_config.Mouse == value) return;
            _config.Mouse = value;
            OnPropertyChanged();
            RefreshName();
        }
    }

    /// <summary>滚轮方向（WheelUp / WheelDown）。</summary>
    public MouseInput Wheel
    {
        get => _config.Wheel;
        set
        {
            if (_config.Wheel == value) return;
            _config.Wheel = value;
            OnPropertyChanged();
            RefreshName();
        }
    }

    /// <summary>触发模式。</summary>
    public TriggerMode Mode
    {
        get => _config.Mode;
        set
        {
            if (_config.Mode == value) return;
            _config.Mode = value;
            OnPropertyChanged();
            ModeName = InputNameMapper.GetModeName(value);
        }
    }

    /// <summary>模式显示名。</summary>
    public string ModeName
    {
        get => _modeName;
        private set => Set(ref _modeName, value);
    }

    /// <summary>连发间隔（毫秒，1~100）。</summary>
    public int IntervalMs
    {
        get => _intervalMs;
        set
        {
            var clamped = Compat.Clamp(value, Constants.MinIntervalMs, Constants.MaxIntervalMs);
            if (_intervalMs == clamped) return;
            _intervalMs = clamped;
            _config.IntervalMs = clamped;
            OnPropertyChanged();
        }
    }

    /// <summary>是否启用。</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            _config.Enabled = value;
            OnPropertyChanged();
        }
    }

    /// <summary>重新读取配置刷新显示（录制后调用）。</summary>
    public void RefreshFromConfig()
    {
        IntervalMs = _config.IntervalMs;
        Enabled = _config.Enabled;
        RefreshName();
        OnPropertyChanged(nameof(Kind));
        OnPropertyChanged(nameof(Mode));
        OnPropertyChanged(nameof(Mouse));
        OnPropertyChanged(nameof(Wheel));
    }

    private void RefreshName()
    {
        TargetName = InputNameMapper.GetTargetName(_config);
    }

    /// <summary>字段赋值 + 属性通知（仅对私有后备字段包装属性使用）。</summary>
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public event PropertyChangedEventHandler? PropertyChanged;
}
