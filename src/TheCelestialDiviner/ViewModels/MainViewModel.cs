using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
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

    /// <summary>管理连发键方案（“从左侧键盘增删按键方案”按钮）：左键添加 / 右键取消，直至显式退出。</summary>
    AddScheme,

    /// <summary>设置全局开关热键（方案面板顶部的热键设置项）。</summary>
    SetMasterKey,

    /// <summary>设置切换方案热键（底栏“切换方案设置”项）：左键键位 / 直接按键设置，右键已设置键位取消，单次操作即完成退出。</summary>
    SetCycleKey,

    /// <summary>设置独立关闭热键（底栏“独立关闭热键”项）：交互与切换方案热键一致，
    /// 左键键位 / 直接按键设置，右键已设置键位取消（回到空键位），单次操作即完成退出。</summary>
    SetCloseKey
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
    private readonly WebOverlayService _webOverlay = new();

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
    private string _closeKeyText = "未设置";           // 独立关闭热键显示名（默认空键位）
    private string _hintText = s_defaultHint;
    private double _soundVolume = Constants.DefaultSoundVolume;
    private bool _globalVisualEnabled = true;          // 键位可视化总开关（默认开启；关闭时仅禁用显示，各键开关状态保留）
    private int _keyboardMode; // 键盘注入模式：0 普通 / 1 扫描码 / 2 消息
    private bool _ddAutoActivate;   // DD 缺驱动自动回退后挂起：自动获取完成时升回 DD 模式（用户手动切换即取消）

    /// <summary>本次启动因"崩溃熔断"把 DD 强制改回普通模式：用于在注释区给出可读说明。</summary>
    private bool _ddCrashFallback;
    private string? _ddFetchStatus;  // DD 驱动获取进度提示（注释区展示；null = 无进行中/刚结束的提示）
    private readonly System.Windows.Threading.DispatcherTimer _ddStatusTimer = new(); // 获取状态提示自动消退
    // UI 线程卡顿探针：200ms 定时器，实际间隔明显超时即说明 UI 线程被占（点击 / 拖动会同时变迟钝）。
    // 与输入管线探针互补——那个测"事件送达晚多少"，这个测"UI 线程本身有没有被堵住"。
    private readonly System.Windows.Threading.DispatcherTimer _uiProbeTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly Stopwatch _uiProbeSw = new();
    private int _lastUiStallLogTick;
    private int _activeProfile;                        // 方案面板当前档位（0/1/2 ↔ ①②③）
    private bool _pickingActive;                       // 键位录入选择态
    private PickKind _pickingKind = PickKind.None;     // 键位录入选择态用途
    private TriggerMode _selectedMode = TriggerMode.Toggle; // 方案面板当前标签模式
    private ToggleSection _selectedSection = ToggleSection.Normal; // 方案面板当前开关分区
    private ToggleSection _pickingSection = ToggleSection.Normal;  // 本次选择态录入的分区快照
    private InputSource? _dualFirstPick;               // 双宏两步录入：已选的第 1 键
    private string _pickingPromptText = "请选择按键";  // 选择态提示条文字
    private int _lastPickingExitTick;                  // 最近一次退出选择态的时刻（Environment.TickCount，退出冷却防陈旧点击重进）
    private volatile bool _masterKeyHeld;              // 总开关键按下沿标记（仅钩子线程读写；按住自动重复只响应第一次，抬起复位）
    private volatile bool _cycleKeyHeld;               // 切换方案热键按下沿标记（同上）
    private volatile bool _closeKeyHeld;               // 独立关闭热键按下沿标记（同上）
    private bool _pickingActiveMirror;                 // 选择态的钩子线程镜像（Volatile.Read/Write 访问；PickingActive 变更时同步，调度器分流判定用）
    private int _lastMasterUiLagLogTick;               // 总开关 UI 呈现延迟告警限频（Environment.TickCount）

    /// <summary>全局总开关提示语音服务（开启 = “启动”，关闭 = “关闭”）。</summary>
    private readonly SoundCueService _soundCue = new();

    /// <summary>键盘注入模式变更通知（导入配置后由 VM 触发，UI 回填下拉框）。</summary>
    public event Action<int>? KeyboardModeChanged;

    /// <summary>
    /// 驱动引导获取确认请求（View 弹 Yes/No 框）：返回用户是否同意自动下载安装。
    /// 由用户发起官方渠道下载，程序仅做下载器（DD：闭源不分发；Interception：官方 LGPL 原版）。
    /// </summary>
    public event Func<string, bool>? DdDriverFetchConfirmRequested;

    /// <summary>
    /// 「选哪条驱动路线」导引请求（View 弹选择窗口）：
    /// 返回 4 = Interception / 3 = DD / 0 = 暂用普通模式 / -1 = 直接关窗（等同于暂不）。
    /// 只在从未做过导引时调用一次，见 <see cref="RunDriverAdvisorOnce"/>。
    /// </summary>
    public event Func<SystemDriverAdvisor.Advice, int>? DriverChoiceRequested;

    /// <summary>方案档位数量（①②③④）。</summary>
    public const int ProfileCount = AppConfig.ProfileCount;

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
            UpdateHotkeyHighlight();
            RefreshSchemeList();
            ApplyConfigToScheduler();
            SyncVisualizerRegistry();
            SaveConfig();
            ApplyAppearance();   // 档位外观随切换：配色 / 图标 / 应用名 / 键帽配色 / 注释区公告
            AddLog($"已切换到方案{ProfileDisplayName(index)}。");
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

    /// <summary>四个方案档位的自定义名称快照（空串 = 未命名，用默认序号）。</summary>
    public IReadOnlyList<string> ProfileNames => _config.ProfileNames;

    /// <summary>档位自定义名（未命名 / 越界返回空串）。</summary>
    public string ProfileName(int index) =>
        index >= 0 && index < _config.ProfileNames.Count ? _config.ProfileNames[index] : "";

    /// <summary>档位显示名：自定义名优先，未命名回退默认序号 ①②③④
    /// （切换方案热键的提示键帽、日志与冲突提示统一用它）。</summary>
    public string ProfileDisplayName(int index)
    {
        var name = ProfileName(index);
        return string.IsNullOrWhiteSpace(name) ? ProfileLabel(index) : name;
    }

    /// <summary>档位标签文本：未命名 = "方案①"（原样式），已命名 = 自定义名。</summary>
    public string ProfileTabText(int index)
    {
        var name = ProfileName(index);
        return string.IsNullOrWhiteSpace(name) ? $"方案{ProfileLabel(index)}" : name;
    }

    /// <summary>
    /// 应用四个档位的方案定制（「选项」→「方案定制」对话框确定时调用）：
    /// 档位名称 + 各档位外观（主题预设 / 默认主题下的图标、应用名、键帽配色、三色），
    /// 统一归一化（去空白、限长、校验图标键 / 配色名 / 色值）后落盘，并立即按当前档位换肤。
    /// </summary>
    public void SetProfileCustomization(IReadOnlyList<string> names, IReadOnlyList<ProfileAppearance> appearances)
    {
        var appliedNames = new List<string>(ProfileCount);
        var appliedLooks = new List<ProfileAppearance>(ProfileCount);
        for (var i = 0; i < ProfileCount; i++)
        {
            appliedNames.Add(i < names.Count ? names[i] ?? "" : "");
            appliedLooks.Add(i < appearances.Count && appearances[i] is { } a ? a.Clone() : new ProfileAppearance());
        }
        _config.ProfileNames = appliedNames;
        _config.ProfileAppearances = appliedLooks;
        _config.NormalizeProfileNames();
        _config.NormalizeProfileAppearances();
        SaveConfig();
        OnPropertyChanged(nameof(ProfileNames));   // 档位标签文本 / 悬停说明刷新
        ApplyAppearance();                          // 当前档位整套外观（含键帽配色 / 图标 / 标题 / 公告）
        AddLog("方案定制已更新：" + string.Join(" / ", Enumerable.Range(0, ProfileCount)
            .Select(i => $"{ProfileTabText(i)} → {SectThemes.ByIndex(_config.ProfileAppearances[i].Theme).DisplayName}")));
    }

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
        // Web 叠加源事件流：与悬浮层渲染同一份合并后数据（任意线程触发，服务内部加锁）。
        _visualizer.WebSnapshot += (keys, bumps) => _webOverlay.SetSnapshot(keys, bumps);
        _visualizer.WebPulse += (label, holdMs, count) => _webOverlay.BroadcastPulse(label, holdMs, count);
        _visualizer.WebReminder += visible => _webOverlay.SetReminder(visible);
        // 桌面悬浮层静音联动：客户端在线（且配置允许）→ 静音——前台游戏 + OBS 捕获下
        // 分层窗口与 DWM 争抢会秒级卡顿（2026-10-24 日志实测心跳停 10s），而录制画面的
        // 键帽由浏览器源渲染不受影响；客户端全部离线 → 恢复。事件在 ThreadPool 线程触发。
        _webOverlay.ClientCountChanged += () =>
        {
            var mute = _webOverlay.ClientCount > 0 && _config.WebOverlayMuteDesktop;
            _visualizer.SetDesktopMuted(mute);
        };

        _config = _configService.Load();
        MigrateProfileCycleDefault();   // v1.8.2：切换方案热键默认值改为 F10（不覆盖已自定义的）
        SyncProfileRuntime();
        // 主题：手动切换过夜间/白天则固定，否则跟随系统深浅；强调色随当前档位的方案定制外观。
        var themeDark = _config.NightMode || (_config.ThemeFollowSystem && ThemeHelper.IsDarkMode());
        (App.Current as App)?.ApplyTheme(themeDark, CurrentLook);
        // 当前档位外观的应用图标 / 键帽配色 / 公告源：悬浮层创建前先登记静态量，创建后即生效。
        _visualizer.SetSectIcon(CurrentLook.AppIconUri);
        _webOverlay.SetAppIcon(CurrentLook.AppIconUri);   // Web 叠加源提醒键帽图标随启动同步
        _config.KeycapScheme = KeycapSchemes.IsKnown(CurrentLook.KeycapScheme) ? CurrentLook.KeycapScheme : "Pansy";
        _appliedNoticeResource = CurrentLook.NoticeResourceName;
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
        // Web 叠加源：令牌首次生成 + 按配置启动（启动时序与悬浮窗解耦，服务自持线程）。
        if (string.IsNullOrEmpty(_config.WebOverlayToken))
        {
            _config.WebOverlayToken = Guid.NewGuid().ToString("N");
            SaveConfig();
        }
        if (_config.WebOverlayEnabled) StartWebOverlay();
        Views.KeycapOverlayWindow.ApplyScheme(KeycapSchemes.Resolve(_config.KeycapScheme));
        _webOverlay.SetScheme(KeycapSchemes.Resolve(_config.KeycapScheme));   // Web 叠加源配色随启动同步
        if (_config.VisualizerLeft is { } l && _config.VisualizerTop is { } t)
            _visualizer.SetPosition(l, t);
        _visualizer.SetOpacity(_config.VisualizerOpacity);   // 键帽透明度（窗口未创建时暂存，AttachOverlay 后应用）
        _webOverlay.SetOpacity(_config.VisualizerOpacity);   // Web 叠加源透明度随启动同步
        // 调度器字段默认为启用，须与配置中的总开关状态同步，
        // 否则冷启动时横幅显示"已关闭"而方案实际处于待触发状态。
        _scheduler.SetMasterEnabled(_globallyEnabled);
        _masterKeyText = _config.GlobalSwitch.HasKey
            ? InputNameMapper.GetKeyName(_config.GlobalSwitch.VirtualKey, _config.GlobalSwitch.Extended)
            : "未设置";
        _cycleKeyText = _config.ProfileCycle.HasKey
            ? InputNameMapper.GetKeyName(_config.ProfileCycle.VirtualKey, _config.ProfileCycle.Extended)
            : "未设置";
        _closeKeyText = _config.IndependentClose.HasKey
            ? InputNameMapper.GetKeyName(_config.IndependentClose.VirtualKey, _config.IndependentClose.Extended)
            : "未设置";
        _soundVolume = Compat.Clamp(_config.SoundVolume, 0, 100);
        _soundCue.Volume = _soundVolume / 100.0;
        _soundCue.ApplyTheme(CurrentLook.Theme, ThemeSoundFor(CurrentLook.Theme));   // 主题语音方案（高松灯 / 墨提斯 + 按主题自定义）

        // 调度器日志 → UI 日志面板（钩子线程触发，封送到 UI 线程）。
        _scheduler.Log += msg =>
            Application.Current?.Dispatcher.BeginInvoke(() => AddLog(msg));

        // 命令。
        ImportCommand = RelayCommand.Create(() => ImportRequested?.Invoke());
        ExportCommand = RelayCommand.Create(() => ExportRequested?.Invoke());
        StartPickingCommand = RelayCommand.Create(StartPicking);
        SetMasterKeyPickingCommand = RelayCommand.Create(StartMasterKeyPicking);
        SetCycleKeyPickingCommand = RelayCommand.Create(StartCycleKeyPicking);
        SetCloseKeyPickingCommand = RelayCommand.Create(StartCloseKeyPicking);
        SourceClickedCommand = new RelayCommand(p =>
        {
            if (p is KeySourceViewModel svm && !svm.IsSpacer && svm.Source is not null)
                HandleSourceClicked(svm);
        });
        ToggleGlobalCommand = RelayCommand.Create(ToggleGlobalEnabled);
        ToggleGlobalVisualCommand = RelayCommand.Create(() => GlobalVisualEnabled = !GlobalVisualEnabled);
        ToggleStatusReminderCommand = RelayCommand.Create(() => StatusReminderEnabled = !StatusReminderEnabled);
        ToggleWebOverlayCommand = RelayCommand.Create(() => WebOverlayEnabled = !WebOverlayEnabled);
        ToggleDivinerVoiceCommand = RelayCommand.Create(() => DivinerVoiceEnabled = !DivinerVoiceEnabled);
        OpenVoiceSettingsCommand = RelayCommand.Create(() => VoiceSettingsRequested?.Invoke());
        CustomizeProfilesCommand = RelayCommand.Create(() => ProfileCustomizeRequested?.Invoke());
        CheckUpdateCommand = RelayCommand.Create(() => _ = RunCheckUpdateAsync());
        // 底栏「有更新」按钮（仅检测到新版本时可见）：直接弹确认框（版本 + 更新日志）。
        UpdateAvailableCommand = RelayCommand.Create(() => _ = InstallKnownUpdateAsync());
        // 「选项 → 更新日志」：弹出窗口展示全部历史版本（数据源 = 内嵌 CHANGELOG.md）。
        ShowChangelogCommand = RelayCommand.Create(() => ChangelogRequested?.Invoke());
        // 底栏设置菜单（键盘注入 / 键帽配色 / 键帽可视化）：参数沿用原下拉框语义，
        // 点击选项即热生效 + 落盘。
        SetKeyboardModeCommand = new RelayCommand(o =>
        {
            if (int.TryParse(o?.ToString(), out var mode)) KeyboardMode = mode;
        });
        // DD 驱动缺失时「键盘注入」里的获取入口：DD 模式项此时已灰显不可选，
        // 下载确认只能从这里发起（确认后后台获取，结束经 AutoFetchCompleted 重算可用态）。
        FetchDdDriverCommand = RelayCommand.Create(() =>
        {
            _ddAutoActivate = true;   // 主动点「获取 DD 驱动」= 期望 DD 模式：装好即自动启用（拒绝时内部会清掉）
            RequestDdDriverFetch();
            RefreshDdDriverState();   // 确认后 IsAutoFetchRunning 已置位 → 入口转「获取中…」并禁用
        });
        // 双驱动路线第二条：Interception（开源、官方签名驱动）。安装后必须重启，
        // 所以这里只负责"装好 + 提示重启"，不在安装后强行切模式（重启前切了也用不了）。
        InstallInterceptionDriverCommand = RelayCommand.Create(RequestInterceptionInstall);
        UninstallInterceptionDriverCommand = RelayCommand.Create(RequestInterceptionUninstall);
        SetKeycapSchemeCommand = new RelayCommand(o =>
        {
            if (o is string name) KeycapSchemeName = name;
        });
        SetVisualizerModeCommand = new RelayCommand(o =>
        {
            if (int.TryParse(o?.ToString(), out var mode)) VisualizerModeIndex = mode;
        });
        // 方案定制（「选项」首项）：四个档位的名称 + 外观（主题绑定 / 默认主题自定义）。
        CustomizeProfilesCommand = RelayCommand.Create(() => ProfileCustomizeRequested?.Invoke());
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

        // 配置落盘防抖：总开关 / 热键等高频状态变更合并写盘，避免每次翻转同步写三次文件卡 UI。
        _configSaveTimer.Tick += (_, _) =>
        {
            _configSaveTimer.Stop();
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
        // 热键键位高亮（按钮创建后；总开关键黑底 / 切换方案与独立关闭热键灰底）。
        UpdateHotkeyHighlight();

        // 崩溃熔断（必须早于模式决策）：上一次如果带着 DD 跑、且没干净收场——进程没走完退出时序，
        // 或者退出后驱动才把野指针写出去把机器蓝了——这次强制退回普通模式。
        // 不做的话每次启动都会自动再试一次 DD，表现就是"开一次蓝一次"。
        var ddCrashedBefore = DdCrashGuard.TryDetectPreviousCrash(out var ddCrashDetail);

        // 键盘注入模式：默认 DD 驱动（物理级）；配置优先，越界回退默认。
        // DD 需 dd63330.dll + 管理员权限：缺失时先回退普通模式，窗口就绪后引导用户
        // 一键从官方渠道获取（DdDriverFetchConfirmRequested，用户发起下载），
        // 完成后自动升回 DD 模式。
        // 4 = Interception（双驱动路线第二条）：需已安装驱动并重启过，缺失时只回退 + 提示，
        // 不自动弹下载（它是用户显式选择的分支，装不装由「键盘注入」里的安装入口决定）。
        _keyboardMode = _config.KeyboardMode is >= 0 and <= 4
            ? _config.KeyboardMode
            : 3;

        // 「重启后自动切换」待办（3 = DD / 4 = Interception）：把"重启后还得记得再手动选一次"这步包办。
        // 来源：①互斥弹窗里确认卸载 Interception（→3）；②首次导引里选了要装 Interception（→4）。
        // 判据一律用 IsDriverPresent：/uninstall 只删注册项，驱动要到重启才摘掉，
        // 那段窗口里 DriverInstalled 已是 false，但切过去依旧是"假状态"。
        var pendingMode = _config.PendingModeAfterReboot;
        if (pendingMode == 0 && _config.PendingDdAfterReboot) pendingMode = 3;   // 旧字段迁移
        if (pendingMode is 3 or 4)
        {
            var keepPending = false;
            var target = SystemDriverAdvisor.ModeLabel(pendingMode);
            if (pendingMode == 3 && ddCrashedBefore)
            {
                Logger.Warn("待激活的 DD 已取消：上次运行疑似由 DD 驱动异常结束。");
                FinishDdFetchStatus("上次未正常结束，已取消自动切到 DD；确认环境无碍后可手动选择。", 25);
            }
            else if (pendingMode == 3 && InterceptionDriverService.IsDriverPresent)
            {
                keepPending = true;   // 还没重启（或卸载没成）：保留待办，等真正重启后再生效
                Logger.Warn("待激活的 DD 暂缓：Interception 驱动仍在系统里（卸载后需重启才摘得掉）。");
                FinishDdFetchStatus("Interception 驱动仍在系统里：请重启电脑，重启后会自动切到 DD 驱动。", 25);
            }
            else if (pendingMode == 4 && !InterceptionDriverService.IsDriverPresent)
            {
                Logger.Warn("待激活的 Interception 已取消：重启后没检测到驱动（安装可能没成功）。");
                FinishDdFetchStatus("没检测到 Interception 驱动，自动切换已取消；可在「键盘注入」里重新安装。", 25);
            }
            else
            {
                _keyboardMode = pendingMode;
                _config.KeyboardMode = pendingMode;
                Logger.Info($"按上次的请求自动切到 {target}（重启完成，环境已满足）。");
                FinishDdFetchStatus($"已按上次的请求自动切到 {target}。", 10);
            }
            if (!keepPending)
            {
                _config.PendingModeAfterReboot = 0;
                _config.PendingDdAfterReboot = false;
                SaveConfig();
            }
        }

        if (ddCrashedBefore && _keyboardMode == DdCrashGuard.DdKeyboardMode)
        {
            _keyboardMode = 0;
            _ddAutoActivate = false;    // 不弹"获取驱动"引导：用户刚崩过，别再把他推回 DD
            _ddCrashFallback = true;
            _config.KeyboardMode = 0;   // 必须落盘：否则下次启动又会自动试 DD，等于没熔断
            _config.UseScanCodes = false;
            SaveConfig();
            Logger.Warn($"崩溃熔断：{ddCrashDetail}；本次已退回普通模式，DD 驱动需用户手动重新选择。");
        }
        else if (ddCrashedBefore)
        {
            Logger.Info($"上次运行未干净收场（{ddCrashDetail}），但本次不使用 DD，无需熔断。");
        }

        if (_keyboardMode == 3 && DdDriverService.EnsureReady() && InterceptionDriverService.IsDriverPresent)
        {
            Logger.Warn("启动：Interception 驱动已安装，DD 注入预计无效（两者在键盘栈上冲突）。");
            FinishDdFetchStatus("Interception 驱动已安装：DD 注入会失效，请先卸载它并重启再用 DD。", 30);
        }

        if (_keyboardMode == 3 && !DdDriverService.EnsureReady())
        {
            _keyboardMode = 0;
            _ddAutoActivate = true;
            Logger.Warn($"DD 驱动未就绪，先以普通模式运行，待引导获取：{DdDriverService.LastError}");
        }
        else if (_keyboardMode == 4 && !InterceptionDriverService.EnsureReady())
        {
            // Interception 不可用（未安装 / 装完没重启 / HVCI 拦下）：回退普通模式。
            // 不做自动下载：用户当初是显式选了这条分支，缺什么由「键盘注入」里的安装入口补。
            _keyboardMode = 0;
            Logger.Warn($"Interception 驱动未就绪，先以普通模式运行：{InterceptionDriverService.LastError}");
            FinishDdFetchStatus($"Interception 驱动不可用：{InterceptionDriverService.LastError}", 25);
        }
        InputSimulatorService.KeyboardMode = _keyboardMode;
        EchoGuard.Reset(RttSeedFor(_keyboardMode));   // 启动即清空陈旧名额 / 按路线设置往返窗口

        // 运行快照（启动时间 + 实际模式）：下次启动据此判断"上次是不是带着 DD 跑的"。
        DdCrashGuard.SaveRunState(_keyboardMode);

        DdDriverService.AutoFetchCompleted += OnDdAutoFetchCompleted;
        DdDriverService.AutoFetchProgress += OnDdAutoFetchProgress;
        InterceptionDriverService.PrepareCompleted += OnInterceptionPrepareCompleted;
        InterceptionDriverService.PrepareStateChanged += OnInterceptionPrepareProgress;

        // 获取状态提示自动消退（成功 8s / 硬性失败 20s）：注释区是公告的展示位，
        // 不该被一条过期的状态长期顶掉；重试期间不消退（状态本身会不断刷新）。
        _ddStatusTimer.Tick += (_, _) =>
        {
            _ddStatusTimer.Stop();
            DdFetchStatus = null;
        };

        // 熔断提示：用户必须知道"为什么突然变回普通模式"，以及怎么把 DD 要回来，
        // 否则只会觉得程序坏了。AddLog 的 Logs 集合界面上没有绑定，必须同步写注释区。
        if (_ddCrashFallback)
        {
            AddLog("上次运行未正常结束，已自动退回普通模式（避免再次蓝屏）。"
                   + "确认环境无碍后，可在「选项 → 键盘注入」重新选择 DD 驱动。");
            FinishDdFetchStatus("上次未正常结束，已退回普通模式；DD 驱动暂停自动加载。", 30);
        }

        // UI 线程卡顿探针：定时器实际间隔比设定值多出 >250ms 即记录（限频 2s）。
        // 只要这个数值大，就说明界面线程在等锁 / 等 GC / 渲染积压，而不是"输入没送到"。
        _uiProbeTimer.Tick += (_, _) =>
        {
            var late = _uiProbeSw.ElapsedMilliseconds - 200;
            _uiProbeSw.Restart();
            if (late > 250 && unchecked(Environment.TickCount - _lastUiStallLogTick) > 2000)
            {
                _lastUiStallLogTick = Environment.TickCount;
                Logger.Warn($"UI 线程卡顿 {late}ms（此时点击 / 拖动会明显迟滞）。");
            }
        };
    }

    /// <summary>
    /// DD 驱动获取进度回调（后台线程触发，封送 UI 线程）：写入 <see cref="DdFetchStatus"/>，
    /// 由 View 显示在左下注释区 —— 下载百分比、第几次失败、多久后重试都在这里，
    /// 解决"确认下载后界面长时间毫无动静、失败与重试完全不可感知"的问题。
    /// </summary>
    private void OnDdAutoFetchProgress(DdDriverService.DdFetchProgress progress)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            DdFetchStatus = progress.Message;
            RefreshDdDriverState();   // 「获取 DD 驱动…」入口的 获取中 / 可重试 状态同步
            if (progress.Stage is DdDriverService.DdFetchStage.Succeeded
                or DdDriverService.DdFetchStage.Failed)
            {
                ScheduleDdStatusClear(TimeSpan.FromSeconds(
                    progress.Stage == DdDriverService.DdFetchStage.Succeeded ? 8 : 20));
            }
        });
    }

    /// <summary>安排注释区获取状态在指定延时后清除（重复调用以最后一次为准）。</summary>
    private void ScheduleDdStatusClear(TimeSpan delay)
    {
        _ddStatusTimer.Stop();
        _ddStatusTimer.Interval = delay;
        _ddStatusTimer.Start();
    }

    /// <summary>写入获取流程的终态提示（成功 / 失败的最后一句）并安排自动消退。</summary>
    private void FinishDdFetchStatus(string message, int seconds)
    {
        DdFetchStatus = message;
        ScheduleDdStatusClear(TimeSpan.FromSeconds(seconds));
    }

    /// <summary>
    /// 引导获取 DD 驱动（UI 线程调用）：弹出确认框——告知用户本软件键盘连发完全依赖
    /// DD 驱动运作；由用户发起官方渠道下载，程序只做下载器（下载 → 解包 → 安装全程自动，
    /// 失败自动重试直至成功），不分发闭源驱动；拒绝则保持普通模式并日志提示手动途径。
    /// </summary>
    private void RequestDdDriverFetch()
    {
        // 带上具体原因：手动把驱动放进目录却仍不可用时（最常见的是拿到了 32 位版本），
        // 用户必须在这里看到"为什么"，否则只会觉得"放了没用"。
        var reason = DdDriverService.LastError is { Length: > 0 } err ? $"\n\n具体原因：{err}" : "";
        var confirmed = DdDriverFetchConfirmRequested?.Invoke(
            "本软件的键盘连发功能完全依赖 DD 驱动运作，当前未找到可用的 DD 驱动（dd63330.dll），"
            + "未安装驱动时连发可能被多数游戏过滤而无效。"
            + reason + "\n\n"
            + "DD 驱动虽由作者免费提供，但它是闭源软件，我们无权将其打包分发，"
            + "所以这一步需要由您来完成：\n\n"
            + "是否自动从 DD 官方发布渠道下载并安装（约 3.7MB，仅首次）？\n"
            + "确认后全程自动完成并启用 DD 驱动模式。\n\n"
            + "⚠ 国内访问 GitHub 很不稳定：连续失败、卡住几分钟、连上又断，都是正常现象。\n"
            + "   程序会自动重试（间隔逐次拉长，最长 60 秒一次），请让它自己跑完，不要关程序或反复点。\n\n"
            + "下载与重试进度显示在窗口左下的注释区，也可在「选项 → 键盘注入」查看获取状态。") == true;
        if (confirmed)
        {
            DdDriverService.StartAutoFetch();
            return;
        }
        _ddAutoActivate = false;
        AddLog("已保持普通模式。DD 驱动是连发功能的核心依赖，建议尽快获取："
               + "可从 ddxoft 官网（www.ddxoft.com）下载 64 位（x64）版 dd63330.dll 放到程序目录后重启，"
               + "或稍后在「选项」→「键盘注入」→「获取 DD 驱动…」重新发起。");
        // 确认框关掉后原因仍留在注释区，便于对照着改文件。
        if (DdDriverService.LastError is { Length: > 0 } kept)
            FinishDdFetchStatus($"DD 驱动不可用：{kept}", 30);
    }

    /// <summary>
    /// DD 驱动自动获取完成回调（后台线程触发，封送 UI 线程）：
    /// 仅在"配置期望 DD 但因缺驱动自动回退"的挂起态下激活——重试 EnsureReady
    /// （内部含下载完成后首次在线授权的重试），成功则经 KeyboardMode 属性
    /// 正常切换（同步模拟器 + 落盘 + 日志）。用户中途手动切换过模式则放弃。
    /// </summary>
    private void OnDdAutoFetchCompleted()
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            // 获取流程结束（成功 / 失败均会触发）：先重算菜单可用态 —— 装成功则 DD 项由灰转可选，
            // 失败则继续保持灰显（后台仍在自动重试）。
            RefreshDdDriverState();
            if (!_ddAutoActivate)
            {
                // 用户没在等 DD 模式（例如手动点了获取但当前用的是普通模式）：明确告知装好了、去哪儿切。
                if (DdDriverService.IsAvailable)
                    FinishDdFetchStatus("DD 驱动已安装，可在「选项 → 键盘注入」里选择 DD 驱动模式。", 8);
                return;
            }
            _ddAutoActivate = false;
            if (!DdDriverService.EnsureReady())
            {
                AddLog($"DD 驱动自动获取后初始化失败，可在「选项」→「键盘注入」再点一次 DD 驱动模式重试：{DdDriverService.LastError}");
                FinishDdFetchStatus($"DD 驱动已安装但初始化失败：{DdDriverService.LastError}", 20);
                return;
            }
            KeyboardMode = 3;
            FinishDdFetchStatus("DD 驱动已就绪：已启用 DD 驱动模式（物理级注入）。", 8);
        });
    }

    /// <summary>
    /// Interception 安装进度回调（后台线程触发，封送 UI 线程）：与 DD 获取共用注释区状态位，
    /// 「键盘注入」里的安装入口同步转「安装中…」。
    /// </summary>
    private void OnInterceptionPrepareProgress(InterceptionDriverService.PrepareProgress progress)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            DdFetchStatus = progress.Message;
            RefreshInterceptionDriverState();
            if (progress.Stage is InterceptionDriverService.PrepareStage.Succeeded
                or InterceptionDriverService.PrepareStage.Failed)
            {
                ScheduleDdStatusClear(TimeSpan.FromSeconds(
                    progress.Stage == InterceptionDriverService.PrepareStage.Succeeded ? 30 : 25));
            }
        });
    }

    /// <summary>
    /// Interception 安装结束（后台线程）：重算菜单可用态。
    /// 装好不等于可用——类过滤驱动要重启才挂上，所以这里只刷新状态、不切模式；
    /// 用户重启后再次选择该模式即可（配置里已经是 4 的话，重启后会自动升回）。
    /// </summary>
    private void OnInterceptionPrepareCompleted()
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            RefreshInterceptionDriverState();
            if (!InterceptionDriverService.DriverInstalled) return;
            if (InterceptionDriverService.State == InterceptionDriverService.DriverState.Ready)
            {
                FinishDdFetchStatus("Interception 驱动已就绪，可在「选项 → 键盘注入」里选择该模式。", 10);
                return;
            }
            AddLog("Interception 驱动已安装：请重启电脑，重启后才能选择该注入模式。");
            FinishDdFetchStatus("Interception 驱动已安装：请重启电脑后使用。", 30);
        });
    }

    /// <summary>
    /// 引导安装 Interception 驱动（UI 线程）：确认框说明来历（开源 / 官方 Release / LGPL）、
    /// 代价（装完必须重启、与 DD 二选一、HVCI 可能拦下），确认后后台下载安装。
    /// </summary>
    private void RequestInterceptionInstall()
    {
        var confirmed = DdDriverFetchConfirmRequested?.Invoke(
            "Interception 是一条开源的物理级注入路线（oblitum/Interception，LGPL 3.0），"
            + "驱动由官方 GitHub Release 提供、带原作者数字签名，本程序只做原版下载与安装。\n\n"
            + "代价与限制：\n"
            + "· 它替换的是系统键盘类过滤驱动，**安装完成后必须重启电脑**才生效；\n"
            + "· 与 DD 驱动二选一（同一时刻只用一条注入路线）；\n"
            + "· 开启「内存完整性 / 内核隔离」的系统可能拒绝加载该驱动。\n\n"
            + "官方文件已随程序内置（interception\\ 目录），安装不需要联网。\n\n"
            + "是否现在安装？") == true;
        if (confirmed)
        {
            InterceptionDriverService.StartPrepare();
            RefreshInterceptionDriverState();
            return;
        }
        AddLog("已取消安装 Interception 驱动。需要时可再次从「选项 → 键盘注入 → 安装 Interception 驱动…」发起。");
    }

    /// <summary>
    /// 卸载 Interception 驱动（UI 线程）：确认 → 调官方安装器 /uninstall。
    /// 卸载后如正在使用该模式，先退回普通模式（否则下一次注入直接失败，用户只会觉得"程序坏了"）。
    /// </summary>
    private void RequestInterceptionUninstall()
    {
        var confirmed = DdDriverFetchConfirmRequested?.Invoke(
            "将调用官方安装器卸载 Interception 驱动（移除键盘类过滤驱动注册）。\n\n"
            + "· 卸载后同样需要重启电脑才彻底生效；\n"
            + "· 卸载后该注入模式不可用，可改回 DD 驱动或普通模式；\n"
            + "· 只是不用了、不想重启的话，直接切到别的注入模式即可，不必卸载。\n\n"
            + "确认卸载？") == true;
        if (!confirmed) return;

        if (KeyboardMode == 4) KeyboardMode = 0;   // 先退出该模式，避免卸载后注入全失败
        var ok = InterceptionDriverService.Uninstall();
        RefreshInterceptionDriverState();
        if (ok)
        {
            AddLog("Interception 驱动已卸载：请重启电脑，重启后键盘类过滤驱动才会彻底移除。");
            FinishDdFetchStatus("Interception 驱动已卸载：请重启电脑。", 30);
            return;
        }
        var reason = InterceptionDriverService.LastError ?? "未知原因";
        AddLog($"Interception 驱动卸载未完成（{reason}）。");
        FinishDdFetchStatus($"Interception 驱动卸载未完成：{reason}", 25);
    }

    /// <summary>导引是否已在本会话跑过（防重复弹窗）。</summary>
    private bool _driverAdvisorDone;

    /// <summary>
    /// 启动后的「驱动路线导引」：探测系统版本 / 内核完整性开关 / 键盘栈上已有的过滤驱动，
    /// 判断这台机器适合哪条物理级注入路线，首次运行时给一次选择机会。
    ///
    /// 为什么需要：同一个版本号以下，DD 与 Interception 的可用性完全不同 ——
    /// Win11 25H2 + 内核完整性开启时两条都用不了，25H2 关掉开关时 Interception 更稳、
    /// DD 有 0x50 风险，24H2 及更早则是 DD 更省事（免重启）。让用户自己猜是没道理的。
    ///
    /// 只在两种情况弹窗：①从未做过导引（首次）；②当前配置的物理级路线在这台机器上明显不合适。
    /// 其余情况只写日志 + 必要时注释区一句提示，不打扰。
    /// </summary>
    public void RunDriverAdvisorOnce()
    {
        if (_driverAdvisorDone) return;
        _driverAdvisorDone = true;

        SystemDriverAdvisor.Advice advice;
        try { advice = SystemDriverAdvisor.Evaluate(); }
        catch (Exception ex)
        {
            Logger.Warn($"驱动导引：环境探测失败（跳过导引，不影响使用）：{ex.Message}");
            return;
        }

        Logger.Info("驱动导引：" + SystemDriverAdvisor.Describe(advice));
        foreach (var warning in advice.Warnings) Logger.Warn("驱动导引：" + warning);

        // 已经导引过：只在"当前路线在这台机器上明显不合适"时提示一句，不再弹窗。
        if (_config.DriverAdvisorAnswered)
        {
            if (advice.NoPhysicalRouteAvailable && KeyboardMode is 3 or 4)
            {
                FinishDdFetchStatus($"当前环境不适合物理级注入（{advice.OsName} + 内核完整性开关）：建议改用普通模式。", 30);
            }
            else if (advice.Is25H2Plus && KeyboardMode == 3)
            {
                FinishDdFetchStatus("Win11 25H2 上 DD 驱动有蓝屏（0x50）风险，建议改用 Interception 驱动。", 30);
            }
            return;
        }

        // 当前用的是 普通 / 扫描码 / 消息 的老用户：他们没在追驱动，别拿「装哪个驱动」去打断。
        // （新用户默认就是 3，会正常走到下面的判断；他们在菜单里随时能看到驱动入口。）
        if (_keyboardMode is not (3 or 4))
        {
            Logger.Info($"驱动导引：当前为 {SystemDriverAdvisor.ModeLabel(_keyboardMode)}，不弹驱动选择窗口。");
            return;
        }

        // 老用户零打扰（升级保护）：如果当前配置的路线本身就是这台机器的最优解、而且现在就能用，
        // 就不弹窗、不改变任何行为 —— 典型场景：Win10 / 24H2 + DD 库在位 + 配置就是 DD，
        // 升级前后表现必须完全一致，不能被一个「要不要换驱动」的弹窗打断。
        if (advice.RecommendedMode == _keyboardMode && IsCurrentModeViable(advice))
        {
            _config.DriverAdvisorAnswered = true;
            SaveConfig();
            Logger.Info($"驱动导引：当前已是本机最优路线（{advice.RecommendedLabel}）且可用，跳过选择窗口。");
            return;
        }

        // 首次导引：给一次选择。View 没接线（或用户直接关窗）时按暂不处理，绝不阻塞启动。
        var choice = DriverChoiceRequested?.Invoke(advice) ?? -1;
        _config.DriverAdvisorAnswered = true;
        SaveConfig();
        if (choice < 0)
        {
            AddLog($"驱动导引已跳过。本次建议：{advice.RecommendedLabel}"
                   + (advice.Reasons.Count > 0 ? $"（{advice.Reasons[0]}）" : ""));
            return;
        }
        ApplyDriverChoice(choice);
    }

    /// <summary>
    /// 当前配置的注入路线在本次环境里是否可用（只查状态，不初始化设备）：
    /// 用于判断「要不要弹导引窗口」——已经能正常用的老用户不该被打扰。
    /// </summary>
    private bool IsCurrentModeViable(SystemDriverAdvisor.Advice advice) => _keyboardMode switch
    {
        4 => advice.InterceptionPresent,
        3 => DdDriverService.IsAvailable && !advice.InterceptionPresent,
        _ => true   // 普通 / 扫描码 / 消息：不依赖任何驱动，永远可用
    };

    /// <summary>
    /// 执行导引里选中的路线：能立刻用就直接切；需要装的先装，并登记"重启后自动切换"，
    /// 免得用户装完忘了回头再选一次（重启后 DD / Interception 都不会自动生效）。
    /// </summary>
    private void ApplyDriverChoice(int mode)
    {
        switch (mode)
        {
            case 4 when InterceptionDriverService.IsAvailable:
                KeyboardMode = 4;
                break;
            case 4:
                _config.PendingModeAfterReboot = 4;
                SaveConfig();
                InterceptionDriverService.StartPrepare();
                Logger.Info("驱动导引：选择 Interception，开始安装（装完重启自动切换）。");
                AddLog("正在安装 Interception 驱动：装完请重启，重启后会自动切到 Interception 模式。");
                FinishDdFetchStatus("正在安装 Interception 驱动：装完重启后自动切换。", 20);
                break;
            case 3 when DdDriverService.IsAvailable:
                KeyboardMode = 3;
                break;
            case 3:
                _ddAutoActivate = true;   // 装好即自动启用
                RequestDdDriverFetch();
                break;
            default:
                KeyboardMode = 0;
                AddLog("驱动导引：已选择暂用普通模式（可随时在「选项 → 键盘注入」里改）。");
                break;
        }
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

    /// <summary>进入 / 退出方案管理模式（左键键位添加、再点一次取消（右键同样），Esc / 空白处 / 按钮自身退出）。</summary>
    public ICommand StartPickingCommand { get; }

    /// <summary>进入总开关键录入选择态（单次录入即完成并退出，与方案管理模式不同）。</summary>
    public ICommand SetMasterKeyPickingCommand { get; }

    /// <summary>进入切换方案热键录入选择态（左键键位 / 直接按键设置，右键已设置键位取消，单次操作即完成退出）。</summary>
    public ICommand SetCycleKeyPickingCommand { get; }

    /// <summary>进入独立关闭热键录入选择态（交互同切换方案热键：左键键位 / 直接按键设置，
    /// 右键已设置键位取消回到空键位，单次操作即完成退出）。</summary>
    public ICommand SetCloseKeyPickingCommand { get; }

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

    /// <summary>独立关闭热键显示名（未设置 = "未设置" = 空键位，总开关键回到统一启停）。</summary>
    public string CloseKeyText
    {
        get => _closeKeyText;
        private set => Set(ref _closeKeyText, value);
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
            // 镜像先行写入：选择态进出的瞬间钩子线程以镜像分流，早到的 UI 侧处理
            // 由 HandleHookDownPicking 的权威态判定兜底。
            Volatile.Write(ref _pickingActiveMirror, value);
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
                // 诊断埋点：切换开关 / 按压模式只翻可见性（不重建行），正常应在几毫秒内；
                // >30ms 说明仍有布局层面的额外开销。限频记录供定位。
                var sw = Stopwatch.StartNew();
                OnPropertyChanged(nameof(SectionTabsVisible));
                ApplySchemeFilter();
                sw.Stop();
                if (sw.ElapsedMilliseconds > 30)
                    Logger.Warn($"切换模式耗时 {sw.ElapsedMilliseconds}ms（{SchemeRows.Count} 行，仅切换可见性）。");
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
            if (Set(ref _selectedSection, value)) ApplySchemeFilter();   // 同上：只翻可见性
        }
    }

    /// <summary>当前模式 / 分区下是否有可见的连发键（控制空态提示显隐）。</summary>
    public bool HasSchemeRows => SchemeRows.Any(r => r.IsVisible);

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

    /// <summary>当前独立关闭热键对应的输入源（未设置返回 null）。</summary>
    public InputSource? IndependentCloseSource => _config.IndependentClose.HasKey
        ? new InputSource
        {
            Kind = InputKind.Keyboard,
            VirtualKey = _config.IndependentClose.VirtualKey,
            Extended = _config.IndependentClose.Extended
        }
        : null;

    /// <summary>是否已设置独立关闭热键（已设置 = 总开关键只负责开启，关闭交给独立关闭热键）。</summary>
    public bool HasIndependentCloseKey => _config.IndependentClose.HasKey;

    // ---------- 运行时初始化 ----------
    /// <summary>
    /// 运行时初始化：提升定时器分辨率（连发间隔精度依赖，无状态栏展示）、安装钩子。
    /// 在 UI 线程的窗口 Loaded 阶段调用一次。
    /// </summary>
    public void InitializeRuntime(bool isAdmin)
    {
        IsAdmin = isAdmin;
        PermissionText = isAdmin ? "权限：管理员" : "权限：非管理员（功能受限）";

        // 定时器分辨率**按需持有**：只有真正在连发时才把系统定时器提到 1ms。
        // 常驻 1ms 会让整机按约 1000Hz 出定时器中断——空闲时没有任何 Sleep(1) 级需求，
        // 白白烧 CPU / 耗电，界面与其它程序的调度也会跟着变钝。
        _scheduler.FiringChanged += firing =>
        {
            if (firing) _timerResolution.Start();
            else _timerResolution.Stop();   // 回到系统默认分辨率（重复调用安全）
        };

        // UI 卡顿探针随运行时启动（诊断用，200ms 一次，开销可忽略）。
        _uiProbeSw.Restart();
        _uiProbeTimer.Start();

        HookInstalled = _hookService.Install();
        HookStatusText = HookInstalled
            ? "钩子：已安装"
            : "钩子：安装失败（请以管理员身份运行）";
        if (!HookInstalled)
            AddLog("低级钩子安装失败，请右键以管理员身份运行本程序。");

        AddLog($"初始化完成：总开关{(_globallyEnabled ? "已开启" : "已关闭（按 [" + _masterKeyText + "] 开启）")}，"
               + $"方案 {_config.Schemes.Count} 个。");

        // 公告更新检查：后台查询 Gitee，成功且内容有变化才覆盖注释区默认内容（失败保持旧内容）。
        _ = CheckNoticeUpdateAsync();

        // 更新预检：后台查询 Gitee Release，发现新版本点亮「检查更新」小红点（失败静默）。
        _ = CheckUpdateDotAsync();

        // DD 驱动缺失引导：窗口可见后弹出确认（用户发起官方渠道下载，程序仅做下载器）。
        if (_ddAutoActivate && !DdDriverService.IsAutoFetchRunning)
            RequestDdDriverFetch();
    }

    // ---------- 钩子事件接入（由 View 绑定；钩子线程直通调用，不经 UI 封送） ----------
    /// <summary>
    /// 输入源按下（钩子线程调用）：调度器路径零封送直通——连发启停不再排在 UI 线程
    /// 积压工作之后（高负载下松键即停）；仅 UI 动作（总开关 / 档位切换 / 键位录入）
    /// 封送回 UI 线程。优先独立关闭热键 → 总开关键 → 切换方案热键 → 键位录入选择态 → 调度器。
    /// </summary>
    public void HandleHookDown(InputSource source)
    {
        // 独立关闭热键：先于总开关键判定（两者互斥，同一键位不会被同时占用）。
        // 只负责关闭——已关闭时不重复关闭（不播语音、不刷日志）。
        if (IndependentCloseSource is { } xk && xk.Equals(source))
        {
            if (_closeKeyHeld) return;
            _closeKeyHeld = true;
            if (KeyRecorder.IsAnyRecording || Volatile.Read(ref _pickingActiveMirror)) return;
            CloseGlobalEnabledFromHook();
            return;
        }

        if (GlobalSwitchSource is { } gk && gk.Equals(source))
        {
            // 按下沿判定：按住时 OS 会持续投递 KEYDOWN（自动重复，最快约 31ms 一次），
            // 若每次都翻转，一次长按会把总开关来回拨几十次、最终状态随机，体感“按了没反应”。
            // 只响应第一次按下，抬起（HandleHookUp）复位。沿标记仅钩子线程读写。
            if (_masterKeyHeld) return;
            _masterKeyHeld = true;
            // 录制 / 选择态不响应（避免把总开关键录为方案源或热键时误切总开关）。
            // volatile 直读判定，钩子线程即可完成，不等 UI。
            if (KeyRecorder.IsAnyRecording || Volatile.Read(ref _pickingActiveMirror)) return;
            // 已设置独立关闭热键 → 总开关键只负责开启（已开启时按下什么都不做），
            // 关闭统一交给独立关闭热键；未设置时维持原来的单键启停语义。
            if (HasIndependentCloseKey)
            {
                // 已开启时不重复开启：给一句可见反馈（否则用户会以为热键失灵）；
                // 每次按下只出现一条（按下沿标记已过滤自动重复）。
                if (_scheduler.MasterEnabled)
                {
                    var closeName = _config.IndependentClose.HasKey
                        ? InputNameMapper.GetKeyName(_config.IndependentClose.VirtualKey,
                            _config.IndependentClose.Extended)
                        : "独立关闭热键";
                    RunOnUi(() => AddLog($"总开关已开启：全局开关热键只负责开启，关闭请按 [{closeName}]。"));
                    return;
                }
                ApplyGlobalEnabled(true);
            }
            else
            {
                ToggleGlobalEnabledFromHook();
            }
            return;
        }

        if (ProfileCycleSource is { } ck && ck.Equals(source))
        {
            // 同总开关键：按住自动重复只切换一次档位。
            if (_cycleKeyHeld) return;
            _cycleKeyHeld = true;
            // 切换方案热键：按下按顺序切换非空方案档位。录入选择态 / 录制期间不响应
            // （该键已被占用，不能录为方案源；也避免录入切换方案热键时误触切换）。
            RunOnUi(() =>
            {
                if (!KeyRecorder.IsAnyRecording && !PickingActive)
                    CycleToNextProfile();
            });
            return;
        }

        if (Volatile.Read(ref _pickingActiveMirror))
        {
            // 管理模式逻辑整体依赖 UI 状态（提示条 / 命中测试 / 录入），封送 UI 线程处理；
            // 选择态是非常规战斗路径，多一跳封送延迟无关紧要。
            RunOnUi(() => HandleHookDownPicking(source));
            return;
        }

        _scheduler.HandleSourceDown(source);   // 调度器自带 _gate 锁，明确支持钩子线程调用
    }

    /// <summary>选择态按下处理（UI 线程）：键盘按键直接录入（Esc 退出；双宏成对录入中
    /// 先放弃第 1 键，偶数态才退出）；鼠标按下交 View 命中测试（落在键鼠图块上 →
    /// 右键取消 / 其余键录入，否则请求退出）；滚轮滚动不打断选择态。</summary>
    private void HandleHookDownPicking(InputSource source)
    {
        // 镜像早到兜底（进/出选择态瞬间穿越的钩子事件）：以 UI 线程权威态为准。
        if (!PickingActive) return;

        // 注：曾加过"吞掉进入选择态那一次点击"的保护，是误判——WPF 的 Button.Click 在**抬起**时触发，
        // 进入时的按下早已在管理态生效之前处理完，不存在晚到；那个保护只会吞掉用户**下一次**按下
        // （进入后很快点空白就不退出）。已移除。
        if (source.Kind == InputKind.Mouse)
            Logger.Info($"管理态收到鼠标按下（{source.Mouse}）。");

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
        }
    }

    /// <summary>输入源释放（钩子线程直通调用）：复位热键按下沿标记并转交调度器。
    /// Hold 连发的松键即停走此路径，不再受 UI 线程繁忙影响。</summary>
    public void HandleHookUp(InputSource source)
    {
        // 热键按下沿标记随抬起复位（低级钩子全局投递，抬起事件不会丢）。
        if (GlobalSwitchSource is { } gk && gk.Equals(source)) _masterKeyHeld = false;
        if (ProfileCycleSource is { } ck && ck.Equals(source)) _cycleKeyHeld = false;
        if (IndependentCloseSource is { } xk && xk.Equals(source)) _closeKeyHeld = false;
        _scheduler.HandleSourceUp(source);
    }

    /// <summary>钩子线程 → UI 线程封送（应用退出中 Application.Current 为 null 时静默丢弃）。</summary>
    private static void RunOnUi(Action action) =>
        Application.Current?.Dispatcher.BeginInvoke(action);

    // ---------- 全局总开关 ----------
    /// <summary>
    /// 切换总开关（横幅、控件透明度、调度器、提示语音、配置持久化）。
    /// UI 入口（按钮 / 托盘菜单；UI 线程调用）。
    /// 开启播“启动”、关闭播“关闭”；关闭时调度器立即停止所有连发任务。
    /// </summary>
    public void ToggleGlobalEnabled() => ApplyGlobalEnabled(!GloballyEnabled);

    /// <summary>总开关热键入口（钩子线程调用）：调度器立即生效，不等 UI——游戏窗口最大化
    /// 时 UI 线程可能被悬浮窗 Hide/Show 的 DWM 同步长时间卡住，旧实现整条启停链都排在
    /// 积压之后（体感“按了没反应”，连按数次还会在 UI 恢复后爆发回放、停在错误状态）。
    /// 以调度器当前态为翻转基准：连按次序与最终态始终和实际按键一一对应。</summary>
    private void ToggleGlobalEnabledFromHook() => ApplyGlobalEnabled(!_scheduler.MasterEnabled);

    /// <summary>独立关闭热键入口（钩子线程调用）：只关闭不开启——已关闭时直接返回，
    /// 避免连按 / 自动重复把"关闭"再执行一遍（语音与日志不重复）。
    /// 同样以调度器当前态为基准，不等 UI 线程。</summary>
    private void CloseGlobalEnabledFromHook()
    {
        if (!_scheduler.MasterEnabled) return;
        ApplyGlobalEnabled(false);
    }

    /// <summary>应用总开关目标态：调度器与配置字段就地生效（任意线程安全）；
    /// UI 呈现（横幅 / 控件 / 语音 / 落盘 / 日志）封送 UI 线程（已在 UI 线程则直接执行）。</summary>
    private void ApplyGlobalEnabled(bool enabled)
    {
        _scheduler.SetMasterEnabled(enabled);
        _config.GlobalSwitch.Enabled = enabled;
        var requestedTick = Environment.TickCount;   // UI 积压探针基准（ApplyUi 实际执行时刻之差）

        void ApplyUi()
        {
            // 诊断埋点：请求 → UI 实际执行的延迟（>300ms = UI 线程积压，
            // 语音播报与横幅随之迟滞——热键状态本身已即时生效）。限频记录。
            var lag = unchecked(Environment.TickCount - requestedTick);
            if (lag > 300 && unchecked(Environment.TickCount - _lastMasterUiLagLogTick) > 2000)
            {
                _lastMasterUiLagLogTick = Environment.TickCount;
                Logger.Warn($"总开关 UI 呈现延迟 {lag}ms（UI 线程积压）。");
            }

            GloballyEnabled = enabled;   // 横幅 / 控件 / 提醒键帽联动（含 _visualizer.SetMasterSwitch）
            if (enabled) _soundCue.PlayStart(DivinerVoiceEnabled); else _soundCue.PlayStop();
            SaveConfigDeferred();   // 防抖落盘：热键长按 / 快速连按时不反复同步写文件
            AddLog(enabled ? "总开关已开启（启动）。" : "总开关已关闭，所有连发已停止。");
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) ApplyUi();
        else dispatcher.BeginInvoke(ApplyUi);
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
            UpdateHotkeyHighlight();
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

        // 冲突验证：不能与切换方案热键 / 独立关闭热键相同（同一键位两个语义会互相抢占）。
        if (ProfileCycleSource is { } ck && ck.Equals(key))
        {
            AddLog($"设置失败：[{InputNameMapper.GetSourceName(key)}] 已被切换方案热键占用。");
            return false;
        }
        if (IndependentCloseSource is { } xk && xk.Equals(key))
        {
            AddLog($"设置失败：[{InputNameMapper.GetSourceName(key)}] 已被独立关闭热键占用。");
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
        UpdateHotkeyHighlight();
        AddLog($"总开关键已设置为 [{InputNameMapper.GetSourceName(key)}]。"
               + (HasIndependentCloseKey
                   ? "（已设置独立关闭热键：本键只负责开启，关闭由独立关闭热键负责。）"
                   : ""));
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
            UpdateHotkeyHighlight();
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
        if (IndependentCloseSource is { } xk && xk.Equals(key))
        {
            AddLog($"设置失败：[{InputNameMapper.GetSourceName(key)}] 已被独立关闭热键占用。");
            return false;
        }

        // 冲突验证：不能与任一方案档位的连发键相同（四个档位全查，
        // 否则切换到该档位后按下此键时切换与连发互相抢占）。
        var keyStr = TaskSchedulerService.BuildSourceKey(key);
        for (var i = 0; i < ProfileCount; i++)
        {
            if (!_config.Profiles[i].ContainsKey(keyStr)) continue;
            AddLog($"设置失败：[{InputNameMapper.GetSourceName(key)}] 已被方案{ProfileDisplayName(i)}的连发键占用。");
            return false;
        }

        _config.ProfileCycle.HasKey = true;
        _config.ProfileCycle.VirtualKey = key.VirtualKey;
        _config.ProfileCycle.Extended = key.Extended;
        CycleKeyText = InputNameMapper.GetSourceName(key);
        OnPropertyChanged(nameof(ProfileCycleSource));
        SaveConfig();
        UpdateHotkeyHighlight();
        AddLog($"切换方案热键已设置为 [{InputNameMapper.GetSourceName(key)}]：按下按 ①→②→③→④ 顺序切换非空方案档位。");
        return true;
    }

    /// <summary>
    /// 设置独立关闭热键（null 表示清除 = 回到空键位，恢复"全局开关热键统一键位启停"）。
    /// 占用冲突（总开关键 / 切换方案热键 / 任一档位的连发键）时返回 false 且不修改。
    /// </summary>
    public bool SetIndependentCloseKey(InputSource? key)
    {
        if (key is null)
        {
            _config.IndependentClose.HasKey = false;
            CloseKeyText = "未设置";
            OnPropertyChanged(nameof(IndependentCloseSource));
            OnPropertyChanged(nameof(HasIndependentCloseKey));
            SaveConfig();
            UpdateHotkeyHighlight();
            AddLog("独立关闭热键已取消（空键位）：全局开关热键恢复统一键位启停。");
            return true;
        }

        if (key.Kind != InputKind.Keyboard)
        {
            AddLog("独立关闭热键仅支持键盘键（鼠标 / 滚轮不可用）。");
            return false;
        }
        if (GlobalSwitchSource is { } gk && gk.Equals(key))
        {
            AddLog($"设置失败：[{InputNameMapper.GetSourceName(key)}] 已被全局开关热键占用。");
            return false;
        }
        if (ProfileCycleSource is { } ck && ck.Equals(key))
        {
            AddLog($"设置失败：[{InputNameMapper.GetSourceName(key)}] 已被切换方案热键占用。");
            return false;
        }

        // 与切换方案热键同一套占用校验：四个方案档位的连发键全查，
        // 否则切到该档位后按下此键时「关闭」与「连发」互相抢占。
        var keyStr = TaskSchedulerService.BuildSourceKey(key);
        for (var i = 0; i < ProfileCount; i++)
        {
            if (!_config.Profiles[i].ContainsKey(keyStr)) continue;
            AddLog($"设置失败：[{InputNameMapper.GetSourceName(key)}] 已被方案{ProfileDisplayName(i)}的连发键占用。");
            return false;
        }

        _config.IndependentClose.HasKey = true;
        _config.IndependentClose.VirtualKey = key.VirtualKey;
        _config.IndependentClose.Extended = key.Extended;
        CloseKeyText = InputNameMapper.GetSourceName(key);
        OnPropertyChanged(nameof(IndependentCloseSource));
        OnPropertyChanged(nameof(HasIndependentCloseKey));
        SaveConfig();
        UpdateHotkeyHighlight();
        var masterName = _config.GlobalSwitch.HasKey
            ? InputNameMapper.GetKeyName(_config.GlobalSwitch.VirtualKey, _config.GlobalSwitch.Extended)
            : "未设置";
        AddLog($"独立关闭热键已设置为 [{InputNameMapper.GetSourceName(key)}]："
               + $"全局开关热键 [{masterName}] 此后只负责开启，关闭统一由本键负责。");
        return true;
    }

    /// <summary>
    /// 切换方案热键按下：按 ①→②→③→④ 顺序切到下一个非空方案档位（空档位跳过，环绕循环）；
    /// 其余档位均为空时不切换并提示。实际切换时播放“切换”提示语音，并在键帽可视化区域
    /// 显示目标方案代号键帽（与状态提醒键帽同语义：无视键位可视化总开关独立运行，触发后自然淡出）。
    /// </summary>
    public void CycleToNextProfile()
    {
        for (var step = 1; step < ProfileCount; step++)
        {
            var index = (_activeProfile + step) % ProfileCount;
            if (_config.Profiles[index].Count == 0) continue;
            ActiveProfile = index;
            _soundCue.PlayCycle();
            _visualizer.ShowCycleKeycap(ProfileDisplayName(index));   // 提示键帽用档位自定义名（未命名回退序号）
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

    // ---------- 注释区公告（Notice.md / Notice2.md：内嵌兜底 + Gitee 远端更新，按主题各一份） ----------
    /// <summary>注释区默认公告内容：按当前档位主题取该主题的远端缓存，未同步过时用该主题的内嵌公告。</summary>
    public string NoticeContent
    {
        get
        {
            var look = CurrentLook;
            var cached = CachedNotice(look);
            return string.IsNullOrEmpty(cached) ? NoticeService.EmbeddedNotice(look.Theme) : cached;
        }
    }

    /// <summary>主题各自的公告缓存（默认 / 衍天 → NoticeContent，莫问 → MoWenNoticeContent）。</summary>
    private string CachedNotice(ResolvedAppearance look) =>
        look.UsesMoWenNotice ? _config.MoWenNoticeContent : _config.NoticeContent;

    /// <summary>写入主题的公告缓存。</summary>
    private void SetCachedNotice(ResolvedAppearance look, string content)
    {
        if (look.UsesMoWenNotice) _config.MoWenNoticeContent = content;
        else _config.NoticeContent = content;
    }

    /// <summary>
    /// 查询当前主题的公告更新（Gitee raw，后台执行不阻塞启动）：拉取成功且内容与当前不同，
    /// 覆盖该主题缓存并落盘 + 刷新注释区；没更新 / 失败静默保持旧默认内容。
    /// 切换档位（主题变化）时同样调用（期间主题再变则丢弃本次结果）。
    /// </summary>
    public async Task CheckNoticeUpdateAsync()
    {
        try
        {
            var look = CurrentLook;
            var remote = await NoticeService.FetchLatestAsync(look.NoticeRemoteUrl);
            if (remote is null || !ReferenceEquals(look.Theme, CurrentLook.Theme)) return;   // 主题已变 → 丢弃
            if (remote == NoticeContent) return;
            SetCachedNotice(look, remote);
            SaveConfig();
            OnPropertyChanged(nameof(NoticeContent));   // View 订阅刷新注释区默认内容
            AddLog($"注释区公告已同步远端更新（{look.Theme.DisplayName}）。");
        }
        catch (Exception ex)
        {
            // 双保险：公告检查任何异常不得影响主流程（FetchLatestAsync 内部已兜底）。
            Logger.Error("公告更新检查异常。", ex);
        }
    }

    // ---------- 检查更新（Gitee Release：检查 → 确认 → 下载安装自动重启） ----------
    private bool _checkingUpdate;

    /// <summary>已确认到的新版本（启动预检 / 手动检查写入）：底栏「有更新」按钮据此直接弹确认框，
    /// 不必重新查询远端；确认无更新时置 null。</summary>
    private UpdateCheck? _latestUpdate;

    /// <summary>检查 / 下载更新进行中（按钮禁用并显示“检查中…”）。</summary>
    public bool CheckingUpdate
    {
        get => _checkingUpdate;
        private set => Set(ref _checkingUpdate, value);
    }

    // 默认熄灭：Visibility.Visible == 0，写成无初始化的字段会默认"亮着"，
    // 于是即使已是最新版、启动预检也没发现更新，小红点照样挂在那里（只有手动检查一次才灭）。
    private Visibility _updateDotVisibility = Visibility.Collapsed;

    /// <summary>「检查更新」菜单项的小红点可见性：仅当**确认存在**更新的版本时才亮起
    /// （启动后台预检 / 手动检查发现新版 → Visible；已是最新 / 远端不可用 → Collapsed）。
    /// 更新安装重启后远端 = 当前，自然不再亮。</summary>
    public Visibility UpdateDotVisibility
    {
        get => _updateDotVisibility;
        private set => Set(ref _updateDotVisibility, value);
    }

    /// <summary>更新确认弹窗请求（View 弹 Yes/No 框列出远端版本与说明，返回是否继续）。</summary>
    public event Func<UpdateCheck, bool>? UpdateConfirmRequested;

    /// <summary>检查 / 更新结果消息弹窗请求（View 弹信息框；参数 = 标题、内容）。</summary>
    public event Action<string, string>? UpdateMessageRequested;

    /// <summary>更新包就绪并已启动自更新脚本 → 请求走正常退出时序（脚本接管覆盖与重启）。</summary>
    public event Action? UpdateRestartRequested;

    /// <summary>
    /// 检查更新主流程：查询 Gitee 最新 Release → 有新版本经确认后下载 → 解包校验 →
    /// 启动自更新脚本 → 请求退出（脚本在进程退出后覆盖安装目录并重启）。
    /// 没更新 / 远端不可用 / 用户取消 / 任何失败均停在当前版本。
    /// </summary>
    private async Task RunCheckUpdateAsync()
    {
        if (CheckingUpdate) return;
        CheckingUpdate = true;
        try
        {
            AddLog("正在检查更新（Gitee Release）…");
            var probe = await UpdateService.FetchLatestAsync();
            if (probe.Check is not { } check)
            {
                AddLog($"检查更新失败：{probe.Failure}");
                UpdateMessageRequested?.Invoke("检查更新",
                    $"未能从 Gitee 获取新版本：{probe.Failure}。\n当前版本 v{UpdateService.CurrentVersion.ToString(3)}。");
                return;
            }
            if (check.Version <= UpdateService.CurrentVersion)
            {
                AddLog($"检查更新：已是最新版本（远端 {check.TagName}）。");
                _latestUpdate = null;
                UpdateDotVisibility = Visibility.Collapsed;   // 确认无更新：熄灭小红点 / 收起「有更新」
                UpdateMessageRequested?.Invoke("检查更新",
                    $"当前已是最新版本 v{UpdateService.CurrentVersion.ToString(3)}。");
                return;
            }

            // 有新版本 → 记录（底栏「有更新」按钮的数据源）+ 点亮小红点，弹框确认。
            _latestUpdate = check;
            UpdateDotVisibility = Visibility.Visible;
            await ConfirmAndInstallAsync(check);
        }
        catch (Exception ex)
        {
            Logger.Error("检查 / 安装更新失败。", ex);
            UpdateMessageRequested?.Invoke("检查更新", $"更新失败：{ex.Message}");
        }
        finally
        {
            CheckingUpdate = false;   // 重启路径下进程即将退出，复位无实际影响
        }
    }

    /// <summary>
    /// 底栏「有更新」按钮：用已确认到的版本直接走"确认 → 下载 → 重启"，
    /// 不重新查询远端（启动预检 / 检查更新已经拿到版本与更新日志）。
    /// 极端情况下没有缓存的版本（例如按钮刚出现就被点），退回完整检查流程。
    /// </summary>
    private async Task InstallKnownUpdateAsync()
    {
        if (CheckingUpdate) return;
        if (_latestUpdate is not { } check)
        {
            await RunCheckUpdateAsync();
            return;
        }
        CheckingUpdate = true;
        try
        {
            await ConfirmAndInstallAsync(check);
        }
        catch (Exception ex)
        {
            Logger.Error("安装更新失败。", ex);
            UpdateMessageRequested?.Invoke("检查更新", $"更新失败：{ex.Message}");
        }
        finally
        {
            CheckingUpdate = false;
        }
    }

    /// <summary>
    /// 确认并安装更新（检查更新 / 底栏「有更新」共用）：确认框（版本 + 更新日志）→
    /// 下载 + 解包校验 → 启动自更新脚本 → 请求退出（脚本在进程退出后覆盖并重启）。
    /// 不含 CheckingUpdate 守卫与异常兜底，由调用方负责。
    /// </summary>
    private async Task ConfirmAndInstallAsync(UpdateCheck check)
    {
        AddLog($"发现新版本 {check.TagName}。");
        if (UpdateConfirmRequested?.Invoke(check) != true)
        {
            AddLog("已取消更新。");   // 取消后红点 / 「有更新」按钮保留，用户可稍后再装
            return;
        }

        AddLog($"开始下载更新包 {check.AssetName}…");
        var zip = await UpdateService.DownloadAsync(check);
        AddLog("更新包就绪，程序将退出并自动完成安装重启。");
        UpdateService.PrepareAndLaunchUpdater(zip);
        UpdateRestartRequested?.Invoke();
    }

    /// <summary>
    /// 启动后台更新预检（不打扰用户，不弹窗）：发现 Gitee 有比当前更新的 Release 时
    /// 点亮「选项 → 检查更新」的小红点提醒；无更新 / 网络不可用一律静默，并且**明确熄灭**红点——
    /// 红点必须由"确认有新版本"点亮，不能靠默认值亮着（那样已是最新版也会挂一个点）。
    /// </summary>
    private async Task CheckUpdateDotAsync()
    {
        try
        {
            var probe = await UpdateService.FetchLatestAsync();
            if (probe.Check is { } c && c.Version > UpdateService.CurrentVersion)
            {
                _latestUpdate = c;                            // 「有更新」按钮的数据源
                UpdateDotVisibility = Visibility.Visible;
                AddLog($"发现新版本 {c.TagName}：「检查更新」红点与底栏「有更新」已亮。");
            }
            else
            {
                _latestUpdate = null;
                UpdateDotVisibility = Visibility.Collapsed;   // 已是最新 / 远端不可用：不亮红点
                AddLog(probe.Failure is { } failure
                    ? $"启动更新预检：远端不可用（{failure}）。"
                    : "启动更新预检：没有更新的版本。");
            }
        }
        catch (Exception ex)
        {
            // 双保险静默（FetchLatestAsync 内部已兜底）：启动预检任何失败不得影响主流程。
            UpdateDotVisibility = Visibility.Collapsed;
            Logger.Info($"启动更新预检失败（不影响使用）：{ex.Message}");
        }
    }

    // ---------- 日志 / 保存 ----------
    /// <summary>把当前配置对象交给外部保存（退出时序用）。</summary>
    public AppConfig CurrentConfig => _config;

    /// <summary>音量滑块防抖保存定时器（拖动结束后 500ms 落盘）。</summary>
    private readonly System.Windows.Threading.DispatcherTimer _volumeSaveTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(500)
    };

    /// <summary>配置落盘防抖定时器（高频状态变更合并写盘，避免每次翻转都同步写三次文件卡 UI）。</summary>
    private readonly System.Windows.Threading.DispatcherTimer _configSaveTimer = new()
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

    /// <summary>防抖保存配置：500ms 内多次状态变更合并为一次写盘（总开关热键快速切换场景）。
    /// 退出时序仍由 App.ExitApp 直接落盘兜底，不会丢状态。</summary>
    public void SaveConfigDeferred()
    {
        _configSaveTimer.Stop();
        _configSaveTimer.Start();
    }

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

    /// <summary>按当前档位解析外观应用主题资源：手动切换过夜间/白天后固定，否则跟随系统；
    /// 强调色取自当前档位的方案定制（主题预设 + 默认主题自定义覆盖）。</summary>
    private void ApplyTheme()
    {
        var isDark = _config.NightMode || (_config.ThemeFollowSystem && ThemeHelper.IsDarkMode());
        (App.Current as App)?.ApplyTheme(isDark, CurrentLook);
        // 已创建的键位图块实例刷子随主题刷新（UpdateTheme 只重建静态刷子）。
        foreach (var b in MouseButtons) b.RefreshTheme();
        foreach (var row in KeyboardRows)
            foreach (var b in row) b.RefreshTheme();
        foreach (var b in NumpadKeys) b.RefreshTheme();
    }

    /// <summary>当前档位（活动方案）的外观定制。</summary>
    public ProfileAppearance CurrentAppearance => _config.ProfileAppearances[_activeProfile];

    /// <summary>四个档位的外观定制快照（方案定制对话框读取）。</summary>
    public IReadOnlyList<ProfileAppearance> ProfileAppearances => _config.ProfileAppearances;

    /// <summary>当前档位解析后的最终外观（主题预设 + 默认主题自定义覆盖）：界面换肤的唯一依据。</summary>
    public ResolvedAppearance CurrentLook => CurrentAppearance.Resolve();

    /// <summary>档位外观变更（App 重建托盘图标与提示文字；View 换标题栏名称与图标）。</summary>
    public event Action<ResolvedAppearance>? AppearanceChanged;

    /// <summary>已应用的公告资源名（切换档位时仅当公告源真的变了才重新同步远端）。</summary>
    private string _appliedNoticeResource = "";

    /// <summary>
    /// 按当前档位的解析外观应用整套换肤：
    /// 强调色（主题资源 + 键位图块刷子）、键帽配色、应用图标（窗口 / 任务栏 / 托盘 / 键帽）、
    /// 应用名（窗口标题由 View 消费 AppearanceChanged）、注释区默认公告（随主题 Notice.md ↔ Notice2.md）。
    /// 启动 / 切换方案档位 / 方案定制确定 / 导入配置各调用一次。
    /// </summary>
    private void ApplyAppearance()
    {
        var look = CurrentLook;
        var noticeChanged = !string.Equals(_appliedNoticeResource, look.NoticeResourceName, StringComparison.Ordinal);
        _appliedNoticeResource = look.NoticeResourceName;

        ApplyTheme();
        // 键帽配色：解析值（默认主题 = 自定义或主题预设；预设主题 = 主题配色）
        var scheme = KeycapSchemes.IsKnown(look.KeycapScheme) ? look.KeycapScheme : "Pansy";
        if (!string.Equals(_config.KeycapScheme, scheme, StringComparison.OrdinalIgnoreCase))
        {
            _config.KeycapScheme = scheme;
            OnPropertyChanged(nameof(KeycapSchemeName));   // 设置菜单 ● 选中标记
        }
        Views.KeycapOverlayWindow.ApplyScheme(KeycapSchemes.Resolve(scheme));
        _webOverlay.SetScheme(KeycapSchemes.Resolve(scheme));   // Web 叠加源配色随档位换肤
        _visualizer.SetSectIcon(look.AppIconUri);          // 状态提醒 / 调整模式键帽图标
        _webOverlay.SetAppIcon(look.AppIconUri);           // Web 叠加源提醒键帽图标随档位换肤
        _soundCue.ApplyTheme(look.Theme, ThemeSoundFor(look.Theme));   // 主题语音方案随主题整套切换（高松灯 ↔ 墨提斯）
        OnPropertyChanged(nameof(DivinerVoiceButtonText)); // “成为衍天/莫问高手”按钮文案随主题
        AppearanceChanged?.Invoke(look);                   // 窗口标题与图标、托盘图标与提示
        OnPropertyChanged(nameof(CurrentAppearance));
        if (!noticeChanged) return;
        OnPropertyChanged(nameof(NoticeContent));          // 注释区默认公告随主题切换
        _ = CheckNoticeUpdateAsync();                      // 该主题公告远端同步（后台，失败保持内嵌内容）
    }

    /// <summary>
    /// 键盘注入模式：0 普通 SendInput / 1 扫描码 / 2 PostMessage 消息模式 / 3 DD 虚拟驱动 /
    /// 4 Interception 驱动（开源）。界面菜单热切换，自动保存。
    /// 消息模式仅目标窗口在前台时有效；3 与 4 都是物理级注入（无 LLKHF_INJECTED 标记），
    /// 前者需 dd63330.dll、后者需已安装驱动并重启过，两者互斥使用。
    /// </summary>
    public int KeyboardMode
    {
        get => _keyboardMode;
        set
        {
            if (value is < 0 or > 4) value = 0;
            _ddAutoActivate = false;   // 用户手动切换：取消自动升回 DD 的挂起态
            var previousMode = _keyboardMode;   // 放弃切换时回退用（如 DD 与 Interception 冲突且用户取消卸载）
            if (!Set(ref _keyboardMode, value)) return;
            // 用户改主意选了别的模式：撤销"重启后自动切换"的待办，别在重启后强推某条路线。
            if (value != _config.PendingModeAfterReboot && _config.PendingModeAfterReboot != 0)
            {
                var dropped = _config.PendingModeAfterReboot;
                _config.PendingModeAfterReboot = 0;
                Logger.Info($"已取消「重启后自动切到 {SystemDriverAdvisor.ModeLabel(dropped)}」：用户改选了其他注入模式。");
            }
            // DD 模式：先确保虚拟驱动就绪；缺失则回退普通模式，并引导用户一键获取
            //（确认后后台下载安装，完成自动升回 DD 模式）。
            if (value == 3 && !DdDriverService.EnsureReady())
            {
                var reason = DdDriverService.LastError ?? "未知原因";
                Logger.Warn($"切换到 DD 驱动失败，回退普通模式：{reason}");
                AddLog($"DD 驱动模式暂不可用（{reason}），已先回退普通模式。");
                // 同一句原因送到注释区：手动放错文件（如放了 32 位版）时用户必须能看到为什么，
                // AddLog 走的 Logs 集合界面上没有绑定，等于没提示。
                FinishDdFetchStatus($"DD 驱动模式不可用：{reason}", 20);
                value = 0;
                Set(ref _keyboardMode, 0);
                _ddAutoActivate = true;
                if (DdDriverService.IsAutoFetchRunning)
                    AddLog("DD 驱动正在后台获取（失败自动重试直至成功），完成后将自动启用 DD 模式。");
                else
                    RequestDdDriverFetch();
            }
            else if (value == 3 && InterceptionDriverService.IsDriverPresent)
            {
                // 实测结论（2026-10-30，本机）：装过 Interception 之后，DD 注入的按键
                // 一个都进不了系统（探针：DD_btn(0)=1、DD_key 不停调，低级钩子 0 条），
                // 与"有没有建立 Interception 上下文"无关 —— 是键盘类过滤驱动把 DD 虚拟设备
                // 的注入路径挡住了。所以两个驱动不是"不同时用"，而是"装了就不能用另一个"。
                Logger.Warn("DD 模式：检测到 Interception 驱动已安装，DD 注入完全无效（实测按键进不了系统）。");
                var confirmed = DdDriverFetchConfirmRequested?.Invoke(
                    "DD 驱动与已安装的 Interception 驱动互斥，两者不能同时使用：\n\n"
                    + "· Interception 是键盘「类过滤驱动」，拦在所有键盘设备前面，会把 DD 虚拟键盘"
                    + "注入的按键全部吞掉（实测：DD 初始化成功，但注入的按键一条都进不了系统）；\n"
                    + "· 卸载后必须重启电脑，驱动才会真正从键盘栈上摘掉；重启之前 DD 依然无效。\n\n"
                    + "是否现在卸载 Interception 驱动？（卸载后请重启，再回来选择 DD 驱动）") == true;

                if (confirmed)
                {
                    var uninstalled = InterceptionDriverService.Uninstall();
                    RefreshInterceptionDriverState();
                    if (uninstalled)
                    {
                        // 置"待激活"：重启后启动流程会自动切到 DD，用户不必记得再点一次。
                        // 之所以不现在切，是因为驱动重启前仍挂在键盘栈上，切了就是个"假 DD"。
                        _config.PendingModeAfterReboot = 3;
                        _config.PendingDdAfterReboot = false;
                        SaveConfig();
                        Logger.Info("已卸载 Interception 驱动（为切回 DD），已登记重启后自动切到 DD。");
                        AddLog("Interception 驱动已卸载：请重启电脑，重启后会自动切到 DD 驱动（无需再手动选择）。");
                        FinishDdFetchStatus("Interception 驱动已卸载：重启后会自动切到 DD 驱动，无需再手动选择。", 30);
                    }
                    else
                    {
                        var uninstallError = InterceptionDriverService.LastError ?? "未知原因";
                        Logger.Warn($"卸载 Interception 驱动失败：{uninstallError}");
                        AddLog($"卸载 Interception 驱动失败（{uninstallError}），DD 驱动仍不可用。");
                        FinishDdFetchStatus($"卸载 Interception 驱动失败：{uninstallError}", 25);
                    }
                }
                else
                {
                    AddLog("已取消：DD 与 Interception 互斥，需先卸载 Interception 并重启才能使用 DD。");
                    FinishDdFetchStatus("DD 与 Interception 互斥：请先卸载 Interception 驱动并重启。", 25);
                }

                // 无论卸载成功与否，**重启前 DD 都发不出按键**（驱动还在内核里）：
                // 这时切到 DD 只会得到"看着是 DD、实际什么都不发"的假状态，必须退回原模式。
                value = previousMode;
                Set(ref _keyboardMode, previousMode);
                return;
            }

            // Interception 模式（双驱动路线第二条）：驱动未安装 / 没重启 / 被 HVCI 拦下时
            // 一律回退普通模式并把具体原因送到注释区 —— 这一步不可用不是"程序坏了"，
            // 而是"还差一个动作"，提示必须能让用户知道差的是哪一步。
            if (value == 4 && !InterceptionDriverService.EnsureReady())
            {
                var reason = InterceptionDriverService.LastError ?? "未知原因";
                Logger.Warn($"切换到 Interception 驱动失败，回退普通模式：{reason}");
                AddLog($"Interception 驱动模式暂不可用（{reason}），已先回退普通模式。");
                FinishDdFetchStatus($"Interception 驱动模式不可用：{reason}", 25);
                value = 0;
                Set(ref _keyboardMode, 0);
                _ddAutoActivate = false;
            }

            // 注：切到 Interception 时不主动 Shutdown DD —— DD 的虚拟设备保持初始化是无害的（空闲），
            // 而反复重入 DD_btn(0) 反而有把驱动内部状态搞坏的风险；切回 DD 时无需重新初始化即可用。

            // 同步到模拟器（连发线程每次注入时读取）。
            InputSimulatorService.KeyboardMode = value;
            // 切换驱动路线必须同时清两样东西，否则第一条物理按键就会被旧状态吃掉：
            // ① 回环名额（旧驱动登记的名额在新驱动的时间尺度上等于"永远匹配不上"）；
            // ② 按住表（旧驱动上丢过释放事件的键会一直被视为按住，开关再也停不下来）。
            EchoGuard.Reset(RttSeedFor(value));
            _scheduler.ResetInputState();
            _config.KeyboardMode = value;
            _config.UseScanCodes = value == 1; // 旧字段语义保留（持久化兼容）
            SaveConfig();
            // 刷新运行快照：用户切走 DD 之后再崩，不该算到 DD 头上。
            DdCrashGuard.SaveRunState(value);
            Logger.Info(value is 3 or 4
                ? $"键盘注入已切换到 {(value == 3 ? "DD 驱动" : "Interception 驱动")}（回环窗口种子 {RttSeedFor(value)}ms）。"
                : $"键盘注入已切换到模式 {value}。");
            AddLog(value switch
            {
                1 => "键盘注入已切换：扫描码模式（DirectInput 兼容）。",
                2 => "键盘注入已切换：消息模式（仅游戏前台时有效）。",
                3 => $"键盘注入已切换：DD 驱动模式（{Path.GetFileName(DdDriverService.LoadedPath)}）。",
                4 => $"键盘注入已切换：Interception 驱动模式（{Path.GetFileName(InterceptionDriverService.LoadedPath)}，开源）。",
                _ => "键盘注入已切换：普通模式（SendInput）。"
            });
        }
    }

    /// <summary>
    /// 各注入路线的回环往返延迟种子（毫秒，实测值）：Interception ≈0~16ms、DD ≈170ms、
    /// 普通注入不产生回环（用默认值即可）。EchoGuard 用它决定"多久没等到回环就判定丢失"。
    /// </summary>
    private static int RttSeedFor(int mode) => mode switch
    {
        3 => 170,   // DD：dd63330 虚拟设备固定上报节拍
        4 => 20,    // Interception：探针实测 40Hz 连发下中位 0ms、最大 16ms
        _ => 60
    };

    /// <summary>
    /// DD 驱动是否可用（可加载的 x64 dd63330.dll 在位）：决定「键盘注入 → DD 驱动」能否点选。
    /// 只查文件、不初始化设备——驱动在位但初始化失败时仍允许点选，由 EnsureReady 说明原因；
    /// 文件不在位（从未下载 / 下载失败 / 被删除）则灰显，避免"看着能选、点了却弹窗回退"。
    /// </summary>
    public bool IsDdDriverAvailable => DdDriverService.IsAvailable;

    /// <summary>DD 驱动是否正在后台获取（获取期间「获取 DD 驱动…」显示进度并禁用）。</summary>
    public bool IsDdDriverFetching => DdDriverService.IsAutoFetchRunning;

    /// <summary>
    /// 驱动状态提示（注释区展示，DD 获取与 Interception 安装共用这一条通道）：
    /// null = 无提示（平时 / 结束后自动消退）。下载百分比、第 N 次失败、几秒后重试、
    /// 成功 / 需重启 / 终止，全部走这条文本。
    /// </summary>
    public string? DdFetchStatus
    {
        get => _ddFetchStatus;
        private set => Set(ref _ddFetchStatus, value);
    }

    /// <summary>重算 DD 驱动可用 / 获取中状态并通知 UI（菜单展开、获取开始 / 结束时调用）。</summary>
    public void RefreshDdDriverState()
    {
        OnPropertyChanged(nameof(IsDdDriverAvailable));
        OnPropertyChanged(nameof(IsDdDriverFetching));
    }

    /// <summary>
    /// Interception 驱动是否可用（x64 库在位 + 键盘类上线了该过滤驱动）：决定菜单项能否点选。
    /// 装完没重启也算可用 —— 点选时由 EnsureReady 给出"请重启"这种可执行原因。
    /// </summary>
    public bool IsInterceptionDriverAvailable => InterceptionDriverService.IsAvailable;

    /// <summary>Interception 官方包是否正在下载 / 安装（安装入口据此转「安装中…」并禁用）。</summary>
    public bool IsInterceptionDriverPreparing => InterceptionDriverService.IsPreparing;

    /// <summary>重算 Interception 驱动可用 / 安装中状态并通知 UI。</summary>
    public void RefreshInterceptionDriverState()
    {
        OnPropertyChanged(nameof(IsInterceptionDriverAvailable));
        OnPropertyChanged(nameof(IsInterceptionDriverPreparing));
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

    /// <summary>键帽 Web 叠加源（OBS 浏览器源）开关联动命令（底栏 video 图标按钮）。</summary>
    public ICommand ToggleWebOverlayCommand { get; }

    /// <summary>“成为衍天高手”开关联动命令（底栏按钮）。</summary>
    public ICommand ToggleDivinerVoiceCommand { get; }

    /// <summary>当前主题是否莫问高手（按钮文案与启动音变体的判定源）。</summary>
    public bool IsMoWenTheme => ReferenceEquals(CurrentLook.Theme, SectThemes.MoWen);

    /// <summary>底栏按钮文案：随当前档位主题切换（莫问高手主题 = 「成为莫问高手」，其余 = 「成为衍天高手」）。</summary>
    public string DivinerVoiceButtonText => IsMoWenTheme ? "成为莫问高手" : "成为衍天高手";

    /// <summary>语音设置（底栏按钮）：弹出模态框为开启 / 关闭 / 方案切换语音导入自定义音频。</summary>
    public ICommand OpenVoiceSettingsCommand { get; }

    /// <summary>语音设置对话框请求（View 接线后订阅；打开模态框）。</summary>
    public event Action? VoiceSettingsRequested;

    /// <summary>方案定制（「选项」菜单首项）：弹出模态框为四个方案档位命名并绑定主题 / 定制外观。</summary>
    public ICommand CustomizeProfilesCommand { get; }

    /// <summary>方案定制对话框请求（View 接线后订阅；打开模态框）。</summary>
    public event Action? ProfileCustomizeRequested;

    /// <summary>检查更新命令（Gitee Release：检查 → 确认 → 下载安装并自动重启）。</summary>
    public ICommand CheckUpdateCommand { get; }

    /// <summary>更新日志命令（「选项 → 更新日志」：弹窗展示全部历史版本的更新内容）。</summary>
    public ICommand ShowChangelogCommand { get; }

    /// <summary>底栏「有更新」按钮：确认并安装已检测到的新版本（版本号 + 更新日志 → 下载 → 重启）。</summary>
    public ICommand UpdateAvailableCommand { get; }

    /// <summary>更新日志窗口请求（View 接线后订阅；打开模态窗）。</summary>
    public event Action? ChangelogRequested;

    /// <summary>底栏设置菜单：切换键盘注入模式（参数 = 模式索引 0 普通 / 1 扫描码 / 2 消息 / 3 DD 驱动 / 4 Interception 驱动）。</summary>
    public ICommand SetKeyboardModeCommand { get; }

    /// <summary>底栏设置菜单：获取 DD 驱动（缺失时出现的下载安装入口，用户确认后从官方渠道获取）。</summary>
    public ICommand FetchDdDriverCommand { get; }

    /// <summary>
    /// 底栏设置菜单：安装 Interception 驱动（双驱动路线的第二条）。
    /// 走官方 GitHub Release 原版包（LGPL，允许再分发），安装后必须重启。
    /// </summary>
    public ICommand InstallInterceptionDriverCommand { get; }

    /// <summary>底栏设置菜单：卸载 Interception 驱动（调官方安装器 /uninstall，同样需重启）。</summary>
    public ICommand UninstallInterceptionDriverCommand { get; }

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

    /// <summary>键帽 Web 叠加源（底栏按钮）：开启后本机启动 HTTP+WebSocket 服务，OBS 添加
    /// 「浏览器源」粘贴源 URL 即可——CEF 透明背景渲染键帽，录制画面里键帽透明浮动，
    /// 屏幕观感完全不变。URL 一次性粘贴后随配置持久（端口 / 令牌不变则无需重配）。
    /// 透明度 / 配色 / 提醒键帽 / 可视化过滤与桌面悬浮层实时同源。</summary>
    public bool WebOverlayEnabled
    {
        get => _config.WebOverlayEnabled;
        set
        {
            if (_config.WebOverlayEnabled == value) return;
            _config.WebOverlayEnabled = value;
            if (value) StartWebOverlay();
            else _webOverlay.Stop();
            SaveConfig();
            AddLog(value
                ? $"键帽 Web 叠加源已开启：OBS 添加「浏览器源」并粘贴 {WebOverlayUrl}（已复制到剪贴板）。"
                : "键帽 Web 叠加源已关闭。");
            OnPropertyChanged(nameof(WebOverlayEnabled));   // 底栏按钮图标随开关切换金色 ↔ 常规色
        }
    }

    /// <summary>OBS 浏览器源 URL（127.0.0.1 + 配置端口 + 令牌）。</summary>
    private string WebOverlayUrl => WebOverlayService.BuildUrl(_config.WebOverlayPort, _config.WebOverlayToken);

    /// <summary>启动 Web 叠加源监听（失败记录原因，不弹窗——底栏开关状态保持开启，用户可排查后重启）。</summary>
    private void StartWebOverlay()
    {
        var error = _webOverlay.Start(_config.WebOverlayPort, _config.WebOverlayToken);
        if (error is null)
        {
            try { System.Windows.Clipboard.SetText(WebOverlayUrl); } catch { /* 剪贴板被占用：忽略 */ }
        }
        else
        {
            AddLog($"键帽 Web 叠加源启动失败：{error}");
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
                ? $"{DivinerVoiceButtonText}：总开关启动的语音播报改为「{(IsMoWenTheme ? "莫问糕手启动" : "衍天高手启动")}」。"
                : $"{DivinerVoiceButtonText}已关闭：总开关启动的语音播报恢复「启动」。");
            OnPropertyChanged(nameof(DivinerVoiceEnabled));   // 底栏按钮墨迹随开关切换金色 ↔ 常规色
        }
    }

    // ---------- 语音设置（主题语音方案：开启 / 关闭 / 方案切换，按主题独立设置） ----------

    /// <summary>语音用途显示名（对话框标题 / 日志）。</summary>
    public static string VoiceCueName(SoundCue cue) => cue switch
    {
        SoundCue.Start => "开启",
        SoundCue.Stop => "关闭",
        _ => "方案切换",
    };

    /// <summary>主题在配置中对应的语音方案（下标钳位；加载 / 导入时已归一化到主题数）。</summary>
    private CustomSoundConfig ThemeSoundFor(SectTheme theme) =>
        _config.ThemeSounds[Compat.Clamp(SectThemes.IndexOf(theme), 0, SectThemes.All.Length - 1)];

    private CustomSoundConfig ThemeSoundConfig(int themeIndex) =>
        _config.ThemeSounds[Compat.Clamp(themeIndex, 0, SectThemes.All.Length - 1)];

    private static string? GetStoredSound(CustomSoundConfig config, SoundCue cue) => cue switch
    {
        SoundCue.Start => config.Start,
        SoundCue.Stop => config.Stop,
        _ => config.Cycle,
    };

    private static void SetStoredSound(CustomSoundConfig config, SoundCue cue, string? storedName)
    {
        switch (cue)
        {
            case SoundCue.Start: config.Start = storedName; break;
            case SoundCue.Stop: config.Stop = storedName; break;
            default: config.Cycle = storedName; break;
        }
    }

    /// <summary>指定主题的某用途是否已设置自定义语音（语音设置“重置”按钮的可用态）。</summary>
    public bool HasThemeVoice(int themeIndex, SoundCue cue) =>
        GetStoredSound(ThemeSoundConfig(themeIndex), cue) is { Length: > 0 };

    /// <summary>指定主题某用途的存储文件名（“{用途前缀}_原文件名”；未设置为空串）。</summary>
    public string ThemeVoiceStoredName(int themeIndex, SoundCue cue) =>
        GetStoredSound(ThemeSoundConfig(themeIndex), cue) ?? "";

    /// <summary>语音设置对话框“默认音频”项的显示名：标注该主题的配套默认来源
    /// （默认主题 / 衍天高手 = 高松灯，莫问高手 = 墨提斯；配套文件缺失时显示纯“默认音频”）。</summary>
    public string VoiceDefaultLabel(int themeIndex, SoundCue cue)
    {
        var (start, stop, cycle) = SectThemes.CueFiles(SectThemes.ByIndex(themeIndex));
        var file = cue switch { SoundCue.Start => start, SoundCue.Stop => stop, _ => cycle };
        var path = SoundCueService.FindCandidate(file);
        return path is null ? "默认音频" : $"默认音频（{SoundCueService.ShortDisplayName(path)}）";
    }

    /// <summary>
    /// 导入主题语音（复制到语音目录 → 落盘）。返回 null = 成功，否则为失败原因。
    /// 编辑当前激活主题时热应用；其他主题仅落盘，切换到该主题时经 ApplyAppearance 生效。
    /// 文件被占用 / 无权限等异常由 View 提示；失败后恢复原有装载。
    /// </summary>
    public string? ImportThemeVoice(int themeIndex, SoundCue cue, string sourcePath)
    {
        try
        {
            _soundCue.UnloadCue(cue);   // 释放媒体源占用，否则同名音频文件无法覆盖
            var stored = SoundCueService.ImportCustom(cue, sourcePath);
            SetStoredSound(ThemeSoundConfig(themeIndex), cue, stored);
            SaveConfig();
            HotApplyThemeVoice(themeIndex);
            AddLog($"{SectThemes.ByIndex(themeIndex).DisplayName} · {VoiceCueName(cue)}语音已设置为 [{Path.GetFileName(sourcePath)}]。");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Error("导入自定义语音失败。", ex);
            HotApplyThemeVoice(themeIndex);   // 失败后恢复原装载（编辑当前主题时）
            return ex.Message;
        }
    }

    /// <summary>重置指定主题某用途语音为主题配套默认（删除自定义文件 → 落盘；当前主题热应用）。</summary>
    public void ResetThemeVoice(int themeIndex, SoundCue cue)
    {
        var config = ThemeSoundConfig(themeIndex);
        var old = GetStoredSound(config, cue);
        if (old is not { Length: > 0 }) return;
        SetStoredSound(config, cue, null);
        _soundCue.UnloadCue(cue);
        SoundCueService.DeleteCustom(old);
        SaveConfig();
        HotApplyThemeVoice(themeIndex);
        AddLog($"{SectThemes.ByIndex(themeIndex).DisplayName} · {VoiceCueName(cue)}语音已重置为主题配套默认。");
    }

    /// <summary>试听指定主题方案的某用途音频（独立试听播放器，当前主题之外也可试听）：
    /// 自定义音频优先，否则主题配套音频；两者都不可用时静默。</summary>
    public void PreviewThemeVoice(int themeIndex, SoundCue cue)
        => _soundCue.PreviewFile(ResolveThemeVoicePath(themeIndex, cue));

    /// <summary>解析主题方案的音频文件路径：自定义（语音目录）优先，否则主题配套（程序目录候选）。</summary>
    private string? ResolveThemeVoicePath(int themeIndex, SoundCue cue)
    {
        var stored = GetStoredSound(ThemeSoundConfig(themeIndex), cue);
        if (stored is { Length: > 0 })
        {
            var path = Path.Combine(SoundCueService.SoundsDir, stored);
            if (File.Exists(path)) return path;
        }
        var (start, stop, cycle) = SectThemes.CueFiles(SectThemes.ByIndex(themeIndex));
        var file = cue switch { SoundCue.Start => start, SoundCue.Stop => stop, _ => cycle };
        return SoundCueService.FindCandidate(file);
    }

    /// <summary>编辑当前激活主题时热应用语音方案；其他主题仅落盘（切换档位时经 ApplyAppearance 生效）。</summary>
    private void HotApplyThemeVoice(int themeIndex)
    {
        if (themeIndex != SectThemes.IndexOf(CurrentLook.Theme)) return;
        _soundCue.ApplyTheme(CurrentLook.Theme, ThemeSoundFor(CurrentLook.Theme));
    }

    /// <summary>
    /// 键帽配色方案名（KeycapSchemes.All 之一）：设置菜单「键帽配色」与方案定制共用。
    /// 当前档位绑定「默认主题」（可定制）时同步写入该档位外观，随档位持久化；
    /// 绑定预设主题（衍天 / 莫问）时仅为临时应用 —— 下次应用该档位外观会回到主题预设配色。
    /// </summary>
    public string KeycapSchemeName
    {
        get => _config.KeycapScheme;
        set
        {
            if (_config.KeycapScheme == value) return;
            _config.KeycapScheme = value;
            if (CurrentLook.Theme.Customizable) CurrentAppearance.KeycapScheme = value;
            Views.KeycapOverlayWindow.ApplyScheme(KeycapSchemes.Resolve(value));
            _webOverlay.SetScheme(KeycapSchemes.Resolve(value));   // Web 叠加源配色实时同源
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

    /// <summary>
    /// 键帽透明度（0~100，默认 100 = 完全不透明；底栏滑块调节）。
    /// 拖动实时作用于悬浮窗，落盘防抖（拖动过程不反复写文件）；导入配置后同步还原。
    /// </summary>
    public double VisualizerOpacity
    {
        get => Compat.Clamp(_config.VisualizerOpacity, 0, 100);
        set
        {
            var v = Compat.Clamp(value, 0, 100);
            if (Math.Abs(_config.VisualizerOpacity - v) < 0.001) return;
            _config.VisualizerOpacity = v;
            _visualizer.SetOpacity(v);
            _webOverlay.SetOpacity(v);   // Web 叠加源透明度实时同源
            SaveConfigDeferred();   // 拖动中高频变更合并落盘（不做日志，避免刷屏）
            OnPropertyChanged(nameof(VisualizerOpacity));
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
    /// 启动 / 导入 / 删除 / 总开关键迁移）。双宏方案额外登记辅键源（次键 eye → 该键显示）。
    /// </summary>
    private void SyncVisualizerRegistry()
    {
        var entries = new List<KeyValuePair<string, bool>>(_config.Schemes.Count);
        foreach (var (key, scheme) in _config.Schemes)
        {
            entries.Add(new(key, GetVisualEnabled(key)));
            // 双宏辅键：辅键键帽（连发脉冲 / 物理按下）按次键的 VisualEnabled 独立开关。
            if (scheme.Section != ToggleSection.Dual || scheme.Targets.Count < 2) continue;
            if (TargetToSource(scheme.Targets[1]) is { } partner)
                entries.Add(new(TaskSchedulerService.BuildSourceKey(partner), scheme.Targets[1].VisualEnabled));
        }
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

    /// <summary>设置方案内指定目标键的可视化开关（双宏辅键行 eye；实时生效 + 落盘）。</summary>
    private void SetTargetVisualEnabled(string key, int targetIndex, bool visible)
    {
        if (!_config.Schemes.TryGetValue(key, out var scheme) ||
            scheme.Targets.Count <= targetIndex) return;
        var target = scheme.Targets[targetIndex];
        if (target.VisualEnabled == visible) return;
        target.VisualEnabled = visible;
        SyncVisualizerRegistry();   // 辅键源注册表项随开关刷新（开关立即作用于键帽 / 脉冲）
        SaveConfig();
        AddLog($"键位可视化 [{InputNameMapper.GetTargetName(target)}] 已{(visible ? "开启" : "关闭")}。");
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
