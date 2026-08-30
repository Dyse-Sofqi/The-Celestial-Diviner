using System.IO;

namespace TheCelestialDiviner.Helpers;

/// <summary>
/// 轻量文本日志：写入 %APPDATA%\TheCelestialDiviner\logs\app-yyyyMMdd.log。
/// 线程安全；失败时静默忽略（日志不应影响主流程）。
/// </summary>
public static class Logger
{
    private static readonly object Gate = new();
    private static string? _logDir;

    private static string LogDir =>
        _logDir ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Constants.AppFolderName, "logs");

    /// <summary>记录一条信息日志。</summary>
    public static void Info(string message) => Write("INFO", message);

    /// <summary>记录一条警告日志。</summary>
    public static void Warn(string message) => Write("WARN", message);

    /// <summary>记录一条错误日志（含异常）。</summary>
    public static void Error(string message, Exception? ex = null)
        => Write("ERROR", ex is null ? message : $"{message} | {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDir);
                var file = Path.Combine(LogDir, $"app-{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(file,
                    $"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // 日志写入失败时静默忽略，避免影响主流程。
        }
    }
}
