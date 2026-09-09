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
                NormalizeTiming(config);
                config.SoundVolume = Compat.Clamp(config.SoundVolume, 0, 100);
                config.VisualizerOpacity = Compat.Clamp(config.VisualizerOpacity, 0, 100);
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
    /// v2 → v3：开关模式分区（常规 / 轮转 / 双宏）——字段有默认值，反序列化自动补齐，仅推进版本号。
    /// v3 → v4：新增方案三档位（①②③）——存量方案整体迁入档位①，其余档位为空，当前档位默认①。
    /// </summary>
    public static void MigrateIfNeeded(AppConfig config)
    {
        if (config.Version >= AppConfig.CurrentVersion) return;
        var fromVersion = config.Version;

        if (fromVersion < 2)
        {
            if (!config.GlobalSwitch.HasKey &&
                !config.Schemes.ContainsKey($"K:{Constants.DefaultMasterKeyVk}:0"))
            {
                // 默认键 F9：未被存量方案占用时启用，避免与注册源冲突。
                config.GlobalSwitch.HasKey = true;
                config.GlobalSwitch.VirtualKey = Constants.DefaultMasterKeyVk;
            }
            config.GlobalSwitch.Enabled = false; // 总开关默认关闭（v2 语义）
        }

        if (fromVersion < 4)
        {
            // v3 → v4：旧配置无档位字段（反序列化时属性初始化器已生成 4 个空档位），
            // 先清空再把存量方案整体迁入档位①，其余档位为空。
            // v4 配置（Version >= 4）不走此处：Profiles 已随 JSON 反序列化还原。
            config.Profiles.Clear();
            config.Profiles.Add(config.Schemes);
            while (config.Profiles.Count < 4)
                config.Profiles.Add(new Dictionary<string, KeyScheme>(StringComparer.Ordinal));
        }
        if (fromVersion < 8)
        {
            // v7 → v8：状态提醒默认激活——存量配置（含开发期落盘的关闭态）统一置为开启；
            // 迁移只执行一次（落盘为 v8 后跳过），用户此后手动关闭不会被重置。
            config.StatusReminderEnabled = true;
        }
        // 反序列化默认 ActiveProfile = 0（方案①），无需显式处理。
        // v5：键位可视化开关表——字段有默认值（空字典 = 全部键位默认开启），无需迁移动作。
        // v6：连发时序增加按压时长（HoldMs）——缺失 / 非法值由 NormalizeTiming 落到默认 26ms，无需迁移动作。
        // v7：状态提醒开关——字段有默认值，无需迁移动作。
        // v9：切换方案热键——字段有默认值（HasKey = false 未设置），无需迁移动作。
        // v10：“成为衍天高手”语音按钮——字段有默认值（关闭），无需迁移动作。
        // v11：注释区公告缓存——字段有默认值（空 = 内嵌默认公告），无需迁移动作。
        // v12：键帽透明度——字段有默认值（100 = 完全不透明），无需迁移动作。
        // v13：自定义提示音——字段有默认值（空 = 内嵌默认音频），无需迁移动作。

        config.Version = AppConfig.CurrentVersion;
        Logger.Info($"配置 v{fromVersion} 已迁移到 v{AppConfig.CurrentVersion}（总开关默认关闭、默认键 F9、开关模式分区、方案四档位、键位可视化）。" );
    }

    /// <summary>
    /// 连发时序归一化：间隔低于下限（10ms）提升到下限；按压时长缺失 / 非法（≤0，
    /// 旧版配置无此字段）落到默认值，其余钳位到 10~200。
    /// 加载与导入配置后调用。
    /// </summary>
    public static void NormalizeTiming(AppConfig config)
    {
        config.DefaultIntervalMs =
            Compat.Clamp(config.DefaultIntervalMs, Constants.MinIntervalMs, Constants.MaxIntervalMs);
        config.DefaultHoldMs = NormalizeHold(config.DefaultHoldMs);
        if (config.TimingPreset is not 0 and not 1) config.TimingPreset = 0;
        foreach (var scheme in config.Schemes.Values)
            foreach (var target in scheme.Targets)
            {
                target.IntervalMs =
                    Compat.Clamp(target.IntervalMs, Constants.MinIntervalMs, Constants.MaxIntervalMs);
                target.HoldMs = NormalizeHold(target.HoldMs);
            }
    }

    /// <summary>单个按压时长归一化：缺失 / 非法（≤0）落默认值，其余钳位。</summary>
    private static int NormalizeHold(int holdMs) =>
        holdMs <= 0 ? Constants.DefaultHoldMs
                    : Compat.Clamp(holdMs, Constants.MinHoldMs, Constants.MaxHoldMs);

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
                File.Copy(tmpPath, ConfigPath, true);
                File.Delete(tmpPath);
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
