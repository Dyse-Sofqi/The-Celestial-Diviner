using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using TheCelestialDiviner.Helpers;

namespace TheCelestialDiviner.Services;

/// <summary>Gitee Release 检查结果（远端版本 + zip 附件 + 发布说明）。</summary>
public sealed class UpdateCheck
{
    /// <summary>远端标签名（如 v1.6.1）。</summary>
    public required string TagName { get; init; }

    /// <summary>解析后的远端版本号。</summary>
    public required Version Version { get; init; }

    /// <summary>zip 附件下载地址。</summary>
    public required string AssetUrl { get; init; }

    /// <summary>zip 附件文件名。</summary>
    public required string AssetName { get; init; }

    /// <summary>发布说明（Release body，原样 markdown 文本）。</summary>
    public string Notes { get; init; } = "";
}

/// <summary>检查更新探测结果：<see cref="Check"/> 与 <see cref="Failure"/> 互斥（有版本或有原因）。</summary>
public sealed class UpdateProbe
{
    public UpdateCheck? Check { get; init; }

    /// <summary>失败原因（可直接展示给用户的一句话；Check 为 null 时非空）。</summary>
    public string? Failure { get; init; }

    public static UpdateProbe Ok(UpdateCheck check) => new() { Check = check };
    public static UpdateProbe Fail(string reason) => new() { Failure = reason };
}

/// <summary>
/// 软件自更新服务（Gitee Release）：检查最新版 → 下载 zip → 解包校验 →
/// 写自更新批处理并启动（等待主程序退出 → 覆盖安装目录 → 重启 → 清理临时目录），
/// 随后由调用方走正常退出时序。脚本与更新包全部落在 %TEMP%，不污染安装目录。
/// 发版要求：Gitee 上创建 Release 并上传与仓库 dist 相同布局的 zip
/// （根级为与 zip 同名的包裹目录，内含 exe 与依赖 DLL；不含闭源 DD 驱动——
/// 用户自行从 ddxoft 官网获取，覆盖安装不会删除已放置的驱动。
/// 下方解包下钻逻辑同样兼容旧根级散文件布局）。
/// </summary>
public static class UpdateService
{
    private const string ExeName = "TheCelestialDiviner.exe";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };   // 超时按请求单独控制
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TheCelestialDiviner");
        client.DefaultRequestHeaders.CacheControl =
            new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        return client;
    }

    /// <summary>当前程序版本（csproj Version；显示取前 3 段，如 1.6.0）。</summary>
    public static Version CurrentVersion { get; } =
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    /// <summary>当前程序版本短显示（前 3 段，如 1.7.5；标题栏 / 托盘 / 更新提示共用）。</summary>
    public static string DisplayVersion => CurrentVersion.ToString(3);

    private const string LatestReleaseApi = "https://gitee.com/api/v5/repos/sofqi/The-Celestial-Diviner/releases/latest";

    /// <summary>备用通道：Gitee raw 的 CHANGELOG.md（raw 走 giteeusercontent 静态分发，
    /// 不在 Release API 的匿名限流体系内；CHANGELOG 首个 "## vX.Y.Z" 即最新发布版本）。</summary>
    private const string RawChangelogUrl = "https://gitee.com/sofqi/The-Celestial-Diviner/raw/main/CHANGELOG.md";

    /// <summary>更新包附件名约定（dist 与双端 Release 同一命名）；Gitee 自动附带的源码包
    /// （vX.Y.Z.zip / .tar.gz）不以 TheCelestialDiviner 开头，天然被排除。</summary>
    private static readonly Regex s_assetNameRegex =
        new(@"^TheCelestialDiviner-.+-win64\.zip$", RegexOptions.IgnoreCase);

    /// <summary>CHANGELOG.md 里的版本标题（首个即最新发布段；「未发布」段不以 v 开头，天然跳过）。</summary>
    private static readonly Regex s_changelogVersionRegex = new(@"^##\s+(v\d+(?:\.\d+){1,3})\b", RegexOptions.Multiline);

    /// <summary>
    /// 检查 Gitee 最新 Release：主通道 = Release API（匿名）；
    /// API 不可用（限流 / 无响应）时降级 raw CHANGELOG 备用通道。
    /// 任何失败都带原因返回（<see cref="UpdateProbe.Failure"/>），不再笼统一句"暂无 Release"。
    /// </summary>
    public static async Task<UpdateProbe> FetchLatestAsync()
    {
        var (check, failure) = await FetchFromReleaseApiAsync().ConfigureAwait(false);
        if (check is not null) return UpdateProbe.Ok(check);

        var fallback = await FetchFromRawChangelogAsync().ConfigureAwait(false);
        if (fallback is not null)
        {
            Logger.Info($"Gitee Release API 不可用（{failure}），备用通道（raw CHANGELOG）发现 {fallback.TagName}。");
            return UpdateProbe.Ok(fallback);
        }
        Logger.Info($"检查更新失败（Gitee）：{failure}");
        return UpdateProbe.Fail(failure ?? "远端不可用");
    }

    /// <summary>主通道：Gitee Release API。返回 (结果, 失败原因)——成功时原因 null，失败时结果 null。</summary>
    private static async Task<(UpdateCheck? Check, string? Failure)> FetchFromReleaseApiAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var response = await Http.GetAsync(LatestReleaseApi, cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                // 403 = Gitee 匿名 API 按 IP 限流（响应体 "403 Forbidden (Rate Limit Exceeded)"），
                // 与"暂无 Release"（404）分开说，用户才知道稍后再试即可。
                return (null, response.StatusCode == HttpStatusCode.Forbidden
                    ? "Gitee 接口限流（403，稍后再试，或到 Release 页手动下载）"
                    : $"Gitee 接口返回 {(int)response.StatusCode}（{response.StatusCode}）");
            }
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var tag = root.GetProperty("tag_name").GetString()?.Trim() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version))
                return (null, $"Gitee Release 版本号无法解析（tag={tag}）");

            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";
                if (url.Length == 0 || !s_assetNameRegex.IsMatch(name)) continue;
                var notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "";
                return (new UpdateCheck { TagName = tag, Version = version, AssetUrl = url, AssetName = name, Notes = notes }, null);
            }
            return (null, "Gitee Release 没有更新包附件（TheCelestialDiviner-*-win64.zip）");
        }
        catch (OperationCanceledException)
        {
            return (null, "网络超时（10 秒无响应）");
        }
        catch (HttpRequestException ex)
        {
            return (null, $"网络不可用（{ex.Message}）");
        }
        catch (Exception ex)
        {
            Logger.Info($"检查更新失败（Gitee Release API）：{ex.Message}");
            return (null, "Gitee 返回内容无法解析");
        }
    }

    /// <summary>
    /// 备用通道：从 Gitee raw 的 CHANGELOG.md 推断最新版本（首个 "## vX.Y.Z" 标题），
    /// 按发版命名约定（TheCelestialDiviner-x.y.z-win64.zip）拼 Release 直链下载地址。
    /// raw 有分钟级缓存，作为"API 被限流时仍能发现 / 安装更新"的兜底足够；任何失败返回 null
    /// （主流程继续报主通道的失败原因）。
    /// </summary>
    private static async Task<UpdateCheck?> FetchFromRawChangelogAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var response = await Http.GetAsync(RawChangelogUrl, cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var match = s_changelogVersionRegex.Match(text);
            if (!match.Success) return null;
            var tag = match.Groups[1].Value;                       // "v1.8.4"（标题原文，免得重拼丢段）
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version)) return null;
            var name = $"TheCelestialDiviner-{tag.TrimStart('v', 'V')}-win64.zip";
            return new UpdateCheck
            {
                TagName = tag,
                Version = version,
                AssetUrl = $"https://gitee.com/sofqi/The-Celestial-Diviner/releases/download/{tag}/{name}",
                AssetName = name,
                Notes = $"（经备用通道获取，更新说明请到 Gitee Release 页查看）\n\n发现新版本 {tag}。",
            };
        }
        catch (Exception ex)
        {
            Logger.Info($"备用通道（raw CHANGELOG）失败：{ex.Message}");
            return null;
        }
    }

    /// <summary>下载更新包到 %TEMP%\TheCelestialDiviner\Update，返回 zip 路径；失败抛异常由调用方提示。</summary>
    public static async Task<string> DownloadAsync(UpdateCheck check)
    {
        var dir = Path.Combine(Path.GetTempPath(), Constants.AppFolderName, "Update");
        Directory.CreateDirectory(dir);
        var zipPath = Path.Combine(dir, check.AssetName);

        // net48 无带 CancellationToken 的流式读取重载：默认完成模式整包缓冲下载，
        // 取消令牌覆盖全程（含内容读取），3 分钟超时兜底（更新包 ~2MB）。
        using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3)))
        using (var response = await Http.GetAsync(check.AssetUrl, cts.Token).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            File.WriteAllBytes(zipPath, await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false));
        }
        return zipPath;
    }

    /// <summary>
    /// 解包更新 zip 并启动自更新批处理（隐藏窗口）：轮询等待主程序退出 → xcopy 覆盖安装目录 →
    /// 重启主程序 → 清理临时目录与脚本自身。调用方随即走正常退出时序（IsExiting = true + ExitApp）。
    /// </summary>
    public static void PrepareAndLaunchUpdater(string zipPath)
    {
        var root = Path.Combine(Path.GetTempPath(), Constants.AppFolderName, "Update");
        var payloadDir = Path.Combine(root, "payload");
        if (Directory.Exists(payloadDir)) Directory.Delete(payloadDir, true);
        // net48 的 ExtractToDirectory 无 overwriteFiles 参数：payload 目录已先删除，全新解包不冲突。
        ZipFile.ExtractToDirectory(zipPath, payloadDir);

        // 兼容压缩包内套一层目录的情况：根下没有 exe 而唯一子目录里有 → 下钻。
        if (!File.Exists(Path.Combine(payloadDir, ExeName)))
        {
            var dirs = new DirectoryInfo(payloadDir).GetDirectories();
            if (dirs.Length == 1 && File.Exists(Path.Combine(dirs[0].FullName, ExeName)))
                payloadDir = dirs[0].FullName;
        }
        if (!File.Exists(Path.Combine(payloadDir, ExeName)))
            throw new InvalidOperationException("更新包内容异常（未找到主程序 exe）。");

        var appDir = AppContext.BaseDirectory;
        var batPath = Path.Combine(root, "update.bat");
        // 批处理仅用 ASCII（规避代码页问题）：等待进程退出 → 覆盖 → 重启 → 清理。
        File.WriteAllText(batPath, string.Join("\r\n",
            "@echo off",
            "setlocal",
            "set \"SRC=%~1\"",
            "set \"DST=%~2\"",
            ":waitloop",
            $"tasklist /FI \"IMAGENAME eq {ExeName}\" 2>nul | find /I \"{ExeName}\" >nul",
            "if not errorlevel 1 (",
            "  ping -n 2 127.0.0.1 >nul",
            "  goto waitloop",
            ")",
            "xcopy \"%SRC%\\*\" \"%DST%\\\" /e /y /q /i >nul 2>&1",
            $"start \"\" \"%DST%\\{ExeName}\"",
            "rd /s /q \"%~dp0payload\" 2>nul",
            "del /q \"%~dp0*.zip\" 2>nul",
            "del \"%~f0\""));

        Process.Start(new ProcessStartInfo
        {
            FileName = batPath,
            Arguments = $"\"{payloadDir}\" \"{appDir}\"",
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }
}
