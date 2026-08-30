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

    /// <summary>构建鼠标区 7 个控件 + 键盘区标准 104 键布局（含占位空白）。</summary>
    private void BuildSourceButtons()
    {
        // 鼠标区。
        foreach (var (mouse, name, icon) in s_mouseDefs)
        {
            var source = new InputSource { Kind = InputKind.Mouse, Mouse = mouse };
            MouseButtons.Add(new KeySourceViewModel(source, name, icon));
        }

        // 键盘区：每行一个集合；占位空白 IsSpacer = true（IsHitTestVisible=false）。
        int K(int vk) => vk; // 局部函数：语义标注虚拟键码

        var rows = new[]
        {
            // 行 1：Esc + F1~F12
            new[] { (K(0x1B), "Esc"), (K(0x70), "F1"), (K(0x71), "F2"), (K(0x72), "F3"),
                    (K(0x73), "F4"), (K(0x74), "F5"), (K(0x75), "F6"), (K(0x76), "F7"),
                    (K(0x77), "F8"), (K(0x78), "F9"), (K(0x79), "F10"), (K(0x7A), "F11"),
                    (K(0x7B), "F12") },
            // 行 2：` ~ 1..0 - = Backspace
            new[] { (K(0xC0), "`"), (K(0x31), "1"), (K(0x32), "2"), (K(0x33), "3"),
                    (K(0x34), "4"), (K(0x35), "5"), (K(0x36), "6"), (K(0x37), "7"),
                    (K(0x38), "8"), (K(0x39), "9"), (K(0x30), "0"), (K(0xBD), "-"),
                    (K(0xBB), "="), (K(0x08), "Backspace") },
            // 行 3：Tab Q..P [ ] \
            new[] { (K(0x09), "Tab"), (K(0x51), "Q"), (K(0x57), "W"), (K(0x45), "E"),
                    (K(0x52), "R"), (K(0x54), "T"), (K(0x59), "Y"), (K(0x55), "U"),
                    (K(0x49), "I"), (K(0x4F), "O"), (K(0x50), "P"), (K(0xDB), "["),
                    (K(0xDD), "]"), (K(0xDC), "\\") },
            // 行 4：Caps A..L ; ' Enter
            new[] { (K(0x14), "Caps"), (K(0x41), "A"), (K(0x53), "S"), (K(0x44), "D"),
                    (K(0x46), "F"), (K(0x47), "G"), (K(0x48), "H"), (K(0x4A), "J"),
                    (K(0x4B), "K"), (K(0x4C), "L"), (K(0xBA), ";"), (K(0xDE), "'"),
                    (K(0x0D), "Enter") },
            // 行 5：Shift Z..M , . / Shift
            new[] { (K(0xA0), "LShift"), (K(0x5A), "Z"), (K(0x58), "X"), (K(0x43), "C"),
                    (K(0x56), "V"), (K(0x42), "B"), (K(0x4E), "N"), (K(0x4D), "M"),
                    (K(0xBC), ","), (K(0xBE), "."), (K(0xBF), "/"), (K(0xA1), "RShift") },
            // 行 6：Ctrl Win Alt Space Alt Win Menu Ctrl（左右区分 VK）
            new[] { (K(0xA2), "LCtrl"), (K(0x5B), "LWin"), (K(0xA4), "LAlt"),
                    (K(0x20), "空格"), (K(0xA5), "RAlt"), (K(0x5C), "RWin"),
                    (K(0x5D), "菜单"), (K(0xA3), "RCtrl") },
            // 行 7：编辑导航区（Insert..PageDown / 方向键）
            new[] { (K(0x2D), "Ins"), (K(0x24), "Home"), (K(0x21), "PgUp"),
                    (K(0x25), "←"), (K(0x26), "↑"), (K(0x27), "→"),
                    (K(0x23), "End"), (K(0x22), "PgDn"), (K(0x28), "↓"),
                    (K(0x2E), "Del") },
        };

        foreach (var row in rows)
        {
            var vmRow = new ObservableCollection<KeySourceViewModel>();
            foreach (var (vk, name) in row)
            {
                var source = new InputSource { Kind = InputKind.Keyboard, VirtualKey = vk };
                vmRow.Add(new KeySourceViewModel(source, name));
            }
            KeyboardRows.Add(vmRow);
        }
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
            // 用属性统一驱动：状态文本、横幅、按钮透明度同步刷新。
            GloballyEnabled = imported.GlobalSwitch.Enabled;
            MasterKeyText = imported.GlobalSwitch.HasKey
                ? InputNameMapper.GetKeyName(imported.GlobalSwitch.VirtualKey)
                : "未设置";
            // 扫描码模式随配置同步（导入 / 导出）。
            UseScanCodeMode = imported.UseScanCodes;
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

    /// <summary>检查候选全局开关键是否与已注册方案冲突（设置对话框实时校验）。</summary>
    public bool IsGlobalKeyConflicting(InputSource key) =>
        _config.Schemes.ContainsKey(TaskSchedulerService.BuildSourceKey(key));
}
