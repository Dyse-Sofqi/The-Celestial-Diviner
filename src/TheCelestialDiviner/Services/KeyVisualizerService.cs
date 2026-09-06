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
/// 订阅全局钩子的按下 / 释放事件，把命中注册表的按键封送到 UI 线程驱动悬浮层。
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

    /// <summary>
    /// 当前按住中的键位（按下顺序；真源）。释放时移除，快照驱动悬浮层逐键独立更新，
    /// 多键同时按住时各自保持按压态互不顶替。
    /// </summary>
    private readonly List<(InputSource Source, string Label)> _held = new();

    /// <summary>最近一次目标键脉冲时刻（Environment.TickCount）。发射中目标键键态在抖动，不做幽灵判定。</summary>
    private int _lastFireTick;

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
    private KeycapOverlayWindow? _overlay;
    private Dispatcher? _dispatcher;

    /// <summary>悬浮窗创建前的暂存位置（AttachOverlay 时应用；修复启动时序：VM 构造早于窗口创建）。</summary>
    private (double Left, double Top)? _pendingPosition;

    public KeyVisualizerService(InputHookService hooks)
    {
        _hooks = hooks;
        hooks.SourceDown += OnSourceDown;
        hooks.SourceUp += OnSourceUp;
    }

    /// <summary>初始化悬浮层（UI 线程调用；重复调用安全）。创建后立即应用暂存位置。</summary>
    public void AttachOverlay()
    {
        if (_overlay is not null) return;
        _dispatcher = Application.Current?.Dispatcher;
        _overlay = new KeycapOverlayWindow();
        // 启动时序：VM 构造时窗口尚未存在，SetPosition 只能暂存；窗口创建后在此应用。
        if (_pendingPosition is { } pos)
            _overlay.SetSavedPosition(pos.Left, pos.Top);

        // 幽灵按住项看门狗：EchoGuard 吞掉物理释放时的兜底回收（见 PurgeGhostHeld）。
        _ghostTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _ghostTimer.Tick += (_, _) => PurgeGhostHeld();
        _ghostTimer.Start();

        // 悬浮层创建后补应用此前积压的提醒状态（VM 构造期 SetReminderEnabled 无处落地）。
        RefreshReminder();
    }

    /// <summary>进入位置调整模式（虚拟键帽拖拽，确认回调 Left/Top）。</summary>
    public void BeginAdjust(Action<double, double> onConfirm) =>
        _overlay?.BeginAdjust(onConfirm);

    /// <summary>设置悬浮窗位置（null = 忽略）。窗口未创建时暂存，创建后自动应用。</summary>
    public void SetPosition(double? left, double? top)
    {
        if (left is not { } l || top is not { } t) return;
        if (_overlay is { } overlay)
            overlay.SetSavedPosition(l, t);
        else
            _pendingPosition = (l, t);
    }

    /// <summary>订阅调度器连发脉冲（目标键每发射一次 → 键帽脉冲 + 连击角标）；
    /// 同时订阅连发激活状态（状态提醒键帽的显隐驱动）。</summary>
    public void AttachScheduler(TaskSchedulerService scheduler)
    {
        scheduler.TargetFired += OnTargetFired;
        scheduler.FiringChanged += OnFiringChanged;
    }

    /// <summary>连发目标键脉冲（任务线程触发，封送 UI 线程；不占用物理按住快照）。</summary>
    private void OnTargetFired(TargetKeyConfig target)
    {
        bool global;
        lock (_gate)
            global = _globalEnabled;
        if (!global) return;

        System.Threading.Volatile.Write(ref _lastFireTick, Environment.TickCount);

        var label = KeycapTargetName(target);
        var holdMs = target.HoldMs;
        var overlay = _overlay;
        var dispatcher = _dispatcher;
        dispatcher?.BeginInvoke(() => overlay?.HoldPulse(label, holdMs, repeat: 1));
    }

    /// <summary>
    /// 幽灵按住项回收（UI 线程看门狗周期调用）。DD 注入与物理输入在系统层不可区分，
    /// EchoGuard 消注入回波时可能误吞物理释放（停止连发的抬起被吞 → 按住项残留 →
    /// 快照恒非空 → 键帽永久卡在按下态）。对每个按住项探测实时键态：
    /// 系统层已弹起却仍留存的即幽灵，移除并刷新快照让悬浮层弹起收尾。
    /// 连发发射中目标键键态本身在抖动，1 秒内不做判定（停发后下一轮再回收）。
    /// </summary>
    private void PurgeGhostHeld()
    {
        lock (_gate)
        {
            if (_held.Count == 0 || !_globalEnabled) return;
            if (Environment.TickCount - System.Threading.Volatile.Read(ref _lastFireTick) < 1000) return;

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
    // 以下字段仅 UI 线程读写（调度器事件经 Dispatcher 封送），无需加锁。

    /// <summary>全部连发停止后恢复提醒键帽显示的延时。</summary>
    private static readonly TimeSpan ReminderResumeDelay = TimeSpan.FromSeconds(2.5);

    private bool _masterSwitch;      // 应用总开关（GloballyEnabled；随 VM 属性同步）
    private bool _reminderEnabled;   // 底栏“状态提醒”按钮开关
    private bool _firing;            // 连发任务激活态（FiringChanged 镜像）
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
        if (desired == _reminderShown || _overlay is null) return;
        _reminderShown = desired;
        _overlay.SetReminderVisible(desired);
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

    /// <summary>把当前快照封送 UI 线程全量协调渲染（bumpLabel 指定本次按下递增连击的键帽）。</summary>
    private void DispatchSnapshot(string? bumpLabel)
    {
        List<string> snapshot;
        lock (_gate)
            snapshot = _globalEnabled ? BuildSnapshotLocked() : new List<string>();
        var overlay = _overlay;
        var dispatcher = _dispatcher;
        dispatcher?.BeginInvoke(() => overlay?.UpdateKeys(snapshot, bumpLabel));
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
