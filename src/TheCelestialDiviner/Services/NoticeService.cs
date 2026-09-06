using System.IO;
using System.Net.Http;
using System.Reflection;
using TheCelestialDiviner.Helpers;

namespace TheCelestialDiviner.Services;

/// <summary>
/// 注释区公告服务：内嵌仓库根目录的 Notice.md 作为兜底默认内容（随程序打包），
/// 启动时从 Gitee raw 地址查询远端版本——拉取成功且内容有变化才作为更新；
/// 没更新 / 断网 / 404 等任何失败一律返回 null，由调用方保持旧默认内容不变。
/// </summary>
public static class NoticeService
{
    private const string EmbeddedResourceName = "TheCelestialDiviner.Notice.md";

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

    /// <summary>内嵌默认公告（Notice.md 随程序烘焙；读取失败返回空串，注释区回退为无默认内容）。</summary>
    public static string EmbeddedNotice { get; } = LoadEmbedded();

    /// <summary>读取内嵌公告资源。</summary>
    private static string LoadEmbedded()
    {
        try
        {
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(EmbeddedResourceName);
            if (stream is null) throw new InvalidOperationException($"内嵌公告资源缺失：{EmbeddedResourceName}");
            using (stream)
            using (var reader = new StreamReader(stream))
                return reader.ReadToEnd().TrimEnd();
        }
        catch (Exception ex)
        {
            Logger.Error("内嵌公告资源读取失败。", ex);
            return "";
        }
    }

    /// <summary>
    /// 拉取远端公告。成功返回去除尾部空白的有效文本；
    /// 失败 / 超长 / 空内容返回 null（调用方保持当前内容），异常仅记文件日志不上抛。
    /// </summary>
    public static async Task<string?> FetchLatestAsync()
    {
        try
        {
            using var response = await Http.GetAsync(Constants.NoticeRemoteUrl).ConfigureAwait(false);
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
