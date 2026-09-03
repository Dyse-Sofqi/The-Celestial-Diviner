using System.Collections.ObjectModel;
using TheCelestialDiviner.Helpers;
using TheCelestialDiviner.Models;
using TheCelestialDiviner.Services;

namespace TheCelestialDiviner.ViewModels;

/// <summary>
/// 主视图模型（集合与方案操作部分）：输入源按钮构建、方案编辑落盘、方案启停/清空、
/// 配置导入导出、全局开关键与注册源冲突检测。
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>鼠标输入源定义表（与键盘区一致的数据源）。</summary>
    private static readonly (MouseInput Mouse, string Name, string Icon)[] s_mouseDefs =
    {
        (MouseInput.Left, "左键", "🖱"),
        (MouseInput.Right, "右键", "🖱"),
        (MouseInput.Middle, "中键", "🖱"),
        (MouseInput.WheelUp, "滚轮上", "🔼"),
        (MouseInput.WheelDown, "滚轮下", "🔽"),
        (MouseInput.XButton1, "侧键1", "🖲"),
        (MouseInput.XButton2, "侧键2", "🖲"),
    };

    /// <summary>构建鼠标区 7 个控件 + 键盘区真实物理布局（宽度比例与导航簇位置均拟真）。</summary>
    private void BuildSourceButtons()
    {
        // 鼠标区。
        foreach (var (mouse, name, icon) in s_mouseDefs)
        {
            var source = new InputSource { Kind = InputKind.Mouse, Mouse = mouse };
            MouseButtons.Add(new KeySourceViewModel(source, name, icon));
        }

        // 键盘区：真实 ANSI 键宽（1 物理单位 = 4 格）+ 右侧导航簇拟位。
        // 行总宽 90 格（主键区 15u + 间隔 0.25u + 导航殧 3u + 右余白 4.25u），
        // 每行用占位空白补齐（vk=0 + isSpacer，不参与钩子与配置），
        // 使各行单位宽度一致、按键位置与实体键盘对应。
        InputSource S() => new() { Kind = InputKind.Keyboard, VirtualKey = 0 };
        KeySourceViewModel Key(int vk, string name, double units = 4) =>
            new(new InputSource { Kind = InputKind.Keyboard, VirtualKey = vk }, name, units: units);
        KeySourceViewModel Gap(double units) => new(S(), "", isSpacer: true, units: units);

        var rows = new List<KeySourceViewModel>[]
        {
            // 行 1：Esc + F1~F12（功能键行的真实分组间隔：Esc 后大间隔，F4/F8 后额外半键）
            new()
            {
                Key(0x1B, "Esc"), Gap(5),
                Key(0x70, "F1"), Gap(1), Key(0x71, "F2"), Gap(1), Key(0x72, "F3"), Gap(1), Key(0x73, "F4"), Gap(2),
                Key(0x74, "F5"), Gap(1), Key(0x75, "F6"), Gap(1), Key(0x76, "F7"), Gap(1), Key(0x77, "F8"), Gap(2),
                Key(0x78, "F9"), Gap(1), Key(0x79, "F10"), Gap(1), Key(0x7A, "F11"), Gap(1), Key(0x7B, "F12"),
                Gap(20)
            },
            // 行 2：`~ 1..0 - = Backspace（2u）；右侧导航殧此行无键
            new()
            {
                Key(0xC0, "`"), Key(0x31, "1"), Key(0x32, "2"), Key(0x33, "3"), Key(0x34, "4"),
                Key(0x35, "5"), Key(0x36, "6"), Key(0x37, "7"), Key(0x38, "8"), Key(0x39, "9"),
                Key(0x30, "0"), Key(0xBD, "-"), Key(0xBB, "="), Key(0x08, "Backspace", 8),
                Gap(1), Gap(12), Gap(17)
            },
            // 行 3：Tab(1.5u) Q..P [ ] \(1.5u)；导航殧上排：Ins Home PgUp
            new()
            {
                Key(0x09, "Tab", 6),
                Key(0x51, "Q"), Key(0x57, "W"), Key(0x45, "E"), Key(0x52, "R"), Key(0x54, "T"),
                Key(0x59, "Y"), Key(0x55, "U"), Key(0x49, "I"), Key(0x4F, "O"), Key(0x50, "P"),
                Key(0xDB, "["), Key(0xDD, "]"), Key(0xDC, "\\", 6),
                Gap(1), Key(0x2D, "Ins"), Key(0x24, "Home"), Key(0x21, "PgUp"), Gap(17)
            },
            // 行 4：Caps(1.75u) A..L ; ' Enter(2.25u)；导航殧下排：Del End PgDn
            new()
            {
                Key(0x14, "Caps", 7),
                Key(0x41, "A"), Key(0x53, "S"), Key(0x44, "D"), Key(0x46, "F"), Key(0x47, "G"),
                Key(0x48, "H"), Key(0x4A, "J"), Key(0x4B, "K"), Key(0x4C, "L"),
                Key(0xBA, ";"), Key(0xDE, "'"), Key(0x0D, "Enter", 9),
                Gap(1), Key(0x2E, "Del"), Key(0x23, "End"), Key(0x22, "PgDn"), Gap(17)
            },
            // 行 5：LShift(2.25u) Z..M , . / RShift(2.75u)；导航殧：↑ 居中（倒 T 上点）
            new()
            {
                Key(0xA0, "LShift", 9),
                Key(0x5A, "Z"), Key(0x58, "X"), Key(0x43, "C"), Key(0x56, "V"), Key(0x42, "B"),
                Key(0x4E, "N"), Key(0x4D, "M"),
                Key(0xBC, ","), Key(0xBE, "."), Key(0xBF, "/"), Key(0xA1, "RShift", 11),
                Gap(1), Gap(4), Key(0x26, "↑"), Gap(4), Gap(17)
            },
            // 行 6：Ctrl Win Alt 空格(6.25u) Alt Win Menu Ctrl；导航殧：← ↓ →（倒 T 下排）
            new()
            {
                Key(0xA2, "LCtrl", 5), Key(0x5B, "LWin", 5), Key(0xA4, "LAlt", 5),
                Key(0x20, "空格", 25),
                Key(0xA5, "RAlt", 5), Key(0x5C, "RWin", 5), Key(0x5D, "菜单", 5), Key(0xA3, "RCtrl", 5),
                Gap(1), Key(0x25, "←"), Key(0x28, "↓"), Key(0x27, "→"), Gap(17)
            },
        };

        foreach (var row in rows)
            KeyboardRows.Add(new ObservableCollection<KeySourceViewModel>(row));
    }

    /// <summary>把配置中的注册状态刷新到全部输入源按钮（启动 / 导入后调用）。</summary>
    private void RefreshAllButtons()
    {
        foreach (var b in MouseButtons)
            b.ApplyScheme(_config.Schemes.GetValueOrDefault(TaskSchedulerService.BuildSourceKey(b.Source!)));

        foreach (var row in KeyboardRows)
            foreach (var b in row)
                if (!b.IsSpacer)
                    b.ApplyScheme(_config.Schemes.GetValueOrDefault(TaskSchedulerService.BuildSourceKey(b.Source!)));
    }

    /// <summary>View 层确认方案对话框后调用：写入配置 → 刷新按钮 → 重启调度器 → 保存。</summary>
    public void CommitScheme(KeySourceViewModel svm, KeyScheme scheme)
    {
        var key = TaskSchedulerService.BuildSourceKey(svm.Source!);
        if (scheme.Targets.Count == 0) _config.Schemes.Remove(key);
        else _config.Schemes[key] = scheme;

        svm.ApplyScheme(scheme);
        ApplyConfigToScheduler();
        SaveConfig();
        AddLog($"输入源 [{svm.Name}] 方案已保存（{scheme.Targets.Count} 个目标键）。");
    }

    /// <summary>右键菜单：启用 / 停用方案。</summary>
    private void ToggleSchemeEnabled(KeySourceViewModel svm)
    {
        var key = TaskSchedulerService.BuildSourceKey(svm.Source!);
        if (!_config.Schemes.TryGetValue(key, out var scheme))
        {
            AddLog($"输入源 [{svm.Name}] 未注册方案。");
            return;
        }

        scheme.Enabled = !scheme.Enabled;
        svm.ApplyScheme(scheme);
        ApplyConfigToScheduler();
        SaveConfig();
        AddLog($"输入源 [{svm.Name}] 方案已{(scheme.Enabled ? "启用" : "停用")}。");
    }

    /// <summary>右键菜单：清空方案。</summary>
    private void ClearScheme(KeySourceViewModel svm)
    {
        var key = TaskSchedulerService.BuildSourceKey(svm.Source!);
        if (_config.Schemes.Remove(key))
        {
            svm.ApplyScheme(null);
            ApplyConfigToScheduler();
            SaveConfig();
        }
        AddLog($"输入源 [{svm.Name}] 方案已清空。");
    }

    /// <summary>配置导入（View 层已读入 JSON 文本）。</summary>
    public bool ImportFromJson(string json)
    {
        try
        {
            var imported = System.Text.Json.JsonSerializer.Deserialize<AppConfig>(json);
            if (imported is null) throw new InvalidOperationException("内容为空");

            _config = imported;
            // 旧版本配置先迁移到 v2 语义（总开关默认关闭、默认键 F9）。
            ConfigService.MigrateIfNeeded(imported);
            // 用属性统一驱动：状态文本、横幅、按钮透明度同步刷新。
            GloballyEnabled = imported.GlobalSwitch.Enabled;
            MasterKeyText = imported.GlobalSwitch.HasKey
                ? InputNameMapper.GetKeyName(imported.GlobalSwitch.VirtualKey)
                : "未设置";
            SoundVolume = Compat.Clamp(imported.SoundVolume, 0, 100);
            // 键盘注入模式随配置同步（导入 / 导出；旧配置回退 UseScanCodes 语义）。
            var mode = imported.KeyboardMode is >= 0 and <= 3
                ? imported.KeyboardMode
                : imported.UseScanCodes ? 1 : 0;
            if (KeyboardMode != mode)
            {
                KeyboardMode = mode;
                KeyboardModeChanged?.Invoke(mode);
            }
            OnPropertyChanged(nameof(GlobalSwitchSource));
            RefreshAllButtons();
            ApplyConfigToScheduler();
            _scheduler.SetMasterEnabled(GloballyEnabled);
            SaveConfig();
            AddLog($"配置已导入（{imported.Schemes.Count} 个方案）。");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("配置导入失败。", ex);
            AddLog("配置导入失败：文件格式无效。");
            return false;
        }
    }

    /// <summary>配置导出（View 层将返回值写入文件）。</summary>
    public string ExportToJson() =>
        System.Text.Json.JsonSerializer.Serialize(_config, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });

    /// <summary>
    /// 编辑总开关键时的方案迁移：若旧总开关键注册过方案，则把该方案转移到新总开关键，
    /// 避免“总开关键与方案源相同”导致按键被吞。新键已有方案时丢弃旧方案并提示。
    /// </summary>
    public void RelocateSchemeFromMasterKey(InputSource oldKey, InputSource newKey)
    {
        var oldKeyStr = TaskSchedulerService.BuildSourceKey(oldKey);
        if (!_config.Schemes.TryGetValue(oldKeyStr, out var scheme)) return;

        _config.Schemes.Remove(oldKeyStr);
        var newKeyStr = TaskSchedulerService.BuildSourceKey(newKey);
        if (_config.Schemes.ContainsKey(newKeyStr))
        {
            AddLog($"原总开关键 [{InputNameMapper.GetSourceName(oldKey)}] 的方案已丢弃（新键已有方案）。");
        }
        else
        {
            _config.Schemes[newKeyStr] = scheme;
            AddLog($"原总开关键 [{InputNameMapper.GetSourceName(oldKey)}] 的方案已转移到 [{InputNameMapper.GetSourceName(newKey)}]。");
        }
        RefreshAllButtons();
        ApplyConfigToScheduler();
    }

    /// <summary>检查候选全局开关键是否与已注册方案冲突（设置对话框实时校验）。</summary>
    public bool IsGlobalKeyConflicting(InputSource key) =>
        _config.Schemes.ContainsKey(TaskSchedulerService.BuildSourceKey(key));
}
