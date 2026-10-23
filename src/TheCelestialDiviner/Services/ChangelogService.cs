using System.IO;
using System.Reflection;
using TheCelestialDiviner.Helpers;

namespace TheCelestialDiviner.Services;

/// <summary>
/// 更新日志服务：读取构建时内嵌的仓库根目录 CHANGELOG.md（「选项 → 更新日志」窗口的数据源）。
/// 与 Notice.md 同策略——文件缺失 / 读取失败不抛异常，返回空串由窗口显示占位文案。
/// </summary>
public static class ChangelogService
{
    private const string ResourceName = "TheCelestialDiviner.CHANGELOG.md";

    private static string? _cached;

    /// <summary>读取内嵌更新日志全文（首次读入后缓存；失败返回空串）。</summary>
    public static string Load()
    {
        if (_cached is not null) return _cached;
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
            if (stream is null) throw new InvalidOperationException($"内嵌更新日志资源缺失：{ResourceName}");
            using var reader = new StreamReader(stream);
            _cached = reader.ReadToEnd().Replace("\r\n", "\n").TrimEnd();
        }
        catch (Exception ex)
        {
            Logger.Error("内嵌更新日志读取失败。", ex);
            _cached = "";
        }
        return _cached;
    }
}
