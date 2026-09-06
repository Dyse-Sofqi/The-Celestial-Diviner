using System.Diagnostics;
using TheCelestialDiviner.Helpers;
using TheCelestialDiviner.Models;

namespace TheCelestialDiviner.Services;

/// <summary>单个运行中的连发任务（一个任务对应一个逻辑触发单元 + 一条线程；
/// 双宏开关的两个目标键共用一个任务，线程内轮流触发）。</summary>
public sealed class RunningTask
{
    /// <summary>目标键配置快照（方案编辑时整体重建；按索引轮流触发）。</summary>
    public required List<TargetKeyConfig> Configs { get; init; }

    /// <summary>所属输入源（用于日志显示）。</summary>
    public required InputSource Source { get; init; }

    /// <summary>开关模式分区（轮转分区内互斥：开启新键自动停止旧键）。</summary>
    public ToggleSection Section { get; init; } = ToggleSection.Normal;

    /// <summary>触发信号是否有效：Toggle=已开启 / Hold=源按住中。volatile 保证跨线程可见。</summary>
    public volatile bool Active;

    /// <summary>是否已请求停止线程（方案重建 / 退出时置位）。</summary>
    public volatile bool StopRequested;

    /// <summary>工作线程。</summary>
    public Thread? Thread;

    private int _inFlight;   // 在途点击计数（含按压 Sleep，退出 / 重建前须归零防止按键卡死）

    /// <summary>是否有正在执行的点击（按下 → 按住 → 抬起全过程）。</summary>
    public bool IsBusy => Volatile.Read(ref _inFlight) > 0;

    /// <summary>标记一次点击开始（任务线程 / 滚轮线程池回调调用）。</summary>
    public void BeginClick() => Interlocked.Increment(ref _inFlight);

    /// <summary>标记一次点击结束。</summary>
    public void EndClick() => Interlocked.Decrement(ref _inFlight);
}

/// <summary>
/// 连发任务调度器：每个启用的目标键一条独立后台线程（双宏开关的两个键共一条），
/// Stopwatch 控制间隔。
/// 模式逻辑：
/// - Toggle：注册源按一次启动连发，再按一次停止；
/// - Hold：注册源按住期间连发，松开即停；
/// - 全局暂停规则：任一 Hold 任务激活（正在连发）时，暂停所有 Toggle 任务，Hold 全部释放后恢复。
/// - 轮转互斥（ToggleSection.Rotate）：Rotate 分区某键启动时，自动停止该分区其他运行中的任务。
/// 键盘自动重复产生的连续 WM_KEYDOWN 只视为一次按下（防误切换）。
/// 滚轮输入源视为“即按即松”的脉冲（每次滚动 = 一次按下+释放）。
/// </summary>
public sealed class TaskSchedulerService
{
    /// <summary>运行日志事件（供 UI 日志面板显示）。</summary>
    public event Action<string>? Log;

    /// <summary>连发脉冲事件：目标键每实际发射一次触发一次（任务线程回调，订阅方自行封送）。</summary>
    public event Action<TargetKeyConfig>? TargetFired;

    /// <summary>
    /// 连发活动状态变更事件：从"全部停止" ↔ "任一任务激活"翻转时触发一次
    /// （参数 = 是否有连发中；钩子线程 / 任务线程回调，订阅方自行封送 UI 线程）。
    /// </summary>
    public event Action<bool>? FiringChanged;

    private readonly object _gate = new();
    private static readonly object RandGate = new();
    private static readonly Random Rand = new();   // 仅在 RandGate 内使用（.NET Framework 无 Random.Shared）
    private readonly List<RunningTask> _tasks = new();
    private readonly Dictionary<InputSource, List<RunningTask>> _sourceMap = new();
    private readonly HashSet<InputSource> _downSources = new();   // 物理按住中的输入源（防自动重复）
    private int _activeHoldCount;                                  // 正在连发的 Hold 任务数（Interlocked）
    private bool _masterEnabled = true;
    private bool _anyFiring;                                       // 上次聚合激活态（FiringChanged 去重）

    /// <summary>根据配置重建全部任务：先停止并回收旧线程，再为新方案启动线程。</summary>
    public void ApplyConfig(AppConfig config)
    {
        lock (_gate)
        {
            StopAllCore();
            foreach (var t in _tasks) t.StopRequested = true;
            _tasks.Clear();
            _sourceMap.Clear();
            _downSources.Clear();
            _activeHoldCount = 0;

            foreach (var (sourceKey, scheme) in config.Schemes)
            {
                if (!scheme.Enabled || scheme.Targets.Count == 0) continue;
                var source = ParseSourceKey(sourceKey);
                if (source is null) continue;

                var enabledTargets = scheme.Targets.Where(t => t.Enabled).ToList();
                if (enabledTargets.Count == 0) continue;

                if (scheme.Section == ToggleSection.Dual)
                {
                    // 双宏开关：两个目标键共用一个任务，由 TaskLoop 按索引轮流触发（1-2-1-2…）。
                    AddTask(new RunningTask
                    {
                        Configs = enabledTargets.Select(t => t.Clone()).ToList(),
                        Source = source.Clone(),
                        Section = scheme.Section
                    });
                }
                else
                {
                    foreach (var target in enabledTargets)
                    {
                        AddTask(new RunningTask
                        {
                            Configs = new List<TargetKeyConfig> { target.Clone() },
                            Source = source.Clone(),
                            Section = scheme.Section
                        });
                    }
                }
            }

            OnLog($"任务调度器已应用配置：{_tasks.Count} 个连发任务运行中。");
            NotifyFiringChangedLocked();   // 重建后全部处于停止态：向状态提醒键帽同步（若此前连发中）
        }
    }

    /// <summary>创建并启动一个连发任务（须在锁内调用）。</summary>
    private void AddTask(RunningTask task)
    {
        task.Thread = new Thread(() => TaskLoop(task))
        {
            Name = $"AutoFire-{InputNameMapper.GetTargetName(task.Configs[0])}",
            IsBackground = true
        };
        _tasks.Add(task);
        if (!_sourceMap.TryGetValue(task.Source, out var list))
            _sourceMap[task.Source] = list = new List<RunningTask>();
        list.Add(task);
        task.Thread.Start();
    }

    /// <summary>全局启用 / 停用。停用时立即停止所有连发（触发信号清零）。</summary>
    public void SetMasterEnabled(bool enabled)
    {
        lock (_gate)
        {
            _masterEnabled = enabled;
            if (!enabled)
            {
                StopAllCore();
                OnLog("全局停用：所有连发任务已停止。");
            }
            else
            {
                OnLog("全局启用：连发任务待触发（不自动重启）。");
            }
            NotifyFiringChangedLocked();
        }
    }

    /// <summary>全局开关当前是否启用。</summary>
    public bool MasterEnabled
    {
        get { lock (_gate) return _masterEnabled; }
    }

    /// <summary>输入源按下事件（来自钩子，UI 线程外调用）。</summary>
    public void HandleSourceDown(InputSource source)
    {
        lock (_gate)
        {
            if (!_masterEnabled) return;

            // 防键盘自动重复：按住期间重复的 down 不再处理。
            if (source.Kind == InputKind.Keyboard)
            {
                if (!_downSources.Add(source)) return;
            }
            else if (source.Kind == InputKind.Mouse && source.Mouse is not (MouseInput.WheelUp or MouseInput.WheelDown))
            {
                if (!_downSources.Add(source)) return;
            }

            if (!_sourceMap.TryGetValue(source, out var tasks)) return;

            var isWheel = source.Kind == InputKind.Mouse &&
                          source.Mouse is MouseInput.WheelUp or MouseInput.WheelDown;

            foreach (var task in tasks)
            {
                if (isWheel && task.Configs[0].Mode == TriggerMode.Hold)
                {
                    // 滚轮为脉冲输入（无“按住”状态）：Hold 目标键直接发送一次完整点击。
                    // 点击含按压时长 Sleep（毫秒级），不得阻塞钩子线程 → 投递线程池；
                    // 以配置实例为锁串行化，保证同方案连续滚动格的 down/up 严格成对不交错。
                    var cfg = task.Configs[0];
                    task.BeginClick();   // 入队前计数：防止"已入队未启动"的点击躲过退出等待
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try
                        {
                            lock (cfg)
                                InputSimulatorService.Click(cfg, JitterMs(cfg.HoldMs, Constants.MinHoldMs, Constants.MaxHoldMs));
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"滚轮脉冲点击异常（{InputNameMapper.GetTargetName(cfg)}）。", ex);
                        }
                        finally
                        {
                            task.EndClick();
                        }
                    });
                }
                else
                {
                    // Toggle：翻开关状态（含滚轮每滚动一格切换一次）；Hold：置位激活。
                    Activate(task);
                }
            }

            if (!isWheel)
                OnLog($"输入源 [{InputNameMapper.GetSourceName(source)}] 按下。");
            NotifyFiringChangedLocked();
        }
    }

    /// <summary>输入源释放事件（来自钩子，UI 线程外调用）。</summary>
    public void HandleSourceUp(InputSource source)
    {
        lock (_gate)
        {
            _downSources.Remove(source);
            if (!_masterEnabled) return;
            if (!_sourceMap.TryGetValue(source, out var tasks)) return;

            foreach (var task in tasks)
            {
                // 仅 Hold 任务依赖释放事件；Toggle 的 Active 不受 up 影响。
                if (task.Configs[0].Mode == TriggerMode.Hold) Deactivate(task);
            }

            OnLog($"输入源 [{InputNameMapper.GetSourceName(source)}] 释放。");
            NotifyFiringChangedLocked();
        }
    }

    /// <summary>停止全部任务（退出时序用；用户入口已由全局总开关取代）：
    /// 清零所有触发信号（Hold 释放、Toggle 关闭）。方案保持待触发状态。</summary>
    public void StopAll()
    {
        lock (_gate)
        {
            StopAllCore();
            OnLog("已停止所有连发任务（退出）。");
            NotifyFiringChangedLocked();
        }
    }

    /// <summary>核心停止逻辑（须在锁内调用）：解除全部激活状态，并等待在途点击完成
    /// （点击含按压 Sleep，不等待会在退出 / 重建时把目标键留在"按住"状态；
    /// 上限 500ms 覆盖最大按压 200ms + 抖动，超时放弃属可接受的极端情况）。</summary>
    private void StopAllCore()
    {
        foreach (var task in _tasks) Deactivate(task);
        _downSources.Clear();

        var deadline = Environment.TickCount + 500;
        foreach (var task in _tasks)
            while (task.IsBusy && Environment.TickCount < deadline)
                Thread.Sleep(1);
    }

    /// <summary>
    /// 输入源按下时激活任务。
    /// Toggle：翻转开关状态（启动 → 再按停止）；Hold：置位并递增全局 Hold 计数。
    /// 轮转互斥：Rotate 分区的 Toggle 被启动时，自动停止该分区内正在运行的其他任务。
    /// </summary>
    private void Activate(RunningTask task)
    {
        var name = InputNameMapper.GetTargetName(task.Configs[0]);
        if (task.Configs[0].Mode == TriggerMode.Toggle)
        {
            // 开关模式：按一次启动 / 再按一次停止。
            task.Active = !task.Active;
            if (task.Active)
            {
                OnLog($"连发 [{name}] 已启动（Toggle）。");
                if (task.Section == ToggleSection.Rotate)
                    StopOtherRotateTasks(task);
            }
            else
            {
                OnLog($"连发 [{name}] 已停止（Toggle）。");
            }
        }
        else if (!task.Active)
        {
            // 按压模式：置位激活，全局 Hold 计数 +1（暂停所有 Toggle）。
            task.Active = true;
            Interlocked.Increment(ref _activeHoldCount);
        }
    }

    /// <summary>轮转互斥（须在锁内调用）：停止 Rotate 分区内其他正在运行的任务。</summary>
    private void StopOtherRotateTasks(RunningTask keepTask)
    {
        foreach (var other in _tasks)
        {
            if (ReferenceEquals(other, keepTask)) continue;
            if (other.Section != ToggleSection.Rotate || !other.Active) continue;
            Deactivate(other);
            OnLog($"连发 [{InputNameMapper.GetTargetName(other.Configs[0])}] 已自动停止（轮转互斥）。");
        }
    }

    /// <summary>解除激活。Hold 解除会使全局 Hold 计数 -1，从而恢复被暂停的 Toggle。</summary>
    private void Deactivate(RunningTask task)
    {
        if (!task.Active) return;
        task.Active = false;
        if (task.Configs[0].Mode == TriggerMode.Hold)
            Interlocked.Decrement(ref _activeHoldCount);
    }

    /// <summary>聚合连发激活态并在翻转时触发事件（须在锁内调用）：任一任务 Active 即视为连发中
    /// （Toggle 被 Hold 暂停时 Hold 必在连发，语义一致）；状态无变化不触发。</summary>
    private void NotifyFiringChangedLocked()
    {
        var any = _tasks.Any(t => t.Active);
        if (any == _anyFiring) return;
        _anyFiring = any;
        FiringChanged?.Invoke(any);
    }

    /// <summary>任务主循环：Stopwatch + Sleep(1)（配合 timeBeginPeriod(1)）控制间隔；异常自动恢复。
    /// 每轮按压时长与间隔各自独立抖动 ±20%（消除恒定周期的机器指纹）。
    /// 多目标任务（双宏开关）按索引轮流触发，未触发时相位归零（重新激活从第 1 键开始）。</summary>
    private void TaskLoop(RunningTask task)
    {
        var sw = new Stopwatch();
        var phase = 0;

        while (!task.StopRequested)
        {
            try
            {
                var isToggle = task.Configs[0].Mode == TriggerMode.Toggle;

                // 触发条件：信号有效，且 Toggle 类任务未被任何 Hold 暂停。
                var firing = task.Active &&
                             (!isToggle || Interlocked.CompareExchange(ref _activeHoldCount, 0, 0) == 0);

                if (!firing)
                {
                    phase = 0; // 停止期间相位归零：重新激活时从第 1 个目标键开始。
                    Thread.Sleep(2);
                    sw.Reset();
                    continue;
                }

                // 发射一次完整输入（按住 HoldMs → 抬起）；多目标依次轮换。
                var cfg = task.Configs[phase];
                task.BeginClick();
                try
                {
                    InputSimulatorService.Click(cfg, JitterMs(cfg.HoldMs, Constants.MinHoldMs, Constants.MaxHoldMs));
                }
                finally
                {
                    task.EndClick();
                }
                TargetFired?.Invoke(cfg);   // 可视化：目标键脉冲键帽
                phase = (phase + 1) % task.Configs.Count;
                sw.Restart();

                // 间隔等待（弹起 → 下次按下，独立抖动）：保持响应停止信号，每毫秒轮询一次。
                var gapMs = JitterMs(cfg.IntervalMs, Constants.MinIntervalMs, Constants.MaxIntervalMs);
                while (!task.StopRequested && task.Active &&
                       (!isToggle || Interlocked.CompareExchange(ref _activeHoldCount, 0, 0) == 0) &&
                       sw.ElapsedMilliseconds < gapMs)
                {
                    Thread.Sleep(1);
                }
            }
            catch (Exception ex)
            {
                // 任务异常：记录日志后短暂休眠继续循环（自动恢复，不中断服务）。
                Logger.Error($"连发任务 [{InputNameMapper.GetTargetName(task.Configs[0])}] 异常。", ex);
                OnLog($"连发任务 [{InputNameMapper.GetTargetName(task.Configs[0])}] 异常已自动恢复。");
                Thread.Sleep(100);
            }
        }
    }

    /// <summary>
    /// 时序抖动：baseMs × (1 ± 20%) 均匀取整后钳位到 [min, max]。
    /// 按压与间隔各自独立调用（不共享随机量），避免固定比例形成可识别节奏。
    /// </summary>
    private static int JitterMs(int baseMs, int min, int max)
    {
        lock (RandGate)
        {
            // 百分比在 80~120 含端点均匀取值，整数运算：26ms → 20~31ms。
            var percent = 100 - Constants.TimingJitterPercent + Rand.Next(Constants.TimingJitterPercent * 2 + 1);
            return Compat.Clamp(baseMs * percent / 100, min, max);
        }
    }

    /// <summary>解析配置字典键为输入源（格式：K:VK:EXT 或 M:MouseInput）。</summary>
    public static InputSource? ParseSourceKey(string key)
    {
        try
        {
            var parts = key.Split(':');
            if (parts.Length == 3 && parts[0] == "K")
            {
                return new InputSource
                {
                    Kind = InputKind.Keyboard,
                    VirtualKey = int.Parse(parts[1]),
                    Extended = parts[2] == "1"
                };
            }
            if (parts.Length == 2 && parts[0] == "M" &&
                Enum.TryParse<MouseInput>(parts[1], out var mouse))
            {
                return new InputSource { Kind = InputKind.Mouse, Mouse = mouse };
            }
        }
        catch
        {
            // 非法键：返回 null 由调用方跳过。
        }
        return null;
    }

    /// <summary>生成配置字典键（与 ParseSourceKey 对应）。</summary>
    public static string BuildSourceKey(InputSource source) => source.Kind switch
    {
        InputKind.Keyboard => $"K:{source.VirtualKey}:{(source.Extended ? 1 : 0)}",
        InputKind.Mouse => $"M:{source.Mouse}",
        _ => throw new ArgumentOutOfRangeException(nameof(source))
    };

    private void OnLog(string message)
    {
        Logger.Info(message);
        Log?.Invoke(message);
    }
}
