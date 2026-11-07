using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using TheCelestialDiviner.Helpers;

namespace TheCelestialDiviner.Services;

/// <summary>
/// 运行依赖自检（启动早期调用）。
///
/// 背景：目标框架 net48 时，System.Text.Json 8.x 是**外挂** DLL 链（见发布包内
/// System.Text.Json.dll / System.Text.Encodings.Web.dll / System.Memory.dll /
/// System.Buffer / Unsafe / …）。用户只替换 exe、解压不完整、杀软隔离 dll、
/// exe 被放进别的程序目录（同名旧 dll 顶掉）时，ConfigService 首次被触碰会在
/// 静态字段初始化阶段炸掉，用户只能看到一句"发生内部错误：…类型初始值设定项引发异常"，
/// 毫无修复线索。
///
/// 判断策略：**以功能性探测为准**（能真的序列化 + 反序列化才算通过），
/// 文件清单只用于把缺失项写进提示语——这样不会因为将来发布包精简了某个
/// 冗余 dll 而误判、把能跑的环境挡在门外。
///
/// 自愈（全程无需用户动手，机制均已实测）：config 只在进程启动时被 CLR 读一次，进程内
/// 无法补救——AssemblyResolve 对"找到但版本不符"的 0x80131040 绑定失败**不触发**（事件
/// 一次都不回调，微型测试实证），APP_CONFIG_FILE 重定向到我们的代码能执行时也已晚于
/// 首次配置读取。唯一能落地的接力是：① <see cref="TryRestoreConfig"/> 把带重定向的
/// config 复制成当前 exe 同名的 config（dll 全在时只缺这一份），调用方随即**自动重启
/// 一次**让下次绑定正常，接力进程带 --self-repaired 标记防无限重启；
/// ② 复制失败 / dll 也缺等救不回的场景，才弹 <see cref="BuildMessage"/> 的完整提示。
/// </summary>
public static class DependencyCheck
{
    /// <summary>发布包里 config 的原名（<see cref="TryRestoreConfig"/> 的首选复制来源）。</summary>
    private const string ShippedConfigName = "TheCelestialDiviner.exe.config";

    /// <summary>当前运行的 exe 完整路径（提示语与启动日志共用）：exe 被改名或以缓存副本
    /// 运行时，这里一眼可见——缺的 config 名就是"这个路径 + .config"。</summary>
    public static string ExePath { get; } =
        Assembly.GetEntryAssembly()?.Location is { Length: > 0 } location
            ? location
            : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppDomain.CurrentDomain.FriendlyName);

    /// <summary>
    /// System.Text.Json 8.x 在 net48 下的依赖链（发布包根目录应齐全）。
    /// 同时作为**更新包内容校验**的清单来源：更新脚本里逐项核对目标目录的
    /// `if not exist` 语句也由它生成，避免清单两处维护走样。
    /// </summary>
    public static IReadOnlyList<string> RequiredAssemblies { get; } =
    [
        "System.Text.Json.dll",
        "System.Text.Encodings.Web.dll",
        "System.Memory.dll",
        "System.Buffers.dll",
        "System.Runtime.CompilerServices.Unsafe.dll",
        "System.Threading.Tasks.Extensions.dll",
        "System.Numerics.Vectors.dll",
        "Microsoft.Bcl.AsyncInterfaces.dll",
        "System.ValueTuple.dll"
    ];

    /// <summary>
    /// 功能性探测：真实跑一遍配置读写的核心路径（构造选项 → Serialize → Deserialize）。
    /// 通过返回 null；失败返回原始异常（由调用方展开内层原因）。
    /// </summary>
    public static Exception? Probe()
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            var json = JsonSerializer.Serialize(new { probe = 1 }, options);
            var back = JsonSerializer.Deserialize<Dictionary<string, int>>(json, options);
            if (back is not null && back.TryGetValue("probe", out var v) && v == 1) return null;
            return new InvalidOperationException($"序列化往返结果异常：{json}");
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    /// <summary>当前程序目录下缺失的依赖文件名（含 exe.config，缺失会导致绑定重定向失效）。</summary>
    public static IReadOnlyList<string> MissingFiles()
    {
        var missing = new List<string>(MissingIn(AppDomain.CurrentDomain.BaseDirectory));

        // 绑定重定向（System.Runtime.CompilerServices.Unsafe 等）由 <exe>.config 提供；
        // 路径按当前 exe 名推导，用户重命名 exe 后失效的情况也能被覆盖到。
        var configFile = AppDomain.CurrentDomain.SetupInformation.ConfigurationFile;
        if (!string.IsNullOrEmpty(configFile) && !File.Exists(configFile))
            missing.Add(Path.GetFileName(configFile));

        return missing;
    }

    /// <summary>指定目录下缺失的依赖 dll（更新包内容校验复用同一份清单）。</summary>
    public static IReadOnlyList<string> MissingIn(string directory)
    {
        var missing = new List<string>();
        foreach (var name in RequiredAssemblies)
        {
            if (!File.Exists(Path.Combine(directory, name))) missing.Add(name);
        }
        return missing;
    }

    /// <summary>
    /// 自愈：exe 被改名 / 以下载缓存副本运行时，CLR 按新 exe 名找 config，找不到则绑定
    /// 重定向失效（Unsafe 4.0.4.1 的引用撞上 6.0.0.0 的实际 dll → 0x80131040）。这里把
    /// 同目录里带绑定重定向的 config 复制成当前 exe 同名的 config。config 在进程启动时
    /// 就被 CLR 读取定型，**本次启动救不回来**（进程内无补救手段，见类注释的实测结论），
    /// 复制生效的是下一次启动——调用方随即自动重启一次接力（App.TryRelaunchSelf）。
    /// 返回复制所用的来源 config 文件名；无需修复 / 无法修复返回 null。
    /// </summary>
    public static string? TryRestoreConfig()
    {
        try
        {
            var configFile = AppDomain.CurrentDomain.SetupInformation.ConfigurationFile;
            if (string.IsNullOrEmpty(configFile) || File.Exists(configFile)) return null;

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var shipped = Path.Combine(baseDir, ShippedConfigName);
            string source;
            if (File.Exists(shipped) && CarriesUnsafeRedirect(shipped))
            {
                source = shipped;
            }
            else
            {
                // 原名 config 不在（整个包都被改了名之类）：退而求其次，同目录恰好只有
                // 一份带绑定重定向的 *.exe.config 才敢抄，避免把别的程序的 config 拿来用。
                var candidates = Directory.EnumerateFiles(baseDir, "*.exe.config")
                    .Where(CarriesUnsafeRedirect)
                    .ToList();
                if (candidates.Count != 1) return null;
                source = candidates[0];
            }

            File.Copy(source, configFile, overwrite: false);
            Logger.Info($"依赖自愈：exe 以副本名运行（{Path.GetFileName(configFile)} 缺失），"
                        + $"已从 {Path.GetFileName(source)} 复制补齐，重启后生效。");
            return Path.GetFileName(source);
        }
        catch (Exception ex)
        {
            Logger.Warn("依赖自愈（补齐副本的 config）失败：" + ex.Message);
            return null;
        }
    }

    /// <summary>来源 config 必须带 System.Runtime.CompilerServices.Unsafe 的绑定重定向
    /// （PublicKeyToken b03f5f7f11d50a3a），否则抄过来也治不了本故障。</summary>
    private static bool CarriesUnsafeRedirect(string path)
    {
        try { return File.ReadAllText(path).Contains("b03f5f7f11d50a3a"); }
        catch { return false; }
    }

    /// <summary>依赖自检失败时给用户看的可操作提示（不是堆栈，是修复步骤）。</summary>
    public static string BuildMessage(IReadOnlyList<string> missing, Exception error)
    {
        var sb = new StringBuilder();
        sb.AppendLine("程序运行所需的组件不完整或版本不匹配，无法启动。");
        sb.AppendLine();

        if (missing.Count > 0)
        {
            sb.AppendLine("程序目录缺少以下文件：");
            foreach (var name in missing) sb.AppendLine("    " + name);
            sb.AppendLine();
        }

        // 改名 / 缓存副本是这类故障的常见根因（缺的往往就是"与改名 exe 同名的 config"），
        // 把实际路径亮出来，一眼能看出跑的不是原名的 TheCelestialDiviner.exe。
        sb.AppendLine("当前运行的程序：" + ExePath);
        sb.AppendLine("程序目录：" + AppDomain.CurrentDomain.BaseDirectory);
        sb.AppendLine();

        sb.AppendLine("诊断信息：" + ExceptionText.Describe(error));
        sb.AppendLine();
        sb.AppendLine("修复步骤：");
        sb.AppendLine("1. 完全退出本程序；");
        sb.AppendLine("2. 删除当前程序目录（配置保存在 %APPDATA%\\TheCelestialDiviner，不受影响）；");
        sb.AppendLine("3. 重新解压完整的发布包，从解压目录运行 TheCelestialDiviner.exe");
        sb.AppendLine("   （不要只替换 exe，也不要放在压缩包内或其他程序的目录里运行）；");
        sb.AppendLine("4. 若仍然失败，查看杀毒软件隔离区，把程序目录加入白名单后重新解压；");
        sb.AppendLine("5. 若每次开机都复发：检查桌面快捷方式与开机自启项，它们可能指向了改名或");
        sb.AppendLine("   缓存的副本（见上面「当前运行的程序」），改回指向原名 TheCelestialDiviner.exe。");
        return sb.ToString();
    }

    /// <summary>自愈复制成功后的提示：不说"组件不完整"，只说已修复 + 请重启 + 治本建议。
    /// 只在 config 是唯一缺失项时使用（dll 也缺的话复制救不全，仍走完整提示）。</summary>
    public static string BuildRepairedMessage(string sourceConfigName)
    {
        var sb = new StringBuilder();
        sb.AppendLine("问题已自动修复，请重新启动本程序。");
        sb.AppendLine();
        sb.AppendLine("当前运行的程序是改名副本：");
        sb.AppendLine("    " + ExePath);
        sb.AppendLine();
        sb.AppendLine($"它缺少与自己同名的配置文件，已把同目录的 {sourceConfigName} 复制补齐。");
        sb.AppendLine("配置在启动时读取，本次运行无法自动恢复——点击「确定」后重新打开程序即可。");
        sb.AppendLine();
        sb.AppendLine("建议：把桌面快捷方式 / 开机自启改回指向原名 TheCelestialDiviner.exe，");
        sb.AppendLine("或直接从解压目录运行，避免每次开机又报同样的错。");
        return sb.ToString();
    }
}
