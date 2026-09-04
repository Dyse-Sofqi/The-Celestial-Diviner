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

    /// <summary>默认连发间隔（毫秒）。</summary>
    public const int DefaultIntervalMs = 10;

    /// <summary>连发间隔下限（毫秒）。</summary>
    public const int MinIntervalMs = 10;

    /// <summary>连发间隔上限（毫秒）。</summary>
    public const int MaxIntervalMs = 100;

    /// <summary>录制目标键的超时（毫秒）。</summary>
    public const int RecordTimeoutMs = 5000;

    /// <summary>全局开关键默认虚拟键码（F9 = 0x78）。</summary>
    public const int DefaultMasterKeyVk = 0x78;

    /// <summary>全局开关提示语音默认音量（0~100）。</summary>
    public const double DefaultSoundVolume = 70;

    /// <summary>主窗口设计尺寸（逻辑像素）。</summary>
    public const double MainWindowWidth = 1000;

    public const double MainWindowHeight = 600;

    /// <summary>鼠标键数量（左/右/中/滚轮上/滚轮下/侧键1/侧键2）。</summary>
    public const int MouseKeyCount = 7;
}
