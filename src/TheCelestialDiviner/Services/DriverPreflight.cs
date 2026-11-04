using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;
using TheCelestialDiviner.Helpers;

namespace TheCelestialDiviner.Services;

/// <summary>预检结论级别。</summary>
public enum PreflightLevel
{
    /// <summary>只是告知（例如检测到反作弊驱动），不影响决策。</summary>
    Info,

    /// <summary>能让用户自己权衡的风险：默认按钮是"取消"，但可以强制继续。</summary>
    Warning,

    /// <summary>装上去只有坏处 —— 不给"继续"。</summary>
    Blocking
}

/// <summary>一条预检结论。</summary>
public sealed record PreflightItem(PreflightLevel Level, string Title, string Detail);

/// <summary>预检报告（纯数据，UI 与日志直接消费）。</summary>
public sealed class PreflightReport
{
    public IReadOnlyList<PreflightItem> Items { get; init; } = Array.Empty<PreflightItem>();

    public IReadOnlyList<PreflightItem> Blocking =>
        Items.Where(i => i.Level == PreflightLevel.Blocking).ToArray();

    public IReadOnlyList<PreflightItem> Warnings =>
        Items.Where(i => i.Level == PreflightLevel.Warning).ToArray();

    public IReadOnlyList<PreflightItem> Infos =>
        Items.Where(i => i.Level == PreflightLevel.Info).ToArray();

    public bool HasBlocking => Items.Any(i => i.Level == PreflightLevel.Blocking);

    /// <summary>弹窗正文（分三段：必须处理 / 提醒 / 参考）。</summary>
    public string ToText()
    {
        var sb = new StringBuilder();
        Append(sb, "以下问题会让这次安装失败，或让键盘鼠标整体不可用：", Blocking);
        Append(sb, "以下情况可能影响可用性，请自行判断：", Warnings);
        Append(sb, "参考信息：", Infos);
        return sb.ToString().TrimEnd();
    }

    private static void Append(StringBuilder sb, string header, IReadOnlyList<PreflightItem> items)
    {
        if (items.Count == 0) return;
        if (sb.Length > 0) sb.AppendLine();
        sb.AppendLine(header);
        foreach (var item in items) sb.AppendLine($"· {item.Title}：{item.Detail}");
    }
}

/// <summary>
/// 安装前预检：把"装上去一定出事"的机器挡在下载之前。
///
/// 最重要的一条是 HVCI —— 被拒绝加载的类过滤驱动不会安静地失效，
/// 而是让<b>整个设备类</b>的所有设备一起报 Code 39（键盘鼠标全废）。
/// 所以这里把它判成 Blocking，而不是装完再报错。
/// </summary>
public static class DriverPreflight
{
    /// <summary>已知会拦截未知内核驱动的反作弊驱动（只做 Info 级提示，不吓人）。</summary>
    private static readonly (string FileName, string Name)[] AntiCheatDrivers =
    {
        ("vgk.sys", "Vanguard（无畏契约）"),
        ("BEDaisy.sys", "BattlEye"),
        ("EasyAntiCheat.sys", "Easy Anti-Cheat"),
        ("EasyAntiCheat_EOS.sys", "Easy Anti-Cheat（EOS）"),
        ("ACE-BASE.sys", "腾讯 ACE"),
        ("ACE-GAME.sys", "腾讯 ACE")
    };

    /// <summary>跑一次预检（纯读，约 5~15ms：一次设备类注册表读 + 一次 drivers 目录列举）。</summary>
    public static PreflightReport Evaluate(bool isAdmin)
    {
        var items = new List<PreflightItem>();

        if (!isAdmin)
            items.Add(new PreflightItem(PreflightLevel.Blocking, "缺少管理员权限",
                "安装类过滤驱动需要管理员权限，请右键以管理员身份重新运行本程序"));

        AppendKernelIntegrity(items);
        AppendDuplicate(items);
        AppendDdConflict(items);
        AppendSecureBoot(items);
        AppendForeignFilters(items);
        AppendAntiCheat(items);

        return new PreflightReport { Items = items };
    }

    /// <summary>
    /// 内核完整性三件套的拦截项（null = 没开，可以装）。
    /// 单独暴露出来，是因为它不只是"预检建议"：<see cref="InterceptionDriverService"/> 在真正跑
    /// 安装器之前会再问一次 —— 导引 / 菜单 / 代码路径都可能绕过 UI 预检，这是唯一的硬闸。
    /// </summary>
    public static PreflightItem? KernelIntegrityItem()
    {
        SystemDriverAdvisor.Advice advice;
        try { advice = SystemDriverAdvisor.Evaluate(); }
        catch (Exception ex)
        {
            Logger.Warn($"预检：读取内核完整性状态失败（按未开启处理）：{ex.Message}");
            return null;
        }

        var on = new List<string>();
        if (advice.HvciOn) on.Add("内存完整性（HVCI）");
        if (advice.ShadowStacksOn) on.Add("内核模式硬件强制堆栈保护");
        if (advice.SmartAppControlOn) on.Add("Smart App Control");
        if (on.Count == 0) return null;

        return new PreflightItem(PreflightLevel.Blocking, string.Join(" / ", on) + " 已开启",
            "开启时该驱动不会被加载；而注册进过滤链却加载不了，会让整个键盘类报 Code 39（键鼠一起失灵）。\n"
            + "本工具的连发在剑网三里只认物理级驱动（DD / Interception），普通 / 扫描码 / 消息模式对剑网三无效，"
            + "所以没有「先用别的模式凑合」这条路。\n"
            + "要装就只有一个前提：在 Windows 安全中心 → 设备安全性 → 内核隔离 里关闭「内存完整性」并重启，"
            + "再回来安装。是否关闭由你决定 —— 代价是系统对恶意驱动的防护会变弱");
    }

    /// <summary>HVCI / 内核堆栈保护 / Smart App Control —— 直接复用系统导引的判定，不另写一套。</summary>
    private static void AppendKernelIntegrity(List<PreflightItem> items)
    {
        var item = KernelIntegrityItem();
        if (item is not null) items.Add(item);
    }

    /// <summary>过滤链里已经有重复项：先修再装，别在坏状态上再叠一层。</summary>
    private static void AppendDuplicate(List<PreflightItem> items)
    {
        var duplicated = InterceptionRegistration.AllClasses
            .Where(InterceptionRegistration.HasDuplicate)
            .Select(InterceptionRegistration.Label)
            .ToArray();
        if (duplicated.Length == 0) return;

        items.Add(new PreflightItem(PreflightLevel.Blocking, $"{string.Join("、", duplicated)}过滤链里有重复项",
            "同一个过滤驱动被登记了多次（多见于反复安装）。请先用「选项 → 键盘注入 → 检查与修复驱动注册…」清理，再安装"));
    }

    /// <summary>DD 驱动：用户态库随包常驻，"在位"不等于"在用"，所以只提醒不拦。</summary>
    private static void AppendDdConflict(List<PreflightItem> items)
    {
        try
        {
            if (!DdDriverService.IsAvailable) return;
            items.Add(new PreflightItem(PreflightLevel.Warning, "DD 驱动可用",
                "两者在系统键盘栈上互斥：装上 Interception 后 DD 注入会失效，要用 DD 需先卸载 Interception 并重启"));
        }
        catch (Exception ex)
        {
            Logger.Warn($"预检：探测 DD 驱动失败（忽略）：{ex.Message}");
        }
    }

    private static void AppendSecureBoot(List<PreflightItem> items)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
            if (key?.GetValue("UEFISecureBootEnabled") is not int enabled || enabled == 0) return;
            items.Add(new PreflightItem(PreflightLevel.Warning, "Secure Boot 已开启",
                "驱动签名来自原作者的个人证书（2011 年签发），个别新系统会因此拒绝加载；若安装后提示未生效，先查这里"));
        }
        catch (Exception ex)
        {
            Logger.Warn($"预检：读取 Secure Boot 状态失败（忽略）：{ex.Message}");
        }
    }

    /// <summary>两个类上的第三方过滤驱动：它们与 Interception 同栈，冲突时整类一起挂。</summary>
    private static void AppendForeignFilters(List<PreflightItem> items)
    {
        foreach (var kind in InterceptionRegistration.AllClasses)
        {
            var foreign = InterceptionRegistration.Foreign(kind);
            if (foreign.Length == 0) continue;
            items.Add(new PreflightItem(PreflightLevel.Warning, $"{InterceptionRegistration.Label(kind)}上还挂着其它过滤驱动",
                $"{string.Join(" / ", foreign)} —— 同一类里有多个过滤驱动时，任一加载失败都会让整类设备一起报 Code 39。"
                + "若装完出现键鼠无反应，先把这些软件停掉"));
        }
    }

    private static void AppendAntiCheat(List<PreflightItem> items)
    {
        var found = new List<string>();
        try
        {
            // 固定用 Windows\System32：Environment.SystemDirectory 在 32 位进程下会给 SysWOW64。
            var driversDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "drivers");
            if (!Directory.Exists(driversDir)) return;
            var present = new HashSet<string>(
                Directory.EnumerateFiles(driversDir, "*.sys").Select(Path.GetFileName),
                StringComparer.OrdinalIgnoreCase);
            foreach (var (fileName, name) in AntiCheatDrivers)
                if (present.Contains(fileName)) found.Add(name);
        }
        catch (Exception ex)
        {
            Logger.Warn($"预检：扫描反作弊驱动失败（忽略）：{ex.Message}");
            return;
        }

        if (found.Count == 0) return;
        items.Add(new PreflightItem(PreflightLevel.Info, "检测到反作弊驱动",
            $"{string.Join(" / ", found.Distinct())} —— 这类程序会校验/拦截键盘类过滤驱动，可能导致驱动装好后不生效或键鼠失灵"));
    }
}
