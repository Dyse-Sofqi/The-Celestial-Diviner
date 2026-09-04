namespace TheCelestialDiviner.Services;

/// <summary>
/// DD 注入回环消除器（仅 DD 键盘注入模式使用）。
///
/// dd63330 经键盘类驱动过滤注入，事件在系统层面与物理按键完全同源：
/// 无 LLKHF_INJECTED 标记、Raw Input 亦归因到物理键盘设备——低级钩子
/// 无法从事件本身区分"自己注入的回环"与"用户物理输入"。若不消除，
/// 任务注入的目标键会再次触发注册源：Toggle 自环方案被回环立即关断、
/// Hold 自环方案被回环抬起不断打断（表现即"F1-F12 区域在 DD 模式失效"）。
///
/// 识别依据（时间线）：回环事件由本进程注入产生，其到达钩子的时刻
/// （KBDLLHOOKSTRUCT.time，与注入同一时基）与注入时刻相差仅数毫秒。
/// 每次成功注入登记一个"待决回环名额"，事件在名额有效期内到达即判回环。
///
/// 撞车自愈：物理事件与回环抢中同一名额时，真实回环因名额耗尽而漏过，
/// 整体效果守恒——例如自环 Toggle 的物理按键要么自身翻转、要么其 displaced
/// 回环翻转，恰好一次；Hold 自环的物理释放要么直接通过、要么由 displaced
/// 回环抬起来停止任务，不会出现任务卡死。
///
/// 名额有过期时间：回环未按期到达（钩子被系统移除等异常）自动失效，
/// 不会吞掉之后的物理事件。
/// </summary>
public static class EchoGuard
{
    /// <summary>注入 → 回环到达的最大允许延迟。实测 1~3ms，留裕量。
    /// 依赖程序启动时的 timeBeginPeriod(1)（TimerResolutionService）提供毫秒级
    /// 时间戳粒度——默认 15.6ms 粒度下预登记与内核事件时间戳可能跨刻度相差 15.6ms。</summary>
    private const int MatchWindowMs = 12;

    /// <summary>每个虚拟键每个方向的待决名额上限。须覆盖 MatchWindowMs 内的全部
    /// 注入（最小连发间隔 1ms → 约 12 个 + 裕量），否则钩子线程被抢占、回环
    /// 延迟送达时，旧时间戳在池中找不到匹配名额而漏过。</summary>
    private const int MaxPendingPerVk = 24;

    private sealed class VkState
    {
        public readonly List<int> PendingDown = new();
        public readonly List<int> PendingUp = new();
    }

    private static readonly object Gate = new();
    private static readonly Dictionary<int, VkState> States = new();

    /// <summary>
    /// 预登记待决回环名额（注入线程调用，必须在 DD_key 之前——
    /// 回环事件可能在注入调用返回之前就到达钩子）。
    /// 返回名额时间戳，供注入失败时 <see cref="CancelNote"/> 撤销。
    /// </summary>
    public static int NoteInjection(int vk, bool down)
    {
        lock (Gate)
        {
            var state = GetOrCreate(vk);
            var list = down ? state.PendingDown : state.PendingUp;
            if (list.Count >= MaxPendingPerVk) list.RemoveAt(0);
            var stamp = Environment.TickCount;
            list.Add(stamp);
            return stamp;
        }
    }

    /// <summary>注入失败时撤销预登记的名额（注入线程调用）。</summary>
    public static void CancelNote(int vk, bool down, int stamp)
    {
        lock (Gate)
        {
            if (!States.TryGetValue(vk, out var state)) return;
            (down ? state.PendingDown : state.PendingUp).Remove(stamp);
        }
    }

    /// <summary>
    /// 钩子线程判定一个键盘事件是否为本进程 DD 注入的回环。
    /// 返回 true = 判定回环（调用方应丢弃该事件，不得分发给输入源）。
    /// </summary>
    /// <param name="vk">事件虚拟键码。</param>
    /// <param name="down">true = 按下事件。</param>
    /// <param name="eventTime">KBDLLHOOKSTRUCT.time（事件生成时刻，毫秒时基与 Environment.TickCount 一致）。</param>
    public static bool TryConsume(int vk, bool down, int eventTime)
    {
        lock (Gate)
        {
            if (!States.TryGetValue(vk, out var state)) return false;
            var list = down ? state.PendingDown : state.PendingUp;
            if (list.Count == 0) return false;

            // 取与事件时间最接近且在窗口内的名额（突发延迟下比"最旧优先"分布更均匀）。
            var best = -1;
            var bestDelta = int.MaxValue;
            for (var i = 0; i < list.Count; i++)
            {
                var delta = unchecked(eventTime - list[i]);
                if (delta is >= -MatchWindowMs and <= MatchWindowMs && (best < 0 || Abs(delta) < bestDelta))
                {
                    best = i;
                    bestDelta = Abs(delta);
                }
            }
            if (best >= 0)
            {
                list.RemoveAt(best);
                return true;
            }

            // 清理过期名额（回环未到达的异常情况），避免陈旧名额吞掉物理事件。
            list.RemoveAll(t => unchecked(eventTime - t) > MatchWindowMs);
            return false;
        }
    }

    /// <summary>清空全部状态（DD 驱动重新初始化时调用，防止跨会话陈旧名额）。</summary>
    public static void Reset()
    {
        lock (Gate) States.Clear();
    }

    private static VkState GetOrCreate(int vk)
    {
        if (!States.TryGetValue(vk, out var state))
            States[vk] = state = new VkState();
        return state;
    }

    private static int Abs(int v) => v < 0 ? -v : v;
}
