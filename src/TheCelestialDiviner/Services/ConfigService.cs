using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using TheCelestialDiviner.Helpers;
using TheCelestialDiviner.Models;

namespace TheCelestialDiviner.Services;

/// <summary>
/// 配置文件服务：%APPDATA%\TheCelestialDiviner\config.json 的读写。
/// 任何变更由 ViewModel 触发自动保存；文件损坏时备份原件并生成新配置。
/// </summary>
public sealed class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly object Gate = new();

    private static string ConfigDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Constants.AppFolderName);

    /// <summary>配置文件完整路径。</summary>
    public string ConfigPath { get; } = Path.Combine(ConfigDir, Constants.ConfigFileName);

    /// <summary>加载配置；文件不存在返回默认配置，损坏时备份并返回新配置。</summary>
    public AppConfig Load()
    {
        lock (Gate)
        {
            try
            {
                if (!File.Exists(ConfigPath))
                {
                    Logger.Info("配置文件不存在，使用默认配置。");
                    return new AppConfig();
                }

                var json = File.ReadAllText(ConfigPath);
                var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
                MigrateIfNeeded(config);
                config.SoundVolume = Math.Clamp(config.SoundVolume, 0, 100);
                return config;
            }
            catch (Exception ex)
            {
                // 配置损坏：备份原件，返回新配置。
                Logger.Error("配置文件解析失败，已备份并生成新配置。", ex);
                BackupCorruptedFile();
                return new AppConfig();
            }
        }
    }

    /// <summary>
    /// 配置版本迁移（加载与导入共用）。
    /// v1 → v2：取消“全部停止”，全局开关升级为按键总开关：默认关闭、
    /// 默认键 F9（仅当未被存量方案占用时）、热键可自定义。
    /// </summary>
    public static void MigrateIfNeeded(AppConfig config)
    {
        if (config.Version >= AppConfig.CurrentVersion) return;
        var fromVersion = config.Version;

        if (!config.GlobalSwitch.HasKey &&
            !config.Schemes.ContainsKey($"K:{Constants.DefaultMasterKeyVk}:0"))
        {
            // 默认键 F9：未被存量方案占用时启用，避免与注册源冲突。
            config.GlobalSwitch.HasKey = true;
            config.GlobalSwitch.VirtualKey = Constants.DefaultMasterKeyVk;
        }
        config.GlobalSwitch.Enabled = false; // 总开关默认关闭（v2 语义）
        config.Version = AppConfig.CurrentVersion;
        Logger.Info($"配置 v{fromVersion} 已迁移到 v{AppConfig.CurrentVersion}（总开关默认关闭、默认键 F9）。");
    }

    /// <summary>保存配置（原子写入：先写临时文件再替换）。</summary>
    public void Save(AppConfig config)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);
                var json = JsonSerializer.Serialize(config, JsonOptions);
                var tmpPath = ConfigPath + ".tmp";
                File.WriteAllText(tmpPath, json);
                File.Move(tmpPath, ConfigPath, overwrite: true);
            }
            catch (Exception ex)
            {
                Logger.Error("配置文件保存失败。", ex);
            }
        }
    }

    /// <summary>将损坏的配置文件重命名为 .bak 保留现场。</summary>
    private void BackupCorruptedFile()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var bakPath = Path.Combine(ConfigDir, Constants.ConfigBackupFileName);
                File.Copy(ConfigPath, bakPath, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("备份损坏配置文件失败。", ex);
        }
    }
}
