using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
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

/// <summary>
/// 软件自更新服务（Gitee Release）：检查最新版 → 下载 zip → 解包校验 →
/// 写自更新批处理并启动（等待主程序退出 → 覆盖安装目录 → 重启 → 清理临时目录），
/// 随后由调用方走正常退出时序。脚本与更新包全部落在 %TEMP%，不污染安装目录。
/// 发版要求：Gitee 上创建 Release 并上传与仓库 dist 相同布局的 zip（根级含 exe 与 dd63330.*）。
/// </summary>
public static class UpdateService
{
    private const string LatestReleaseApi = "https://gitee.com/api/v5/repos/sofqi/The-Celestial-Diviner/releases/latest";
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

    /// <summary>
    /// 查询 Gitee 最新 Release（匿名 API；无 Release / 附件缺失 / 版本号无法解析 → null）。
    /// </summary>
    public static async Task<UpdateCheck?> FetchLatestAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var response = await Http.GetAsync(LatestReleaseApi, cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;   // 404 = 暂无 Release，其余按不可用处理
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var tag = root.GetProperty("tag_name").GetString()?.Trim() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version)) return null;

            string assetUrl = "", assetName = "";
            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";
                if (url.Length == 0 || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                assetUrl = url;
                assetName = name;
                break;
            }
            if (assetUrl.Length == 0) return null;   // 无 zip 附件 = 没有可安装的更新包

            var notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "";
            return new UpdateCheck { TagName = tag, Version = version, AssetUrl = assetUrl, AssetName = assetName, Notes = notes };
        }
        catch (Exception ex)
        {
            Logger.Info($"检查更新失败（Gitee）：{ex.Message}");
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
