namespace TheCelestialDiviner.Helpers;

/// <summary>全局常量（尺寸、默认值、路径等）。</summary>
public static class Constants
{
    /// <summary>配置目录名（%APPDATA% 下）。</summary>
    public const string AppFolderName = "TheCelestialDiviner";

    /// <summary>配置文件名。</summary>
    public const string ConfigFileName = "config.json";

    /// <summary>配置备份文件名（损坏时保留原件）。</summary>
    public const string ConfigBackupFileName = "config.json.bak";

    // ---------- 连发时序（周期 = 按压时长 + 间隔，两者逐发独立抖动 ±20%） ----------
    // 默认 26ms 的依据：逐帧轮询输入的游戏每帧采样一次，只有"按压 ≥ 帧窗口"才能保证
    // 按下状态必被采样到、"间隔 ≥ 帧窗口"才能保证两次按压不被合并为一次持续按住。
    // 40fps（帧窗口 25ms）是常见游戏的帧率下限，故按压/间隔默认均取 25ms + 余量 = 26ms，
    // 对应可靠连发约 19 发/秒（上限 = 1 / 2×帧窗口），且全帧率区间零丢失。

    /// <summary>常规档按压时长/间隔（毫秒）：均 ≥ 40fps 帧窗口（25ms），40~100 帧全区间
    /// 零丢失，约 19 发/秒（上限 = 1 / 2×帧窗口，任何游戏动作冷却都吃不满）。</summary>
    public const int RegularHoldMs = 26;
    public const int RegularIntervalMs = 26;

    /// <summary>极限档按压时长/间隔（毫秒）：帧率 ≥90 时登记上限 ~45 发/秒（逼近帧窗口
    /// 10ms 的采样上限 50 发/秒）；低于 ~55 帧开始按比例丢发，40 帧触发拍频混叠反而骤降
    /// 到 ~5 发/秒，仅适合帧率稳定 ≥90 的场景。</summary>
    public const int ExtremeHoldMs = 11;
    public const int ExtremeIntervalMs = 11;

    /// <summary>默认连发间隔（毫秒）：弹起到下一次按下的间隔（= 常规档）。</summary>
    public const int DefaultIntervalMs = RegularIntervalMs;

    /// <summary>默认按压时长（毫秒）：按下到弹起的持续时间（= 常规档）。</summary>
    public const int DefaultHoldMs = RegularHoldMs;

    /// <summary>连发间隔下限（毫秒）。</summary>
    public const int MinIntervalMs = 10;

    /// <summary>连发间隔上限（毫秒）。</summary>
    public const int MaxIntervalMs = 100;

    /// <summary>按压时长下限（毫秒）。</summary>
    public const int MinHoldMs = 10;

    /// <summary>按压时长上限（毫秒）。</summary>
    public const int MaxHoldMs = 200;

    /// <summary>连发时序抖动幅度（±20%，按压与间隔各自独立取随机值）。</summary>
    public const int TimingJitterPercent = 20;

    /// <summary>录制目标键的超时（毫秒）。</summary>
    public const int RecordTimeoutMs = 5000;

    /// <summary>注释区公告远端地址（Gitee raw 文件）：软件启动时查询更新，
    /// 成功拉取且内容有变化才覆盖本地缓存；没更新 / 失败保持旧内容。</summary>
    public const string NoticeRemoteUrl = "https://gitee.com/sofqi/The-Celestial-Diviner/raw/main/Notice.md";

    /// <summary>公告内容长度上限（字符）：超长视为异常内容丢弃，防止远端文件误传撑爆注释区。</summary>
    public const int NoticeMaxLength = 2000;

    /// <summary>全局开关键默认虚拟键码（F9 = 0x78）。</summary>
    public const int DefaultMasterKeyVk = 0x78;

    /// <summary>全局开关提示语音默认音量（0~100）。</summary>
    public const double DefaultSoundVolume = 70;

    /// <summary>主窗口设计尺寸（逻辑像素）。</summary>
    public const double MainWindowWidth = 1000;

    public const double MainWindowHeight = 600;

    /// <summary>鼠标键数量（左/右/中/滚轮上/滚轮下/侧键1/侧键2）。</summary>
    public const int MouseKeyCount = 7;

    // ---------- 主题强调色（全局唯一落点：改这里即可全局生效，无需另处搜索替换） ----------
    // 引用点：
    //   AccentPrimaryHex / AccentGoldHex → App.ApplyTheme 主题资源 + KeySourceViewModel 图块刷子
    //   AccentDualHex                     → KeySourceViewModel 双宏图块（紫深一档，随主题紫手动同步）

    /// <summary>主题紫（开关模式图块 / 模式标签选中 / 档位选中 / 横幅开启 / 录入提示条）。</summary>
    public const string AccentPrimaryHex = "#836899";

    /// <summary>主题金（按压模式图块 / 按压模式分段 / 横幅关闭）。</summary>
    public const string AccentGoldHex = "#cea23a";

    /// <summary>双宏图块底色（主题紫深一档；调主色时按需同步）。</summary>
    public const string AccentDualHex = "#a364ea";
}
