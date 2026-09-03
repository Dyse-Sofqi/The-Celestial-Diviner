using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using TheCelestialDiviner.Helpers;
using TheCelestialDiviner.Models;
using TheCelestialDiviner.Services;

namespace TheCelestialDiviner.ViewModels;

/// <summary>
/// 主视图模型（核心部分）：服务引用、状态属性、命令、钩子事件接入、日志。
/// 输入源集合与方案编辑操作见 <see href="MainViewModel.Collections.cs"/>。
/// 所有公开方法假定在 UI 线程调用（钩子事件已封送）。
/// </summary>
public sealed partial class MainViewModel : INotifyPropertyChanged
{
    // ---------- 服务引用（生命周期与 VM 相同） ----------
    private readonly ConfigService _configService;
    private readonly InputHookService _hookService;
    private readonly TaskSchedulerService _scheduler;
    private readonly TimerResolutionService _timerResolution;

    // ---------- 状态字段 ----------
    private AppConfig _config = new();
    private bool _globallyEnabled; // 总开关默认关闭（v2 语义，加载后随配置覆盖）
    private bool _logsExpanded;
    private bool _hookInstalled;
    private bool _isAdmin = true;
    private bool _timerHighRes;
    private string _hookStatusText = "钩子：未安装";
    private string _permissionText = "权限：检测中";
    private string _timerStatusText = "定时器：默认分辨率";
    private string _masterStateText = "全局开关：已关闭";
    private string _masterKeyText = "F9";
    private string _hintText = "左键点击按键设置方案，右键更多操作";
    private double _soundVolume = Constants.DefaultSoundVolume;
    private int _keyboardMode; // 键盘注入模式：0 普通 / 1 扫描码 / 2 消息

    /// <summary>全局总开关提示语音服务（开启 = “启动”，关闭 = “关闭”）。</summary>
    private readonly SoundCueService _soundCue = new();

    /// <summary>键盘注入模式变更通知（导入配置后由 VM 触发，UI 回填下拉框）。</summary>
    public event Action<int>? KeyboardModeChanged;

    /// <summary>创建主视图模型：加载配置、构建输入源集合、应用调度器。</summary>
    public MainViewModel(ConfigService configService, InputHookService hookService,
        TaskSchedulerService scheduler, TimerResolutionService timerResolution)
    {
        _configService = configService;
        _hookService = hookService;
        _scheduler = scheduler;
        _timerResolution = timerResolution;

        _config = _configService.Load();
        _globallyEnabled = _config.GlobalSwitch.Enabled;
        _masterKeyText = _config.GlobalSwitch.HasKey
            ? InputNameMapper.GetKeyName(_config.GlobalSwitch.VirtualKey)
            : "未设置";
        _soundVolume = Math.Clamp(_config.SoundVolume, 0, 100);
        _soundCue.Volume = _soundVolume / 100.0;

        // 调度器日志 → UI 日志面板（钩子线程触发，封送到 UI 线程）。
        _scheduler.Log += msg =>
            Application.Current?.Dispatcher.BeginInvoke(() => AddLog(msg));

        // 命令。
        SetGlobalSwitchCommand = RelayCommand.Create(
            () => GlobalSwitchSetupRequested?.Invoke());
        ImportCommand = RelayCommand.Create(() => ImportRequested?.Invoke());
        ExportCommand = RelayCommand.Create(() => ExportRequested?.Invoke());
        ToggleLogCommand = RelayCommand.Create(() => LogsExpanded = !LogsExpanded);
        SourceClickedCommand = new RelayCommand(p =>
        {
            if (p is KeySourceViewModel svm && !svm.IsSpacer && svm.Source is not null)
                SchemeEditRequested?.Invoke(svm);
        });
        EditSchemeCommand = new RelayCommand(p =>
        {
            if (p is KeySourceViewModel svm && !svm.IsSpacer && svm.Source is not null)
                SchemeEditRequested?.Invoke(svm);
        });
        ToggleSchemeCommand = new RelayCommand(p =>
        {
            if (p is KeySourceViewModel svm) ToggleSchemeEnabled(svm);
        });
        ClearSchemeCommand = new RelayCommand(p =>
        {
            if (p is KeySourceViewModel svm) ClearScheme(svm);
        });
        ToggleGlobalCommand = RelayCommand.Create(ToggleGlobalEnabled);

        // 音量滑块防抖：拖动停止 500ms 后落盘（避免拖动过程高频写配置文件）。
        _volumeSaveTimer.Tick += (_, _) =>
        {
            _volumeSaveTimer.Stop();
            _config.SoundVolume = _soundVolume;
            SaveConfig();
        };

        // 构建输入源按钮集合并从配置恢复注册状态。
        BuildSourceButtons();
        ApplyConfigToScheduler();
        RefreshAllButtons();

        // 键盘注入模式：从配置恢复（优先新字段 KeyboardMode，兼容旧 UseScanCodes）并同步到模拟器。
        _keyboardMode = _config.KeyboardMode is >= 0 and <= 3
            ? _config.KeyboardMode
            : _config.UseScanCodes ? 1 : 0;
        // DD 模式冷启动：驱动未就绪时静默回退普通模式（初始化阶段 UI 日志面板未展示，避免误导）。
        if (_keyboardMode == 3 && !DdDriverService.EnsureReady())
        {
            _keyboardMode = 0;
        }
        InputSimulatorService.KeyboardMode = _keyboardMode;
    }

    // ---------- 集合 ----------
    /// <summary>鼠标区 7 个输入源控件。</summary>
    public ObservableCollection<KeySourceViewModel> MouseButtons { get; } = new();

    /// <summary>键盘区行（含占位空白，用于对齐标准 104 键布局）。</summary>
    public ObservableCollection<ObservableCollection<KeySourceViewModel>> KeyboardRows { get; } = new();

    /// <summary>运行日志（最新在最上）。</summary>
    public ObservableCollection<string> Logs { get; } = new();

    // ---------- 命令 ----------
    /// <summary>打开全局开关键设置对话框。</summary>
    public ICommand SetGlobalSwitchCommand { get; }

    /// <summary>导入配置。</summary>
    public ICommand ImportCommand { get; }

    /// <summary>导出配置。</summary>
    public ICommand ExportCommand { get; }

    /// <summary>折叠 / 展开日志面板。</summary>
    public ICommand ToggleLogCommand { get; }

    /// <summary>输入源左键点击 → 编辑方案。</summary>
    public ICommand SourceClickedCommand { get; }

    /// <summary>右键菜单：编辑方案。</summary>
    public ICommand EditSchemeCommand { get; }

    /// <summary>右键菜单：启用/停用方案。</summary>
    public ICommand ToggleSchemeCommand { get; }

    /// <summary>右键菜单：清空方案。</summary>
    public ICommand ClearSchemeCommand { get; }

    /// <summary>托盘 / 横幅：全局启用 / 停用切换。</summary>
    public ICommand ToggleGlobalCommand { get; }

    // ---------- View 回调事件 ----------
    /// <summary>请求打开方案设置对话框（参数：输入源控件 VM）。</summary>
    public event Action<KeySourceViewModel>? SchemeEditRequested;

    /// <summary>请求打开全局开关键设置对话框。</summary>
    public event Action? GlobalSwitchSetupRequested;

    /// <summary>请求弹出导入文件对话框。</summary>
    public event Action? ImportRequested;

    /// <summary>请求弹出导出保存对话框。</summary>
    public event Action? ExportRequested;

    // ---------- 状态属性 ----------
    /// <summary>全局功能是否启用（停用时横幅显示 + 控件半透明）。</summary>
    public bool GloballyEnabled
    {
        get => _globallyEnabled;
        private set
        {
            if (!Set(ref _globallyEnabled, value)) return;
            MasterStateText = value ? "全局开关：已开启" : "全局开关：已关闭";
            OnPropertyChanged(nameof(ShowDisabledBanner));
            foreach (var b in MouseButtons) b.GloballyDisabled = !value;
            foreach (var row in KeyboardRows)
                foreach (var b in row)
                    if (!b.IsSpacer) b.GloballyDisabled = !value;
        }
    }

    /// <summary>是否显示"全局已停用"横幅。</summary>
    public bool ShowDisabledBanner => !_globallyEnabled;

    /// <summary>日志面板是否展开。</summary>
    public bool LogsExpanded
    {
        get => _logsExpanded;
        set => Set(ref _logsExpanded, value);
    }

    /// <summary>低级钩子是否安装成功。</summary>
    public bool HookInstalled
    {
        get => _hookInstalled;
        private set => Set(ref _hookInstalled, value);
    }

    /// <summary>钩子状态文本（顶部状态栏）。</summary>
    public string HookStatusText
    {
        get => _hookStatusText;
        private set => Set(ref _hookStatusText, value);
    }

    /// <summary>是否以管理员身份运行。</summary>
    public bool IsAdmin
    {
        get => _isAdmin;
        private set => Set(ref _isAdmin, value);
    }

    /// <summary>权限状态文本（顶部状态栏）。</summary>
    public string PermissionText
    {
        get => _permissionText;
        private set => Set(ref _permissionText, value);
    }

    /// <summary>定时器是否已提升到 1ms。</summary>
    public bool TimerHighRes
    {
        get => _timerHighRes;
        private set => Set(ref _timerHighRes, value);
    }

    /// <summary>定时器状态文本（顶部状态栏）。</summary>
    public string TimerStatusText
    {
        get => _timerStatusText;
        private set => Set(ref _timerStatusText, value);
    }

    /// <summary>全局开关状态文本（顶部状态栏）。</summary>
    public string MasterStateText
    {
        get => _masterStateText;
        private set => Set(ref _masterStateText, value);
    }

    /// <summary>全局开关键显示名（未设置为"未设置"）。</summary>
    public string MasterKeyText
    {
        get => _masterKeyText;
        private set => Set(ref _masterKeyText, value);
    }

    /// <summary>底部提示文字。</summary>
    public string HintText
    {
        get => _hintText;
        private set => Set(ref _hintText, value);
    }

    /// <summary>当前全局开关键对应的输入源（未设置返回 null）。</summary>
    public InputSource? GlobalSwitchSource => _config.GlobalSwitch.HasKey
        ? new InputSource
        {
            Kind = InputKind.Keyboard,
            VirtualKey = _config.GlobalSwitch.VirtualKey,
            Extended = _config.GlobalSwitch.Extended
        }
        : null;

    // ---------- 运行时初始化 ----------
    /// <summary>
    /// 运行时初始化：提升定时器分辨率、安装钩子、回填状态栏文本。
    /// 在 UI 线程的窗口 Loaded 阶段调用一次。
    /// </summary>
    public void InitializeRuntime(bool isAdmin)
    {
        IsAdmin = isAdmin;
        PermissionText = isAdmin ? "权限：管理员" : "权限：非管理员（功能受限）";

        _timerResolution.Start();
        TimerHighRes = _timerResolution.IsHighResolution;
        TimerStatusText = TimerHighRes ? "定时器：1ms 高精度" : "定时器：默认分辨率";

        HookInstalled = _hookService.Install();
        HookStatusText = HookInstalled
            ? "钩子：已安装"
            : "钩子：安装失败（请以管理员身份运行）";
        if (!HookInstalled)
            AddLog("低级钩子安装失败，请右键以管理员身份运行本程序。");

        AddLog($"初始化完成：总开关{(_globallyEnabled ? "已开启" : "已关闭（按 [" + _masterKeyText + "] 开启）")}，"
               + $"方案 {_config.Schemes.Count} 个。");
    }

    // ---------- 钩子事件接入（由 View 在安装成功后绑定） ----------
    /// <summary>输入源按下（钩子线程调用）：优先处理总开关键，其余交调度器。</summary>
    public void HandleHookDown(InputSource source)
    {
        if (GlobalSwitchSource is { } gk && gk.Equals(source))
        {
            // 热键录制期间不响应（避免把总开关键录为方案源时误切总开关）。
            if (!KeyRecorder.IsAnyRecording)
                Application.Current?.Dispatcher.BeginInvoke(ToggleGlobalEnabled);
            return;
        }
        _scheduler.HandleSourceDown(source);
    }

    /// <summary>输入源释放（钩子线程调用）。</summary>
    public void HandleHookUp(InputSource source) => _scheduler.HandleSourceUp(source);

    // ---------- 全局总开关 ----------
    /// <summary>
    /// 切换总开关（横幅、控件透明度、调度器、提示语音、配置持久化）。
    /// 开启播“启动”、关闭播“关闭”；关闭时调度器立即停止所有连发任务。
    /// </summary>
    public void ToggleGlobalEnabled()
    {
        GloballyEnabled = !GloballyEnabled;
        _config.GlobalSwitch.Enabled = GloballyEnabled;
        _scheduler.SetMasterEnabled(GloballyEnabled);
        if (GloballyEnabled) _soundCue.PlayStart(); else _soundCue.PlayStop();
        SaveConfig();
        AddLog(GloballyEnabled ? "总开关已开启（启动）。" : "总开关已关闭，所有连发已停止。");
    }

    /// <summary>设置全局开关键（null 表示清除）。冲突时返回 false 且不修改。</summary>
    public bool SetGlobalSwitchKey(InputSource? key)
    {
        if (key is null)
        {
            _config.GlobalSwitch.HasKey = false;
            MasterKeyText = "未设置";
            OnPropertyChanged(nameof(GlobalSwitchSource));
            SaveConfig();
            AddLog("全局开关键已清除（总开关只能通过界面托盘菜单切换）。");
            return true;
        }

        // 冲突验证：不能与任何已注册方案的注册源相同（复用调度器的键格式）。
        var candidateKey = TaskSchedulerService.BuildSourceKey(key);
        if (_config.Schemes.ContainsKey(candidateKey))
        {
            AddLog($"设置失败：[{InputNameMapper.GetSourceName(key)}] 已被方案占用。");
            return false;
        }

        // 换键时：旧总开关键若有方案则迁移到新键（保持方案不丢失，且避免键位重叠）。
        if (GlobalSwitchSource is { } oldKey && !oldKey.Equals(key))
            RelocateSchemeFromMasterKey(oldKey, key);

        _config.GlobalSwitch.HasKey = true;
        _config.GlobalSwitch.VirtualKey = key.VirtualKey;
        _config.GlobalSwitch.Extended = key.Extended;
        MasterKeyText = InputNameMapper.GetSourceName(key);
        OnPropertyChanged(nameof(GlobalSwitchSource));
        SaveConfig();
        AddLog($"总开关键已设置为 [{InputNameMapper.GetSourceName(key)}]。");
        return true;
    }

    /// <summary>提示语音音量（0~100；同步语音服务并防抖持久化）。</summary>
    public double SoundVolume
    {
        get => _soundVolume;
        set
        {
            value = Math.Clamp(value, 0, 100);
            if (!Set(ref _soundVolume, value)) return;
            _soundCue.Volume = value / 100.0;
            _volumeSaveTimer.Stop();
            _volumeSaveTimer.Start();
        }
    }

    /// <summary>退出前静音提示语音（避免退出时序还播报语音）。</summary>
    public void SoundCueMuteForExit() => _soundCue.Volume = 0;

    // ---------- 日志 / 保存 ----------
    /// <summary>把当前配置对象交给外部保存（退出时序用）。</summary>
    public AppConfig CurrentConfig => _config;

    /// <summary>音量滑块防抖保存定时器（拖动结束后 500ms 落盘）。</summary>
    private readonly System.Windows.Threading.DispatcherTimer _volumeSaveTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(500)
    };

    /// <summary>追加一条 UI 日志（最新在最上，最多保留 200 条）。</summary>
    public void AddLog(string message)
    {
        Logs.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {message}");
        while (Logs.Count > 200) Logs.RemoveAt(Logs.Count - 1);
    }

    /// <summary>保存配置到 %APPDATA%（任何变更触发）。</summary>
    public void SaveConfig() => _configService.Save(_config);

    /// <summary>
    /// 键盘注入模式：0 普通 SendInput / 1 扫描码 / 2 PostMessage 消息模式 / 3 DD 虚拟驱动。
    /// 界面下拉框热切换，自动保存。消息模式仅目标窗口在前台时有效；
    /// DD 模式需 dd63330.dll 与管理员权限，键盘注入无 LLKHF_INJECTED 标记。
    /// </summary>
    public int KeyboardMode
    {
        get => _keyboardMode;
        set
        {
            if (value is < 0 or > 3) value = 0;
            if (!Set(ref _keyboardMode, value)) return;
            // DD 模式：先确保虚拟驱动就绪（失败则回退普通模式并提示）。
            if (value == 3 && !DdDriverService.EnsureReady())
            {
                AddLog($"DD 驱动模式不可用：{DdDriverService.LastError}。已回退普通模式。");
                value = 0;
                Set(ref _keyboardMode, 0);
            }

            // 同步到模拟器（连发线程每次注入时读取）。
            InputSimulatorService.KeyboardMode = value;
            _config.KeyboardMode = value;
            _config.UseScanCodes = value == 1; // 旧字段语义保留（持久化兼容）
            SaveConfig();
            AddLog(value switch
            {
                1 => "键盘注入已切换：扫描码模式（DirectInput 兼容）。",
                2 => "键盘注入已切换：消息模式（仅游戏前台时有效）。",
                3 => $"键盘注入已切换：DD 驱动模式（{Path.GetFileName(DdDriverService.LoadedPath)}）。",
                _ => "键盘注入已切换：普通模式（SendInput）。"
            });
        }
    }

    /// <summary>把当前配置应用到连发调度器（方案变更后调用）。</summary>
    private void ApplyConfigToScheduler() => _scheduler.ApplyConfig(_config);

    /// <summary>INotifyPropertyChanged。</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
