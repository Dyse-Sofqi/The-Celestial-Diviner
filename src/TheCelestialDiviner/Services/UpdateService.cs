using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
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
/// 软件自更新服务（Gitee Release）：检查最新版 → 下载 zip → 解包校验 → 备份当前安装目录 →
/// 写自更新批处理并启动（覆盖（带重试）→ 核对文件清单 → 成功重启新版本 / 失败回滚并重启旧版本 →
/// 写结果标记 → 清理临时目录），随后由调用方走正常退出时序。脚本与更新包全部落在 %TEMP%，不污染安装目录。
/// 发版要求：Gitee 上创建 Release 并上传与仓库 dist 相同布局的 zip
/// （根级为与 zip 同名的包裹目录，内含 exe 与依赖 DLL；不含闭源 DD 驱动——
/// 用户自行从 ddxoft 官网获取，覆盖安装不会删除已放置的驱动。
/// 下方解包下钻逻辑同样兼容旧根级散文件布局）。
/// </summary>
public static class UpdateService
{
    private const string ExeName = "TheCelestialDiviner.exe";

    /// <summary>更新包、payload、备份与脚本的统一临时根目录。</summary>
    private static string UpdateRoot => Path.Combine(Path.GetTempPath(), Constants.AppFolderName, "Update");

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
        var dir = UpdateRoot;
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
    /// 解包更新 zip → 校验内容 → 可写性预检 → 备份当前安装目录 → 写自更新批处理并启动。
    /// 调用方随即走正常退出时序；脚本在进程退出后完成覆盖与重启。
    ///
    /// 加固要点（历史事故：个别用户点「有更新」后首次启动即崩在 ConfigService 的类型初始值
    /// 设定项异常，根因是覆盖结果半新半旧——而旧脚本无论如何都会把 exe 拉起来）：
    /// 1. 覆盖前验包（清单齐全 / 无 0 字节文件），坏包根本不进入覆盖阶段；
    /// 2. 覆盖前验可写性，目录不可写当场报错，而不是"退出之后静默失败"；
    /// 3. 覆盖前整目录备份到 %TEMP%，覆盖失败或清点对不上就回滚，用户至少还有旧版本；
    /// 4. 脚本用 robocopy 取代 xcopy 并**判定退出码**（xcopy 默认遇错即停，旧脚本把输出和
    ///    退出码一起丢弃了，任何一处失败都会得到"半新半旧但照样启动"的目录）；
    /// 5. 覆盖结果写进 %APPDATA% 的状态标记，新版本 / 回滚后的旧版本下次启动都能如实告知。
    /// </summary>
    public static void PrepareAndLaunchUpdater(string zipPath, Version targetVersion)
    {
        var root = UpdateRoot;
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
        ValidatePayload(payloadDir);

        var appDir = AppContext.BaseDirectory;
        EnsureWritable(appDir);

        WriteState($"status=pending\r\ntarget={targetVersion}\r\nfrom={DisplayVersion}\r\n");
        BackupInstallDir(appDir, Path.Combine(root, "backup"));

        var batPath = Path.Combine(root, "update.bat");
        File.WriteAllText(batPath, BuildUpdaterScript());

        // 传递给脚本的目录**必须去掉尾部反斜杠**：AppContext.BaseDirectory 恒以 "\" 结尾，
        // 拼进命令行就成了 "...\"，"\" 会转义掉后面的引号，robocopy 收到的是坏路径
        // （直接失败 → 每次更新都回滚）。只有 "C:\" 这类根目录例外，保留原样。
        var appDirArg = appDir.Length > 3 ? appDir.TrimEnd('\\') : appDir;

        Logger.Info($"自更新已就绪：{DisplayVersion} → {targetVersion}（安装目录 {appDirArg}）。");

        Process.Start(new ProcessStartInfo
        {
            FileName = batPath,
            Arguments = $"\"{payloadDir}\" \"{appDirArg}\" \"{UpdateStatePath}\"",
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }

    /// <summary>
    /// 生成自更新批处理（仅 ASCII，规避代码页问题）。
    /// 流程：覆盖（robocopy，带重试，判定退出码）→ 逐项清点关键文件 → 通过则重启新版本；
    /// 否则用备份回滚、写失败标记、重启旧版本 → 清理临时目录与脚本自身。
    /// </summary>
    private static string BuildUpdaterScript()
    {
        var lines = new List<string>
        {
            "@echo off",
            "setlocal enabledelayedexpansion",
            "set \"SRC=%~1\"",
            "set \"DST=%~2\"",
            "set \"STATE=%~3\"",
            "set \"BAK=%~dp0backup\"",
            "set \"RC=0\"",
            "set /a TRY=0",
            "",
            "rem ---- copy payload (retry: the old process may still hold locks for a moment) ----",
            ":copyloop",
            "set /a TRY+=1",
            "robocopy \"%SRC%\" \"%DST%\" /E /R:2 /W:1 /NP /NFL /NDL /NJH /NJS >nul 2>&1",
            "set \"RC=!errorlevel!\"",
            "if !RC! LSS 8 goto verify",
            "if !TRY! GEQ 4 goto verify",
            "ping -n 2 127.0.0.1 >nul",
            "goto copyloop",
            "",
            ":verify",
            "rem ---- verify critical files (list comes from DependencyCheck) ----"
        };

        // 清点项与运行期依赖自检共用同一份清单，避免两处维护走样。
        foreach (var name in DependencyCheck.RequiredAssemblies)
            lines.Add($"if not exist \"%DST%\\{name}\" set \"RC=98\"");
        lines.Add($"if not exist \"%DST%\\{ExeName}\" set \"RC=98\"");
        lines.Add($"if not exist \"%DST%\\{ExeName}.config\" set \"RC=98\"");

        lines.AddRange(
        [
            "",
            "if !RC! GEQ 8 goto rollback",
            $"start \"\" /D \"%DST%\" \"%DST%\\{ExeName}\"",
            "goto cleanup",
            "",
            "rem ---- rollback: restore previous install, restart it, leave a message ----",
            ":rollback",
            $"if exist \"%BAK%\\{ExeName}\" robocopy \"%BAK%\" \"%DST%\" /E /R:2 /W:1 /NP /NFL /NDL /NJH /NJS >nul 2>&1",
            "> \"%STATE%\" echo status=fail",
            ">> \"%STATE%\" echo code=!RC!",
            $"if exist \"%DST%\\{ExeName}\" start \"\" /D \"%DST%\" \"%DST%\\{ExeName}\"",
            "",
            ":cleanup",
            "ping -n 4 127.0.0.1 >nul",
            "rd /s /q \"%BAK%\" 2>nul",
            "rd /s /q \"%~dp0payload\" 2>nul",
            "del /q \"%~dp0*.zip\" 2>nul",
            // 自删除（必须是最后一行）：(goto) 让 cmd 释放对本脚本的读取句柄，随后才能删掉自己；
            // 直接 del "%~f0" 在部分环境下会因句柄未释放而失败，留下脚本残骸。
            "(goto) 2>nul & del \"%~f0\""
        ]);

        // 末尾补 CRLF：批处理最后一行没有换行符时，个别环境会吞掉最后一条命令（自删除失效）。
        return string.Join("\r\n", lines) + "\r\n";
    }

    /// <summary>
    /// 更新包内容校验：主程序、依赖清单、0 字节文件。
    /// 任何一项不过都直接中止——把问题挡在「程序已经退出、正在覆盖安装目录」之前。
    /// </summary>
    private static void ValidatePayload(string payloadDir)
    {
        if (!File.Exists(Path.Combine(payloadDir, ExeName)))
            throw new InvalidOperationException(
                "更新包内容异常（未找到主程序），已中止更新。请重新下载，或到 Release 页手动下载完整包。");

        var missing = new List<string>(DependencyCheck.MissingIn(payloadDir));
        if (!File.Exists(Path.Combine(payloadDir, ExeName + ".config")))
            missing.Add(ExeName + ".config");
        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"更新包不完整（缺少 {string.Join("、", missing)}），已中止更新。"
                + "请重新下载，或到 Release 页手动下载完整包。");

        var empty = new List<string>();
        foreach (var path in Directory.EnumerateFiles(payloadDir, "*", SearchOption.AllDirectories))
        {
            if (new FileInfo(path).Length == 0) empty.Add(Path.GetFileName(path));
        }
        if (empty.Count > 0)
            throw new InvalidOperationException(
                $"更新包内含 0 字节文件（{string.Join("、", empty)}），疑似下载不完整，已中止更新。"
                + "请重试，或到 Release 页手动下载完整包。");
    }

    /// <summary>安装目录可写性预检：不可写时给出可读原因（比"退出后覆盖失败"早得多）。</summary>
    private static void EnsureWritable(string appDir)
    {
        var probe = Path.Combine(appDir, ".update-write-probe");
        try
        {
            File.WriteAllText(probe, "probe");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "程序目录不可写，无法自动更新。请把程序放到有写权限的目录，或手动下载完整包覆盖安装。", ex);
        }
        try { File.Delete(probe); } catch { /* 探测文件删不掉不影响更新 */ }
    }

    /// <summary>
    /// 覆盖前把当前安装目录整份复制到 %TEMP% 作为回滚源。
    /// 单文件复制失败（被独占 / 被安全软件占用）只记日志不中断——备份少一个文件仍能救回主体；
    /// 目录过大（>200MB）时放弃备份：宁可没有回滚，也不要把用户的临时盘塞爆。
    /// </summary>
    private static void BackupInstallDir(string appDir, string backupDir)
    {
        try
        {
            if (Directory.Exists(backupDir)) Directory.Delete(backupDir, true);

            var files = Directory.EnumerateFiles(appDir, "*", SearchOption.AllDirectories).ToList();
            long total = 0;
            foreach (var f in files) total += new FileInfo(f).Length;
            if (total > 200L * 1024 * 1024)
            {
                Logger.Warn($"安装目录 {total / 1024 / 1024}MB 超过 200MB 备份上限，本次更新不具备回滚能力。");
                return;
            }

            Directory.CreateDirectory(backupDir);
            var failed = 0;
            foreach (var file in files)
            {
                var rel = file.Substring(appDir.Length).TrimStart('\\');
                var dst = Path.Combine(backupDir, rel);
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                    File.Copy(file, dst, true);
                }
                catch (Exception ex)
                {
                    failed++;
                    Logger.Warn($"更新备份跳过 {rel}：{ex.Message}");
                }
            }
            Logger.Info($"更新前备份完成：{files.Count - failed}/{files.Count} 个文件 → {backupDir}");
        }
        catch (Exception ex)
        {
            // 备份失败不阻断更新（旧行为没有备份也能装），但要留痕。
            Logger.Warn($"更新前备份失败，本次更新不具备回滚能力：{ex.Message}");
        }
    }

    /// <summary>更新状态标记（%APPDATA%，跨进程交接更新结果；bat 失败时改写它）。</summary>
    private static string UpdateStatePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Constants.AppFolderName, "update-state.txt");

    private static void WriteState(string content)
    {
        try
        {
            var path = UpdateStatePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, Encoding.ASCII);
        }
        catch (Exception ex)
        {
            Logger.Warn($"写入更新状态标记失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 读取并清除上次按钮更新的结果标记，返回要提示用户的一句话（无需提示返回 null）。
    /// 三种结局：失败且已回滚（status=fail）/ 更新没生效（版本没变）/ 成功（只记日志）。
    /// 标记是"一次性消费"的——读完即删，不会每次启动都弹。
    /// </summary>
    public static string? ConsumeUpdateState()
    {
        string[] lines;
        try
        {
            var path = UpdateStatePath;
            if (!File.Exists(path)) return null;
            lines = File.ReadAllLines(path);
            File.Delete(path);
        }
        catch (Exception ex)
        {
            Logger.Warn($"读取更新状态标记失败：{ex.Message}");
            return null;
        }

        var state = ParseState(lines);
        state.TryGetValue("status", out var status);
        state.TryGetValue("target", out var targetText);
        state.TryGetValue("code", out var code);
        state.TryGetValue("from", out var fromText);

        if (string.Equals(status, "fail", StringComparison.OrdinalIgnoreCase))
        {
            Logger.Error($"上次自动更新失败（code={code}，{fromText} → {targetText}），已回滚到 {DisplayVersion}。");
            return $"上次自动更新没有完成（错误码 {code ?? "未知"}），已回滚到当前版本 {DisplayVersion}。"
                 + $"{Environment.NewLine}{Environment.NewLine}"
                 + "请到 Release 页手动下载完整安装包，解压到新目录后运行；"
                 + "若反复失败，请把程序目录加入杀毒软件白名单后重试。";
        }

        if (!string.IsNullOrEmpty(targetText) && Version.TryParse(targetText, out var target))
        {
            if (CurrentVersion >= target)
            {
                Logger.Info($"上次自动更新成功：{fromText} → {DisplayVersion}。");
                return null;
            }
            Logger.Warn($"更新标记存在但版本未变（target={targetText}，current={DisplayVersion}）。");
            return $"上次更新到 {targetText} 没有生效（当前仍是 {DisplayVersion}）。"
                 + $"{Environment.NewLine}{Environment.NewLine}"
                 + "更新脚本可能被安全软件拦截，请到 Release 页手动下载完整安装包。";
        }

        return null;
    }

    /// <summary>解析 "key=value" 形式的标记文件（忽略空行与无等号的行）。</summary>
    private static Dictionary<string, string> ParseState(IEnumerable<string> lines)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            map[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
        }
        return map;
    }
}
