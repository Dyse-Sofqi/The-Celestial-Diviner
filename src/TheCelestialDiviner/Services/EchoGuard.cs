using System.Collections.Concurrent;
using TheCelestialDiviner.Helpers;

namespace TheCelestialDiviner.Services;

/// <summary>
/// 驱动注入回环消除器（物理级注入模式共用：DD / Interception）。
///
/// dd63330 与 Interception 都经内核键盘栈注入，事件在系统层面与物理按键完全同源：
/// 无 LLKHF_INJECTED 标记、Raw Input 亦归因到物理键盘设备——低级钩子
/// 无法从事件本身区分"自己注入的回环"与"用户物理输入"。若不消除，
/// 任务注入的目标键会再次触发注册源：Toggle 自环方案被回环立即关断
/// （表现即"开关模式连发莫名其妙自动停止"）、Hold 自环方案被回环抬起不断打断。
///
/// 识别依据（顺序配额）：注入与回环在同一虚拟键同一方向上严格保序、一一对应。
/// 每次成功注入登记一个"待决回环名额"（FIFO），该键同方向的下一次到达事件
/// 即消费一个名额判为回环——不设到达延迟门槛。旧版按"注入时刻 ±12ms 时间窗"
/// 匹配，游戏卡顿引发的输入管线延迟尖峰下回环迟到即漏判，漏过的回环被当成
/// 物理按键把运行中的 Toggle 翻转关断；FIFO 按序匹配对延迟完全不敏感。
///
/// 撞车自愈：物理事件与回环抢中同一名额时，真实回环因名额耗尽而漏过，
/// 整体效果守恒——例如自环 Toggle 的物理按键要么自身翻转、要么其 displaced
/// 回环翻转，恰好一次；Hold 自环的物理释放要么直接通过、要么由 displaced
/// 回环抬起来停止任务，不会出现任务卡死。
///
/// 名额有过期时间：回环真丢失（钩子线程被系统超时跳过、钩子被移除等异常）
/// 时名额到期失效并在下次该键事件时惰性清理，不会长期滞留吞掉之后的物理
/// 按键。代价：回环丢失后、过期前的第一次物理按键可能被吞（按了无效果，
/// 再按恢复）——属罕见极端情况，且好于漏气回环把 Toggle 翻转关断。
/// </summary>
public static class EchoGuard
{
    /// <summary>名额过期时间（毫秒）。到期名额视为回环真丢失，在下次该键事件时
    /// 惰性清理。须覆盖极端输入管线积压下的回环延迟（秒级整体冻结除外——
    /// 冻结期间注入与回环一起停摆，恢复后按序到达仍在窗口内）。</summary>
    private const int ExpiryMs = 2000;

    /// <summary>
    /// 每个虚拟键每个方向的待决名额上限。**这个值不能大**：名额一旦因回环丢失而滞留，
    /// 每一个滞留名额都会在之后"吃掉"一次同键同方向的物理按键（自环开关按一次停不下来，
    /// 就是这么来的）。正常深度 = 往返延迟 / 连发周期（DD 172ms ÷ 26ms ≈ 7，取 4 倍裕量），
    /// 超出丢最旧：宁可漏判回环（事件按物理输入处理），也不让物理按键被连续吞掉。
    /// </summary>
    private const int MaxPendingPerVk = 32;

    /// <summary>
    /// 回环往返延迟估计（毫秒，EWMA）。两条驱动路线差别很大：
    /// Interception 实测 0~16ms（探针 40Hz 压测，中位 0ms），DD 实测约 170ms
    /// （dd63330 虚拟设备固定上报节拍）。用实测值自适应，比写死时间窗更稳。
    /// </summary>
    private static int _expectedRttMs = 60;

    /// <summary>
    /// 名额可接受的最大到达延迟：超过即视为"这条回环没回来"（钩子被系统跳过 /
    /// 驱动丢事件），丢弃名额并把当前事件当物理输入放行 —— 关键取舍：
    /// 宁可漏判一次回环（多触发一次），也不吞掉用户的物理按键（停不下来）。
    /// </summary>
    private static int AcceptLimitMs
    {
        get
        {
            var limit = _expectedRttMs * 2 + 40;
            if (limit < 60) limit = 60;
            if (limit > 600) limit = 600;
            return limit;
        }
    }

    /// <summary>
    /// 一次注入的待决回环名额：登记时刻 + 驱动调用耗时（调用返回后回填）。
    /// 耗时单列出来是为了把"到达延迟"拆成两段——延迟里含驱动调用本身的阻塞时间，
    /// 只看总数会把驱动卡顿误判成 win32k 队列积压。
    /// </summary>
    public sealed class InjectionNote
    {
        /// <summary>登记时刻（Environment.TickCount，与 KBDLLHOOKSTRUCT.time 同时基）。</summary>
        public readonly int Stamp = Environment.TickCount;

        /// <summary>驱动调用耗时（毫秒）；-1 = 调用尚未返回（回环先到了）。</summary>
        public int CostMs = -1;
    }

    private sealed class VkState
    {
        /// <summary>每个虚拟键独立的锁：钩子线程处理某个键时，不必排在别的键的注入后面。</summary>
        public readonly object Gate = new();
        public readonly List<InjectionNote> PendingDown = new();
        public readonly List<InjectionNote> PendingUp = new();
    }

    // 按 VK 分片而非一把全局锁：原先"钩子线程处理任意按键"与"所有注入线程"共用一把锁，
    // 多任务连发（30+ 任务 ≈ 600 次注入/秒）时输入路径被锁住，表现为点击迟滞 / 拖窗口发涩。
    private static readonly ConcurrentDictionary<int, VkState> States = new();
    private static int _lastEchoLatencyLogTick;   // 回环到达延迟告警限频（Environment.TickCount）
    private static int _lastDropLogTick;          // 名额丢弃告警限频（同上）

    /// <summary>
    /// 预登记待决回环名额（注入线程调用，必须在 DD_key 之前——
    /// 回环事件可能在注入调用返回之前就到达钩子）。
    /// 返回名额对象，供注入失败时 <see cref="CancelNote"/> 撤销、并在调用返回后回填耗时。
    /// </summary>
    public static InjectionNote NoteInjection(int vk, bool down)
    {
        var state = States.GetOrAdd(vk, _ => new VkState());
        lock (state.Gate)
        {
            var list = down ? state.PendingDown : state.PendingUp;
            if (list.Count >= MaxPendingPerVk) list.RemoveAt(0);
            var note = new InjectionNote();
            list.Add(note);
            return note;
        }
    }

    /// <summary>注入失败时撤销预登记的名额（注入线程调用）。</summary>
    public static void CancelNote(int vk, bool down, InjectionNote note)
    {
        if (!States.TryGetValue(vk, out var state)) return;
        lock (state.Gate)
        {
            (down ? state.PendingDown : state.PendingUp).Remove(note);
        }
    }

    /// <summary>
    /// 钩子线程判定一个键盘事件是否为本进程驱动注入的回环。
    /// 返回 true = 判定回环（调用方应丢弃该事件，不得分发给输入源）。
    /// </summary>
    /// <param name="vk">事件虚拟键码。</param>
    /// <param name="down">true = 按下事件。</param>
    /// <param name="eventTime">KBDLLHOOKSTRUCT.time（事件生成时刻，毫秒时基与 Environment.TickCount 一致）。</param>
    public static bool TryConsume(int vk, bool down, int eventTime)
    {
        if (!States.TryGetValue(vk, out var state)) return false;   // 该键没有待决名额：不建状态、不取锁
        lock (state.Gate)
        {
            var list = down ? state.PendingDown : state.PendingUp;
            if (list.Count == 0) return false;

            // 丢弃"不可能还是回环"的名额：过期（> ExpiryMs）或超过本路线的往返上限。
            // 这一步是防"吞物理按键"的关键——名额滞留时，唯一的下场就是吞掉用户的下一次真实按键
            // （自环开关按一次停不下来）。所以超出窗口的名额一律丢弃，并让当前事件按物理输入放行。
            var dropped = 0;
            while (list.Count > 0)
            {
                var head = list[0];
                var age = unchecked(eventTime - head.Stamp);
                if (age < 0 || age > ExpiryMs || age > AcceptLimitMs)
                {
                    list.RemoveAt(0);
                    dropped++;
                    continue;
                }
                break;
            }
            if (dropped > 0 && unchecked(Environment.TickCount - _lastDropLogTick) > 2000)
            {
                _lastDropLogTick = Environment.TickCount;
                Logger.Warn($"丢弃 {dropped} 个未匹配的回环名额（vk=0x{vk:X2}, down={down}；"
                            + $"回环可能被系统跳过或驱动丢失，本事件按物理输入处理）。");
            }
            if (list.Count == 0) return false;

            // FIFO 按序消除：同键同方向事件在输入管线内保序，注入名额与回环事件一一对应。
            // 诊断埋点：名额登记（注入时刻）→ 回环到达（钩子）的管线延迟。
            // 这段延迟 = 驱动调用阻塞 + win32k 投递排队，两者必须分开报，
            // 否则"驱动卡 1.8 秒"会被读成"输入队列积压 1.8 秒"，把排查引向错误方向。
            var note = list[0];
            list.RemoveAt(0);
            var latency = unchecked(eventTime - note.Stamp);
            ObserveRoundTrip(latency);
            if (latency > 100 && unchecked(Environment.TickCount - _lastEchoLatencyLogTick) > 2000)
            {
                _lastEchoLatencyLogTick = Environment.TickCount;
                var split = note.CostMs < 0
                    ? "驱动调用尚未返回（回环先到）"
                    : $"其中驱动调用 {note.CostMs}ms、win32k 投递 {latency - note.CostMs}ms";
                Logger.Warn($"注入输入到达延迟 {latency}ms（vk=0x{vk:X2}, down={down}；{split}）。");
            }
            return true;
        }
    }

    /// <summary>
    /// 用实测回环延迟更新估计值（EWMA，限幅 0~600ms）。
    /// 两条路线的往返差异很大（Interception ≈0~16ms，DD ≈170ms），
    /// 自适应比写死窗口更稳；模式切换时由 <see cref="Reset"/> 重置成该路线的种子值。
    /// </summary>
    private static void ObserveRoundTrip(int latencyMs)
    {
        if (latencyMs < 0) return;
        if (latencyMs > 600) latencyMs = 600;
        var next = (_expectedRttMs * 7 + latencyMs) / 8;
        _expectedRttMs = next < 0 ? 0 : next > 600 ? 600 : next;
    }

    /// <summary>
    /// 清空全部状态（驱动重新初始化 / 切换注入模式时调用，防止跨驱动、跨会话的陈旧名额）。
    /// </summary>
    /// <param name="seedRttMs">
    /// 该路线的回环延迟种子值：Interception 用 20（实测 0~16ms），DD 用 170（实测约 170ms），
    /// 未知路线用 60。种子决定切换后最初几个事件的可接受窗口。
    /// </param>
    public static void Reset(int seedRttMs = 60)
    {
        States.Clear();
        _expectedRttMs = seedRttMs < 0 ? 0 : seedRttMs > 600 ? 600 : seedRttMs;
    }
}
