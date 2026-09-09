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

        // 键盘区：真实 ANSI 键宽（1 物理单位 = 4 格）。
        // 左列 = 主键区 15u + 间隔 0.25u + 导航簇 3u + 间隔 0.25u（74 格）：
        // 数字区（小键盘）与导航簇的间隔与字母区与导航簇的间隔等宽。
        // 右列 = 小键盘 16 格（4u），单独 6 行 4 列网格：+ 与 ENT 竖向两格、N0 双倍宽。
        // 每行用占位空白补齐（vk=0 + isSpacer，不参与钩子与配置），
        // 使各行单位宽度一致、按键位置与实体键盘对应。
        InputSource S() => new() { Kind = InputKind.Keyboard, VirtualKey = 0 };
        KeySourceViewModel Key(int vk, string name, double units = 4, bool ext = false) =>
            new(new InputSource { Kind = InputKind.Keyboard, VirtualKey = vk, Extended = ext }, name, units: units);
        KeySourceViewModel Gap(double units) => new(S(), "", isSpacer: true, units: units);
        // 小键盘图块：row 为该键所在键行（0 = 功能键行，1 = 数字行 … 5 = 底部修饰行）。
        KeySourceViewModel Npad(int vk, string name, int row, int col, bool ext = false,
            int rowSpan = 1, int colSpan = 1, string icon = "") =>
            new(new InputSource { Kind = InputKind.Keyboard, VirtualKey = vk, Extended = ext },
                name, icon: icon, row: row, col: col, rowSpan: rowSpan, colSpan: colSpan);

        var rows = new List<KeySourceViewModel>[]
        {
            // 行 1：Esc + F1~F12（右缘与退格键对齐 = 60u：Esc 后间隔 2u 最大，
            // F4/F8 后组间隔 0.75u 次之，组内 0.5u 最小；尾部 14u 空白补齐行宽）
            new()
            {
                Key(0x1B, "Esc"), Gap(2),
                Key(0x70, "F1"), Gap(0.5), Key(0x71, "F2"), Gap(0.5), Key(0x72, "F3"), Gap(0.5), Key(0x73, "F4"), Gap(0.75),
                Key(0x74, "F5"), Gap(0.5), Key(0x75, "F6"), Gap(0.5), Key(0x76, "F7"), Gap(0.5), Key(0x77, "F8"), Gap(0.75),
                Key(0x78, "F9"), Gap(0.5), Key(0x79, "F10"), Gap(0.5), Key(0x7A, "F11"), Gap(0.5), Key(0x7B, "F12"),
                Gap(14)
            },
            // 行 2：`~ 1..0 - = 退格（2u）；右侧导航簇 / 小键盘此行无键
            new()
            {
                Key(0xC0, "`"), Key(0x31, "1"), Key(0x32, "2"), Key(0x33, "3"), Key(0x34, "4"),
                Key(0x35, "5"), Key(0x36, "6"), Key(0x37, "7"), Key(0x38, "8"), Key(0x39, "9"),
                Key(0x30, "0"), Key(0xBD, "-"), Key(0xBB, "="), Key(0x08, "退格", 8),
                Gap(1), Gap(12), Gap(1)
            },
            // 行 3：Tab(1.5u) Q..P [ ] \(1.5u)；导航殧上排：INS HM PU
            new()
            {
                Key(0x09, "Tab", 6),
                Key(0x51, "Q"), Key(0x57, "W"), Key(0x45, "E"), Key(0x52, "R"), Key(0x54, "T"),
                Key(0x59, "Y"), Key(0x55, "U"), Key(0x49, "I"), Key(0x4F, "O"), Key(0x50, "P"),
                Key(0xDB, "["), Key(0xDD, "]"), Key(0xDC, "\\", 6),
                Gap(1), Key(0x2D, "INS"), Key(0x24, "HM"), Key(0x21, "PU"),
                Gap(1)
            },
            // 行 4：Caps(1.75u) A..L ; ' Enter(2.25u)；导航殧下排：DEL END PD
            new()
            {
                Key(0x14, "Caps", 7),
                Key(0x41, "A"), Key(0x53, "S"), Key(0x44, "D"), Key(0x46, "F"), Key(0x47, "G"),
                Key(0x48, "H"), Key(0x4A, "J"), Key(0x4B, "K"), Key(0x4C, "L"),
                Key(0xBA, ";"), Key(0xDE, "'"), Key(0x0D, "Enter", 9),
                Gap(1), Key(0x2E, "DEL"), Key(0x23, "END"), Key(0x22, "PD"),
                Gap(1)
            },
            // 行 5：LShift(2.25u) Z..M , . / RShift(2.75u)；导航殧：↑ 居中（倒 T 上点）
            new()
            {
                Key(0xA0, "LShift", 9),
                Key(0x5A, "Z"), Key(0x58, "X"), Key(0x43, "C"), Key(0x56, "V"), Key(0x42, "B"),
                Key(0x4E, "N"), Key(0x4D, "M"),
                Key(0xBC, ","), Key(0xBE, "."), Key(0xBF, "/"), Key(0xA1, "RShift", 11),
                Gap(1), Gap(4), Key(0x26, "↑"), Gap(4),
                Gap(1)
            },
            // 行 6：Ctrl Win Alt 空格(6.25u) Alt Win Menu Ctrl；导航殧：← ↓ →（倒 T 下排）
            new()
            {
                Key(0xA2, "LCtrl", 5), Key(0x5B, "LWin", 5), Key(0xA4, "LAlt", 5),
                Key(0x20, "空格", 25),
                Key(0xA5, "RAlt", 5), Key(0x5C, "RWin", 5), Key(0x5D, "菜单", 5), Key(0xA3, "RCtrl", 5),
                Gap(1), Key(0x25, "←"), Key(0x28, "↓"), Key(0x27, "→"),
                Gap(1)
            },
        };

        foreach (var row in rows)
            KeyboardRows.Add(new ObservableCollection<KeySourceViewModel>(row));

        // 小键盘（右列，6 行 4 列网格；行 0 = 功能键行留空）。
        // 真实布局：NL / * - 顶排；7 8 9 +（+ 竖跨行 3-4）；4 5 6；1 2 3 ENT（ENT 竖跨行 5-6）；
        // N0 双倍宽 + Num. 底排。
        NumpadKeys.Add(Npad(0x90, "NL", 1, 0));
        NumpadKeys.Add(Npad(0x6F, "/", 1, 1, ext: true));
        NumpadKeys.Add(Npad(0x6A, "*", 1, 2));
        NumpadKeys.Add(Npad(0x6D, "-", 1, 3));
        NumpadKeys.Add(Npad(0x67, "N7", 2, 0));
        NumpadKeys.Add(Npad(0x68, "N8", 2, 1));
        NumpadKeys.Add(Npad(0x69, "N9", 2, 2));
        NumpadKeys.Add(Npad(0x6B, "+", 2, 3, rowSpan: 2));
        NumpadKeys.Add(Npad(0x64, "N4", 3, 0));
        NumpadKeys.Add(Npad(0x65, "N5", 3, 1));
        NumpadKeys.Add(Npad(0x66, "N6", 3, 2));
        NumpadKeys.Add(Npad(0x61, "N1", 4, 0));
        NumpadKeys.Add(Npad(0x62, "N2", 4, 1));
        NumpadKeys.Add(Npad(0x63, "N3", 4, 2));
        NumpadKeys.Add(Npad(0x0D, "ENT", 4, 3, ext: true, rowSpan: 2));
        NumpadKeys.Add(Npad(0x60, "N0", 5, 0, colSpan: 2));
        // 小键盘句点：图标行显示省略的“Num”、名称行显示“.”（与键盘区图块两行结构一致）。
        NumpadKeys.Add(Npad(0x6E, "N.", 5, 2));
    }

    /// <summary>枚举全部输入源按钮（鼠标区 + 键盘区 + 小键盘区，不含占位空白）。</summary>
    private IEnumerable<KeySourceViewModel> AllSourceButtons()
    {
        foreach (var b in MouseButtons) yield return b;
        foreach (var row in KeyboardRows)
            foreach (var b in row)
                if (!b.IsSpacer) yield return b;
        foreach (var b in NumpadKeys) yield return b;
    }

    /// <summary>按输入源查找按钮（含鼠标区 / 键鼠区 / 小键盘区；未找到返回 null）。</summary>
    private KeySourceViewModel? FindSourceButton(InputSource source)
        => AllSourceButtons().FirstOrDefault(b => b.Source is not null && b.Source.Equals(source));

    /// <summary>目标键配置 → 输入源（用于定位目标键图块；滚轮目标无输入源返回 null）。</summary>
    private static InputSource? TargetToSource(TargetKeyConfig t) => t.Kind switch
    {
        TargetKind.Keyboard => new InputSource
        {
            Kind = InputKind.Keyboard,
            VirtualKey = t.VirtualKey,
            Extended = t.Extended
        },
        TargetKind.Mouse => new InputSource { Kind = InputKind.Mouse, Mouse = t.Mouse },
        _ => null
    };

    /// <summary>把配置中的注册状态刷新到全部输入源按钮（启动 / 导入后调用）。</summary>
    private void RefreshAllButtons()
    {
        foreach (var b in MouseButtons)
            b.ApplyScheme(_config.Schemes.GetValueOrDefault(TaskSchedulerService.BuildSourceKey(b.Source!)));

        foreach (var row in KeyboardRows)
            foreach (var b in row)
                if (!b.IsSpacer)
                    b.ApplyScheme(_config.Schemes.GetValueOrDefault(TaskSchedulerService.BuildSourceKey(b.Source!)));

        foreach (var b in NumpadKeys)
            b.ApplyScheme(_config.Schemes.GetValueOrDefault(TaskSchedulerService.BuildSourceKey(b.Source!)));

        // 双宏辅键标记：先清除全部，再对启用中双宏方案的辅键图块施加蓝色配对提示。
        foreach (var b in AllSourceButtons()) b.MarkDualPartner(null);
        foreach (var scheme in _config.Schemes.Values)
        {
            if (scheme.Section != ToggleSection.Dual || !scheme.Enabled || scheme.Targets.Count < 2) continue;
            var partner = TargetToSource(scheme.Targets[1]);
            if (partner is null) continue;
            FindSourceButton(partner)?.MarkDualPartner(
                $"⇄ {InputNameMapper.GetTargetName(scheme.Targets[0])} · {InputNameMapper.GetTargetName(scheme.Targets[1])}");
        }
    }

    /// <summary>刷新“总开关键”在键鼠区的绿色高亮（构造 / 修改 / 清除 / 导入后调用）。</summary>
    private void UpdateMasterKeyHighlight()
    {
        var master = GlobalSwitchSource;
        foreach (var b in MouseButtons)
            b.IsMasterKey = master is not null && master.Equals(b.Source);
        foreach (var row in KeyboardRows)
            foreach (var b in row)
                if (!b.IsSpacer)
                    b.IsMasterKey = master is not null && master.Equals(b.Source);
        foreach (var b in NumpadKeys)
            b.IsMasterKey = master is not null && master.Equals(b.Source);
    }

    // ---------- 方案面板：录入选择态与连发键管理 ----------

    private const string s_defaultHint = "点击“从左侧键盘管理方案”进入管理模式：左键键位添加方案，右键键位取消方案";

    // 选择态提示条文案（管理模式的操作说明；双宏成对录入有独立文案，成对完成 / 放弃第 1 键后回到此处）。
    private const string s_managePrompt =
        "管理方案中：左键键位添加连发 · 右键键位取消方案 · Esc 或点击空白处退出";
    private const string s_dualManagePrompt =
        "双宏成对录入：左键选第 1 键，再左键选第 2 键（成对生效）· 右键键位取消整对 · Esc 或点击空白处退出";

    /// <summary>判断方案是否为"目标键 = 触发键自身"的连发方案（新版唯一方案形态）。</summary>
    private static bool IsSelfRepeatScheme(InputSource source, KeyScheme scheme)
    {
        if (scheme.Targets.Count != 1) return false;
        var t = scheme.Targets[0];
        if (t.ModCtrl || t.ModShift || t.ModAlt || t.Kind == TargetKind.Wheel) return false;
        return source.Kind switch
        {
            InputKind.Keyboard => t.Kind == TargetKind.Keyboard &&
                                  t.VirtualKey == source.VirtualKey && t.Extended == source.Extended,
            InputKind.Mouse => t.Kind == TargetKind.Mouse && t.Mouse == source.Mouse,
            _ => false
        };
    }

    /// <summary>判断方案是否为合法双宏开关：Section=Dual、两个目标键、第 1 键 = 触发键自身。</summary>
    private static bool IsDualMacroScheme(InputSource source, KeyScheme scheme)
    {
        if (scheme.Section != ToggleSection.Dual || scheme.Targets.Count != 2) return false;
        var kept = scheme.Targets;
        // 第 1 键 = 开关键位（自触发）；第 2 键为辅助键，两者均不可为滚轮（无按住语义）。
        if (kept.Any(t => t.ModCtrl || t.ModShift || t.ModAlt || t.Kind == TargetKind.Wheel)) return false;
        var first = kept[0];
        return source.Kind switch
        {
            InputKind.Keyboard => first.Kind == TargetKind.Keyboard &&
                                  first.VirtualKey == source.VirtualKey && first.Extended == source.Extended,
            InputKind.Mouse => first.Kind == TargetKind.Mouse && first.Mouse == source.Mouse,
            _ => false
        };
    }

    /// <summary>判断某键是否已被其他双宏方案用作辅键（防两套双宏共用同一辅键）。</summary>
    private bool IsDualPartnerUsed(InputSource source)
    {
        foreach (var scheme in _config.Schemes.Values)
        {
            if (scheme.Section != ToggleSection.Dual || scheme.Targets.Count < 2) continue;
            var partner = TargetToSource(scheme.Targets[1]);
            if (partner is not null && partner.Equals(source)) return true;
        }
        return false;
    }

    /// <summary>
    /// 迁移旧版方案：仅保留"按键自身连发"方案与合法双宏方案（模式 / 间隔沿用原值），
    /// 其余（触发键 ≠ 目标键、多目标、修饰键组合）按新版交互移除并记录日志。
    /// </summary>
    private void MigrateLegacySchemes()
    {
        var kept = 0;
        var removed = new List<string>();
        foreach (var (key, scheme) in _config.Schemes.ToList())
        {
            var source = TaskSchedulerService.ParseSourceKey(key);
            if (source is null) { removed.Add(key); _config.Schemes.Remove(key); continue; }

            if (IsSelfRepeatScheme(source, scheme))
            {
                scheme.Targets[0].Enabled = true;
                kept++;
                continue;
            }
            if (IsDualMacroScheme(source, scheme))
            {
                // 双宏保留各目标键的启用状态：第 2 勾选框控制首键是否参与连发，
                // 随配置持久化（不再强制全部启用，否则会覆盖用户的取消勾选）。
                kept++;
                continue;
            }
            removed.Add(InputNameMapper.GetSourceName(source));
            _config.Schemes.Remove(key);
        }
        if (removed.Count > 0)
            AddLog($"已移除 {removed.Count} 个旧版组合方案（{string.Join("、", removed)}）——新版连发为按键自身连发。");
        if (kept > 0)
            AddLog($"已保留 {kept} 个连发方案。");
    }

    /// <summary>按当前标签模式与开关分区重建方案行列表。</summary>
    private void RefreshSchemeList()
    {
        SchemeRows.Clear();
        var rows = new List<(string Name, SchemeRowViewModel Row)>();
        foreach (var (key, scheme) in _config.Schemes)
        {
            var source = TaskSchedulerService.ParseSourceKey(key);
            if (source is null) continue;

            var captured = key;
            if (scheme.Section == ToggleSection.Dual)
            {
                // 双宏开关：仅开关模式下、且选中双宏分区时显示；两行一体（启用勾选只显示在第 1 行）。
                if (_selectedMode != TriggerMode.Toggle || _selectedSection != ToggleSection.Dual) continue;
                if (!IsDualMacroScheme(source, scheme)) continue;
                rows.Add((InputNameMapper.GetSourceName(source),
                    new SchemeRowViewModel(
                        source,
                        InputNameMapper.GetSourceName(source),
                        "双宏",
                        scheme.Targets[0].IntervalMs,
                        scheme.Targets[0].HoldMs,
                        scheme.Enabled,
                        onEnabledChanged: enabled => SetSchemeEnabled(captured, enabled),
                        onDelete: () => DeleteScheme(captured),
                        onIntervalChanged: ms => UpdateSchemeInterval(captured, ms),
                        onHoldChanged: ms => UpdateSchemeHold(captured, ms),
                        isDual: true,
                        secondKeyName: InputNameMapper.GetTargetName(scheme.Targets[1]),
                        visualEnabled: GetVisualEnabled(captured),
                        onVisualChanged: visible => SetVisualEnabled(captured, visible),
                        firstKeyFiring: scheme.Targets[0].Enabled,
                        onFirstKeyFiringChanged: firing => SetDualFirstKeyFiring(captured, firing))));
                continue;
            }

            if (!IsSelfRepeatScheme(source, scheme)) continue;
            var target = scheme.Targets[0];
            if (target.Mode != _selectedMode) continue;
            // 开关模式再按分区过滤（常规 / 轮转）。
            if (_selectedMode == TriggerMode.Toggle && scheme.Section != _selectedSection) continue;

            rows.Add((InputNameMapper.GetSourceName(source),
                new SchemeRowViewModel(
                    source,
                    InputNameMapper.GetSourceName(source),
                    target.Mode == TriggerMode.Toggle ? (scheme.Section == ToggleSection.Rotate ? "轮转" : "开关") : "按压",
                    target.IntervalMs,
                    target.HoldMs,
                    scheme.Enabled,
                    onEnabledChanged: enabled => SetSchemeEnabled(captured, enabled),
                    onDelete: () => DeleteScheme(captured),
                    onIntervalChanged: ms => UpdateSchemeInterval(captured, ms),
                    onHoldChanged: ms => UpdateSchemeHold(captured, ms),
                    visualEnabled: GetVisualEnabled(captured),
                    onVisualChanged: visible => SetVisualEnabled(captured, visible))));
        }
        foreach (var (_, row) in rows.OrderBy(r => r.Name, StringComparer.CurrentCulture))
            SchemeRows.Add(row);
        OnPropertyChanged(nameof(HasSchemeRows));
        OnPropertyChanged(nameof(SchemesAllEnabled));   // 表头全选框随列表重建刷新
    }

    /// <summary>
    /// 进入方案管理模式（原一次性录入改造）：左键键位添加方案、右键键位取消方案，
    /// 模式持续保持，直至 Esc / 点击空白处 / “退出管理方案”按钮退出。
    /// </summary>
    public void StartPicking()
    {
        if (PickingActive) return;
        // “退出管理方案”的点击先由钩子路径处理（非图块 = 空白处退出），随后同一物理点击
        // 作为陈旧 WPF 点击落回本按钮（退出时按钮恢复可用）：冷却期内忽略再次进入。
        if (Environment.TickCount - _lastPickingExitTick < 150) return;

        PickingActive = true;
        PickingKind = PickKind.AddScheme;
        // 双宏分区仅在开关模式下有效：按压模式录入一律按常规方案（分区子标签此时隐藏）。
        _pickingSection = _selectedMode == TriggerMode.Toggle ? _selectedSection : ToggleSection.Normal;
        _dualFirstPick = null;
        PickingPromptText = _pickingSection == ToggleSection.Dual ? s_dualManagePrompt : s_managePrompt;
        HintText = "正在管理连发键方案：左键键位添加，右键键位取消，Esc 或点击空白处退出";
        AddLog($"进入方案管理（{_selectedMode switch { TriggerMode.Toggle => "开关模式", _ => "按压模式" }}"
               + $"{(_selectedMode == TriggerMode.Toggle ? " · " + SectionName(_selectedSection) : "")}）："
               + "左键键位添加连发，右键键位取消方案，Esc 或点击空白处退出。");
    }

    /// <summary>进入键位录入选择态（设置全局开关热键；单次录入即完成并退出）。</summary>
    public void StartMasterKeyPicking()
    {
        if (PickingActive) return;
        // 同 StartPicking：退出冷却防止退出选择态的陈旧点击立刻把热键录入重新打开。
        if (Environment.TickCount - _lastPickingExitTick < 150) return;
        PickingActive = true;
        PickingKind = PickKind.SetMasterKey;
        _dualFirstPick = null;
        PickingPromptText = "请选择全局开关热键（Esc 取消）";
        HintText = "正在录入总开关键：点击左侧键鼠视图，或直接按下键盘按键（Esc 取消）";
        AddLog("开始设置全局开关热键：点击左侧键鼠或直接按键（Esc 取消）。");
    }

    /// <summary>
    /// 进入切换方案热键录入选择态（底栏"切换方案设置"项）：
    /// 左键键位 / 直接按键设置，右键已设置的键位取消恢复缺省，单次操作即完成退出。
    /// </summary>
    public void StartCycleKeyPicking()
    {
        if (PickingActive) return;
        // 同 StartPicking：退出冷却防止退出选择态的陈旧点击立刻把热键录入重新打开。
        if (Environment.TickCount - _lastPickingExitTick < 150) return;
        PickingActive = true;
        PickingKind = PickKind.SetCycleKey;
        _dualFirstPick = null;
        PickingPromptText = _config.ProfileCycle.HasKey
            ? "请选择切换方案热键（左键键位 / 直接按键设置 · 右键原键位取消 · Esc 取消）"
            : "请选择切换方案热键（左键键位 / 直接按键设置 · Esc 取消）";
        HintText = "正在录入切换方案热键：左键点击左侧键位，或直接按下键盘按键（Esc 取消）";
        AddLog("开始设置切换方案热键：左键点击左侧键位或直接按键完成设置，右键已设置的键位取消（Esc 取消）。");
    }

    /// <summary>退出选择态（方案管理模式 / 总开关键录入：完成录入，或偶数态下 Esc / 空白处 / 退出按钮）。</summary>
    public void CancelPicking()
    {
        if (!PickingActive) return;
        PickingActive = false;
        PickingKind = PickKind.None;
        _dualFirstPick = null;
        _lastPickingExitTick = Environment.TickCount;
        PickingPromptText = "请选择按键";
        HintText = s_defaultHint;
    }

    /// <summary>
    /// 请求退出选择态（Esc / 点击键鼠区空白处 / “退出管理方案”按钮）。
    /// 双宏成对录入中（已选第 1 键、未选第 2 键）不退出：先放弃暂存的第 1 键回到偶数态，
    /// 再次请求才真正退出（成对录入须成对完成后才能退出）。
    /// </summary>
    public void TryExitPicking()
    {
        if (!PickingActive) return;
        if (PickingKind == PickKind.AddScheme && _pickingSection == ToggleSection.Dual && _dualFirstPick is not null)
        {
            AbandonDualFirstPick("成对完成后才能退出管理");
            return;
        }
        var wasManaging = PickingKind == PickKind.AddScheme;
        CancelPicking();
        if (wasManaging) AddLog("已退出方案管理。");
    }

    /// <summary>放弃双宏成对录入中暂存的第 1 键，回到选择第 1 键（reason 描述触发来源，写入日志）。</summary>
    private void AbandonDualFirstPick(string reason)
    {
        if (_dualFirstPick is not { } pending) return;
        _dualFirstPick = null;
        PickingPromptText = s_dualManagePrompt;
        AddLog($"双宏录入：已放弃第 1 键 [{InputNameMapper.GetSourceName(pending)}]（{reason}）。");
    }

    /// <summary>
    /// 键鼠区图块左键点击（WPF 路径）：仅钩子未安装时兜底录入该键。
    /// 钩子正常时点击统一走钩子路径（区分左键添加 / 右键取消 / 空白处退出），
    /// 此处不得重复录入，否则双宏两步录入会被同一次物理点击触发两次。
    /// </summary>
    private void HandleSourceClicked(KeySourceViewModel svm)
    {
        if (PickingActive && !HookInstalled) CompletePick(svm.Source);
    }

    /// <summary>开关分区显示名。</summary>
    private static string SectionName(ToggleSection section) => section switch
    {
        ToggleSection.Normal => "常规开关",
        ToggleSection.Rotate => "轮转开关",
        ToggleSection.Dual => "双宏开关",
        _ => section.ToString()
    };

    /// <summary>双宏键位合法性校验（wheel / 总开关占用 / 方案占用 / 辅键占用 / 两键相同）。
    /// 通过返回 true；失败时 reason 记录拒绝原因。</summary>
    private bool ValidateDualPickKey(InputSource source, InputSource? firstKey, out string reason)
    {
        if (source.Kind == InputKind.Mouse && source.Mouse is MouseInput.WheelUp or MouseInput.WheelDown)
        {
            reason = "滚轮不能作为双宏键（无按住语义），请重选。";
            return false;
        }
        if (GlobalSwitchSource is { } gk && gk.Equals(source))
        {
            reason = $"[{InputNameMapper.GetSourceName(source)}] 已被总开关占用，不能录入为双宏键，请重选。";
            return false;
        }
        if (ProfileCycleSource is { } ck && ck.Equals(source))
        {
            reason = $"[{InputNameMapper.GetSourceName(source)}] 已被切换方案热键占用，不能录入为双宏键，请重选。";
            return false;
        }
        if (firstKey is not null && firstKey.Equals(source))
        {
            reason = "双宏的两个按键不能相同，请选择其他按键。";
            return false;
        }
        var key = TaskSchedulerService.BuildSourceKey(source);
        if (_config.Schemes.ContainsKey(key))
        {
            reason = $"[{InputNameMapper.GetSourceName(source)}] 已有连发方案（如需改为双宏请先删除原方案），请重选。";
            return false;
        }
        if (IsDualPartnerUsed(source))
        {
            reason = $"[{InputNameMapper.GetSourceName(source)}] 已是其他双宏方案的辅键，不能重复使用，请重选。";
            return false;
        }
        reason = "";
        return true;
    }

    /// <summary>
    /// 双宏两步成对录入：第 1 键暂存并保持选择态；第 2 键校验后成对生成方案（落盘 + 应用）。
    /// 管理模式继续保持，可继续成对添加下一组。
    /// </summary>
    private void CompleteDualPick(InputSource source)
    {
        if (_dualFirstPick is null)
        {
            // 第 1 键（开关键位）：只校验 + 暂存，不落盘，继续选择第 2 键。
            if (ValidateDualPickKey(source, null, out var reason))
            {
                _dualFirstPick = source;
                PickingPromptText = $"已选第 1 键 [{InputNameMapper.GetSourceName(source)}]，请左键选择第 2 键（成对生效，Esc 换第 1 键）";
            }
            else
            {
                AddLog(reason);
            }
            return;
        }

        var first = _dualFirstPick;
        // 第 2 键校验失败时保持选择态，允许直接重选。
        if (!ValidateDualPickKey(source, first, out var reason2)) { AddLog(reason2); return; }
        _dualFirstPick = null;
        PickingPromptText = s_dualManagePrompt;   // 回到“选择第 1 键”，管理模式保持
        // 新方案统一用当前时序档位值（常规 26/26 或极限 11/11）；后续可在方案列表行内修改。
        var interval = _config.DefaultIntervalMs;
        var hold = _config.DefaultHoldMs;

        TargetKeyConfig BuildTarget(InputSource s) => s.Kind switch
        {
            InputKind.Keyboard => new TargetKeyConfig
            {
                Kind = TargetKind.Keyboard,
                VirtualKey = s.VirtualKey,
                Extended = s.Extended,
                Mode = TriggerMode.Toggle,
                HoldMs = hold,
                IntervalMs = interval,
                Enabled = true
            },
            _ => new TargetKeyConfig
            {
                Kind = TargetKind.Mouse,
                Mouse = s.Mouse,
                Mode = TriggerMode.Toggle,
                HoldMs = hold,
                IntervalMs = interval,
                Enabled = true
            }
        };

        var keyStr = TaskSchedulerService.BuildSourceKey(first);
        _config.Schemes[keyStr] = new KeyScheme
        {
            Enabled = true,
            Section = ToggleSection.Dual,
            Targets = { BuildTarget(first), BuildTarget(source) }
        };

        RefreshAllButtons();
        ApplyConfigToScheduler();
        SaveConfig();
        RefreshSchemeList();
        SyncVisualizerRegistry();
        AddLog($"双宏键 [{InputNameMapper.GetSourceName(first)}] + [{InputNameMapper.GetSourceName(source)}]"
               + $" 已添加：按下 [{InputNameMapper.GetSourceName(first)}] 两键轮流触发，再按一次停止"
               + $"（按压 {hold}ms / 间隔 {interval}ms）。可继续成对添加，Esc 退出管理。");
    }

    /// <summary>
    /// 录入（添加）连发键：点击 / 按下的键位按选择态用途处理。方案管理模式保持，直至显式退出；
    /// 总开关键录入为单次录入，完成后自动退出。
    /// </summary>
    public void CompletePick(InputSource source)
    {
        if (!PickingActive) return;
        var kind = PickingKind;

        // 总开关键录入：仅支持键盘键；冲突检测与方案迁移由 SetGlobalSwitchKey 负责。
        if (kind == PickKind.SetMasterKey)
        {
            CancelPicking();
            if (source.Kind == InputKind.Keyboard) SetGlobalSwitchKey(source);
            else AddLog("总开关键仅支持键盘键（鼠标 / 滚轮不可用）。");
            return;
        }

        // 切换方案热键录入：仅支持键盘键；冲突检测与占用校验由 SetProfileCycleKey 负责。
        if (kind == PickKind.SetCycleKey)
        {
            CancelPicking();
            if (source.Kind == InputKind.Keyboard) SetProfileCycleKey(source);
            else AddLog("切换方案热键仅支持键盘键（鼠标 / 滚轮不可用）。");
            return;
        }

        // 双宏分区：两步成对录入（暂存 / 成对落盘 / 模式保持交给 CompleteDualPick）。
        if (_pickingSection == ToggleSection.Dual)
        {
            CompleteDualPick(source);
            return;
        }

        // 新方案统一用当前时序档位值（常规 26/26 或极限 11/11）；后续可在方案列表行内修改。
        var interval = _config.DefaultIntervalMs;
        var hold = _config.DefaultHoldMs;

        if (source.Kind == InputKind.Mouse && source.Mouse is MouseInput.WheelUp or MouseInput.WheelDown)
        {
            AddLog("滚轮不能作为连发键（无按住语义），已忽略。");
            return;
        }
        if (GlobalSwitchSource is { } gk && gk.Equals(source))
        {
            AddLog($"[{InputNameMapper.GetSourceName(source)}] 已被总开关占用，不能录入为连发键。");
            return;
        }
        if (ProfileCycleSource is { } ck && ck.Equals(source))
        {
            AddLog($"[{InputNameMapper.GetSourceName(source)}] 已被切换方案热键占用，不能录入为连发键。");
            return;
        }
        if (IsDualPartnerUsed(source))
        {
            AddLog($"[{InputNameMapper.GetSourceName(source)}] 已是双宏方案的辅键，不能单独注册为连发键。");
            return;
        }

        var key = TaskSchedulerService.BuildSourceKey(source);
        var target = source.Kind switch
        {
            InputKind.Keyboard => new TargetKeyConfig
            {
                Kind = TargetKind.Keyboard,
                VirtualKey = source.VirtualKey,
                Extended = source.Extended,
                Mode = _selectedMode,
                HoldMs = hold,
                IntervalMs = interval,
                Enabled = true
            },
            _ => new TargetKeyConfig
            {
                Kind = TargetKind.Mouse,
                Mouse = source.Mouse,
                Mode = _selectedMode,
                HoldMs = hold,
                IntervalMs = interval,
                Enabled = true
            }
        };
        var existed = _config.Schemes.ContainsKey(key);
        _config.Schemes[key] = new KeyScheme { Enabled = true, Section = _pickingSection, Targets = { target } };

        RefreshAllButtons();
        ApplyConfigToScheduler();
        SaveConfig();
        RefreshSchemeList();
        SyncVisualizerRegistry();
        var modeName = _selectedMode switch { TriggerMode.Toggle => "开关", _ => "按压" };
        var sectionName = _selectedMode == TriggerMode.Toggle ? $"（{SectionName(_pickingSection)}）" : "";
        AddLog($"连发键 [{InputNameMapper.GetSourceName(source)}] 已{(existed ? "更新" : "添加")}"
               + $"（{modeName}模式{sectionName}，按压 {hold}ms / 间隔 {interval}ms）。");
    }

    /// <summary>
    /// 管理模式中右键键位：取消该键位的方案。双宏方案被任一键位（开关键位或辅键）右键时
    /// 整对取消（取消一个 = 取消一对）；双宏成对录入中右键暂存的第 1 键 = 放弃该键重新选择。
    /// 切换方案热键录入中右键已设置的键位 = 取消恢复缺省（右键其他键位仅提示）。
    /// </summary>
    public void RemovePickAt(InputSource source)
    {
        // 切换方案热键录入：右键原键位取消恢复缺省（单次操作完成即退出）。
        if (PickingKind == PickKind.SetCycleKey)
        {
            if (ProfileCycleSource is { } current && current.Equals(source))
            {
                CancelPicking();
                SetProfileCycleKey(null);
            }
            else
            {
                AddLog($"[{InputNameMapper.GetSourceName(source)}] 不是当前设置的切换方案热键，右键原键位才可取消。");
            }
            return;
        }

        if (!PickingActive || PickingKind != PickKind.AddScheme) return;

        // 成对录入中：右键暂存的第 1 键 = 放弃暂存，回到选择第 1 键。
        if (_pickingSection == ToggleSection.Dual && _dualFirstPick is { } pending && pending.Equals(source))
        {
            AbandonDualFirstPick("右键取消");
            return;
        }

        var key = TaskSchedulerService.BuildSourceKey(source);
        if (_config.Schemes.ContainsKey(key))
        {
            DeleteScheme(key);   // 双宏方案在 DeleteScheme 内整对取消（含配对日志）
            return;
        }

        // 键位自身无方案：若是某双宏方案的辅键，同样整对取消（取消一个 = 取消一对）。
        foreach (var (ownerKey, scheme) in _config.Schemes.ToList())
        {
            if (scheme.Section != ToggleSection.Dual || scheme.Targets.Count < 2) continue;
            var partner = TargetToSource(scheme.Targets[1]);
            if (partner is null || !partner.Equals(source)) continue;
            DeleteScheme(ownerKey);
            return;
        }

        AddLog($"[{InputNameMapper.GetSourceName(source)}] 没有已注册的方案可取消。");
    }

    /// <summary>方案行内编辑连发间隔：更新目标键配置 → 重建任务实时生效 → 落盘。</summary>
    private void UpdateSchemeInterval(string key, int intervalMs)
    {
        if (!_config.Schemes.TryGetValue(key, out var scheme)) return;
        var clamped = Compat.Clamp(intervalMs, Constants.MinIntervalMs, Constants.MaxIntervalMs);
        var changed = false;
        foreach (var t in scheme.Targets)
        {
            if (t.IntervalMs == clamped) continue;
            t.IntervalMs = clamped;
            changed = true;
        }
        if (!changed) return;
        // 重建任务使新间隔立即生效（调度器任务持有配置克隆，不重建不生效）。
        ApplyConfigToScheduler();
        SaveConfig();
    }

    /// <summary>方案行内编辑按压时长：更新目标键配置 → 重建任务实时生效 → 落盘。</summary>
    private void UpdateSchemeHold(string key, int holdMs)
    {
        if (!_config.Schemes.TryGetValue(key, out var scheme)) return;
        var clamped = Compat.Clamp(holdMs, Constants.MinHoldMs, Constants.MaxHoldMs);
        var changed = false;
        foreach (var t in scheme.Targets)
        {
            if (t.HoldMs == clamped) continue;
            t.HoldMs = clamped;
            changed = true;
        }
        if (!changed) return;
        ApplyConfigToScheduler();
        SaveConfig();
    }

    /// <summary>请求删除全部勾选（启用）的连发键：无勾选直接提示；有则交 View 弹确认框。</summary>
    private void RequestDeleteCheckedSchemes()
    {
        var count = _config.Schemes.Values.Count(s => s.Enabled);
        if (count == 0)
        {
            AddLog("没有勾选的连发键可删除（勾选 = 列表行首复选框）。");
            return;
        }
        DeleteCheckedRequested?.Invoke(count);
    }

    /// <summary>删除全部勾选（启用）的连发键（View 确认后调用，当前方案档位内）。</summary>
    public void DeleteCheckedSchemes()
    {
        var keys = _config.Schemes.Where(kv => kv.Value.Enabled).Select(kv => kv.Key).ToList();
        if (keys.Count == 0) return;
        var names = keys.Select(k => TaskSchedulerService.ParseSourceKey(k) is { } src
            ? InputNameMapper.GetSourceName(src) : k).ToList();
        foreach (var key in keys) _config.Schemes.Remove(key);

        RefreshAllButtons();
        ApplyConfigToScheduler();
        SaveConfig();
        SyncVisualizerRegistry();
        RefreshSchemeList();
        AddLog($"已删除 {keys.Count} 个勾选的连发键（{string.Join("、", names)}）。");
    }

    /// <summary>勾选框切换方案启用状态。</summary>
    private void SetSchemeEnabled(string key, bool enabled)
    {
        if (!_config.Schemes.TryGetValue(key, out var scheme) || scheme.Enabled == enabled) return;
        scheme.Enabled = enabled;
        RefreshAllButtons();
        ApplyConfigToScheduler();
        SaveConfig();
        OnPropertyChanged(nameof(SchemesAllEnabled));   // 全选态随单行勾选联动
        var source = TaskSchedulerService.ParseSourceKey(key);
        AddLog($"连发键 [{(source is null ? key : InputNameMapper.GetSourceName(source))}]"
               + $" 已{(enabled ? "启用" : "停用")}。");
    }

    /// <summary>
    /// 双宏第二勾选框：首键位是否参与连发（写入首目标键 Enabled，随配置持久化）。
    /// 取消勾选后首键仅作启停触发键：调度器重建任务时过滤未启用目标，
    /// 双宏任务只剩次键 → 按首键启动 / 停止次键单独连发（不再交替）。
    /// </summary>
    private void SetDualFirstKeyFiring(string key, bool firing)
    {
        if (!_config.Schemes.TryGetValue(key, out var scheme) ||
            scheme.Section != ToggleSection.Dual || scheme.Targets.Count < 1) return;
        if (scheme.Targets[0].Enabled == firing) return;
        scheme.Targets[0].Enabled = firing;
        ApplyConfigToScheduler();
        SaveConfig();
        var source = TaskSchedulerService.ParseSourceKey(key);
        AddLog($"双宏 [{(source is null ? key : InputNameMapper.GetSourceName(source))}]"
               + (firing ? "：首键位恢复参与连发（1-2-1-2 交替）。"
                         : "：首键位改为仅启停开关（仅次键连发）。"));
    }

    /// <summary>删除连发键方案（双宏方案任一键位删除时整对取消，日志列出两个键位）。</summary>
    private void DeleteScheme(string key)
    {
        var dualPartner = _config.Schemes.TryGetValue(key, out var existing) &&
                          existing.Section == ToggleSection.Dual && existing.Targets.Count >= 2
            ? TargetToSource(existing.Targets[1])
            : null;
        if (_config.Schemes.Remove(key))
        {
            RefreshAllButtons();
            ApplyConfigToScheduler();
            SaveConfig();
            SyncVisualizerRegistry();
            var source = TaskSchedulerService.ParseSourceKey(key);
            var name = source is null ? key : InputNameMapper.GetSourceName(source);
            AddLog(dualPartner is null
                ? $"连发键 [{name}] 已删除。"
                : $"双宏 [{name}] + [{InputNameMapper.GetSourceName(dualPartner)}] 已取消（取消一个 = 取消一对）。");
        }
        RefreshSchemeList();
    }

    /// <summary>配置导入（View 层已读入 JSON 文本）。</summary>
    public bool ImportFromJson(string json)
    {
        try
        {
            var imported = System.Text.Json.JsonSerializer.Deserialize<AppConfig>(json);
            if (imported is null) throw new InvalidOperationException("内容为空");

            _config = imported;
            // 旧版本配置先迁移到当前版本语义，并归一化连发时序（间隔下限 / 按压时长缺失落默认）。
            ConfigService.MigrateIfNeeded(imported);
            ConfigService.NormalizeTiming(imported);
            // 导入后对齐方案档位（Schemes ↔ Profiles[ActiveProfile] 同一实例），
            // 并通知档位按钮同步选中态（导入可能来自不同档位的配置）。
            SyncProfileRuntime();
            OnPropertyChanged(nameof(ActiveProfile));
            // 用属性统一驱动：状态文本、横幅、按钮透明度同步刷新。
            GloballyEnabled = imported.GlobalSwitch.Enabled;
            // 全局可视化总开关随导入配置还原（services 闸门 + UI 图标同步）。
            GlobalVisualEnabled = imported.GlobalVisualEnabled;
            // 可视化方案随导入配置还原（services 过滤 + 下拉选中同步）。
            _visualizer.SetMode((VisualizerMode)Compat.Clamp(imported.VisualizerMode, 0, 2));
            OnPropertyChanged(nameof(VisualizerModeIndex));
            // 状态提醒开关随导入配置还原（services 开关 + 底栏按钮图标同步）。
            _visualizer.SetReminderEnabled(imported.StatusReminderEnabled);
            OnPropertyChanged(nameof(StatusReminderEnabled));
            // “成为衍天高手”语音按钮随导入配置还原（底栏按钮墨迹同步）。
            OnPropertyChanged(nameof(DivinerVoiceEnabled));
            // 键帽配色与悬浮窗位置随导入配置还原。
            Views.KeycapOverlayWindow.ApplyScheme(KeycapSchemes.Resolve(imported.KeycapScheme));
            OnPropertyChanged(nameof(KeycapSchemeName));
            _visualizer.SetPosition(imported.VisualizerLeft, imported.VisualizerTop);
            // 键帽透明度随导入配置还原（钳位后应用，滑块与悬浮窗同步）。
            imported.VisualizerOpacity = Compat.Clamp(imported.VisualizerOpacity, 0, 100);
            _visualizer.SetOpacity(imported.VisualizerOpacity);
            OnPropertyChanged(nameof(VisualizerOpacity));
            // 主题随导入配置还原（夜间模式 / 白天模式）。
            ApplyTheme();
            MasterKeyText = imported.GlobalSwitch.HasKey
                ? InputNameMapper.GetKeyName(imported.GlobalSwitch.VirtualKey, imported.GlobalSwitch.Extended)
                : "未设置";
            // 切换方案热键随导入配置还原（按钮显示名 + 运行期热键源同步）。
            CycleKeyText = imported.ProfileCycle.HasKey
                ? InputNameMapper.GetKeyName(imported.ProfileCycle.VirtualKey, imported.ProfileCycle.Extended)
                : "未设置";
            OnPropertyChanged(nameof(ProfileCycleSource));
            SoundVolume = Compat.Clamp(imported.SoundVolume, 0, 100);
            // 自定义提示音随导入配置还原（文件缺失自动回退默认并留痕）。
            _soundCue.ApplyCustom(imported.CustomSounds);
            // 键盘注入模式随配置同步（导入 / 导出；旧配置回退 UseScanCodes 语义）。
            var mode = imported.KeyboardMode is >= 0 and <= 3
                ? imported.KeyboardMode
                : imported.UseScanCodes ? 1 : 0;
            if (KeyboardMode != mode)
            {
                KeyboardMode = mode;
                KeyboardModeChanged?.Invoke(mode);
            }
            // 时序档位随导入配置还原（单选钮由 code-behind 订阅同步）。
            TimingPresetChanged?.Invoke(imported.TimingPreset);
            OnPropertyChanged(nameof(GlobalSwitchSource));
            MigrateLegacySchemes();
            RefreshAllButtons();
            UpdateMasterKeyHighlight();
            RefreshSchemeList();
            ApplyConfigToScheduler();
            SyncVisualizerRegistry();
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
        SyncVisualizerRegistry();
    }
}
