using System.IO;
using System.Net.Http;
using System.Reflection;
using TheCelestialDiviner.Helpers;

namespace TheCelestialDiviner.Services;

/// <summary>
/// 注释区公告服务：内嵌仓库根目录的公告文件作为兜底默认内容（随程序打包），
/// 启动时从 Gitee raw 地址查询远端版本——拉取成功且内容有变化才作为更新；
/// 没更新 / 断网 / 404 等任何失败一律返回 null，由调用方保持旧默认内容不变。
/// 主题预设各有一份公告：默认主题 / 衍天高手 = Notice.md，莫问高手 = Notice2.md（按主题取内嵌与远端地址）。
/// </summary>
public static class NoticeService
{
    /// <summary>内嵌公告文本缓存（资源名 → 文本；读取失败为空串）。</summary>
    private static readonly Dictionary<string, string> EmbeddedCache = new(StringComparer.Ordinal);

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        // Gitee raw 会 302 到 raw.giteeusercontent.com（带签名的临时地址），HttpClient 默认跟随重定向。
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(4)   // 公告检查不得拖慢启动（后台执行，超时即放弃）
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TheCelestialDiviner");
        client.DefaultRequestHeaders.CacheControl =
            new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };   // 公告要拿最新
        return client;
    }

    /// <summary>指定主题预设的内嵌默认公告（Notice.md / Notice2.md 随程序烘焙；
    /// 读取失败返回空串，注释区回退为无默认内容）。</summary>
    public static string EmbeddedNotice(SectTheme theme) => LoadEmbedded(theme.NoticeResourceName);

    /// <summary>读取内嵌公告资源（按资源名缓存，重复调用不重复读取）。</summary>
    private static string LoadEmbedded(string resourceName)
    {
        lock (EmbeddedCache)
        {
            if (EmbeddedCache.TryGetValue(resourceName, out var cached)) return cached;
        }

        string text;
        try
        {
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            if (stream is null) throw new InvalidOperationException($"内嵌公告资源缺失：{resourceName}");
            using (stream)
            using (var reader = new StreamReader(stream))
                text = reader.ReadToEnd().TrimEnd();
        }
        catch (Exception ex)
        {
            Logger.Error($"内嵌公告资源读取失败（{resourceName}）。", ex);
            text = "";
        }

        lock (EmbeddedCache)
        {
            EmbeddedCache[resourceName] = text;
        }
        return text;
    }

    /// <summary>
    /// 拉取远端公告。成功返回去除尾部空白的有效文本；
    /// 失败 / 超长 / 空内容返回 null（调用方保持当前内容），异常仅记文件日志不上抛。
    /// </summary>
    public static async Task<string?> FetchLatestAsync(string remoteUrl)
    {
        try
        {
            using var response = await Http.GetAsync(remoteUrl).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            var text = (await response.Content.ReadAsStringAsync().ConfigureAwait(false)).TrimEnd();
            return text.Length == 0 || text.Length > Constants.NoticeMaxLength ? null : text;
        }
        catch (Exception ex)
        {
            Logger.Info($"公告远端检查失败（保持当前内容）：{ex.Message}");
            return null;
        }
    }
}
