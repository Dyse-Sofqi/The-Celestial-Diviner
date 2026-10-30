using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using TheCelestialDiviner.Helpers;

namespace TheCelestialDiviner.Services;

/// <summary>
/// 驱动路线「系统导引」：开机看一眼系统版本 + 内核完整性开关 + 键盘栈上已有的过滤驱动，
/// 判断这台机器该走哪条物理级注入路线，并给出理由。
///
/// 背景（2026-10 实测与客户现场）：
/// <list type="bullet">
/// <item>Win11 24H2 起明显收紧内核代码完整性，25H2（Build 26200+）三项开关里任一开启，
/// DD 这类"自建虚拟设备 + 直接改设备栈"的老式驱动就可能起不来，最坏是 0x50 蓝屏
/// （客户机 4 份转储全停在 dd63330.sys +0x10D2 的同一条指令）。</item>
/// <item>Interception 只做键盘类过滤（转发 / 注入），不碰设备栈结构，在同样约束下更稳；
/// 但它在 HVCI 开启时同样装不上 —— 失败方式是"装不上"，不是"把机器搞蓝"。</item>
/// <item>两者在系统层互斥（Interception 装上后 DD 注入完全失效），所以选之前必须先看环境。</item>
/// </list>
/// 本类只做判断，不弹窗、不装驱动 —— 决策与展示交给 ViewModel。
/// </summary>
public static class SystemDriverAdvisor
{
    /// <summary>Win11 25H2 的 Build 号（26200 起）；24H2 为 26100。</summary>
    private const int BuildWin11_24H2 = 26100;
    private const int BuildWin11_25H2 = 26200;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OsVersionInfoEx
    {
        public int Size;
        public int Major;
        public int Minor;
        public int Build;
        public int PlatformId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string CsdVersion;
        public ushort ServicePackMajor;
        public ushort ServicePackMinor;
        public ushort SuiteMask;
        public byte ProductType;
        public byte Reserved;
    }

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int RtlGetVersion(ref OsVersionInfoEx info);

    /// <summary>
    /// 取真实系统 Build。**不能直接用 <c>Environment.OSVersion</c>**：本进程的 manifest 没声明
    /// supportedOS 时，Windows 会把版本谎报成 6.2.9200（Win8），据此判断 25H2 会全盘失准。
    /// RtlGetVersion 不受 manifest 影响。
    /// </summary>
    private static int GetRealBuild()
    {
        try
        {
            var info = new OsVersionInfoEx { Size = Marshal.SizeOf(typeof(OsVersionInfoEx)) };
            if (RtlGetVersion(ref info) == 0 && info.Build > 0) return info.Build;
        }
        catch (Exception ex)
        {
            Logger.Warn($"导引：RtlGetVersion 失败，退回 Environment.OSVersion：{ex.Message}");
        }
        return Environment.OSVersion.Version.Build;
    }

    /// <summary>键盘类 GUID（UpperFilters 挂在它下面）。</summary>
    private const string KeyboardClassKey =
        @"SYSTEM\CurrentControlSet\Control\Class\{4D36E96B-E325-11CE-BFC1-08002BE10318}";

    /// <summary>键盘栈上"本来就该在"的过滤项：系统类驱动 + Interception 自己注册的名字。</summary>
    private static readonly string[] KnownFilters = { "kbdclass", "keyboard" };

    /// <summary>一条建议（字段全部只读快照，供 UI 与日志直接消费）。</summary>
    public sealed class Advice
    {
        /// <summary>系统 Build（如 26200）。</summary>
        public int Build { get; init; }

        /// <summary>系统显示名（如 "Windows 11 25H2"）。</summary>
        public string OsName { get; init; } = "";

        /// <summary>是否 25H2 及以上（DD 的 0x50 高风险区）。</summary>
        public bool Is25H2Plus { get; init; }

        /// <summary>内存完整性（HVCI）是否开启。</summary>
        public bool HvciOn { get; init; }

        /// <summary>内核模式硬件强制堆栈保护（Kernel Shadow Stacks）是否开启。</summary>
        public bool ShadowStacksOn { get; init; }

        /// <summary>Smart App Control 是否处于评估 / 强制状态。</summary>
        public bool SmartAppControlOn { get; init; }

        /// <summary>Interception 是否已在系统里（含"已卸载未重启"）。</summary>
        public bool InterceptionPresent { get; init; }

        /// <summary>DD 的用户态库是否在位（决定 DD 是否"开箱可用"）。</summary>
        public bool DdAvailable { get; init; }

        /// <summary>键盘类 UpperFilters 原始内容。</summary>
        public IReadOnlyList<string> KeyboardFilters { get; init; } = Array.Empty<string>();

        /// <summary>非系统自带、非 Interception 的第三方键盘过滤项（潜在冲突源）。</summary>
        public IReadOnlyList<string> ForeignFilters { get; init; } = Array.Empty<string>();

        /// <summary>推荐路线：3 = DD / 4 = Interception / 0 = 暂用普通模式。</summary>
        public int RecommendedMode { get; init; }

        /// <summary>推荐路线的短名（用于按钮文案）。</summary>
        public string RecommendedLabel { get; init; } = "普通模式";

        /// <summary>推荐理由（逐条，短句）。</summary>
        public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();

        /// <summary>需要提醒用户的风险（不阻塞，只告知）。</summary>
        public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

        /// <summary>两条物理级路线当前是否都不可用（只剩普通 / 扫描码 / 消息）。</summary>
        public bool NoPhysicalRouteAvailable => RecommendedMode == 0;

    }

    /// <summary>采集当前系统环境并给出建议（纯读，无副作用；单次约 1ms 级）。</summary>
    public static Advice Evaluate()
    {
        var build = GetRealBuild();
        var is25 = build >= BuildWin11_25H2;
        var is24 = build >= BuildWin11_24H2;

        var hvci = ReadDword(@"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled") == 1;
        var kss = ReadDword(@"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\KernelShadowStacks", "Enabled") == 1;
        var sacState = ReadDword(@"SYSTEM\CurrentControlSet\Control\CI\Policy", "VerifiedAndReputablePolicyState");
        var sac = sacState is 1 or 2;

        var filters = ReadKeyboardFilters();
        var foreign = filters
            .Where(f => !KnownFilters.Contains(f, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        var interceptionPresent = SafeInterceptionPresent();
        var ddAvailable = DdDriverService.IsAvailable;

        var reasons = new List<string>();
        var warnings = new List<string>();
        int recommended;

        if (interceptionPresent)
        {
            recommended = 4;
            reasons.Add("系统里已经有 Interception 驱动（开机即加载），直接用它最省事");
            if (ddAvailable) warnings.Add("DD 驱动与它互斥：要改用 DD 必须先卸载 Interception 并重启");
        }
        else if (hvci || kss || sac)
        {
            // 内核完整性三件套任一开启：两条物理级路线都进不来，别把用户往"装完还是不能用"上推。
            recommended = 0;
            reasons.Add("这台机器的内核完整性开关是开着的，物理级驱动装上也加载不了");
            if (hvci) reasons.Add("· 内存完整性（HVCI）：开启");
            if (kss) reasons.Add("· 内核模式硬件强制堆栈保护：开启");
            if (sac) reasons.Add("· Smart App Control：评估 / 强制状态");
            warnings.Add("想用物理级注入，需要先关掉上面这些并重启（Win11 25H2 全新安装常默认开启）");
            if (ddAvailable) warnings.Add("即使关掉开关，25H2 上 DD 驱动仍有 0x50 蓝屏风险，建议优先 Interception");
        }
        else if (is25)
        {
            recommended = 4;
            reasons.Add("系统是 Win11 25H2 及以上：DD 这类老式虚拟设备驱动有 0x50 蓝屏风险");
            reasons.Add("Interception 只做键盘类过滤，同样的内核约束下更稳（实测可用）");
            if (ddAvailable) warnings.Add("DD 库虽然在位，但在 25H2 上不保证能用，且会与 Interception 互斥");
        }
        else if (ddAvailable)
        {
            recommended = 3;
            reasons.Add(is24
                ? "系统是 Win11 24H2：DD 驱动可用，且免重启、游戏兼容性最好"
                : "系统内核版本较低：DD 驱动可用，且免重启、游戏兼容性最好");
            warnings.Add("DD 是闭源驱动，已知有内存缺陷（0x50）；崩溃后会触发熔断，不会反复蓝屏");
        }
        else
        {
            recommended = 4;
            reasons.Add("DD 库不在位，而 Interception 可随包安装（装完重启一次即可）");
        }

        if (foreign.Length > 0)
        {
            warnings.Add($"键盘类上还挂着其他过滤驱动（{string.Join(" / ", foreign)}）："
                         + "可能与注入驱动冲突，若出现按键无反应 / 停不下来，先查这里");
        }

        return new Advice
        {
            Build = build,
            OsName = DescribeOs(build),
            Is25H2Plus = is25,
            HvciOn = hvci,
            ShadowStacksOn = kss,
            SmartAppControlOn = sac,
            InterceptionPresent = interceptionPresent,
            DdAvailable = ddAvailable,
            KeyboardFilters = filters,
            ForeignFilters = foreign,
            RecommendedMode = recommended,
            RecommendedLabel = ModeLabel(recommended),
            Reasons = reasons,
            Warnings = warnings
        };
    }

    /// <summary>一行摘要（写日志用；排查"为什么程序推荐了这个"时先看它）。</summary>
    public static string Describe(Advice a)
        => $"系统 {a.OsName}（Build {a.Build}）；HVCI={(a.HvciOn ? "开" : "关")}"
           + $"、ShadowStacks={(a.ShadowStacksOn ? "开" : "关")}、SAC={(a.SmartAppControlOn ? "开" : "关")}；"
           + $"键盘过滤链 [{(a.KeyboardFilters.Count == 0 ? "读不到" : string.Join(",", a.KeyboardFilters))}]；"
           + $"Interception={(a.InterceptionPresent ? "在" : "无")}、DD库={(a.DdAvailable ? "在" : "无")}；"
           + $"推荐 {a.RecommendedLabel}";

    /// <summary>模式索引 → 短名。</summary>
    public static string ModeLabel(int mode) => mode switch
    {
        4 => "Interception 驱动（开源）",
        3 => "DD 驱动",
        1 => "扫描码模式",
        2 => "消息模式",
        _ => "普通模式（SendInput）"
    };

    private static string DescribeOs(int build)
    {
        if (build >= BuildWin11_25H2) return "Windows 11 25H2 或更新";
        if (build >= BuildWin11_24H2) return "Windows 11 24H2";
        if (build >= 22000) return "Windows 11 21H2~23H2";
        if (build >= 10240) return "Windows 10";
        return $"Windows（Build {build}）";
    }

    /// <summary>读键盘类 UpperFilters（REG_MULTI_SZ；读不到返回空集合）。</summary>
    private static string[] ReadKeyboardFilters()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(KeyboardClassKey);
            return key?.GetValue("UpperFilters") as string[] ?? Array.Empty<string>();
        }
        catch (Exception ex)
        {
            Logger.Warn($"导引：读取键盘类 UpperFilters 失败：{ex.Message}");
            return Array.Empty<string>();
        }
    }

    private static int? ReadDword(string subKey, string name)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(subKey);
            return key?.GetValue(name) as int?;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Interception 是否在系统里（异常一律按"不在"处理，不影响启动）。</summary>
    private static bool SafeInterceptionPresent()
    {
        try { return InterceptionDriverService.IsDriverPresent; }
        catch (Exception ex)
        {
            Logger.Warn($"导引：探测 Interception 驱动失败：{ex.Message}");
            return false;
        }
    }
}
