using System.IO;
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
/// </summary>
public static class DependencyCheck
{
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

        sb.AppendLine("诊断信息：" + ExceptionText.Describe(error));
        sb.AppendLine();
        sb.AppendLine("修复步骤：");
        sb.AppendLine("1. 完全退出本程序；");
        sb.AppendLine("2. 删除当前程序目录（配置保存在 %APPDATA%\\TheCelestialDiviner，不受影响）；");
        sb.AppendLine("3. 重新解压完整的发布包，从解压目录运行 TheCelestialDiviner.exe");
        sb.AppendLine("   （不要只替换 exe，也不要放在压缩包内或其他程序的目录里运行）；");
        sb.AppendLine("4. 若仍然失败，查看杀毒软件隔离区，把程序目录加入白名单后重新解压。");
        return sb.ToString();
    }
}
