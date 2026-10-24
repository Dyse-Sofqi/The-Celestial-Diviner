using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;
using TheCelestialDiviner.Helpers;
using TheCelestialDiviner.Models;
using TheCelestialDiviner.Views;

namespace TheCelestialDiviner.Services;

/// <summary>可视化方案过滤模式（底栏下拉选择，持久化）。</summary>
public enum VisualizerMode
{
    /// <summary>全部键位（方案键仍受单键 eye 开关约束）。</summary>
    All = 0,
    /// <summary>修饰键（Shift/Ctrl/Alt/Win）和自定义键（方案内键位）。</summary>
    ModifiersAndCustom = 1,
    /// <summary>仅自定义键（方案内键位）。</summary>
    Custom = 2,
}

/// <summary>
/// 键位可视化服务：持有可可视化键位注册表（含每键开关），
/// 订阅全局钩子的按下 / 释放事件，把命中注册表的按键封送到悬浮窗线程驱动悬浮层
/// （悬浮窗运行在独立 STA 线程，呈现阻塞不波及主 UI 线程，见 AttachOverlay）。
/// 注册表由主视图模型随方案变更同步（默认全量注册 + 默认开启）。
/// </summary>
public sealed class KeyVisualizerService
{
    /// <summary>可视化键位状态：是否注册 + 是否显示（用户可关闭单个键位的可视化）。</summary>
    public readonly struct VisualKeyState
    {
        public VisualKeyState(bool visible) => Visible = visible;
        public bool Visible { get; }
    }

    private readonly object _gate = new();
    private readonly Dictionary<InputSource, VisualKeyState> _registry = new();
    private bool _globalEnabled = true;   // 可视化总闸门（不影响各键开关状态的记忆）
    private VisualizerMode _mode = VisualizerMode.All;   // 可视化方案（默认全部）

    // ---------- UI 封送合并（快照 / 脉冲按 UI 帧合并，见 DispatchSnapshot / OnTargetFired） ----------
    private readonly object _dispatchGate = new();     // 封送合并状态锁（钩子/任务线程 ↔ UI 线程）
    private List<string>? _pendingSnapshot;            // 待渲染的最新快照（后到覆盖先到）
    private Dictionary<string, int> _pendingBumps = new();   // 合并期内各键连击增量（保计数）
    private bool _snapshotQueued;                      // 快照渲染已排队（排队期间只更新待渲染数据）
    private Dictionary<string, (int HoldMs, int Count, int EnqueueTick)> _pendingPulses = new();
    private bool _pulseQueued;                         // 脉冲渲染已排队
    private int _lastUiLagLogTick;                     // UI 积压告警限频（Environment.TickCount）

    /// <summary>
    /// 当前按住中的键位（按下顺序；真源）。释放时移除，快照驱动悬浮层逐键独立更新，
    /// 多键同时按住时各自保持按压态互不顶替。
    /// </summary>
    private readonly List<(InputSource Source, string Label)> _held = new();

    /// <summary>幽灵按住项看门狗（UI 线程周期回收，见 PurgeGhostHeld）。</summary>
    private DispatcherTimer? _ghostTimer;

    /// <summary>设置可视化方案（底栏下拉切换；实时生效。持久化由 VM 写配置负责）。</summary>
    public void SetMode(VisualizerMode mode)
    {
        lock (_gate)
        {
            _mode = mode;
        }
    }

    /// <summary>
    /// 模式过滤（仅在 _globalEnabled 已判定后调用；调用方持锁）：
    /// 全部 = 所有键位；修饰键和自定义键 = 修饰键或方案内键位；自定义键 = 仅方案内键位。
    /// 方案内键位额外受单键 eye 开关约束（调用方处理）；未注册键位仅“全部 / 修饰键”路径可达。
    /// </summary>
    private bool PassesModeFilter(InputSource source, bool registered)
    {
        return _mode switch
        {
            VisualizerMode.All => true,
            VisualizerMode.ModifiersAndCustom => registered || IsModifierKey(source),
            VisualizerMode.Custom => registered,
            _ => true,
        };
    }

    /// <summary>是否修饰键（左/右 Shift、Ctrl、Alt、Win）。</summary>
    private static bool IsModifierKey(InputSource source) =>
        source.Kind == InputKind.Keyboard &&
        source.VirtualKey is 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or 0x5B or 0x5C;

    private readonly InputHookService _hooks;
    private volatile KeycapOverlayWindow? _overlay;
    private volatile Dispatcher? _dispatcher;   // 悬浮窗线程的调度器（悬浮窗运行在独立 STA 线程，见 AttachOverlay）

    /// <summary>悬浮窗创建前的暂存位置（AttachOverlay 时应用；修复启动时序：VM 构造早于窗口创建）。</summary>
    private (double Left, double Top)? _pendingPosition;

    /// <summary>悬浮窗创建前的暂存透明度（0~100 百分比；同 SetPosition 的启动时序处理）。</summary>
    private double? _pendingOpacity;

    public KeyVisualizerService(InputHookService hooks)
    {
        _hooks = hooks;
        hooks.SourceDown += OnSourceDown;
        hooks.SourceUp += OnSourceUp;
    }

    // ---------- Web 叠加源事件流（WebOverlayService 订阅；与悬浮层渲染同一份合并后数据） ----------
    // 事件可在钩子 / 调度器 / 悬浮窗线程触发，订阅方自行加锁；悬浮窗未创建（含 --no-overlay）时照常流出。

    /// <summary>按住键位快照（keys = 修饰键在前按按下顺序；bumps = 本轮各键连击增量，可为空表）。</summary>
    public event Action<IReadOnlyList<string>, IReadOnlyDictionary<string, int>>? WebSnapshot;

    /// <summary>连发脉冲 / 方案切换键帽（label、按压时长 holdMs、合并次数 repeat）。</summary>
    public event Action<string, int, int>? WebPulse;

    /// <summary>状态提醒键帽显隐。</summary>
    public event Action<bool>? WebReminder;

    /// <summary>
    /// 初始化悬浮层（UI 线程调用；重复调用安全）。创建后立即应用暂存位置。
    /// 悬浮窗运行在独立 STA 线程（独立 Dispatcher + 消息泵）：游戏前台最大化时，
    /// 悬浮窗的 Show/Hide/SetWindowPos 与逐帧动画渲染要和 DWM 同步，可能阻塞数秒——
    /// 放在主 UI 线程会把 F9 语音/横幅、主窗口交互整条拖死（体感"聚焦游戏就卡死，
    /// 点任务栏/退出最大化立刻跟上"）。独立线程后这些阻塞只吃掉悬浮窗自己的呈现，
    /// 主线程、钩子线程、连发线程全部不受影响；悬浮窗视觉在最坏情况下短暂冻结属可接受代价。
    /// </summary>
    /// <summary>
    /// 诊断开关：不创建键帽悬浮层（含渲染时钟激励器）。由命令行 `--no-overlay` 置位，
    /// 用于 A/B 判断"拖动发涩"是否由悬浮层（分层窗口 + 独立 UI 线程）引入。仅供诊断。
    /// </summary>
    public static bool SuppressOverlay { get; set; }

    public void AttachOverlay()
    {
        if (_overlay is not null) return;
        if (SuppressOverlay)
        {
            Logger.Warn("诊断开关 --no-overlay 已生效：不创建键帽悬浮层。");
            return;
        }
        var ready = new ManualResetEventSlim(false);
        var thread = new Thread(() =>
        {
            try
            {
                var overlay = new KeycapOverlayWindow();
                overlay.Dispatcher.UnhandledException += (_, e) =>
                {
                    // 悬浮层纯展示：吞掉未处理异常保线程存活，不让键帽故障拖垮应用。
                    Logger.Error("键帽悬浮窗线程未处理异常。", e.Exception);
                    e.Handled = true;
                };
                _overlay = overlay;
                _dispatcher = overlay.Dispatcher;   // 后续所有悬浮层操作都封送到此线程
                // 启动时序：VM 构造时窗口尚未存在，SetPosition/SetOpacity 只能暂存；创建后在此应用。
                if (_pendingPosition is { } pos)
                    overlay.SetSavedPosition(pos.Left, pos.Top);
                if (_pendingOpacity is { } opacity)
                    overlay.SetOpacity(opacity);
                // 启动即预创建 HWND（不显示）：置顶 / 穿透样式在本程序仍是前台时建立，
                // 避免第一次按键时才在游戏前台创建分层窗口（首键卡顿 + 置顶序错位）。
                overlay.PreloadHandle();
                // **不再 Preshow**：预显示会在整个会话里挂着一个"置顶 + 分层 + 空透明"的窗口，
                // DWM 每帧都要把它合成在其它窗口之上（250% 缩放下 1400×250 的带 alpha 表面），
                // 拖动任何窗口时都要多混一层。改为首次有键帽时再 Show（ShowOverlay 统一处理）。
                // 代价：游戏前台首次显示键帽时那一次 Show 可能偏慢（作者实测极端场景 6s）；
                // 取舍是"空闲干净"优先——用户明确过这一点。
                // 渲染时钟激励器**不再在启动时创建**：它是个带 `RepeatBehavior.Forever` 的 16ms 颜色
                // 动画 + 置顶分层窗口，等于让整个进程以 60fps 永不停歇地重绘（每帧 UpdateLayeredWindow）。
                // 空闲时纯属白烧，拖动窗口时还会和合成器抢带宽。改为"有键帽要显示时才建"，
                // 见 KeycapOverlayWindow.ShowOverlay / 空内容回收时的 DestroyPacer。
                RefreshReminder();
            }
            catch (Exception ex)
            {
                Logger.Error("键帽悬浮窗线程初始化失败。", ex);
            }
            finally
            {
                ready.Set();
            }
            Dispatcher.Run();   // 悬浮窗消息泵：IsBackground 线程随进程退出
        })
        { Name = "KeycapOverlayThread", IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait(3000);

        // 幽灵按住项看门狗：EchoGuard 吞掉物理释放时的兜底回收（见 PurgeGhostHeld）。
        // 主 UI 线程计时，仅计算与封送，不直接触碰悬浮窗（跨线程）。
        _ghostTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _ghostTimer.Tick += (_, _) => PurgeGhostHeld();
        _ghostTimer.Start();
    }

    /// <summary>进入位置调整模式（虚拟键帽拖拽，确认回调 Left/Top）。
    /// 封送到悬浮窗线程执行；确认回调再封送回主 UI 线程（回调里写配置 / UI 日志集合，
    /// ObservableCollection 与 WPF 绑定有 UI 线程亲和）。</summary>
    public void BeginAdjust(Action<double, double> onConfirm)
    {
        var uiDispatcher = Application.Current?.Dispatcher;
        _dispatcher?.BeginInvoke(() =>
        {
            _overlay?.BeginAdjust((l, t) =>
            {
                if (uiDispatcher is { } d && !d.CheckAccess()) d.BeginInvoke(() => onConfirm(l, t));
                else onConfirm(l, t);
            });
        });
    }

    /// <summary>设置悬浮窗位置（null = 忽略）。窗口未创建时暂存，创建后封送到悬浮窗线程应用。</summary>
    public void SetPosition(double? left, double? top)
    {
        if (left is not { } l || top is not { } t) return;
        if (_overlay is { } overlay)
            _dispatcher?.BeginInvoke(() => overlay.SetSavedPosition(l, t));
        else
            _pendingPosition = (l, t);
    }

    /// <summary>设置键帽透明度（0~100 百分比；实时生效）。窗口未创建时暂存，创建后封送应用。</summary>
    public void SetOpacity(double percent)
    {
        var v = Compat.Clamp(percent, 0, 100);
        if (_overlay is { } overlay)
            _dispatcher?.BeginInvoke(() => overlay.SetOpacity(v));
        else
            _pendingOpacity = v;
    }

    /// <summary>档位外观的应用图标（状态提醒常驻键帽 / 调整模式虚拟键帽）：
    /// 先登记静态资源名（悬浮层尚未创建时也生效），窗口已存在则重建常驻键帽换图标。</summary>
    public void SetSectIcon(string iconUri)
    {
        KeycapOverlayWindow.SetAppIconUri(iconUri);
        if (_overlay is { } overlay && _dispatcher is { } dispatcher)
            dispatcher.BeginInvoke(overlay.RefreshAppIcon);
    }

    /// <summary>订阅调度器连发脉冲（目标键每发射一次 → 键帽脉冲 + 连击角标）；
    /// 同时订阅连发激活状态（状态提醒键帽的显隐驱动）。</summary>
    public void AttachScheduler(TaskSchedulerService scheduler)
    {
        scheduler.TargetFired += OnTargetFired;
        scheduler.FiringChanged += OnFiringChanged;
    }

    /// <summary>连发目标键脉冲（任务线程触发，封送 UI 线程；不占用物理按住快照）。
    /// 目标键可视化开关为关时不渲染脉冲键帽（与物理按键路径同语义）。
    /// 同键脉冲按 UI 帧合并（极限档 45 发/秒不再每发一次调度）：合并期内计数累加，
    /// 一次 HoldPulse 以 repeat 补齐连击角标，视觉按压一次、计数不丢。</summary>
    private void OnTargetFired(TargetKeyConfig target)
    {
        lock (_gate)
        {
            if (!_globalEnabled) return;
            // 双宏两键各有开关：首键 = 方案级注册项（VisualKeys），辅键 = 目标级注册项。
            // 常规方案目标 = 触发键自身，其开关即该方案的 eye。
            if (TargetToSource(target) is { } source &&
                _registry.TryGetValue(source, out var state) && !state.Visible) return;
        }

        var label = KeycapTargetName(target);
        var holdMs = target.HoldMs;
        if (_dispatcher is null)
        {
            // 悬浮窗未创建（含 --no-overlay）：Web 叠加源仍接收（不合并，事件频率 = 发射频率，
            // 连发发射本就按调度档位限频，localhost 广播可承载）。
            WebPulse?.Invoke(label, holdMs, 1);
            return;
        }
        lock (_dispatchGate)
        {
            var prev = _pendingPulses.TryGetValue(label, out var p) ? p.Count : 0;
            _pendingPulses[label] = (holdMs, prev + 1, Environment.TickCount);
            if (_pulseQueued) return;
            _pulseQueued = true;
        }
        _dispatcher.BeginInvoke(() =>
        {
            Dictionary<string, (int HoldMs, int Count, int EnqueueTick)> pulses;
            lock (_dispatchGate)
            {
                pulses = _pendingPulses;
                _pendingPulses = new Dictionary<string, (int, int, int)>();
                _pulseQueued = false;
            }
            // 诊断埋点：入队 → UI 线程实际执行的派发延迟（>150ms = 悬浮窗线程积压，
            // 键帽动画 / 横幅 / 语音呈现都会随之迟滞）。限频记录。
            var nowTick = Environment.TickCount;
            var worst = 0;
            foreach (var kv in pulses)
            {
                var lag = unchecked(nowTick - kv.Value.EnqueueTick);
                if (lag > worst) worst = lag;
                WebPulse?.Invoke(kv.Key, kv.Value.HoldMs, kv.Value.Count);   // Web 叠加源（合并后语义与悬浮层一致）
                _overlay?.HoldPulse(kv.Key, kv.Value.HoldMs, kv.Value.Count);
            }
            if (worst > 150 && unchecked(nowTick - _lastUiLagLogTick) > 1000)
            {
                _lastUiLagLogTick = nowTick;
                Logger.Warn($"键帽派发延迟 {worst}ms（悬浮窗线程积压）。");
            }
        });
    }

    /// <summary>目标键配置 → 输入源（可视化注册表查询用；滚轮映射为鼠标滚轮源）。</summary>
    private static InputSource? TargetToSource(TargetKeyConfig t) => t.Kind switch
    {
        TargetKind.Keyboard => new InputSource
        {
            Kind = InputKind.Keyboard,
            VirtualKey = t.VirtualKey,
            Extended = t.Extended
        },
        TargetKind.Mouse => new InputSource { Kind = InputKind.Mouse, Mouse = t.Mouse },
        TargetKind.Wheel => new InputSource { Kind = InputKind.Mouse, Mouse = t.Wheel },
        _ => null
    };

    /// <summary>
    /// 幽灵按住项回收（UI 线程看门狗周期调用）。DD 注入与物理输入在系统层不可区分，
    /// EchoGuard 消注入回波时可能误吞物理释放（停止连发的抬起被吞 → 按住项残留 →
    /// 快照恒非空 → 键帽永久卡在按下态）。对每个按住项探测实时键态：
    /// 系统层已弹起却仍留存的即幽灵，移除并刷新快照让悬浮层弹起收尾。
    /// 连发任务激活期间目标键键态本身在抖动（间隔上限 90000ms，固定时间窗已覆盖不了
    /// 整个发射周期），一律停发后再回收，避免脉冲间隙把物理按住项误判为幽灵。
    /// </summary>
    private void PurgeGhostHeld()
    {
        lock (_gate)
        {
            if (_held.Count == 0 || !_globalEnabled) return;
            if (_firing) return;

            var purged = false;
            for (var i = _held.Count - 1; i >= 0; i--)
            {
                if (IsSourceDown(_held[i].Source)) continue;
                _held.RemoveAt(i);
                purged = true;
            }
            if (!purged) return;
        }
        DispatchSnapshot(bumpLabel: null);
    }

    /// <summary>键位当前是否处于按住态（滚轮无按住态，恒视为按住，由自带虚拟释放收尾）。</summary>
    private static bool IsSourceDown(InputSource source)
    {
        var vk = source.Kind switch
        {
            InputKind.Keyboard => source.VirtualKey,
            InputKind.Mouse => source.Mouse switch
            {
                MouseInput.Left => 0x01,
                MouseInput.Right => 0x02,
                MouseInput.Middle => 0x04,
                MouseInput.XButton1 => 0x05,
                MouseInput.XButton2 => 0x06,
                _ => 0,
            },
            _ => 0,
        };
        return vk == 0 || (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;
    }

    /// <summary>替换注册表（方案变更 / 配置导入后由 VM 全量重建）。未列出的键位视为未注册（不显示）。</summary>
    public void ReplaceRegistry(IEnumerable<KeyValuePair<string, bool>> entries)
    {
        lock (_gate)
        {
            _registry.Clear();
            foreach (var entry in entries)
            {
                var source = TaskSchedulerService.ParseSourceKey(entry.Key);
                if (source is null) continue;
                _registry[source] = new VisualKeyState(entry.Value);
            }
        }
    }

    /// <summary>更新单个键位的显示开关（方案行 eye 图标切换）。</summary>
    public void SetVisible(string key, bool visible)
    {
        var source = TaskSchedulerService.ParseSourceKey(key);
        if (source is null) return;
        lock (_gate)
        {
            // 未注册的键位忽略（eye 只对已注册方案的行有意义）。
            if (!_registry.ContainsKey(source)) return;
            _registry[source] = new VisualKeyState(visible);
        }
    }

    /// <summary>
    /// 全局可视化闸门（总开关）：关闭时所有键位统一暂停显示（各键开关状态保留，恢复后延续）；
    /// 配置加载后调用一次以还原持久化状态。
    /// </summary>
    public void SetGlobalEnabled(bool enabled)
    {
        lock (_gate)
        {
            _globalEnabled = enabled;
            if (!enabled) _held.Clear();   // 禁用即清空按住列表，屏幕键帽由空快照淡出收尾
        }
        DispatchSnapshot(bumpLabel: null);
    }

    // ---------- 状态提醒键帽（底栏按钮：总开关开启时常驻应用图标键帽，连发期间隐藏） ----------
    // 提醒键帽的显隐无视键位可视化总开关（_globalEnabled），只由以下状态决定：
    // 按钮开启 + 应用总开关开启 + 无连发任务激活 + 停发恢复倒计时未挂起。
    // _masterSwitch/_reminderEnabled 由 VM 侧线程写入（volatile 保证悬浮窗线程可见）；
    // _firing 与 _reminderShown/_reminderResumeTimer 仅悬浮窗线程（调度器事件经 Dispatcher 封送）读写。

    /// <summary>全部连发停止后恢复提醒键帽显示的延时。</summary>
    private static readonly TimeSpan ReminderResumeDelay = TimeSpan.FromSeconds(2.5);

    private volatile bool _masterSwitch;      // 应用总开关（GloballyEnabled；随 VM 属性同步）
    private volatile bool _reminderEnabled;   // 底栏“状态提醒”按钮开关
    private volatile bool _firing;            // 连发任务激活态（FiringChanged 镜像；PurgeGhostHeld 主线程读取）
    private bool _reminderShown;     // 提醒键帽当前已应用到悬浮层的期望态（悬浮层未创建前不推进）
    private DispatcherTimer? _reminderResumeTimer;   // 停发后恢复倒计时（期间再次连发则取消）

    /// <summary>同步应用总开关状态（VM 的 GloballyEnabled 变更时调用；与键位可视化闸门无关）。</summary>
    public void SetMasterSwitch(bool enabled)
    {
        _masterSwitch = enabled;
        _dispatcher?.BeginInvoke(() =>
        {
            if (!enabled) _reminderResumeTimer?.Stop();
            RefreshReminder();
        });
    }

    /// <summary>状态提醒开关（底栏按钮切换；持久化由 VM 写配置负责）。</summary>
    public void SetReminderEnabled(bool enabled)
    {
        _reminderEnabled = enabled;
        _dispatcher?.BeginInvoke(() =>
        {
            if (!enabled) _reminderResumeTimer?.Stop();
            RefreshReminder();
        });
    }

    /// <summary>连发任务激活状态变更（调度器线程触发，封送 UI 线程）：
    /// 开始连发 → 立即隐藏提醒键帽；全部停止 → 2.5s 后恢复。</summary>
    private void OnFiringChanged(bool firing)
    {
        _dispatcher?.BeginInvoke(() =>
        {
            _firing = firing;
            if (firing)
            {
                _reminderResumeTimer?.Stop();   // 重新连发：取消挂起的恢复倒计时
            }
            else if (_reminderEnabled && _masterSwitch)
            {
                // 全部连发停止：启动恢复倒计时（停止 → 再连发 → 停止时 Stop/Start 重置）。
                if (_reminderResumeTimer is null)
                {
                    _reminderResumeTimer = new DispatcherTimer { Interval = ReminderResumeDelay };
                    _reminderResumeTimer.Tick += (_, _) =>
                    {
                        _reminderResumeTimer?.Stop();
                        RefreshReminder();
                    };
                }
                _reminderResumeTimer.Stop();
                _reminderResumeTimer.Start();
            }
            RefreshReminder();
        });
    }

    /// <summary>按当前状态计算提醒键帽期望态并同步悬浮层（UI 线程调用）。</summary>
    private void RefreshReminder()
    {
        var desired = _reminderEnabled && _masterSwitch && !_firing
                      && _reminderResumeTimer is not { IsEnabled: true };
        if (desired == _reminderShown) return;
        WebReminder?.Invoke(desired);   // Web 叠加源与悬浮层无关：状态变更即通知（悬浮窗未创建时也生效）
        if (_overlay is null) return;   // 悬浮窗未创建：不推进 _reminderShown（AttachOverlay 后重放）
        _reminderShown = desired;
        _overlay.SetReminderVisible(desired);
    }

    // ---------- 切换方案热键键帽（触发即显示目标方案代号，与状态提醒同语义独立运行） ----------
    // 键帽显隐无视键位可视化总开关（_globalEnabled）与模式过滤，不做物理按键快照登记；
    // 走连发脉冲通道渲染：一次完整按下弹起后按空闲宽限独立淡出（触发后自然消失）。

    /// <summary>切换方案键帽的按压时长（一次自然敲击的按下弹起节奏）。</summary>
    private const int CycleKeycapHoldMs = 80;

    /// <summary>
    /// 切换方案热键触发键帽：显示目标方案名称（自定义名；未命名回退默认序号 ①②③④）。
    /// 重复触发复用同帽递增连击角标；停止触发 1.2s 后随活动巡检独立淡出移除。UI 线程外可安全调用（内部封送）。
    /// </summary>
    public void ShowCycleKeycap(string label)
    {
        WebPulse?.Invoke(label, CycleKeycapHoldMs, 1);   // Web 叠加源（与悬浮层提示键帽同语义）
        var overlay = _overlay;
        var dispatcher = _dispatcher;
        dispatcher?.BeginInvoke(() => overlay?.HoldPulse(label, CycleKeycapHoldMs, repeat: 1));
    }

    private void OnSourceDown(InputSource source)
    {
        string label;
        lock (_gate)
        {
            if (!_globalEnabled) return;
            var registered = _registry.TryGetValue(source, out var state);
            if (!PassesModeFilter(source, registered)) return;
            if (registered && !state.Visible) return;

            label = CanonicalLabel(source);
            // 同键重复 down（OS 自动重复 / 连发）与同族修饰键（左/右 Ctrl）去重：同帽计数即可。
            if (_held.All(h => h.Label != label))
                _held.Add((source, label));
        }
        DispatchSnapshot(bumpLabel: label);

        // 滚轮无释放事件（钩子只产生 down）：延时虚拟释放，否则键帽会永久停留。
        if (source.Kind == InputKind.Mouse && source.Mouse is MouseInput.WheelUp or MouseInput.WheelDown)
        {
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                await System.Threading.Tasks.Task.Delay(250);
                lock (_gate)
                {
                    _held.RemoveAll(h => h.Source.Equals(source));
                }
                DispatchSnapshot(bumpLabel: null);   // 禁用态自动转空快照清屏
            });
        }
    }

    private void OnSourceUp(InputSource source)
    {
        lock (_gate)
        {
            // 释放不做可见性过滤：down 被过滤的键本就不在 held 中（移除为无操作），
            // 而 down 已入 held 但配置中途变更（eye / 模式切换）时仍能正确释放不泄漏。
            _held.RemoveAll(h => h.Source.Equals(source));
            // 释放后始终派发：禁用态由 DispatchSnapshot 转空快照清屏，
            // 避免总开关/模式中途切换时屏幕残留冻结键帽。
        }
        DispatchSnapshot(bumpLabel: null);
    }

    /// <summary>把当前快照封送 UI 线程全量协调渲染（bumpLabel 指定本次按下递增连击的键帽）。
    /// 快照按 UI 帧合并：按键事件风暴（物理自动重复约 30 次/秒 + 多任务脉冲）不再逐事件
    /// 排队渲染，UI 线程每次只渲染最新快照；合并期内的连击 bump 按键累加，角标计数不丢。</summary>
    private void DispatchSnapshot(string? bumpLabel)
    {
        List<string> snapshot;
        lock (_gate)
            snapshot = _globalEnabled ? BuildSnapshotLocked() : new List<string>();
        // Web 叠加源（OBS 浏览器源）：与悬浮层同一份快照，悬浮窗未创建（--no-overlay）时照常流出。
        Dictionary<string, int> webBumps;
        lock (_dispatchGate)
            webBumps = new Dictionary<string, int>(_pendingBumps);
        WebSnapshot?.Invoke(snapshot, webBumps);
        if (_dispatcher is null) return;   // 悬浮窗未创建：丢弃（与旧版 BeginInvoke 落空同语义，防排队标志卡死）
        lock (_dispatchGate)
        {
            _pendingSnapshot = snapshot;
            if (bumpLabel is not null)
                _pendingBumps[bumpLabel] = (_pendingBumps.TryGetValue(bumpLabel, out var n) ? n : 0) + 1;
            if (_snapshotQueued) return;
            _snapshotQueued = true;
        }
        _dispatcher.BeginInvoke(() =>
        {
            List<string> snap;
            Dictionary<string, int> bumps;
            lock (_dispatchGate)
            {
                snap = _pendingSnapshot!;
                bumps = _pendingBumps;
                _pendingSnapshot = null;
                _pendingBumps = new Dictionary<string, int>();
                _snapshotQueued = false;
            }
            _overlay?.UpdateKeys(snap, bumps);
        });
    }

    /// <summary>按住键位快照（调用方持锁）：修饰键在前（仅物理按住事件跟踪），其余按按下顺序；全程应用模式过滤。</summary>
    private List<string> BuildSnapshotLocked()
    {
        var labels = new List<string>();
        AppendModifier(labels, 0xA2, 0xA3, "Ctrl");
        AppendModifier(labels, 0xA0, 0xA1, "Shift");
        AppendModifier(labels, 0xA4, 0xA5, "Alt");
        AppendModifier(labels, 0x5B, 0x5C, "Win");
        foreach (var h in _held)
        {
            if (IsModifierKey(h.Source)) continue;
            var registered = _registry.ContainsKey(h.Source);
            if (!PassesModeFilter(h.Source, registered)) continue;
            labels.Add(h.Label);
        }
        return labels;
    }

    /// <summary>
    /// 修饰键去重追加（同族左右键共用一帽）。仅跟踪物理按住事件，不再用 GetAsyncKeyState 兜底：
    /// 连发任务注入的修饰键按下/抬起会被实时键态误采样成真实按键，产生永不释放的幽灵键帽
    /// （“穿插键位松开后可视化仍显示穿插键位”的根因）。
    /// </summary>
    private void AppendModifier(List<string> labels, int vkL, int vkR, string name)
    {
        foreach (var h in _held)
        {
            if (h.Source.Kind != InputKind.Keyboard) continue;
            if (h.Source.VirtualKey != vkL && h.Source.VirtualKey != vkR) continue;
            if (PassesModeFilter(h.Source, _registry.ContainsKey(h.Source)))
                labels.Add(name);
            return;   // 同族只取第一个按住项（左右键共用一帽）
        }
    }

    /// <summary>键位显示标签（修饰键归一化为家族名，左右键共用同帽；鼠标左/中/右键用简称
    /// ——键帽面按标签查表渲染 lucide 图标；滚轮/侧键用短文本）。</summary>
    private static string CanonicalLabel(InputSource s) => s.Kind switch
    {
        InputKind.Keyboard when s.VirtualKey is 0xA2 or 0xA3 => "Ctrl",
        InputKind.Keyboard when s.VirtualKey is 0xA0 or 0xA1 => "Shift",
        InputKind.Keyboard when s.VirtualKey is 0xA4 or 0xA5 => "Alt",
        InputKind.Mouse => s.Mouse switch
        {
            MouseInput.Left => "左键",
            MouseInput.Middle => "中键",
            MouseInput.Right => "右键",
            MouseInput.WheelUp => "↑滚",
            MouseInput.WheelDown => "↓滚",
            MouseInput.XButton1 => "X1",
            MouseInput.XButton2 => "X2",
            _ => InputNameMapper.GetMouseName(s.Mouse),
        },
        _ => InputNameMapper.GetSourceName(s),
    };

    /// <summary>
    /// 连发目标键的键帽显示名：与物理按下的键帽标签同源（鼠标左/中/右键为图标键帽的标识名，
    /// 滚轮/侧键为短文本），保证“同键触发连发”时脉冲与物理按键复用同一键帽；其余键位与方案列表显示名一致。
    /// </summary>
    private static string KeycapTargetName(TargetKeyConfig t)
    {
        var baseName = t.Kind switch
        {
            TargetKind.Mouse => t.Mouse switch
            {
                MouseInput.Left => "左键",
                MouseInput.Middle => "中键",
                MouseInput.Right => "右键",
                MouseInput.XButton1 => "X1",
                MouseInput.XButton2 => "X2",
                _ => InputNameMapper.GetMouseName(t.Mouse),
            },
            TargetKind.Wheel => t.Wheel == MouseInput.WheelUp ? "↑滚" : "↓滚",
            _ => InputNameMapper.GetTargetName(t),
        };
        if (t.ModCtrl) baseName = "Ctrl+" + baseName;
        if (t.ModShift) baseName = "Shift+" + baseName;
        if (t.ModAlt) baseName = "Alt+" + baseName;
        return baseName;
    }
}
