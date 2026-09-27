using TheCelestialDiviner.Helpers;

namespace TheCelestialDiviner.Models;

/// <summary>
/// 单个方案档位的外观定制（「选项」→「方案定制」对话框）。
/// 主题预设为「默认主题」时，下面各项才生效（预设主题下对话框不展示定制项）；
/// 各字段空值 = 用该主题的预设值（见 <see cref="ResolvedAppearance"/>）。
/// </summary>
public sealed class ProfileAppearance
{
    /// <summary>主题预设下标（SectThemes.All：0 默认主题 / 1 衍天高手 / 2 莫问高手；默认 0）。</summary>
    public int Theme { get; set; }

    /// <summary>应用图标键（SectIcons.All；空 = 主题预设图标）。</summary>
    public string IconKey { get; set; } = "";

    /// <summary>应用名（窗口名，版本号不变；空 = 主题预设名：默认 / 衍天高手 → 衍天高手，莫问高手 → 莫问高手）。</summary>
    public string AppName { get; set; } = "";

    /// <summary>键帽配色方案名（KeycapSchemes.All；空 = 主题预设配色）。</summary>
    public string KeycapScheme { get; set; } = "";

    /// <summary>开关模式配色（#RRGGBB；空 = 主题预设色）。</summary>
    public string AccentPrimary { get; set; } = "";

    /// <summary>按压模式配色（#RRGGBB；空 = 主题预设色）。</summary>
    public string AccentGold { get; set; } = "";

    /// <summary>双宏第二配色（#RRGGBB；空 = 主题预设色）。</summary>
    public string AccentDual { get; set; } = "";

    /// <summary>主题预设。</summary>
    public SectTheme Preset => SectThemes.ByIndex(Theme);

    /// <summary>解析最终外观：默认主题按本项自定义覆盖预设，其余主题直接用预设（忽略自定义项）。</summary>
    public ResolvedAppearance Resolve()
    {
        var theme = Preset;
        if (!theme.Customizable)
            return new ResolvedAppearance(theme, theme.AppIconUri, theme.DefaultAppName,
                theme.KeycapScheme, theme.AccentPrimaryHex, theme.AccentGoldHex, theme.AccentDualHex);

        var icon = string.IsNullOrEmpty(IconKey) ? theme.AppIconUri : SectIcons.Resolve(IconKey).AppIconUri;
        var name = string.IsNullOrWhiteSpace(AppName) ? theme.DefaultAppName : AppName;
        var scheme = string.IsNullOrEmpty(KeycapScheme) ? theme.KeycapScheme : KeycapScheme;
        return new ResolvedAppearance(theme, icon, name, scheme,
            Pick(AccentPrimary, theme.AccentPrimaryHex),
            Pick(AccentGold, theme.AccentGoldHex),
            Pick(AccentDual, theme.AccentDualHex));

        static string Pick(string custom, string fallback) =>
            Constants.IsHexColor(custom) ? custom : fallback;
    }

    /// <summary>归一化：主题下标钳位、图标键 / 配色名校验、应用名去空白并限长、色值校验（加载 / 导入 / 对话框确定共用）。</summary>
    public void Normalize()
    {
        Theme = Math.Max(0, Math.Min(Theme, SectThemes.All.Length - 1));
        if (!SectIcons.IsKnown(IconKey)) IconKey = "";
        if (!KeycapSchemes.IsKnown(KeycapScheme)) KeycapScheme = "";
        AppName = (AppName ?? "").Trim();
        if (AppName.Length > Constants.MaxProfileAppNameLength)
            AppName = AppName.Substring(0, Constants.MaxProfileAppNameLength);
        AccentPrimary = Constants.NormalizeHexColor(AccentPrimary);
        AccentGold = Constants.NormalizeHexColor(AccentGold);
        AccentDual = Constants.NormalizeHexColor(AccentDual);
    }

    /// <summary>创建当前实例的副本。</summary>
    public ProfileAppearance Clone() => new()
    {
        Theme = Theme,
        IconKey = IconKey,
        AppName = AppName,
        KeycapScheme = KeycapScheme,
        AccentPrimary = AccentPrimary,
        AccentGold = AccentGold,
        AccentDual = AccentDual
    };

    /// <summary>清空自定义项（恢复主题预设）。</summary>
    public void ClearCustomization()
    {
        IconKey = "";
        AppName = "";
        KeycapScheme = "";
        AccentPrimary = "";
        AccentGold = "";
        AccentDual = "";
    }
}

/// <summary>解析后的最终外观（主题预设 + 默认主题自定义覆盖），界面换肤一律读它。</summary>
public sealed class ResolvedAppearance
{
    public ResolvedAppearance(SectTheme theme, string appIconUri, string appName, string keycapScheme,
        string accentPrimaryHex, string accentGoldHex, string accentDualHex)
    {
        Theme = theme;
        AppIconUri = appIconUri;
        AppName = appName;
        KeycapScheme = keycapScheme;
        AccentPrimaryHex = accentPrimaryHex;
        AccentGoldHex = accentGoldHex;
        AccentDualHex = accentDualHex;
    }

    /// <summary>主题预设。</summary>
    public SectTheme Theme { get; }

    /// <summary>应用图标资源地址（窗口 / 任务栏 / 托盘 / 键帽图标）。</summary>
    public string AppIconUri { get; }

    /// <summary>应用名（窗口标题 = "{AppName} v{版本}"，托盘提示同源）。</summary>
    public string AppName { get; }

    /// <summary>键帽配色方案名。</summary>
    public string KeycapScheme { get; }

    /// <summary>开关模式配色。</summary>
    public string AccentPrimaryHex { get; }

    /// <summary>按压模式配色。</summary>
    public string AccentGoldHex { get; }

    /// <summary>双宏第二配色。</summary>
    public string AccentDualHex { get; }

    /// <summary>注释区公告内嵌资源名（随主题：默认 / 衍天 → Notice.md，莫问 → Notice2.md）。</summary>
    public string NoticeResourceName => Theme.NoticeResourceName;

    /// <summary>注释区公告远端地址。</summary>
    public string NoticeRemoteUrl => Theme.NoticeRemoteUrl;

    /// <summary>是否使用莫问公告缓存槽（公告缓存按主题分槽，不按档位）。</summary>
    public bool UsesMoWenNotice => ReferenceEquals(Theme, SectThemes.MoWen);
}
