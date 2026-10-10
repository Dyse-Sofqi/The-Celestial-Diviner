using System.Collections.Concurrent;
using System.IO;

namespace TheCelestialDiviner.Helpers;

/// <summary>
/// 轻量文本日志：写入 %APPDATA%\TheCelestialDiviner\logs\app-yyyyMMdd.log。
/// 后台单线程异步落盘：调用方仅入队即返回（无磁盘 I/O、无锁等待），
/// 钩子 / 连发 / UI 线程的高频路径不再被写盘阻塞。队列有界，写盘严重积压时
/// 丢弃最旧日志（日志永不影响主流程）；文件按天滚动，进程正常退出时冲刷余量。
/// </summary>
public static class Logger
{
    private static readonly BlockingCollection<string> Queue = new(new ConcurrentQueue<string>(), 4096);
    private static readonly string? LogDir = CreateLogDir();

    static Logger()
    {
        var worker = new Thread(DrainLoop) { Name = "Logger", IsBackground = true };
        worker.Start();
        // 正常退出路径冲刷队列余量（强杀进程允许丢失尾部日志）。
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Queue.CompleteAdding(); } catch { /* 已关闭则忽略 */ }
            try { worker.Join(1500); } catch { /* 超时放弃 */ }
        };
    }

    private static string? CreateLogDir()
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Constants.AppFolderName, "logs");
            Directory.CreateDirectory(dir);   // 只在初始化时创建一次（旧版逐条创建是热路径开销）
            return dir;
        }
        catch
        {
            return null;   // 目录不可用时静默降级为不落盘
        }
    }

    /// <summary>记录一条信息日志。</summary>
    public static void Info(string message) => Write("INFO", message);

    /// <summary>记录一条警告日志。</summary>
    public static void Warn(string message) => Write("WARN", message);

    /// <summary>记录一条错误日志（含异常）。</summary>
    public static void Error(string message, Exception? ex = null)
        => Write("ERROR", ex is null ? message : $"{message} | {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        // 入队即返回，绝不阻塞调用线程（含钩子回调）；CompleteAdding 后 TryAdd 抛异常也静默。
        try { Queue.TryAdd($"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}"); }
        catch { /* 日志写入失败时静默忽略，避免影响主流程 */ }
    }

    /// <summary>消费线程：常驻写盘，按天滚动文件；单行失败静默跳过。</summary>
    private static void DrainLoop()
    {
        StreamWriter? writer = null;
        var day = DateTime.Now.Date;
        try
        {
            foreach (var line in Queue.GetConsumingEnumerable())
            {
                try
                {
                    if (LogDir is null) continue;
                    var today = DateTime.Now.Date;
                    if (writer is null || today != day)
                    {
                        writer?.Dispose();
                        writer = new StreamWriter(Path.Combine(LogDir, $"app-{today:yyyyMMdd}.log"), append: true);
                        day = today;
                    }
                    writer.WriteLine(line);
                    writer.Flush();   // 冲到 OS 缓冲（非 fsync）：崩溃至多丢一行尾，开销可忽略
                }
                catch
                {
                    // 单行失败静默跳过（日志不应影响主流程）。
                }
            }
        }
        finally
        {
            try { writer?.Dispose(); } catch { /* 静默 */ }
        }
    }
}
