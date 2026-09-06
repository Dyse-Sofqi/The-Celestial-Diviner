namespace TheCelestialDiviner.Services;

/// <summary>
/// DD 注入回环消除器（仅 DD 键盘注入模式使用）。
///
/// dd63330 经键盘类驱动过滤注入，事件在系统层面与物理按键完全同源：
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

    /// <summary>每个虚拟键每个方向的待决名额上限。须覆盖过期窗口内的全部注入
    /// （极限档约 45 发/秒 × 2s = 90，取 128 留裕量），超出丢弃最旧名额，
    /// 防止回环系统性丢失（钩子失效）时名额无限累积。</summary>
    private const int MaxPendingPerVk = 128;

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

            // 惰性过期：回环真丢失的名额到期释放，避免陈旧名额吞掉物理事件。
            list.RemoveAll(t => unchecked(eventTime - t) > ExpiryMs);
            if (list.Count == 0) return false;

            // FIFO 按序消除：同键同方向事件在输入管线内保序，注入名额与回环
            // 事件一一对应；不限到达延迟（时间窗硬门槛会在延迟尖峰下漏判）。
            list.RemoveAt(0);
            return true;
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
}
