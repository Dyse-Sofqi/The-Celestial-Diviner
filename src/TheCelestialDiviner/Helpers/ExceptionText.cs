using System.IO;

namespace TheCelestialDiviner.Helpers;

/// <summary>
/// 异常文本化：沿 InnerException 链展开成因，供日志与弹窗使用。
/// 起因：TypeInitializationException / TargetInvocationException 这类"包装异常"的
/// Message 只有一句"类型初始值设定项引发异常"，真实原因（缺文件 / 版本不匹配 /
/// 权限）全在 InnerException 里——只取 Message 等于什么都没说。
/// </summary>
public static class ExceptionText
{
    /// <summary>展开层数上限：足够覆盖 静态构造 → 类型加载 → 程序集加载 的嵌套。</summary>
    private const int MaxDepth = 6;

    /// <summary>
    /// 沿 InnerException 链展开为 "Type: message → Type: message"（最内层即真因）。
    /// 链过长时以 " → …" 收尾，避免弹窗被极端情况撑爆。
    /// </summary>
    public static string Describe(Exception? ex)
    {
        if (ex is null) return "(无异常)";

        var sb = new System.Text.StringBuilder();
        var depth = 0;
        for (var cur = ex; cur is not null; cur = cur.InnerException)
        {
            if (depth > 0) sb.Append(" → ");
            if (depth == MaxDepth)
            {
                sb.Append("…");
                break;
            }
            sb.Append(cur.GetType().Name).Append(": ").Append(cur.Message);
            depth++;
        }
        return sb.ToString();
    }

    /// <summary>
    /// 是否属于"运行环境 / 依赖"类问题（程序集缺失、版本不匹配、被安全软件拦截、
    /// x86/x64 位数错配）。这类异常与用户数据无关，修复手段是重装 / 重新解压完整发布包，
    /// 因此提示语、以及配置损坏时的"备份原件"行为都要区别对待。
    /// </summary>
    public static bool IsDependencyRelated(Exception? ex)
    {
        for (var cur = ex; cur is not null; cur = cur.InnerException)
        {
            if (cur is TypeInitializationException
                or FileNotFoundException
                or FileLoadException
                or TypeLoadException
                or BadImageFormatException
                or MissingMethodException
                or MissingFieldException)
            {
                return true;
            }
        }
        return false;
    }
}
