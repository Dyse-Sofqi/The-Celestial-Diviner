namespace TheCelestialDiviner.Helpers;

/// <summary>可选应用图标（程序自带图标 + 用户提供的门派心法图标）。</summary>
public sealed class SectIcon
{
    public SectIcon(string key, string displayName, string appIconUri)
    {
        Key = key;
        DisplayName = displayName;
        AppIconUri = appIconUri;
    }

    /// <summary>持久化标识（配置里存这个键）。</summary>
    public string Key { get; }

    /// <summary>下拉 / 画廊显示名（心法名，如「太玄经」）。</summary>
    public string DisplayName { get; }

    /// <summary>WPF 资源地址（窗口 / 任务栏 / 托盘 / 键帽图标共用）。</summary>
    public string AppIconUri { get; }
}

/// <summary>
/// 应用图标注册表：程序自带两个（衍天 = app.ico、莫问 = mowen.ico）+
/// 用户提供的 31 个门派心法图标（Resources/icons/&lt;key&gt;.ico，由 48×48 PNG 转制多尺寸 ICO；
/// 第一批 20 个 + 第二批 11 个治疗 / 防御心法）。
/// 「方案定制」的默认主题可选其中之一作为应用图标。
/// </summary>
public static class SectIcons
{
    /// <summary>衍天（程序默认识别图标）。</summary>
    public static readonly SectIcon Diviner =
        new("diviner", "衍天", "pack://application:,,,/Resources/app.ico");

    /// <summary>莫问（主题预设「莫问高手」图标）。</summary>
    public static readonly SectIcon MoWen =
        new("mowen", "莫问", "pack://application:,,,/Resources/mowen.ico");

    /// <summary>门派心法图标（顺序即画廊顺序）。</summary>
    public static readonly SectIcon[] Sects =
    [
        new("yinlongjue",    "隐龙诀",   "pack://application:,,,/Resources/icons/yinlongjue.ico"),
        new("youluoyin",     "幽罗引",   "pack://application:,,,/Resources/icons/youluoyin.ico"),
        new("zhoutiangong",  "周天功",   "pack://application:,,,/Resources/icons/zhoutiangong.ico"),
        new("zixiagong",     "紫霞功",   "pack://application:,,,/Resources/icons/zixiagong.ico"),
        new("aoxuezhanyi",   "傲血战意", "pack://application:,,,/Resources/icons/aoxuezhanyi.ico"),
        new("beiaojue",      "北傲决",   "pack://application:,,,/Resources/icons/beiaojue.ico"),
        new("bingxinjue",    "冰心诀",   "pack://application:,,,/Resources/icons/bingxinjue.ico"),
        new("dujing",        "毒经",     "pack://application:,,,/Resources/icons/dujing.ico"),
        new("fenshanjin",    "分山劲",   "pack://application:,,,/Resources/icons/fenshanjin.ico"),
        new("fenyingjue",    "焚影圣诀", "pack://application:,,,/Resources/icons/fenyingjue.ico"),
        new("gufengjue",     "孤峰诀",   "pack://application:,,,/Resources/icons/gufengjue.ico"),
        new("huajianyou",    "花间游",   "pack://application:,,,/Resources/icons/huajianyou.ico"),
        new("jingyujue",     "惊羽诀",   "pack://application:,,,/Resources/icons/jingyujue.ico"),
        new("linghaijue",    "凌海诀",   "pack://application:,,,/Resources/icons/linghaijue.ico"),
        new("shanhaixinjue", "山海心诀", "pack://application:,,,/Resources/icons/shanhaixinjue.ico"),
        new("taixujianyi",   "太虚剑意", "pack://application:,,,/Resources/icons/taixujianyi.ico"),
        new("taixuanjing",   "太玄经",   "pack://application:,,,/Resources/icons/taixuanjing.ico"),
        new("tianluoguidao", "天罗诡道", "pack://application:,,,/Resources/icons/tianluoguidao.ico"),
        new("wenshuijue",    "问水诀",   "pack://application:,,,/Resources/icons/wenshuijue.ico"),
        new("wufang",        "无方",     "pack://application:,,,/Resources/icons/wufang.ico"),
        // 第二批补充（治疗 / 防御心法）
        new("tielaolv",       "铁牢律",     "pack://application:,,,/Resources/icons/tielaolv.ico"),
        new("xisuijing",      "洗髓经",     "pack://application:,,,/Resources/icons/xisuijing.ico"),
        new("xiangzhi",       "相知",       "pack://application:,,,/Resources/icons/xiangzhi.ico"),
        new("xiaochenjue",    "笑尘诀",     "pack://application:,,,/Resources/icons/xiaochenjue.ico"),
        new("yijinjing",      "易筋经",     "pack://application:,,,/Resources/icons/yijinjing.ico"),
        new("yunshangxinjing","云裳心经",   "pack://application:,,,/Resources/icons/yunshangxinjing.ico"),
        new("butianjue",      "补天诀",     "pack://application:,,,/Resources/icons/butianjue.ico"),
        new("lijingyidao",    "离经易道",   "pack://application:,,,/Resources/icons/lijingyidao.ico"),
        new("lingsu",         "灵素",       "pack://application:,,,/Resources/icons/lingsu.ico"),
        new("mingzunliuliti", "明尊琉璃体", "pack://application:,,,/Resources/icons/mingzunliuliti.ico"),
        new("tieguyi",        "铁骨衣",     "pack://application:,,,/Resources/icons/tieguyi.ico"),
    ];

    /// <summary>全部可选图标（程序自带两个在前，心法图标随后）。</summary>
    public static readonly SectIcon[] All = [Diviner, MoWen, .. Sects];

    /// <summary>按键取图标（未知键 / 空 → 衍天图标）。</summary>
    public static SectIcon Resolve(string? key) =>
        All.FirstOrDefault(i => string.Equals(i.Key, key, StringComparison.OrdinalIgnoreCase)) ?? Diviner;

    /// <summary>键是否合法（配置归一化用）。</summary>
    public static bool IsKnown(string? key) =>
        All.Any(i => string.Equals(i.Key, key, StringComparison.OrdinalIgnoreCase));
}
