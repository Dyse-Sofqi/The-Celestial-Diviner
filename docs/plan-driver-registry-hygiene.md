# 方案：驱动注册卫生（双类快照 · 事务化安装 · 孤儿自愈）

> 状态：**已实现**（2026-11-04）。落地记录与实际偏差见文末「十二、落地记录」。
> 范围：Interception 驱动的状态判定 / 安装 / 卸载 / 异常自愈；不含 DD 驱动（DD 没有系统级注册项）。
> 目标：把这一轮排查里暴露的 9 个问题一次性收敛到同一条管线里，而不是各打各的补丁。

---

## 一、问题清单

| 编号 | 现象 | 位置 | 后果 |
|---|---|---|---|
| P1 | 只读键盘类的 `UpperFilters`，鼠标类完全不看 | `InterceptionDriverService.cs:72`（只有 `KeyboardClassKey`） | 鼠标类坏掉时判定「一切正常」 |
| P2 | 卸载成功判据是 `!DriverInstalled`，仍只看键盘类 | `InterceptionDriverService.cs:767` | 鼠标类残留时报「卸载完成」，用户继续坏 |
| P3 | 卸载菜单项可见性 = 键盘类已注册 | `MainWindow.xaml:933` | 键盘类好了、鼠标类坏着时，入口消失 |
| P4 | `PrepareOnce` 无条件跑 `/install`，没有「已装就跳过」 | `InterceptionDriverService.cs:595-626` | 二次安装可能产生重复过滤项 |
| P5 | 无安装前快照、无安装后校验、无回滚 | 全流程缺失 | 第三方 filter 被冲掉时无从发现、无从恢复 |
| P6 | 无环境预检（HVCI / 反作弊 / 冲突 filter 只看键盘类） | `SystemDriverAdvisor.cs:127`（只做路线推荐，不拦安装） | 用户在注定失败的机器上装一遍 |
| P7 | 删程序目录 = 事实上的「卸载」，驱动留成孤儿 | `App.xaml.cs:392`（刻意退出不动驱动） | 系统里长期躺着一个没人管的类过滤驱动 |
| P8 | 睡眠唤醒后设备槽位耗尽，程序无感知 | `InterceptionDriverService.ResolveKeyboardDevice` | 「设备管理器正常但按键无反应」，用户以为程序坏了 |
| P9 | 自救命令只写在文档里，出事的用户键盘已死 | 无 | 用户只能靠另一台设备搜 |

---

## 二、根因

不是九个 bug，是同一个建模错误：

> **程序把「两个设备类的注册表状态」当成外部环境去读，而不是当成自己拥有、需要事务化管理的状态。**

三条投影：

1. **读的范围只有一半**（P1/P2/P3）—— 官方安装器改了两个类，程序只认一个。
2. **写的时候没有事务**（P4/P5）—— 只有「调用安装器」和「调用卸载器」两个动作，中间没有任何记账。
3. **状态只存在于注册表里**（P6/P7/P8/P9）—— 程序没有「我在这台机器上做过什么」的本地记录，于是既认不出孤儿，也无法在出错时解释原因。

---

## 三、统一模型：S-P-A-V-R

所有入口（手动安装、手动卸载、导引选择、启动自检）走同一条五段管线：

```
        ┌──────────┐   ┌──────────┐   ┌────────┐   ┌────────┐   ┌────────────┐
   ────▶│ S 快照   │──▶│ P 预检   │──▶│ A 执行 │──▶│ V 校验 │──▶│ R 回滚/自愈│
        │ 读双类   │   │ 环境+冲突│   │ 官方   │   │ 双类   │   │ 仅回滚自己 │
        │ 落盘记账 │   │ 阻塞/警告│   │ 安装器 │   │ 四项   │   │ 造成的改动 │
        └──────────┘   └──────────┘   └────────┘   └────────┘   └────────────┘
             │               │              │            │             │
             └───────────────┴──────────────┴────────────┴─────────────┘
                                    全部写入同一份 driver-state.json
```

- **S 必须有**，且失败即中止（拿不到快照就不许动注册表）。
- **P 可跳过**（用户已明确继续），但结果必须落日志。
- **A 永远是官方安装器**（见第七节许可边界），程序不自己装驱动。
- **V 是新增的硬性后置条件**，不允许「调完就当成功」。
- **R 不是自动执行**，除了一种情况：原有项丢失（见 6.5）。

---

## 四、数据模型

### 4.1 快照结构

```csharp
// Services/InterceptionRegistration.cs
public enum DeviceClass { Keyboard, Mouse }

/// <summary>一个设备类的过滤链快照（原样记录，含注册表类型）。</summary>
public sealed record ClassFilterSnapshot(
    DeviceClass Class,
    int ValueType,          // 7 = REG_MULTI_SZ；其它值说明这台机器的键被改过，回滚要放弃
    string[] Upper,         // 原样，不去重、不排序、不去空串（校验时要按原样比对）
    string[] Lower);

/// <summary>一次安装前后的完整记账。</summary>
public sealed record RegistrationRecord(
    DateTime CapturedUtc,
    string AppVersion,
    string DllSha256,
    ClassFilterSnapshot KeyboardBefore,
    ClassFilterSnapshot MouseBefore,
    bool InstalledByUs);
```

要点：**原样保存**（`string[]` 保序、保留空串）。任何「顺手排序/去重」都会让后面的「原有项是否仍在」校验失去意义。

### 4.2 落盘位置

`%APPDATA%\TheCelestialDiviner\driver-state.json`，独立于 `config.json`。

**为什么不塞进 config.json**：配置会被「恢复默认 / 导入方案 / 用户手删」重置，而这份记账是排查证据，不能跟着 UI 设置一起被清掉。反过来，导出/导入配置时也不该带走它（换机器后旧快照是错的）。

### 4.3 配置新增字段（v21）

只放「人可见的意图」，不放证据：

```csharp
/// <summary>Interception 驱动是否由本程序安装过（卸载后置 false；用于孤儿提示）。</summary>
public bool InterceptionInstalledByApp { get; set; }

/// <summary>安装时的程序版本与 UTC 时间（「什么时候装的」——排查第一问）。</summary>
public string InterceptionInstallAppVersion { get; set; } = "";
public DateTime? InterceptionInstallUtc { get; set; }
```

`CurrentVersion` 20 → 21，字段全有默认值，**无迁移动作**，与既有 v10~v20 的处理方式一致。

---

## 五、状态机

不用扁平枚举（会爆炸），用 **1 个主状态 + 3 个正交标志 + 1 个运行态**：

**主状态**（`Registration`）

| 值 | 判据 | 安装项 | 卸载项 | 注入 |
|---|---|---|---|---|
| `None` | 两类都无 `keyboard`/`mouse` | 显示 | 隐藏 | 不可用 |
| `KeyboardOnly` | 只有键盘类有 | 隐藏 | **显示** | 可用 |
| `MouseOnly` | 只有鼠标类有 | 隐藏 | **显示** | 不可用（提示修复） |
| `Both` | 两类都有 | 隐藏 | 显示 | 可用 |

**标志**

| 标志 | 判据 | 处理 |
|---|---|---|
| `HasDuplicate` | 任一类里目标项出现 ≥2 次 | 阻塞安装，引导修复 |
| `HasForeign` | 出现了 `kbdclass`/`mouclass`/`keyboard`/`mouse` 之外的值 | 警告（不阻塞），列出名单 |
| `IsOrphan` | 主状态 ≠ `None`，但 `driver-state.json` 无记录或版本不匹配 | 提示「可能是旧版本残留」 |

**运行态**（仅 `KeyboardOnly`/`Both` 有意义）：`Ready` / `NeedsReboot`，判据沿用现有 `TryProbeContext()`。

> 关键变化：**`IsAvailable`（能不能用）与 `IsRegistered`（系统里有没有）彻底分开**。现有代码把两者揉在 `DriverInstalled` 里，才导致 P1/P2/P3 这一串。

---

## 六、流程

### 6.1 安装

1. `S`：读两类 `UpperFilters`/`LowerFilters` → 写 `driver-state.json`。**读失败即中止**，不弹「安装失败」，弹「无法读取驱动注册表，请检查权限」。
2. `P`：跑 `DriverPreflight.Evaluate()`（见 6.6），产出 `Blocking[] / Warning[] / Info[]`。
   - 有 Blocking → 弹窗列出原因，**不给「继续」按钮**。
   - 只有 Warning → 弹窗，默认按钮是「取消」，可强制继续。
3. **幂等闸**：`if (IsRegistered(Keyboard) || IsRegistered(Mouse)) return Succeeded("已在系统里，无需重装")`。这一句独立于预检，P4 到此为止。
4. `A`：官方 `install-interception.exe /install`（不变）。
5. `V`：四项校验
   | 校验 | 失败处理 |
   |---|---|
   | 两类 `ValueType` 仍是 7 | 提示修复（不自动改） |
   | **安装前存在的每一项仍然存在** | **自动回滚**（止血，见 6.5） |
   | 目标项恰好 1 次 | 提示修复 |
   | 无空串 | 提示修复 |
6. 记 `InterceptionInstalledByApp = true` + 版本 + 时间，刷新状态。

### 6.2 卸载

1. `S`：再拍一次快照（覆盖前一份，但**保留首份为 `.before-install.bak`**）。
2. 调官方 `/uninstall`。
3. `V`：两类目标项都应消失。
4. 判据改为 `Registration == None`，**不是** `!DriverInstalled`。若只剩一类 → 报「部分卸载完成：<类> 仍有残留」，并保持卸载入口可见（P2/P3 同时修掉）。
5. 成功才置 `InterceptionInstalledByApp = false`。

### 6.3 启动自检

启动时（异步、不阻塞 UI）跑一次 `Evaluate()`：

- `IsOrphan` → 日志 + 注释区提示：「系统里还留着 Interception 驱动，可能是旧版本残留，可在「选项 → 键盘注入」里卸载」。**措辞是「可能」，因为用户连 AppData 一起删时我们无法证明是自己装的**。
- `HasDuplicate` / `HasForeign` → 只写日志（启动不打扰）。
- `NeedsReboot` → 维持现状（现有逻辑已覆盖）。

### 6.4 唤醒

订阅 `SystemEvents.PowerModeChanged == Resume`：

1. `Shutdown()`（销毁上下文）→ `EnsureReady()` 重建。
2. 记录 `ResolveKeyboardDevice()` 返回的设备号；**≥8 时写 Warn** 并在日志区提示「Interception 设备槽位接近上限，建议重启」。
3. 重建失败 → 日志说明可能是唤醒导致的设备重枚举，建议重启。

这解决 P8 里「设备管理器正常但没反应」的不可解释状态——至少让用户拿到一句可执行的话。

### 6.5 回滚策略（严格限定）

**自动回滚只允许一种情况**：校验发现「安装前存在的某一项不见了」。

做法：
1. 只把**丢失的项**插回去（保序，插在原索引处）；
2. **不删除**任何安装后新增的项（可能是用户期间装的别的软件，我们不能替人做主）；
3. 若某一类因此只剩目标项，写回该类的系统默认值（键盘 `kbdclass` / 鼠标 `mouclass`）——这是全流程里唯一「构造」值的地方，写日志留痕。

其它校验失败（重复项、类型错、空串）**不自动改**，只提示并给修复入口。理由：重复项不致命，而程序悄悄改注册表的风险远大于收益。

### 6.6 预检项

复用 `SystemDriverAdvisor.Advice`（已经算好了 HVCI / ShadowStacks / SAC），不另写一套：

| 项 | 来源 | 级别 |
|---|---|---|
| HVCI / ShadowStacks / SAC 开启 | 现有 `Advice` | Blocking（注明「关掉并重启后可再试」） |
| Secure Boot 开启 | `HKLM\SYSTEM\CurrentControlSet\Control\SecureBoot\State\UEFISecureBootEnabled` | Warning |
| 两类上的第三方 filter | 现有 `ForeignFilters`，**扩展到鼠标类** | Warning，列名单 |
| DD 驱动在位 | `DdDriverService.IsAvailable` | Blocking（互斥） |
| 已知反作弊驱动（`vgk.sys`/`BEDaisy.sys`/`EasyAntiCheat*.sys`/`ACE-*.sys`） | 扫 `System32\drivers` | Info（只说「检测到 X，驱动可能被拦」，不吓人） |
| 重复项 / 注册表类型异常 | 6.1 的快照 | Blocking |

---

## 七、许可边界（必须守住）

现有实现的三条原则不能破（`InterceptionDriverService.cs:35-43` 已写明）：不修改官方二进制、不重签驱动、不自己实现驱动安装。

本方案在这三条之内：

1. **安装 / 卸载仍然只调官方安装器**，程序不写驱动文件、不建服务项。
2. 快照与校验**只读**注册表，不构成「安装逻辑」。
3. 唯一的写注册表操作是 **6.5 的回滚**，且：
   - 只写回**自己保存的、安装前的原值**；
   - 只在「原有项丢失」这一种情况下触发；
   - 必须先弹窗说明「即将恢复被覆盖的过滤驱动项」，用户确认后执行。
4. 若快照不可用（首次运行的旧用户、类型异常），**宁可不动**，只给手动命令。

---

## 八、改动清单

**新增**

| 文件 | 内容 |
|---|---|
| `Services/InterceptionRegistration.cs` | `DeviceClass` / `ClassFilterSnapshot` / 读双类 / 快照 / 四项校验 / 最小回滚 / 修复。纯注册表，无 UI、无日志以外的副作用 |
| `Services/DriverPreflight.cs` | `Evaluate()` → `Blocking/Warning/Info`，内部复用 `SystemDriverAdvisor` |
| `Services/DriverStateStore.cs` | `driver-state.json` 读写（含 `.before-install.bak`），文件损坏按「无记录」处理 |

**修改**

| 文件 | 改动 |
|---|---|
| `InterceptionDriverService.cs` | `DriverInstalled` → `IsRegistered(DeviceClass)`；`IsAvailable` 拆开；`PrepareOnce` 加幂等闸 + V/R；`Uninstall` 双类判据；唤醒钩子 |
| `SystemDriverAdvisor.cs` | `Advice` 增加 `MouseFilters` / `ForeignMouseFilters`；`ForeignFilters` 合并两类 |
| `MainViewModel.cs` | `IsInterceptionDriverInstalled`（卸载项用，任一即真）与 `IsInterceptionDriverAvailable`（注入用，键盘类且 Ready）分开；新增 `RepairInterceptionRegistrationCommand`；启动自检；`RequestInterceptionUninstall` 结果文案细分 |
| `MainWindow.xaml` | 卸载项 `Visibility` 改绑 `IsInterceptionDriverInstalled`；Interception 子菜单加「检查与修复驱动注册…」 |
| `MainWindow.xaml.cs` | MenuComments 补两条说明 |
| `AppConfig.cs` / `ConfigService.cs` | v21 三个字段（无迁移动作） |
| `使用说明.txt` / `README.md` | 首段加「**删目录前请先在菜单里卸载驱动**」 |

---

## 九、迁移与兼容

- v20 → v21：字段默认值即正确语义（`InstalledByApp = false`），无迁移动作。
- **老用户第一次升级**：`driver-state.json` 不存在 → 若系统里已注册，判为 `IsOrphan` → 只提示，不拦截、不改注册表。第一次安装/卸载时补拍快照。
- 行为变化的只有三处，且都是「原来会误判成正常、现在会说实话」：鼠标类残留、重复项、孤儿驱动。
- 不动 DD 路线、不动注入时序、不动公告/主题。

---

## 十、测试计划

**本机（当前无驱动，`UpperFilters = kbdclass`）可测**

1. 状态判定 = `None`，安装项可见、卸载项隐藏。
2. 快照能读到键盘类 `kbdclass`，鼠标类 `mouclass`。
3. 预检在本机（HVCI 关、无第三方 filter）应全部通过 → 不误报。
4. 模拟：手工把键盘类 `UpperFilters` 加成 `kbdclass\0keyboard\0keyboard` → 重启程序应报 `HasDuplicate` 且阻塞安装；改回后恢复。**测完务必改回 `kbdclass`。**

**需要一台装过 Interception 的机器**

5. 装 → 校验四项全过 → 状态 `Both` + `Ready`。
6. 手动删 `System32\drivers\keyboard.sys` → 状态仍 `KeyboardOnly`，但注入报「需重启 / 加载失败」，且卸载入口**仍然可见**（P3 的回归点）。
7. 手动把鼠标类 `UpperFilters` 去掉 `mouse` 只留 `mouclass` → 状态 `KeyboardOnly`，卸载项仍可见，点卸载后应报「键盘类已清、鼠标类无残留」并置为 `None`（P2 回归点）。
8. 卸载 → 两类都回到默认 → `None`。
9. 睡眠唤醒 3 次 → 上下文能重建，日志有设备号记录。

**回归（最重要）**

10. 正常用户从 v1.9.2 升级：不做任何操作时，日志里除了启动自检那一行，**不应出现任何新提示**。

---

## 十一、不做什么

- **不自动改注册表**（除 6.5 那一种）。所有修复都要用户点一下。
- **不做「只装键盘类」的裁剪安装**。官方安装器会同时改鼠标类，绕开它就等于自己实现安装逻辑（违反第七节）。这个需求记在待办里，要做得先验证「只摘鼠标类」不会导致键鼠全废（issue #181 里有人试过，结果是两者都失效）。
- **不主动卸载别人的 filter**。第三方项一律只读、只报告。
- **不在启动时弹窗**。孤儿提示进日志和注释区，不打断用户。

---

## 十二、落地记录（2026-11-04）

已按本方案实现，`dotnet build -c Release` 通过（0 警告 0 错误）。

**新增文件**

| 文件 | 说明 |
|---|---|
| `Services/InterceptionRegistration.cs` | `DeviceClass` / `ClassFilterSnapshot` / `RegistrationCheck`；读双类、四项校验、最小回滚、修复 |
| `Services/DriverStateStore.cs` | `driver-state.json` + `driver-state.before-install.bak`；`HasStagingArtifacts` 弱证据 |
| `Services/DriverPreflight.cs` | 管理员 / HVCI 三件套 / 重复项 / DD 互斥 / Secure Boot / 第三方 filter / 反作弊 |

**与设计稿的偏差（均为实现时发现，理由记在这里）**

1. **DD 互斥从 Blocking 降为 Warning**。设计稿把「DD 驱动在位」写成阻塞项，但实现时确认
   `DdDriverService.IsAvailable` 的用户态库是**随包常驻**的 —— 按原判据会给几乎所有用户弹
   "不能安装"。改成警告：只说明两者互斥，不拦。
2. **HVCI 三件套维持 Blocking**，并补了一条设计稿没写透的理由：被 HVCI 拒绝加载的类过滤驱动
   不是安静失效，而是让整个设备类一起报 Code 39（见 `DriverPreflight.AppendKernelIntegrity` 注释）。
   这是"宁可不装"的唯一硬理由。
3. **幂等闸放在两处**：`MainViewModel.RequestInterceptionInstall`（UI 侧先拦，避免用户看到无意义弹窗）
   + `InterceptionDriverService.PrepareOnce`（后台兜底，防导引 / 代码路径绕过）。
4. **孤儿判定加了弱证据** `DriverStateStore.HasStagingArtifacts`（暂存目录里有安装器 ⇒ 很可能本程序装的）。
   否则 1.9.x 老用户升级后会被集体误报成"残留"。
5. **修复入口常驻且不依赖安装状态**：它是键鼠已经失灵时的自救通道，藏在"已安装才显示"的分支里等于没有。
6. **未改动 `%APPDATA%` 之外的任何位置**，也没有新增服务 / 计划任务。

**尚未落地的部分**

- `使用说明.txt` 不在仓库里（只存在于发布目录与本机安装目录），首段「删目录前先卸载驱动」需要
  在打包时同步补上；README 已改。
- CHANGELOG 条目留到发版时按版本写（当前只有 `CurrentVersion = 21` 的配置迁移备注）。
- 测试计划第 5~9 条（真机装 / 拆 / 睡眠唤醒）需要一台已装过 Interception 的机器，尚未执行。

---

## 十三、升级路径复查（2026-11-04 晚）

针对"1.9.1 升级上来会不会重演掉驱动"逐个场景走查，发现并修掉 4 个问题：

1. **幂等闸原本挡住了"补库文件"**（严重）。`PrepareOnce` 原先第一步就判断
   `KeyboardRegistered` 并直接返回，导致「驱动已注册、但 `interception.dll` 被杀软隔离」的
   1.9.1 用户点安装后既补不到库、又看不到任何变化，模式项永久灰着。
   改为：**先补全官方文件，再判幂等**（文件与是否已装无关）。
2. **安装入口的可见性改回 `IsAvailable == false`**。上一版改成"未安装才显示"，
   恰好把上面那批用户的唯一补救入口藏掉了。现在语义是"不可用（含库缺失）就显示"。
3. **启动自检补上「悬空项」检测**（注册名在、`.sys` 没了 → 整类 Code 39）。
   这类机器以前只在用户自己发现时才能救；现在启动即检测，并在注释区挂 30 秒提示
   （不弹窗）；重复项也走同一提示位（悬空优先）。
4. **卸载 / 修复补管理员权限前置判断**。非管理员时官方安装器只返回一个看不懂的退出码，
   用户会看到"卸载未完成（exit=5）"；现在直接说清"关掉程序 → 右键以管理员身份运行"。

另外补了 `docs/键盘鼠标失灵-自救.md`：安全模式 + 两条 `reg add` 的完整步骤 ——
修复按钮的前提是"还能点"，而真正变砖的用户点不了，这个外部通道必须有。

**复查结论**：1.9.1 → 当前版本的升级不会主动改任何注册表（启动自检全只读），
不会重装驱动（幂等闸 + 入口可见性双重拦截），唯一的自动写注册表路径是
"安装后被覆盖的原有项恢复"，且只在原有项丢失时触发。
