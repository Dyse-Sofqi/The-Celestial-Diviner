namespace TheCelestialDiviner.Helpers;

/// <summary>
/// 门派 / 主题预设：一套外观的唯一落点。三个主题：
/// ① 默认主题（下标 0）= 继承衍天高手原样式（紫金三色 + 衍天图标 + Notice.md + Pansy），
///    且**全项可定制**（应用图标 / 应用名 / 键帽配色 / 三色由方案档位各自覆盖）；
/// ② 衍天高手（下标 1）= 紫金预设，图标为太玄经（用户指定）；
/// ③ 莫问高手（下标 2）= 青绿预设（#31CBB6 / #66BB4D / #2BE9F6）。
/// 主题由方案档位绑定（每个档位各绑一个，见 ProfileAppearance）。
/// </summary>
public sealed class SectTheme
{
    public SectTheme(string name, string displayName, string defaultAppName, string appIconUri,
        string accentPrimaryHex, string accentGoldHex, string accentDualHex,
        string keycapScheme, string noticeResourceName, string noticeRemoteUrl, bool customizable)
    {
        Name = name;
        DisplayName = displayName;
        DefaultAppName = defaultAppName;
        AppIconUri = appIconUri;
        AccentPrimaryHex = accentPrimaryHex;
        AccentGoldHex = accentGoldHex;
        AccentDualHex = accentDualHex;
        KeycapScheme = keycapScheme;
        NoticeResourceName = noticeResourceName;
        NoticeRemoteUrl = noticeRemoteUrl;
        Customizable = customizable;
    }

    /// <summary>主题短名（日志用，如「默认」/「衍天」/「莫问」）。</summary>
    public string Name { get; }

    /// <summary>主题预设显示名（下拉用：「默认主题」/「衍天高手」/「莫问高手」）。</summary>
    public string DisplayName { get; }

    /// <summary>未自定义应用名时的窗口名（默认主题与衍天高手 = 衍天高手；莫问高手 = 莫问高手）。</summary>
    public string DefaultAppName { get; }

    /// <summary>预设应用图标（默认主题可被方案自定义覆盖）。</summary>
    public string AppIconUri { get; }

    /// <summary>开关模式预设色（占「主题紫」槽位）。</summary>
    public string AccentPrimaryHex { get; }

    /// <summary>按压模式预设色（占「主题金」槽位）。</summary>
    public string AccentGoldHex { get; }

    /// <summary>双宏预设色（占「双宏底色」槽位）。</summary>
    public string AccentDualHex { get; }

    /// <summary>预设键帽配色方案名。</summary>
    public string KeycapScheme { get; }

    /// <summary>注释区公告内嵌资源名。</summary>
    public string NoticeResourceName { get; }

    /// <summary>注释区公告远端同步地址。</summary>
    public string NoticeRemoteUrl { get; }

    /// <summary>是否允许方案档位自定义（仅默认主题为 true；预设主题下「方案定制」隐藏定制项）。</summary>
    public bool Customizable { get; }
}

/// <summary>全部主题预设（顺序 = 配置持久化的下标）。</summary>
public static class SectThemes
{
    /// <summary>默认主题（下标 0）：继承衍天高手原样式，可全项定制。</summary>
    public static readonly SectTheme Default = new(
        name: "默认",
        displayName: "默认主题",
        defaultAppName: "衍天高手",
        appIconUri: SectIcons.Diviner.AppIconUri,
        accentPrimaryHex: Constants.AccentPrimaryHex,
        accentGoldHex: Constants.AccentGoldHex,
        accentDualHex: Constants.AccentDualHex,
        keycapScheme: "Pansy",
        noticeResourceName: "TheCelestialDiviner.Notice.md",
        noticeRemoteUrl: Constants.NoticeRemoteUrl,
        customizable: true);

    /// <summary>衍天高手（下标 1）：紫金预设，图标 = 太玄经。</summary>
    public static readonly SectTheme Diviner = new(
        name: "衍天",
        displayName: "衍天高手",
        defaultAppName: "衍天高手",
        appIconUri: SectIcons.Resolve("taixuanjing").AppIconUri,
        accentPrimaryHex: Constants.AccentPrimaryHex,
        accentGoldHex: Constants.AccentGoldHex,
        accentDualHex: Constants.AccentDualHex,
        keycapScheme: "Pansy",
        noticeResourceName: "TheCelestialDiviner.Notice.md",
        noticeRemoteUrl: Constants.NoticeRemoteUrl,
        customizable: false);

    /// <summary>莫问高手（下标 2）：青绿预设（青绿开关 / 翠绿按压 / 青碧双宏）。</summary>
    public static readonly SectTheme MoWen = new(
        name: "莫问",
        displayName: "莫问高手",
        defaultAppName: "莫问高手",
        appIconUri: SectIcons.MoWen.AppIconUri,
        accentPrimaryHex: Constants.MoWenAccentPrimaryHex,
        accentGoldHex: Constants.MoWenAccentGoldHex,
        accentDualHex: Constants.MoWenAccentDualHex,
        keycapScheme: "Green",
        noticeResourceName: "TheCelestialDiviner.Notice2.md",
        noticeRemoteUrl: Constants.Notice2RemoteUrl,
        customizable: false);

    /// <summary>全部主题（下标即持久化值；0 = 默认主题）。</summary>
    public static readonly SectTheme[] All = [Default, Diviner, MoWen];

    /// <summary>按下标取主题（越界回退默认主题）。</summary>
    public static SectTheme ByIndex(int index) =>
        index >= 0 && index < All.Length ? All[index] : Default;

    /// <summary>主题下标（用于下拉回填）。</summary>
    public static int IndexOf(SectTheme theme) => Math.Max(0, Array.IndexOf(All, theme));
}
