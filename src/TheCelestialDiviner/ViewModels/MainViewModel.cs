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

/// <summary>键位录入选择态的用途（方案面板与底栏各设置项：行为不同，触发 / 取消流程一致）。</summary>
public enum PickKind
{
    /// <summary>未处于选择态。</summary>
    None,

    /// <summary>管理连发键方案（“从左侧键盘管理方案”按钮）：左键添加 / 右键取消，直至显式退出。</summary>
    AddScheme,

    /// <summary>设置全局开关热键（方案面板顶部的热键设置项）。</summary>
    SetMasterKey,

    /// <summary>设置切换方案热键（底栏“切换方案设置”项）：左键键位 / 直接按键设置，右键已设置键位取消，单次操作即完成退出。</summary>
    SetCycleKey
}

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
    private readonly KeyVisualizerService _visualizer;

    // ---------- 状态字段 ----------
    private AppConfig _config = new();
    private bool _globallyEnabled; // 总开关默认关闭（v2 语义，加载后随配置覆盖）
    private bool _hookInstalled;
    private bool _isAdmin = true;
    private string _hookStatusText = "钩子：未安装";
    private string _permissionText = "权限：检测中";
    private string _masterStateText = "全局开关：已关闭";
    private string _masterKeyText = "F9";
    private string _cycleKeyText = "未设置";           // 切换方案热键显示名（默认缺省未设置）
    private string _hintText = s_defaultHint;
    private double _soundVolume = Constants.DefaultSoundVolume;
    private bool _globalVisualEnabled = true;          // 键位可视化总开关（默认开启；关闭时仅禁用显示，各键开关状态保留）
    private int _keyboardMode; // 键盘注入模式：0 普通 / 1 扫描码 / 2 消息
    private int _activeProfile;                        // 方案面板当前档位（0/1/2 ↔ ①②③）
    private bool _pickingActive;                       // 键位录入选择态
    private PickKind _pickingKind = PickKind.None;     // 键位录入选择态用途
    private TriggerMode _selectedMode = TriggerMode.Toggle; // 方案面板当前标签模式
    private ToggleSection _selectedSection = ToggleSection.Normal; // 方案面板当前开关分区
    private ToggleSection _pickingSection = ToggleSection.Normal;  // 本次选择态录入的分区快照
    private InputSource? _dualFirstPick;               // 双宏两步录入：已选的第 1 键
    private string _pickingPromptText = "请选择按键";  // 选择态提示条文字
    private int _lastPickingExitTick;                  // 最近一次退出选择态的时刻（Environment.TickCount，退出冷却防陈旧点击重进）

    /// <summary>全局总开关提示语音服务（开启 = “启动”，关闭 = “关闭”）。</summary>
    private readonly SoundCueService _soundCue = new();

    /// <summary>键盘注入模式变更通知（导入配置后由 VM 触发，UI 回填下拉框）。</summary>
    public event Action<int>? KeyboardModeChanged;

    /// <summary>方案档位数量（①②③④）。</summary>
    public const int ProfileCount = 4;

    /// <summary>当前选中的方案档位（0~3 ↔ ①②③④，默认①）。切换时同步方案字典与调度器并实时落盘。</summary>
    public int ActiveProfile
    {
        get => _activeProfile;
        set
        {
            var index = value is >= 0 and < ProfileCount ? value : 0;
            if (!Set(ref _activeProfile, index)) return;
            _config.ActiveProfile = index;
            // 旧档位镜像字典脱钩，运行期字典指向新档位；调度器重建任务并落盘。
            SyncProfileRuntime();
            RefreshAllButtons();
            UpdateMasterKeyHighlight();
            RefreshSchemeList();
            ApplyConfigToScheduler();
            SyncVisualizerRegistry();
            SaveConfig();
            AddLog($"已切换到方案{ProfileLabel(index)}。");
        }
    }

    /// <summary>档位序号 → 显示名（0 → "①"）。</summary>
    public static string ProfileLabel(int index) => index switch
    {
        0 => "①",
        1 => "②",
        2 => "③",
        _ => "④"
    };

    /// <summary>
    /// 运行期档位对齐：把 Schemes 指向 Profiles[ActiveProfile] 同一实例（方案编辑直接落在活动档位）。
    /// 配置加载 / 导入 / 档位切换后调用；Profiles 不足 4 套时补齐空档位。
    /// </summary>
    private void SyncProfileRuntime()
    {
        while (_config.Profiles.Count < ProfileCount)
            _config.Profiles.Add(new Dictionary<string, KeyScheme>(StringComparer.Ordinal));
        _activeProfile = Compat.Clamp(_config.ActiveProfile, 0, ProfileCount - 1);
        _config.ActiveProfile = _activeProfile; // 越界回退后同步回配置，避免保存越界值。
        _config.Schemes = _config.Profiles[_activeProfile];
    }

    /// <summary>创建主视图模型：加载配置、构建输入源集合、应用调度器。</summary>
    public MainViewModel(ConfigService configService, InputHookService hookService,
        TaskSchedulerService scheduler, TimerResolutionService timerResolution)
    {
        _configService = configService;
        _hookService = hookService;
        _scheduler = scheduler;
        _timerResolution = timerResolution;
        _visualizer = new KeyVisualizerService(hookService);
        _visualizer.AttachScheduler(_scheduler);   // 连发目标键脉冲 → 键帽可视化

        _config = _configService.Load();
        SyncProfileRuntime();
        // 主题：手动切换过夜间/白天则固定，否则跟随系统深浅。
        var themeDark = _config.NightMode || (_config.ThemeFollowSystem && ThemeHelper.IsDarkMode());
        (App.Current as App)?.ApplyTheme(themeDark);
        // 需求：软件打开时无论上次退出时全局开关状态如何，一律重置为关闭。
        // 不读 _config.GlobalSwitch.Enabled；把配置对象里的旧值也覆盖回 false，
        // 保证之后任何时点的 SaveConfig 都不会把“启动即关闭”这一事实覆盖丢失。
        _globallyEnabled = false;
        _config.GlobalSwitch.Enabled = false;
        _globalVisualEnabled = _config.GlobalVisualEnabled;
        _visualizer.SetGlobalEnabled(_globalVisualEnabled);
        _visualizer.SetMode((VisualizerMode)Compat.Clamp(_config.VisualizerMode, 0, 2));
        // 状态提醒：总开关启动即关闭（提醒键帽不显示）；按钮开关随配置还原。
        _visualizer.SetMasterSwitch(_globallyEnabled);
        _visualizer.SetReminderEnabled(_config.StatusReminderEnabled);
        Views.KeycapOverlayWindow.ApplyScheme(KeycapSchemes.Resolve(_config.KeycapScheme));
        if (_config.VisualizerLeft is { } l && _config.VisualizerTop is { } t)
            _visualizer.SetPosition(l, t);
        // 调度器字段默认为启用，须与配置中的总开关状态同步，
        // 否则冷启动时横幅显示"已关闭"而方案实际处于待触发状态。
        _scheduler.SetMasterEnabled(_globallyEnabled);
        _masterKeyText = _config.GlobalSwitch.HasKey
            ? InputNameMapper.GetKeyName(_config.GlobalSwitch.VirtualKey, _config.GlobalSwitch.Extended)
            : "未设置";
        _cycleKeyText = _config.ProfileCycle.HasKey
            ? InputNameMapper.GetKeyName(_config.ProfileCycle.VirtualKey, _config.ProfileCycle.Extended)
            : "未设置";
        _soundVolume = Compat.Clamp(_config.SoundVolume, 0, 100);
        _soundCue.Volume = _soundVolume / 100.0;

        // 调度器日志 → UI 日志面板（钩子线程触发，封送到 UI 线程）。
        _scheduler.Log += msg =>
            Application.Current?.Dispatcher.BeginInvoke(() => AddLog(msg));

        // 命令。
        ImportCommand = RelayCommand.Create(() => ImportRequested?.Invoke());
        ExportCommand = RelayCommand.Create(() => ExportRequested?.Invoke());
        StartPickingCommand = RelayCommand.Create(StartPicking);
        SetMasterKeyPickingCommand = RelayCommand.Create(StartMasterKeyPicking);
        SetCycleKeyPickingCommand = RelayCommand.Create(StartCycleKeyPicking);
        SourceClickedCommand = new RelayCommand(p =>
        {
            if (p is KeySourceViewModel svm && !svm.IsSpacer && svm.Source is not null)
                HandleSourceClicked(svm);
        });
        ToggleGlobalCommand = RelayCommand.Create(ToggleGlobalEnabled);
        ToggleGlobalVisualCommand = RelayCommand.Create(() => GlobalVisualEnabled = !GlobalVisualEnabled);
        ToggleStatusReminderCommand = RelayCommand.Create(() => StatusReminderEnabled = !StatusReminderEnabled);
        ToggleDivinerVoiceCommand = RelayCommand.Create(() => DivinerVoiceEnabled = !DivinerVoiceEnabled);
        // 底栏设置菜单（键盘注入 / 键帽配色 / 键帽可视化）：参数沿用原下拉框语义，
        // 点击选项即热生效 + 落盘。
        SetKeyboardModeCommand = new RelayCommand(o =>
        {
            if (int.TryParse(o?.ToString(), out var mode)) KeyboardMode = mode;
        });
        SetKeycapSchemeCommand = new RelayCommand(o =>
        {
            if (o is string name) KeycapSchemeName = name;
        });
        SetVisualizerModeCommand = new RelayCommand(o =>
        {
            if (int.TryParse(o?.ToString(), out var mode)) VisualizerModeIndex = mode;
        });
        DeleteCheckedSchemesCommand = RelayCommand.Create(RequestDeleteCheckedSchemes);
        AdjustVisualizerCommand = RelayCommand.Create(() =>
            AdjustVisualizerRequested?.Invoke((l, t) =>
            {
                _config.VisualizerLeft = l;
                _config.VisualizerTop = t;
                _visualizer.SetPosition(l, t);   // 立即更新运行中的悬浮窗（不等重启）
                SaveConfig();
                AddLog("可视化键帽位置已保存。");
            }));

        // 音量滑块防抖：拖动停止 500ms 后落盘（避免拖动过程高频写配置文件）。
        _volumeSaveTimer.Tick += (_, _) =>
        {
            _volumeSaveTimer.Stop();
            _config.SoundVolume = _soundVolume;
            SaveConfig();
        };

        // 旧版方案迁移（仅保留按键自身连发）→ 构建输入源按钮集合。
        MigrateLegacySchemes();
        BuildSourceButtons();
        ApplyConfigToScheduler();
        RefreshAllButtons();
        RefreshSchemeList();
        // 键位可视化注册表：当前档位全部方案键位默认注册并开启显示。
        SyncVisualizerRegistry();
        // 按钮创建晚于配置加载：构造阶段补一次暗淡状态传播（开启时暗淡）。
        ApplyDimState();
        // 总开关键绿色高亮（按钮创建后；默认 F9）。
        UpdateMasterKeyHighlight();

        // 键盘注入模式：默认 DD 驱动（物理级）；配置优先，越界回退默认。
        // DD 需 dd63330.dll + 管理员权限，冷启动就绪检查失败时回退普通模式。
        _keyboardMode = _config.KeyboardMode is >= 0 and <= 3
            ? _config.KeyboardMode
            : 3;
        if (_keyboardMode == 3 && !DdDriverService.EnsureReady())
        {
            _keyboardMode = 0;
            Logger.Warn($"DD 驱动初始化失败，本次启动回退普通模式：{DdDriverService.LastError}");
        }
        InputSimulatorService.KeyboardMode = _keyboardMode;
    }

    // ---------- 集合 ----------
    /// <summary>鼠标区 7 个输入源控件。</summary>
    public ObservableCollection<KeySourceViewModel> MouseButtons { get; } = new();

    /// <summary>键盘区行（含占位空白，用于对齐标准 104 键布局）。</summary>
    public ObservableCollection<ObservableCollection<KeySourceViewModel>> KeyboardRows { get; } = new();

    /// <summary>键盘区右列小键盘图块（6 行 4 列网格：+ 与 ENT 竖向两格、N0 双倍宽）。</summary>
    public ObservableCollection<KeySourceViewModel> NumpadKeys { get; } = new();

    /// <summary>运行日志（最新在最上）。</summary>
    public ObservableCollection<string> Logs { get; } = new();

    /// <summary>方案面板当前模式的连发键行。</summary>
    public ObservableCollection<SchemeRowViewModel> SchemeRows { get; } = new();

    // ---------- 命令 ----------
    /// <summary>导入配置。</summary>
    public ICommand ImportCommand { get; }

    /// <summary>导出配置。</summary>
    public ICommand ExportCommand { get; }

    /// <summary>进入 / 退出方案管理模式（左键键位添加、右键键位取消，Esc / 空白处 / 按钮自身退出）。</summary>
    public ICommand StartPickingCommand { get; }

    /// <summary>进入总开关键录入选择态（单次录入即完成并退出，与方案管理模式不同）。</summary>
    public ICommand SetMasterKeyPickingCommand { get; }

    /// <summary>进入切换方案热键录入选择态（左键键位 / 直接按键设置，右键已设置键位取消，单次操作即完成退出）。</summary>
    public ICommand SetCycleKeyPickingCommand { get; }

    /// <summary>键鼠区图块左键点击（选择态 → 录入该键；非选择态 → 无操作）。</summary>
    public ICommand SourceClickedCommand { get; }

    /// <summary>托盘 / 横幅：全局启用 / 停用切换。</summary>
    public ICommand ToggleGlobalCommand { get; }

    // ---------- View 回调事件 ----------
    /// <summary>请求弹出导入文件对话框。</summary>
    public event Action? ImportRequested;

    /// <summary>请求弹出导出保存对话框。</summary>
    public event Action? ExportRequested;

    /// <summary>
    /// 方案管理模式中收到鼠标源按下（钩子线程调用，View 已封送 UI 线程）。
    /// View 命中测试光标位置：落在键鼠图块上 → 右键取消 / 其余键录入该键方案；否则请求退出选择态。
    /// </summary>
    public event Action<InputSource>? PickingMouseReceived;

    // ---------- 状态属性 ----------
    /// <summary>全局开关状态变更（托盘图标 / 提示文字联动；订阅者需自行封送 UI 线程）。</summary>
    public event Action<bool>? GlobalStateChanged;

    /// <summary>全局功能是否启用（停用时横幅显示 + 控件半透明）。</summary>
    public bool GloballyEnabled
    {
        get => _globallyEnabled;
        private set
        {
            if (!Set(ref _globallyEnabled, value)) return;
            MasterStateText = value ? "全局开关：已开启" : "全局开关：已关闭";
            OnPropertyChanged(nameof(ShowDisabledBanner));
            _visualizer.SetMasterSwitch(value);   // 状态提醒键帽随总开关显隐（无视键位可视化闸门）
            ApplyDimState();
            GlobalStateChanged?.Invoke(value);
        }
    }

    /// <summary>是否显示"全局已停用"横幅。</summary>
    public bool ShowDisabledBanner => !_globallyEnabled;

    /// <summary>
    /// 把暗淡状态传播到全部输入源按钮（开启时暗淡、关闭时清晰）。
    /// GloballyEnabled setter 与构造阶段（按钮晚于配置加载创建）共用。
    /// </summary>
    private void ApplyDimState()
    {
        foreach (var b in MouseButtons) b.GloballyDimmed = _globallyEnabled;
        foreach (var row in KeyboardRows)
            foreach (var b in row)
                if (!b.IsSpacer) b.GloballyDimmed = _globallyEnabled;
        foreach (var b in NumpadKeys) b.GloballyDimmed = _globallyEnabled;
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

    /// <summary>切换方案热键显示名（未设置为"未设置"）。</summary>
    public string CycleKeyText
    {
        get => _cycleKeyText;
        private set => Set(ref _cycleKeyText, value);
    }

    /// <summary>底部提示文字。</summary>
    public string HintText
    {
        get => _hintText;
        private set => Set(ref _hintText, value);
    }

    /// <summary>
    /// 键位录入选择态的用途（驱动各设置项的录入文案与后续行为）。
    /// 变更时发 PropertyChanged：XAML DataTrigger（按钮"录入中…"态）与 View 注释区联动依赖通知。
    /// </summary>
    public PickKind PickingKind
    {
        get => _pickingKind;
        private set => Set(ref _pickingKind, value);
    }

    /// <summary>键位录入选择态：键鼠区透明度下降并高亮悬停键，点击/按键完成录入。</summary>
    public bool PickingActive
    {
        get => _pickingActive;
        private set
        {
            if (Set(ref _pickingActive, value)) OnPropertyChanged(nameof(PickingPromptVisible));
        }
    }

    /// <summary>选择态提示条是否显示（与选择态同步，但不在非选择态显示占位）。</summary>
    public Visibility PickingPromptVisible => PickingActive ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>选择态提示条文字（双宏两步录入时提示当前进度）。</summary>
    public string PickingPromptText
    {
        get => _pickingPromptText;
        private set => Set(ref _pickingPromptText, value);
    }

    /// <summary>方案面板当前标签模式（开关 / 按压），新增方案按该模式录入。</summary>
    public TriggerMode SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (Set(ref _selectedMode, value))
            {
                OnPropertyChanged(nameof(SectionTabsVisible));
                RefreshSchemeList();
            }
        }
    }

    /// <summary>开关分区子标签是否可见（仅开关模式显示：常规 / 轮转 / 双宏）。</summary>
    public Visibility SectionTabsVisible =>
        SelectedMode == TriggerMode.Toggle ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>方案面板当前开关分区（常规 / 轮转 / 双宏），新增方案按该分区录入。</summary>
    public ToggleSection SelectedSection
    {
        get => _selectedSection;
        set
        {
            if (Set(ref _selectedSection, value)) RefreshSchemeList();
        }
    }

    /// <summary>当前模式是否存在连发键（控制空态提示显隐）。</summary>
    public bool HasSchemeRows => SchemeRows.Count > 0;

    /// <summary>当前全局开关键对应的输入源（未设置返回 null）。</summary>
    public InputSource? GlobalSwitchSource => _config.GlobalSwitch.HasKey
        ? new InputSource
        {
            Kind = InputKind.Keyboard,
            VirtualKey = _config.GlobalSwitch.VirtualKey,
            Extended = _config.GlobalSwitch.Extended
        }
        : null;

    /// <summary>当前切换方案热键对应的输入源（未设置返回 null）。</summary>
    public InputSource? ProfileCycleSource => _config.ProfileCycle.HasKey
        ? new InputSource
        {
            Kind = InputKind.Keyboard,
            VirtualKey = _config.ProfileCycle.VirtualKey,
            Extended = _config.ProfileCycle.Extended
        }
        : null;

    // ---------- 运行时初始化 ----------
    /// <summary>
    /// 运行时初始化：提升定时器分辨率（连发间隔精度依赖，无状态栏展示）、安装钩子。
    /// 在 UI 线程的窗口 Loaded 阶段调用一次。
    /// </summary>
    public void InitializeRuntime(bool isAdmin)
    {
        IsAdmin = isAdmin;
        PermissionText = isAdmin ? "权限：管理员" : "权限：非管理员（功能受限）";

        // 顶部定时器状态栏已移除，但 1ms 分辨率仍是 Sleep(1) 级连发间隔的功能基础，保留提升。
        _timerResolution.Start();

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
    /// <summary>
    /// 输入源按下（钩子线程调用，已封送 UI 线程）：
    /// 优先总开关键 → 切换方案热键 → 键位录入选择态 → 调度器。
    /// </summary>
    public void HandleHookDown(InputSource source)
    {
        if (GlobalSwitchSource is { } gk && gk.Equals(source))
        {
            // 键位录入选择态 / 录制期间不响应（避免把总开关键录为方案源或热键时误切总开关）。
            if (!KeyRecorder.IsAnyRecording && !_pickingActive)
                Application.Current?.Dispatcher.BeginInvoke(ToggleGlobalEnabled);
            return;
        }

        if (ProfileCycleSource is { } ck && ck.Equals(source))
        {
            // 切换方案热键：按下按顺序切换非空方案档位。录入选择态 / 录制期间不响应
            // （该键已被占用，不能录为方案源；也避免录入切换方案热键时误触切换）。
            if (!KeyRecorder.IsAnyRecording && !_pickingActive)
                Application.Current?.Dispatcher.BeginInvoke(CycleToNextProfile);
            return;
        }

        if (_pickingActive)
        {
            // 管理模式：键盘按键直接录入（Esc 退出；双宏成对录入中先放弃第 1 键，偶数态才退出）；
            // 鼠标按下交 View 命中测试（落在键鼠图块上 → 右键取消 / 其余键录入，否则请求退出）。
            if (source.Kind == InputKind.Keyboard)
            {
                if (source.VirtualKey == 0x1B) TryExitPicking();
                else CompletePick(source);
                return;
            }
            if (source.Kind == InputKind.Mouse &&
                source.Mouse is not (MouseInput.WheelUp or MouseInput.WheelDown))
            {
                PickingMouseReceived?.Invoke(source);
                return;
            }
            return; // 滚轮滚动不打断选择态
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
        if (GloballyEnabled) _soundCue.PlayStart(DivinerVoiceEnabled); else _soundCue.PlayStop();
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
            UpdateMasterKeyHighlight();
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

        // 冲突验证：不能与切换方案热键相同（同一键位两个语义会互相抢占）。
        if (ProfileCycleSource is { } ck && ck.Equals(key))
        {
            AddLog($"设置失败：[{InputNameMapper.GetSourceName(key)}] 已被切换方案热键占用。");
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
        UpdateMasterKeyHighlight();
        AddLog($"总开关键已设置为 [{InputNameMapper.GetSourceName(key)}]。");
        return true;
    }

    /// <summary>设置切换方案热键（null 表示清除恢复缺省）。占用冲突时返回 false 且不修改。</summary>
    public bool SetProfileCycleKey(InputSource? key)
    {
        if (key is null)
        {
            _config.ProfileCycle.HasKey = false;
            CycleKeyText = "未设置";
            OnPropertyChanged(nameof(ProfileCycleSource));
            SaveConfig();
            AddLog("切换方案热键已取消，恢复缺省（未设置）。");
            return true;
        }

        // 冲突验证：仅支持键盘键；不能与总开关键相同。
        if (key.Kind != InputKind.Keyboard)
        {
            AddLog("切换方案热键仅支持键盘键（鼠标 / 滚轮不可用）。");
            return false;
        }
        if (GlobalSwitchSource is { } gk && gk.Equals(key))
        {
            AddLog($"设置失败：[{InputNameMapper.GetSourceName(key)}] 已被总开关占用。");
            return false;
        }

        // 冲突验证：不能与任一方案档位的连发键相同（四个档位全查，
        // 否则切换到该档位后按下此键时切换与连发互相抢占）。
        var keyStr = TaskSchedulerService.BuildSourceKey(key);
        for (var i = 0; i < ProfileCount; i++)
        {
            if (!_config.Profiles[i].ContainsKey(keyStr)) continue;
            AddLog($"设置失败：[{InputNameMapper.GetSourceName(key)}] 已被方案{ProfileLabel(i)}的连发键占用。");
            return false;
        }

        _config.ProfileCycle.HasKey = true;
        _config.ProfileCycle.VirtualKey = key.VirtualKey;
        _config.ProfileCycle.Extended = key.Extended;
        CycleKeyText = InputNameMapper.GetSourceName(key);
        OnPropertyChanged(nameof(ProfileCycleSource));
        SaveConfig();
        AddLog($"切换方案热键已设置为 [{InputNameMapper.GetSourceName(key)}]：按下按 ①→②→③→④ 顺序切换非空方案档位。");
        return true;
    }

    /// <summary>
    /// 切换方案热键按下：按 ①→②→③→④ 顺序切到下一个非空方案档位（空档位跳过，环绕循环）；
    /// 其余档位均为空时不切换并提示。
    /// </summary>
    public void CycleToNextProfile()
    {
        for (var step = 1; step < ProfileCount; step++)
        {
            var index = (_activeProfile + step) % ProfileCount;
            if (_config.Profiles[index].Count == 0) continue;
            ActiveProfile = index;
            return;
        }
        AddLog("切换方案热键：其他方案档位均为空，未切换。");
    }

    /// <summary>提示语音音量（0~100；同步语音服务并防抖持久化）。</summary>
    public double SoundVolume
    {
        get => _soundVolume;
        set
        {
            value = Compat.Clamp(value, 0, 100);
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

    /// <summary>夜间模式（true = 夜间深色，false = 白天浅色；实时生效并落盘，覆盖系统跟随）。</summary>
    public bool NightMode
    {
        get => _config.NightMode;
        set
        {
            if (_config.NightMode == value) return;
            _config.NightMode = value;
            _config.ThemeFollowSystem = false;   // 手动切换后不再跟随系统深浅
            ApplyTheme();          // 实时切换主题资源 + 图块刷子
            SaveConfig();
            OnPropertyChanged(nameof(NightMode));   // 底栏按钮图标随日/夜切换（sun ↔ eclipse）
            AddLog(value ? "已切换：夜间模式。" : "已切换：白天模式。");
        }
    }

    /// <summary>按当前配置应用主题资源：手动切换过夜间/白天后固定，否则跟随系统。</summary>
    private void ApplyTheme()
    {
        var isDark = _config.NightMode || (_config.ThemeFollowSystem && ThemeHelper.IsDarkMode());
        (App.Current as App)?.ApplyTheme(isDark);
        // 已创建的键位图块实例刷子随主题刷新（UpdateTheme 只重建静态刷子）。
        foreach (var b in MouseButtons) b.RefreshTheme();
        foreach (var row in KeyboardRows)
            foreach (var b in row) b.RefreshTheme();
        foreach (var b in NumpadKeys) b.RefreshTheme();
    }

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

    /// <summary>时序档位切换后同步 UI（导入配置回填；单选钮由 code-behind 订阅）。</summary>
    public event Action<int>? TimingPresetChanged;

    /// <summary>批量删除勾选连发键的确认请求（参数 = 勾选数；View 弹确认框，
    /// 确认后调用 <see cref="DeleteCheckedSchemes"/>）。</summary>
    public event Action<int>? DeleteCheckedRequested;

    /// <summary>连发时序档位：0 = 常规（26/26）、1 = 极限（11/11）。
    /// 仅作为之后添加连发键的默认值（行内仍可单独调整），不修改已有方案。</summary>
    public int TimingPreset
    {
        get => _config.TimingPreset;
        set
        {
            if (value is not 0 and not 1) value = 0;
            if (_config.TimingPreset == value) return;
            _config.TimingPreset = value;
            _config.DefaultHoldMs = value == 1 ? Constants.ExtremeHoldMs : Constants.RegularHoldMs;
            _config.DefaultIntervalMs = value == 1 ? Constants.ExtremeIntervalMs : Constants.RegularIntervalMs;
            SaveConfig();
            AddLog(value == 1
                ? "时序档位：极限（按压 11ms / 间隔 11ms）——仅作为之后添加连发键的默认值，已有方案不变。帧率 ≥90 时登记上限约 45 发/秒，低帧率会丢发。"
                : "时序档位：常规（按压 26ms / 间隔 26ms）——仅作为之后添加连发键的默认值，已有方案不变。40~100 帧全区间零丢失。");
            TimingPresetChanged?.Invoke(value);
        }
    }

    /// <summary>
    /// 方案行勾选框的全选态：全启用 = true，全停用 = false，混合 = null（半选显示）。
    /// 表头一键勾选 / 取消全部（null 态点击视为全选，WPF 非三态复选框默认行为）。
    /// </summary>
    public bool? SchemesAllEnabled
    {
        get
        {
            if (_config.Schemes.Count == 0) return false;
            if (_config.Schemes.Values.All(s => s.Enabled)) return true;
            return _config.Schemes.Values.Any(s => s.Enabled) ? null : (bool?)false;
        }
        set
        {
            if (value is null) return;   // 半选态不作为写入值（点击产生的 true/false 才生效）
            SetAllSchemesEnabled(value.Value);
        }
    }

    /// <summary>一键启用 / 停用全部连发键（当前方案档位）。</summary>
    private void SetAllSchemesEnabled(bool enabled)
    {
        var changed = false;
        foreach (var scheme in _config.Schemes.Values)
        {
            if (scheme.Enabled == enabled) continue;
            scheme.Enabled = enabled;
            changed = true;
        }
        if (!changed)
        {
            // 无实际变化（如空列表）也要刷新：让复选框视觉回弹到全选态计算结果。
            OnPropertyChanged(nameof(SchemesAllEnabled));
            return;
        }
        RefreshAllButtons();
        ApplyConfigToScheduler();
        SaveConfig();
        RefreshSchemeList();   // 重建行并刷新全选态显示
        AddLog(enabled ? "已一键启用全部连发键。" : "已一键停用全部连发键。");
    }

    // ---------- 键位可视化 ----------

    /// <summary>键位可视化服务（悬浮层驱动；由 App / MainWindow 接线）。</summary>
    public KeyVisualizerService Visualizer => _visualizer;

    /// <summary>键位可视化总开关（默认开启）。关闭时仅整体禁用显示，各键位开关状态保留，恢复后延续。</summary>
    public bool GlobalVisualEnabled
    {
        get => _globalVisualEnabled;
        set
        {
            if (!Set(ref _globalVisualEnabled, value)) return;
            _config.GlobalVisualEnabled = value;
            _visualizer.SetGlobalEnabled(value);
            SaveConfig();
            AddLog($"键位可视化已{(value ? "开启" : "全部禁用（各键位开关状态保留）")}。");
        }
    }

    /// <summary>状态提醒开关联动命令（底栏 message-square-dot 图标按钮）。</summary>
    public ICommand ToggleStatusReminderCommand { get; }

    /// <summary>“成为衍天高手”开关联动命令（底栏按钮）。</summary>
    public ICommand ToggleDivinerVoiceCommand { get; }

    /// <summary>底栏设置菜单：切换键盘注入模式（参数 = 模式索引 0 普通 / 1 扫描码 / 2 消息 / 3 DD 驱动）。</summary>
    public ICommand SetKeyboardModeCommand { get; }

    /// <summary>底栏设置菜单：切换键帽配色方案（参数 = KeycapSchemes.All 中的方案名）。</summary>
    public ICommand SetKeycapSchemeCommand { get; }

    /// <summary>底栏设置菜单：切换键帽可视化过滤模式（参数 = 索引 0 全部 / 1 修饰键和自定义键 / 2 自定义键）。</summary>
    public ICommand SetVisualizerModeCommand { get; }

    /// <summary>
    /// 状态提醒（底栏按钮）：开启后总开关开启时在键帽悬浮区常驻应用图标键帽，
    /// 任一方案连发触发时自动隐藏，全部连发停止 2.5s 后恢复显示；
    /// 不受键位可视化总开关约束（总开关关闭时键帽随之消失）。
    /// </summary>
    public bool StatusReminderEnabled
    {
        get => _config.StatusReminderEnabled;
        set
        {
            if (_config.StatusReminderEnabled == value) return;
            _config.StatusReminderEnabled = value;
            _visualizer.SetReminderEnabled(value);
            SaveConfig();
            AddLog(value
                ? "状态提醒已开启：总开关开启时常驻应用图标键帽，连发期间自动隐藏。"
                : "状态提醒已关闭。");
            OnPropertyChanged(nameof(StatusReminderEnabled));   // 底栏按钮图标随开关切换金色 ↔ 常规色
        }
    }

    /// <summary>
    /// “成为衍天高手”（底栏按钮）：激活时总开关启动的语音播报改为「衍天高手启动」，
    /// 关闭播报不变（仍为「停止」）；激活态按钮墨迹为金色（AccentGold 随日/夜主题资源自动适配）。
    /// </summary>
    public bool DivinerVoiceEnabled
    {
        get => _config.DivinerVoiceEnabled;
        set
        {
            if (_config.DivinerVoiceEnabled == value) return;
            _config.DivinerVoiceEnabled = value;
            SaveConfig();
            AddLog(value
                ? "成为衍天高手：总开关启动的语音播报改为「衍天高手启动」。"
                : "成为衍天高手已关闭：总开关启动的语音播报恢复「启动」。");
            OnPropertyChanged(nameof(DivinerVoiceEnabled));   // 底栏按钮墨迹随开关切换金色 ↔ 常规色
        }
    }

    /// <summary>键帽配色方案名（KeycapSchemes.All 之一，默认 Silver；切换实时落盘并立即生效）。</summary>
    public string KeycapSchemeName
    {
        get => _config.KeycapScheme;
        set
        {
            if (_config.KeycapScheme == value) return;
            _config.KeycapScheme = value;
            Views.KeycapOverlayWindow.ApplyScheme(KeycapSchemes.Resolve(value));
            SaveConfig();
            AddLog($"键帽配色已切换为 {value}。");
            OnPropertyChanged(nameof(KeycapSchemeName));   // 设置菜单 ● 选中标记随切换刷新
        }
    }

    /// <summary>键帽配色菜单绑定数据（方案名列表）。</summary>
    public string[] KeycapSchemeNames => KeycapSchemes.Names;

    /// <summary>键帽可视化菜单绑定数据（过滤模式名列表）。</summary>
    public string[] VisualizerModeNames { get; } = ["全部", "修饰键和自定义键", "自定义键"];

    /// <summary>
    /// 键帽可视化过滤模式选中项索引（0 全部 / 1 修饰键和自定义键 / 2 自定义键；
    /// 切换实时生效并落盘，导入配置同步还原）。
    /// </summary>
    public int VisualizerModeIndex
    {
        get => Compat.Clamp(_config.VisualizerMode, 0, 2);
        set
        {
            var v = Compat.Clamp(value, 0, 2);
            if (_config.VisualizerMode == v) return;
            _config.VisualizerMode = v;
            _visualizer.SetMode((VisualizerMode)v);
            SaveConfig();
            AddLog($"键帽可视化方案：{VisualizerModeNames[v]}。");
            OnPropertyChanged(nameof(VisualizerModeIndex));   // 设置菜单 ● 选中标记随切换刷新
        }
    }

    /// <summary>键位可视化总开关联动命令。</summary>
    public ICommand ToggleGlobalVisualCommand { get; }

    /// <summary>删除全部勾选（启用）的连发键（表头 ✕；经确认对话框后执行）。</summary>
    public ICommand DeleteCheckedSchemesCommand { get; }

    /// <summary>进入可视化位置调整模式的命令（Window 接线：调悬浮窗 BeginAdjust）。</summary>
    public ICommand AdjustVisualizerCommand { get; private set; } = null!;

    /// <summary>调整模式请求（Window 接线后订阅；参数为确认回调 Left/Top）。</summary>
    public event Action<Action<double, double>>? AdjustVisualizerRequested;
    /// <summary>
    /// 同步键位可视化注册表（当前档位全部方案键位，含每键显示开关；键位集合变化后调用：
    /// 启动 / 导入 / 删除 / 总开关键迁移）。
    /// </summary>
    private void SyncVisualizerRegistry()
    {
        var entries = new List<KeyValuePair<string, bool>>(_config.Schemes.Count);
        foreach (var key in _config.Schemes.Keys)
            entries.Add(new(key, GetVisualEnabled(key)));
        _visualizer.ReplaceRegistry(entries);
    }

    /// <summary>读取键位可视化显示开关（未记忆过的键位默认开启可视化）。</summary>
    public bool GetVisualEnabled(string key)
    {
        return _config.VisualKeys.TryGetValue(key, out var v) ? v : true;
    }

    /// <summary>设置键位可视化显示开关（方案行 eye 图标切换；实时落盘）。</summary>
    public void SetVisualEnabled(string key, bool visible)
    {
        if (GetVisualEnabled(key) == visible) return;
        _config.VisualKeys[key] = visible;
        _visualizer.SetVisible(key, visible);
        SaveConfig();
        var source = TaskSchedulerService.ParseSourceKey(key);
        AddLog($"键位可视化 [{(source is null ? key : InputNameMapper.GetSourceName(source))}] 已{(visible ? "开启" : "关闭")}。");
    }

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
