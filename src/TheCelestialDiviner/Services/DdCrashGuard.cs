using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using TheCelestialDiviner.Helpers;

namespace TheCelestialDiviner.Services;

/// <summary>
/// DD 驱动「崩溃熔断」状态守卫。
///
/// 为什么需要：dd63330.sys 是闭源内核驱动，自身存在 use-after-free（客户机上 4 份转储
/// 全部停在同一个指令偏移 `+0x10D2`）。用户态改不了驱动里那行代码，但可以保证
/// **崩过一次之后不再自动往同一个坑里跳**：只要上一次是带着 DD 驱动跑的、且没有干净收场，
/// 这次启动就强制退回普通模式，把"要不要继续用 DD"交回给用户手动决定。
///
/// 两个互相独立的信号，覆盖两种不同的死法：
/// 1. **会话标记残留**：进程没走完退出时序（蓝屏时进程还活着 / 被强杀 / 断电）；
/// 2. **BugCheck 事件**：进程正常退出了，但驱动在客户端消失几十秒后才崩
///    （本项目的实际故障形态，也是"刚打开就蓝"的真正成因）。
///
/// 只看 0x50 这一种代码：DD 的已知缺陷是访问非法内核内存，其他蓝屏（显卡/内存/磁盘）
/// 不该导致 DD 被熔断，否则会把用户往普通模式里白推一次。
/// </summary>
public static class DdCrashGuard
{
    /// <summary>参与熔断的键盘模式：DD 虚拟驱动。</summary>
    public const int DdKeyboardMode = 3;

    /// <summary>BugCheck 代码：PAGE_FAULT_IN_NONPAGED_AREA。</summary>
    private const uint BugCheckPageFaultInNonPagedArea = 0x50;

    /// <summary>蓝屏事件与本程序上次启动的最大关联间隔：超出即视为无关的另一次蓝屏。</summary>
    private static readonly TimeSpan BugCheckWindow = TimeSpan.FromHours(12);

    private static string StateDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Constants.AppFolderName);

    /// <summary>会话标记：DD 初始化前写入，正常退出时删除。残留 = 上次没走完退出时序。</summary>
    private static string MarkerPath => Path.Combine(StateDir, "dd-session.txt");

    /// <summary>上次运行快照：启动时间 + 当时使用的键盘模式，供下次启动做关联判定。</summary>
    private static string LastRunPath => Path.Combine(StateDir, "dd-last-run.txt");

    /// <summary>
    /// 标记文件是否由**本进程**写入。重复启动的第二个实例会立刻退出并走 OnExit，
    /// 它绝不能顺手删掉第一个实例的标记（否则熔断会被静默掏空）。
    /// </summary>
    private static bool _sessionMarked;

    /// <summary>
    /// 标记"本进程正在使用 DD 内核驱动"。必须写在 **DD 初始化之前**：如果初始化本身
    /// 就把机器蓝屏了，下次启动才能看出"上次是带着 DD 挂掉的"。
    /// </summary>
    public static void MarkSessionInUse()
    {
        try
        {
            Directory.CreateDirectory(StateDir);
            var text = string.Join("\n", new[]
            {
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture),
                typeof(DdCrashGuard).Assembly.GetName().Version?.ToString() ?? "unknown"
            });
            File.WriteAllText(MarkerPath, text);
            _sessionMarked = true;
        }
        catch (Exception ex)
        {
            // 熔断是兜底能力，写不进去不能让主流程失败。
            Logger.Warn($"写入 DD 会话标记失败（崩溃熔断本次失效）：{ex.Message}");
        }
    }

    /// <summary>
    /// 退出时序调用：撤掉会话标记，表示这次是正常退出。
    /// **只有本进程写过标记才删**——重复启动的第二个实例会在秒退时走 OnExit，
    /// 它若顺手删掉正在运行那个实例的标记，熔断就被掏空了。
    /// </summary>
    public static void MarkCleanExit()
    {
        if (!_sessionMarked) return;
        _sessionMarked = false;
        DeleteMarker();
    }

    private static void DeleteMarker()
    {
        try
        {
            if (File.Exists(MarkerPath)) File.Delete(MarkerPath);
        }
        catch (Exception ex)
        {
            Logger.Warn($"清除 DD 会话标记失败（下次启动可能误判为异常退出）：{ex.Message}");
        }
    }

    /// <summary>
    /// 记录本次运行快照（启动时间 + 实际使用的键盘模式）。每次启动、以及用户切换模式时调用，
    /// 供下次启动判断"上次是不是带着 DD 跑的"。
    /// </summary>
    public static void SaveRunState(int keyboardMode, DateTime? startedAt = null)
    {
        try
        {
            Directory.CreateDirectory(StateDir);
            var start = (startedAt ?? DateTime.Now).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            File.WriteAllText(LastRunPath, string.Join("\n", new[]
            {
                start,
                keyboardMode.ToString(CultureInfo.InvariantCulture),
                typeof(DdCrashGuard).Assembly.GetName().Version?.ToString() ?? "unknown"
            }));
        }
        catch (Exception ex)
        {
            Logger.Warn($"写入上次运行快照失败（崩溃熔断本次失效）：{ex.Message}");
        }
    }

    /// <summary>
    /// 判断上一次运行是否"带着 DD 崩了"。读取即消费：会话标记会被删除，避免同一次崩溃
    /// 反复触发；BugCheck 判定靠时间窗口自然失效（下次启动会刷新运行快照）。
    /// </summary>
    /// <param name="detail">人类可读的原因（写日志 / 展示给用户）。</param>
    /// <returns>true = 需要熔断（调用方应退回普通模式）。</returns>
    public static bool TryDetectPreviousCrash(out string? detail)
    {
        detail = null;
        try
        {
            var lastRun = ReadLastRunState();
            var markerLeft = File.Exists(MarkerPath);

            if (markerLeft)
            {
                DeleteMarker();   // 消费掉，保证只触发一次
                if (lastRun.RunAt is null || lastRun.Mode != DdKeyboardMode)
                {
                    Logger.Info("检测到 DD 会话标记残留，但上次运行不是 DD 模式，不触发熔断。");
                    return false;
                }
                detail = $"上次运行未正常结束（{lastRun.RunAt:MM-dd HH:mm} 启动，没走完退出流程）";
                return true;
            }

            if (lastRun.RunAt is null || lastRun.Mode != DdKeyboardMode) return false;

            var bugCheck = FindLatestBugCheck();
            if (bugCheck is null) return false;

            var when = bugCheck.Value.When;
            if (when < lastRun.RunAt.Value || when > lastRun.RunAt.Value + BugCheckWindow)
            {
                // 蓝屏早于我们启动（别的故障）或晚太多（与本程序无关），不牵连 DD。
                return false;
            }

            if (!IsDdRelatedBugCheck(bugCheck.Value.Code))
            {
                // 显卡 / 内存 / 磁盘之类与本程序无关的蓝屏，不该把用户推回普通模式。
                Logger.Info($"上次运行后系统蓝屏过，但终止代码 0x{bugCheck.Value.Code:X} 不属于 DD 已知缺陷，不熔断。");
                return false;
            }

            detail = $"上次运行后系统蓝屏过（{when:MM-dd HH:mm}，终止代码 0x{bugCheck.Value.Code:X}）";
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn($"崩溃熔断判定失败（按未崩溃处理，不影响启动）：{ex.Message}");
            return false;
        }
    }

    private static (DateTime? RunAt, int Mode) ReadLastRunState()
    {
        try
        {
            if (!File.Exists(LastRunPath)) return (null, -1);
            var lines = File.ReadAllLines(LastRunPath);
            if (lines.Length < 2) return (null, -1);

            DateTime? start = null;
            if (DateTime.TryParseExact(lines[0].Trim(), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsed))
            {
                start = parsed;
            }
            var mode = int.TryParse(lines[1].Trim(), out var m) ? m : -1;
            return (start, mode);
        }
        catch
        {
            return (null, -1);
        }
    }

    /// <summary>
    /// 读取最近一次蓝屏（WER-SystemErrorReporting 事件 1001）的时间与终止代码。
    /// 只取最新一条（ReverseDirection），走系统日志索引查询，耗时在毫秒级。
    /// </summary>
    private static (DateTime When, uint Code)? FindLatestBugCheck()
    {
        try
        {
            var query = new EventLogQuery("System", PathType.LogName,
                "*[System[Provider[@Name='Microsoft-Windows-WER-SystemErrorReporting'] and (EventID=1001)]]")
            {
                ReverseDirection = true
            };
            using var reader = new EventLogReader(query);
            var record = reader.ReadEvent();
            if (record is null) return null;
            if (record.TimeCreated is null) return null;

            var code = ExtractBugCheckCode(record);
            Logger.Info($"最近一次蓝屏记录：{record.TimeCreated:yyyy-MM-dd HH:mm:ss}，终止代码 0x{code:X}。");
            return (record.TimeCreated.Value, code);
        }
        catch (Exception ex)
        {
            // 日志被清理 / 无权限读取：当作没有证据，不熔断。
            Logger.Warn($"查询蓝屏事件失败（崩溃熔断跳过事件判定）：{ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 从事件里取终止代码。优先读结构化属性（0x00000050 这种字符串或数值），
    /// 取不到再退回解析消息文本（不同语言版本的消息格式一致，代码总是 0x 前缀）。
    /// </summary>
    private static uint ExtractBugCheckCode(EventRecord record)
    {
        try
        {
            var raw = record.Properties?.FirstOrDefault()?.Value?.ToString();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                var text = raw!.Trim();
                if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    if (uint.TryParse(text.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
                        return hex;
                }
                else if (uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dec))
                {
                    return dec;
                }
            }

            var description = record.FormatDescription();
            if (!string.IsNullOrEmpty(description))
            {
                var idx = description.IndexOf("0x", StringComparison.OrdinalIgnoreCase);
                if (idx >= 0 && description.Length > idx + 2)
                {
                    var length = Math.Min(8, description.Length - idx - 2);
                    var tail = description.Substring(idx + 2, length);
                    if (uint.TryParse(tail, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                        return code;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"解析蓝屏事件终止代码失败：{ex.Message}");
        }
        return 0;
    }

    /// <summary>该终止代码是否属于 DD 驱动的已知缺陷（0x50）。</summary>
    public static bool IsDdRelatedBugCheck(uint code) => code == BugCheckPageFaultInNonPagedArea;
}
