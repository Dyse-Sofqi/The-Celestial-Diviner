using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TheCelestialDiviner.Helpers;

namespace TheCelestialDiviner.Services;

/// <summary>落盘用的快照项（字段显式命名，避免以后改内部的 <see cref="ClassFilterSnapshot"/> 时把旧文件读崩）。</summary>
public sealed class ClassSnapshotDto
{
    public string Class { get; set; } = "";
    public string KeyPath { get; set; } = "";
    public int RegistryType { get; set; } = -1;
    public string[] Upper { get; set; } = Array.Empty<string>();
    public string[] Lower { get; set; } = Array.Empty<string>();
}

/// <summary>一次安装的记账（driver-state.json 的根对象）。</summary>
public sealed class DriverStateFile
{
    public int SchemaVersion { get; set; } = 1;
    public DateTime CapturedUtc { get; set; }
    public string AppVersion { get; set; } = "";
    public string DllSha256 { get; set; } = "";
    public bool InstalledByUs { get; set; }
    public List<ClassSnapshotDto> Before { get; set; } = new();
}

/// <summary>
/// 驱动注册的本地记账：安装前把两个类的过滤链原样存下来。
///
/// 为什么不塞进 config.json：配置会被「恢复默认 / 导入方案 / 用户手删」重置，
/// 而这份记账是排查证据（"原先挂着的第三方过滤驱动还在不在"），不能跟着 UI 设置一起没。
/// 反过来，导出 / 导入配置时也不该带走它 —— 换台机器后旧快照是错的。
///
/// 文件损坏一律按"没有记录"处理：孤儿判定退化成"只提示不断言"，不影响启动与安装。
/// </summary>
public static class DriverStateStore
{
    private const string FileName = "driver-state.json";
    private const string BackupName = "driver-state.before-install.bak";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>记账文件路径（%APPDATA%\TheCelestialDiviner\driver-state.json）。</summary>
    public static string StatePath => Path.Combine(BaseDir, FileName);

    /// <summary>上一份记账的备份（保留 .bak 不会覆盖，方便对"装了几次"这类问题取证）。</summary>
    public static string BackupPath => Path.Combine(BaseDir, BackupName);

    /// <summary>本机是否有过安装记账（孤儿判定的主判据）。</summary>
    public static bool HasRecord => File.Exists(StatePath);

    /// <summary>保存一份新记账；已有文件先转成 .bak 再写。</summary>
    public static bool Save(IReadOnlyList<ClassFilterSnapshot> before, bool installedByUs, string dllSha256)
    {
        try
        {
            Directory.CreateDirectory(BaseDir);
            if (File.Exists(StatePath))
            {
                try { File.Copy(StatePath, BackupPath, overwrite: true); }
                catch (Exception ex) { Logger.Warn($"驱动记账备份失败（忽略，继续写新记账）：{ex.Message}"); }
            }

            var file = new DriverStateFile
            {
                CapturedUtc = DateTime.UtcNow,
                AppVersion = typeof(DriverStateStore).Assembly.GetName().Version?.ToString(3) ?? "",
                DllSha256 = dllSha256,
                InstalledByUs = installedByUs,
                Before = before.Select(ToDto).ToList()
            };
            File.WriteAllText(StatePath, JsonSerializer.Serialize(file, Json));
            Logger.Info($"驱动注册快照已记账（{string.Join("、",
                before.Select(s => $"{InterceptionRegistration.Label(s.Class)} "
                                   + (s.Readable ? string.Join("+", s.Upper) : "读不到")))}）。");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn($"驱动注册快照记账失败（不影响本次安装，但无法自动回滚）：{ex.Message}");
            return false;
        }
    }

    /// <summary>读取记账里的"安装前"快照（无记录 / 解析失败返回 null）。</summary>
    public static ClassFilterSnapshot[]? LoadBefore()
    {
        try
        {
            if (!File.Exists(StatePath)) return null;
            var file = JsonSerializer.Deserialize<DriverStateFile>(File.ReadAllText(StatePath));
            if (file?.Before is null || file.Before.Count == 0) return null;

            var snapshots = new List<ClassFilterSnapshot>();
            foreach (var dto in file.Before)
            {
                if (!Enum.TryParse<DeviceClass>(dto.Class, ignoreCase: true, out var kind)) continue;
                snapshots.Add(new ClassFilterSnapshot(kind, dto.KeyPath, dto.RegistryType,
                    dto.Upper ?? Array.Empty<string>(), dto.Lower ?? Array.Empty<string>()));
            }
            return snapshots.Count == 0 ? null : snapshots.ToArray();
        }
        catch (Exception ex)
        {
            Logger.Warn($"读取驱动记账失败（按无记录处理）：{ex.Message}");
            return null;
        }
    }

    /// <summary>完整卸载后清掉记账（.bak 保留：它是"这次装过什么"的最后证据）。</summary>
    public static void Clear()
    {
        try
        {
            if (File.Exists(StatePath)) File.Delete(StatePath);
        }
        catch (Exception ex)
        {
            Logger.Warn($"清理驱动记账失败（忽略）：{ex.Message}");
        }
    }

    /// <summary>我们的暂存目录里还有安装器 —— 弱证据：这台机器上的驱动很可能是本程序装的。
    /// 用于在记账缺失（老版本升级 / 用户删过 AppData）时，压掉孤儿提示的误报。</summary>
    public static bool HasStagingArtifacts
    {
        get
        {
            try
            {
                var dir = Path.Combine(BaseDir, "drivers", "interception");
                return File.Exists(Path.Combine(dir, "install-interception.exe"));
            }
            catch
            {
                return false;
            }
        }
    }

    private static string BaseDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Constants.AppFolderName);

    private static ClassSnapshotDto ToDto(ClassFilterSnapshot snapshot) => new()
    {
        Class = snapshot.Class.ToString(),
        KeyPath = snapshot.KeyPath,
        RegistryType = snapshot.RegistryType,
        Upper = snapshot.Upper,
        Lower = snapshot.Lower
    };
}
