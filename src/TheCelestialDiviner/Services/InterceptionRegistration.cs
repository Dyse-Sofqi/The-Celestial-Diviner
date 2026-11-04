using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using TheCelestialDiviner.Helpers;

namespace TheCelestialDiviner.Services;

/// <summary>Windows 的输入设备类（Interception 是"类过滤驱动"，挂在类上，不是挂在某个设备上）。</summary>
public enum DeviceClass
{
    /// <summary>键盘类 {4D36E96B-…}（注入实际依赖它）。</summary>
    Keyboard,

    /// <summary>鼠标类 {4D36E96F-…}（官方安装器会一起改，是爆炸半径的另一半）。</summary>
    Mouse
}

/// <summary>
/// 一个设备类过滤链的原样快照。<see cref="Upper"/> 保序、不去重、不删空串 ——
/// 校验要靠"原样"比对"安装前存在的项现在还在不在"，任何归一化都会让这条判据失效。
/// </summary>
public sealed record ClassFilterSnapshot(
    DeviceClass Class,
    string KeyPath,
    int RegistryType,
    string[] Upper,
    string[] Lower)
{
    /// <summary>是否成功读到了该类的过滤链（false = 注册表项缺失 / 无权限）。</summary>
    public bool Readable => RegistryType >= 0;
}

/// <summary>安装后校验结果。任一集合非空即"这次安装不干净"。</summary>
public sealed record RegistrationCheck(
    string[] LostEntries,
    string[] Duplicates,
    string[] BadValues,
    string[] TypeIssues)
{
    /// <summary>原有项被覆盖 —— 唯一允许自动回滚的情况。</summary>
    public bool NeedsRestore => LostEntries.Length > 0;

    /// <summary>需要用户处理（不致命，不自动改）。</summary>
    public bool HasProblems => Duplicates.Length > 0 || BadValues.Length > 0 || TypeIssues.Length > 0;

    /// <summary>一句话摘要（日志 / 安装结果文案用）。</summary>
    public string Describe()
    {
        var parts = new List<string>();
        if (LostEntries.Length > 0) parts.Add($"原有项被覆盖：{string.Join("、", LostEntries)}");
        if (Duplicates.Length > 0) parts.Add($"重复项：{string.Join("、", Duplicates)}");
        if (BadValues.Length > 0) parts.Add($"空项：{string.Join("、", BadValues)}");
        if (TypeIssues.Length > 0) parts.Add($"类型异常：{string.Join("、", TypeIssues)}");
        return parts.Count == 0 ? "通过" : string.Join("；", parts);
    }
}

/// <summary>
/// Interception 的注册表足迹：两个设备类的 UpperFilters / LowerFilters。
///
/// 为什么单独成一层：官方安装器改的是 <b>两个</b> 类（键盘 + 鼠标），而程序原先只认键盘类，
/// 于是"鼠标类坏着、键盘类正常"的机器会被判成一切正常。这里把"读 / 校验 / 最小回滚"收在一处，
/// 上层（<see cref="InterceptionDriverService"/>）只消费结论，不再各写一套注册表逻辑。
///
/// 许可边界：本类只碰注册表，不碰驱动文件、不建服务项 —— 安装 / 卸载仍然只由官方安装器执行。
/// </summary>
public static class InterceptionRegistration
{
    /// <summary>键盘类 GUID（UpperFilters 挂在它下面）。</summary>
    public const string KeyboardClassKey =
        @"SYSTEM\CurrentControlSet\Control\Class\{4D36E96B-E325-11CE-BFC1-08002BE10318}";

    /// <summary>鼠标类 GUID（官方安装器同样会改它）。</summary>
    public const string MouseClassKey =
        @"SYSTEM\CurrentControlSet\Control\Class\{4D36E96F-E325-11CE-BFC1-08002BE10318}";

    private const int TypeMissing = -1;

    /// <summary>类 → 注册表路径。</summary>
    public static string ClassKeyPath(DeviceClass kind) =>
        kind == DeviceClass.Keyboard ? KeyboardClassKey : MouseClassKey;

    /// <summary>类 → Interception 注册的服务名（官方安装器固定用这两个名字）。</summary>
    public static string ServiceName(DeviceClass kind) =>
        kind == DeviceClass.Keyboard ? "keyboard" : "mouse";

    /// <summary>类 → 系统自带的类驱动名（Windows 默认值，也是回滚时唯一允许"构造"的值）。</summary>
    public static string DefaultFilter(DeviceClass kind) =>
        kind == DeviceClass.Keyboard ? "kbdclass" : "mouclass";

    /// <summary>类 → 中文短名（日志 / 弹窗文案）。</summary>
    public static string Label(DeviceClass kind) => kind == DeviceClass.Keyboard ? "键盘类" : "鼠标类";

    /// <summary>全部类（顺序固定：键盘在前，日志读起来稳定）。</summary>
    public static readonly DeviceClass[] AllClasses = { DeviceClass.Keyboard, DeviceClass.Mouse };

    // ---------- 读 ----------

    /// <summary>读一个类的过滤链（只读；读不到返回 <see cref="ClassFilterSnapshot.Readable"/> = false）。</summary>
    public static ClassFilterSnapshot Capture(DeviceClass kind)
    {
        var path = ClassKeyPath(kind);
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(path);
            if (key is null) return new ClassFilterSnapshot(kind, path, TypeMissing, Array.Empty<string>(), Array.Empty<string>());

            var type = ReadMultiString(key, "UpperFilters", out var upper);
            ReadMultiString(key, "LowerFilters", out var lower);
            return new ClassFilterSnapshot(kind, path, type, upper, lower);
        }
        catch (Exception ex)
        {
            Logger.Warn($"读取{Label(kind)}过滤链失败：{ex.Message}");
            return new ClassFilterSnapshot(kind, path, TypeMissing, Array.Empty<string>(), Array.Empty<string>());
        }
    }

    /// <summary>读两个类（快照用；顺序同 <see cref="AllClasses"/>）。</summary>
    public static ClassFilterSnapshot[] CaptureAll() =>
        AllClasses.Select(Capture).ToArray();

    /// <summary>该类是否已注册 Interception 的过滤驱动。</summary>
    public static bool IsRegistered(DeviceClass kind)
    {
        var snapshot = Capture(kind);
        return snapshot.Readable
               && snapshot.Upper.Any(v => string.Equals(v, ServiceName(kind), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>任一类已注册（"系统里有这个驱动"，与"能不能注入"是两件事）。</summary>
    public static bool AnyRegistered => AllClasses.Any(IsRegistered);

    /// <summary>该类上是否已有目标项出现 ≥2 次（回归：二次安装可能留下重复项）。</summary>
    public static bool HasDuplicate(DeviceClass kind)
    {
        var snapshot = Capture(kind);
        return snapshot.Readable
               && snapshot.Upper.Count(v => string.Equals(v, ServiceName(kind), StringComparison.OrdinalIgnoreCase)) > 1;
    }

    /// <summary>
    /// 注册名还在、但 <c>System32\drivers</c> 下的 .sys 已经没了（手动删过 / 杀软隔离过）。
    /// 这种"悬空项"是整类设备一起报 Code 39 的经典成因：留着必炸，删掉即可恢复。
    /// 返回受影响的类名，供启动自检与修复共用。
    /// </summary>
    public static string[] DanglingClasses()
    {
        var result = new List<string>();
        foreach (var kind in AllClasses)
        {
            var service = ServiceName(kind);
            var snapshot = Capture(kind);
            if (!snapshot.Readable) continue;
            if (!snapshot.Upper.Contains(service, StringComparer.OrdinalIgnoreCase)) continue;
            if (!File.Exists(DriverFilePath(service))) result.Add(Label(kind));
        }
        return result.ToArray();
    }

    /// <summary>
    /// 内核驱动的落盘位置。固定用 Windows\System32 而不是 <see cref="Environment.SystemDirectory"/>：
    /// 后者在 32 位进程下会返回 SysWOW64，那里没有内核驱动。
    /// </summary>
    private static string DriverFilePath(string service) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "drivers", service + ".sys");

    /// <summary>该类上除系统类驱动与 Interception 之外的过滤项（第三方输入软件，冲突源）。</summary>
    public static string[] Foreign(DeviceClass kind)
    {
        var snapshot = Capture(kind);
        if (!snapshot.Readable) return Array.Empty<string>();
        return snapshot.Upper
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Where(v => !string.Equals(v, DefaultFilter(kind), StringComparison.OrdinalIgnoreCase))
            .Where(v => !string.Equals(v, ServiceName(kind), StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    // ---------- 校验 ----------

    /// <summary>
    /// 安装后校验（四项）：原有项仍在 / 目标项不重复 / 无空项 / 类型仍是 REG_MULTI_SZ。
    /// 只读，不改任何东西 —— 回滚由调用方按 <see cref="RegistrationCheck.NeedsRestore"/> 决定。
    /// </summary>
    public static RegistrationCheck Verify(IReadOnlyList<ClassFilterSnapshot> before)
    {
        var lost = new List<string>();
        var duplicates = new List<string>();
        var badValues = new List<string>();
        var typeIssues = new List<string>();

        foreach (var snapshot in before)
        {
            if (!snapshot.Readable) continue;   // 安装前就读不到的类，不参与比对
            var now = Capture(snapshot.Class);
            if (!now.Readable)
            {
                lost.Add($"{Label(snapshot.Class)}：读不到过滤链");
                continue;
            }

            foreach (var entry in snapshot.Upper)
            {
                if (string.IsNullOrWhiteSpace(entry)) continue;
                if (!now.Upper.Contains(entry, StringComparer.OrdinalIgnoreCase))
                    lost.Add($"{Label(snapshot.Class)}的 {entry}");
            }

            var count = now.Upper.Count(v => string.Equals(v, ServiceName(snapshot.Class), StringComparison.OrdinalIgnoreCase));
            if (count > 1) duplicates.Add($"{Label(snapshot.Class)}：{ServiceName(snapshot.Class)} 出现 {count} 次");

            if (now.Upper.Any(string.IsNullOrWhiteSpace) || now.Lower.Any(string.IsNullOrWhiteSpace))
                badValues.Add($"{Label(snapshot.Class)}：过滤链里有空项");

            var isMultiString = snapshot.RegistryType == (int)RegistryValueKind.MultiString;
            if (isMultiString && now.RegistryType != snapshot.RegistryType)
                typeIssues.Add($"{Label(snapshot.Class)}：UpperFilters 类型变成 {now.RegistryType}");
        }

        return new RegistrationCheck(lost.ToArray(), duplicates.ToArray(), badValues.ToArray(), typeIssues.ToArray());
    }

    // ---------- 最小回滚 ----------

    /// <summary>
    /// 把被覆盖掉的原有项插回去。**只增不删**：安装后新增的项一律保留（可能是用户期间装的别的软件，
    /// 程序没资格替他做主）。返回实际恢复的条目描述，供日志留痕。
    ///
    /// 唯一会"构造"值的情况：恢复后某一类只剩目标项（= 连系统类驱动都被冲掉了），
    /// 这时补回该类默认值（kbdclass / mouclass）—— 这是 Windows 的既定默认，不补整类会起不来。
    /// </summary>
    public static string[] RestoreMissing(IReadOnlyList<ClassFilterSnapshot> before)
    {
        var restored = new List<string>();

        foreach (var snapshot in before)
        {
            if (!snapshot.Readable) continue;
            var now = Capture(snapshot.Class);
            if (!now.Readable) continue;

            var list = now.Upper.Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
            var changed = false;

            for (var index = 0; index < snapshot.Upper.Length; index++)
            {
                var entry = snapshot.Upper[index];
                if (string.IsNullOrWhiteSpace(entry)) continue;
                if (list.Contains(entry, StringComparer.OrdinalIgnoreCase)) continue;

                list.Insert(Math.Min(index, list.Count), entry);
                restored.Add($"{Label(snapshot.Class)}的 {entry}");
                changed = true;
            }

            if (!changed) continue;

            if (list.Count == 1 && string.Equals(list[0], ServiceName(snapshot.Class), StringComparison.OrdinalIgnoreCase))
            {
                list.Insert(0, DefaultFilter(snapshot.Class));
                restored.Add($"{Label(snapshot.Class)}的 {DefaultFilter(snapshot.Class)}（系统默认，缺了整类都起不来）");
            }

            if (!WriteUpper(snapshot.Class, list.ToArray()))
                Logger.Error($"恢复{Label(snapshot.Class)}过滤链失败：写回注册表未成功（可能需要管理员权限）。");
        }

        return restored.ToArray();
    }

    // ---------- 修复（用户显式触发） ----------

    /// <summary>
    /// 修复过滤链：去空项 → 按快照补回丢失项 → 去重（保留首次出现）→ 缺默认类驱动时补回。
    /// 只在用户点了「检查与修复驱动注册」时调用；返回做过的动作（空 = 本来就是干净的）。
    /// </summary>
    public static IReadOnlyList<string> Repair(IReadOnlyList<ClassFilterSnapshot>? reference)
    {
        var actions = new List<string>();

        foreach (var kind in AllClasses)
        {
            var now = Capture(kind);
            if (!now.Readable)
            {
                actions.Add($"{Label(kind)}：读不到过滤链，未做修改");
                continue;
            }

            var list = now.Upper.Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
            var changed = list.Count != now.Upper.Length;   // 去掉过空项

            // 按快照补回被覆盖的原有项
            var snapshot = reference?.FirstOrDefault(s => s.Class == kind);
            if (snapshot is { Readable: true })
            {
                for (var index = 0; index < snapshot.Upper.Length; index++)
                {
                    var entry = snapshot.Upper[index];
                    if (string.IsNullOrWhiteSpace(entry)) continue;
                    if (list.Contains(entry, StringComparer.OrdinalIgnoreCase)) continue;
                    list.Insert(Math.Min(index, list.Count), entry);
                    actions.Add($"{Label(kind)}：补回 {entry}");
                    changed = true;
                }
            }

            // 悬空项：注册名还在，但 System32\drivers 下的 .sys 已经没了（手动删过 / 杀软隔离过）。
            // 这种项留着必炸 —— 类过滤驱动加载失败会让整个类报 Code 39，删掉它反而是标准的自救做法。
            var service = ServiceName(kind);
            if (list.Contains(service, StringComparer.OrdinalIgnoreCase))
            {
                if (!File.Exists(DriverFilePath(service)))
                {
                    list.RemoveAll(v => string.Equals(v, service, StringComparison.OrdinalIgnoreCase));
                    actions.Add($"{Label(kind)}：移除悬空项 {service}（{service}.sys 已不存在，留着整类设备都会报 Code 39）");
                    changed = true;
                }
            }

            // 去重（保留首次出现）
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var index = list.Count - 1; index >= 0; index--)
            {
                if (seen.Add(list[index])) continue;
                actions.Add($"{Label(kind)}：移除重复项 {list[index]}");
                list.RemoveAt(index);
                changed = true;
            }

            // 系统类驱动缺失：补回默认值（缺了整类设备都会报 Code 39）
            if (!list.Any(v => string.Equals(v, DefaultFilter(kind), StringComparison.OrdinalIgnoreCase)))
            {
                list.Insert(0, DefaultFilter(kind));
                actions.Add($"{Label(kind)}：补回系统默认 {DefaultFilter(kind)}");
                changed = true;
            }

            if (changed && !WriteUpper(kind, list.ToArray()))
                actions.Add($"{Label(kind)}：写回注册表失败（可能需要管理员权限）");
        }

        return actions;
    }

    // ---------- 注册表辅助 ----------

    /// <summary>读 REG_MULTI_SZ，返回注册表类型（缺失返回 -1）；被改成 REG_SZ 的机器按单元素处理。</summary>
    private static int ReadMultiString(RegistryKey key, string name, out string[] values)
    {
        values = Array.Empty<string>();
        try
        {
            var kind = key.GetValueKind(name);
            var raw = key.GetValue(name);
            values = raw switch
            {
                string[] array => array,
                string single => new[] { single },
                _ => Array.Empty<string>()
            };
            return (int)kind;
        }
        catch
        {
            return TypeMissing;   // 值不存在 / 无权限
        }
    }

    /// <summary>写回 UpperFilters（REG_MULTI_SZ）。防御性地再排一次空串：空项会让系统认为整个值损坏。</summary>
    private static bool WriteUpper(DeviceClass kind, string[] values)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(ClassKeyPath(kind), writable: true);
            if (key is null) return false;
            key.SetValue("UpperFilters", values.Where(v => !string.IsNullOrWhiteSpace(v)).ToArray(),
                RegistryValueKind.MultiString);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"写回{Label(kind)}过滤链失败。", ex);
            return false;
        }
    }
}
