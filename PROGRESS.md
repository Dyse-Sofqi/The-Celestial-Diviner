# 衍天高手（The Celestial Diviner）开发进度交接

更新：2026-09-07 — **DD 驱动自动获取（无感获取官方驱动）+ HIDDriver 调研结论**

## 本次变更（DD 驱动自动获取）
- 📋 用户需求：引入开源驱动替代 DD 免手动下载。调研结论：dengqizhou30/HIDDriver
  （Apache-2.0）驱动为**测试证书签名**，需 bcdedit testsigning + nointegritychecks +
  重启测试模式 + devcon 手动安装 + 自编译（仓库无预编译产物），对终端用户不可用；
  kmclass（AGPL-3.0）同样要求测试模式；Interception（LGPL）驱动已签名但安装需改
  键盘类过滤驱动 + 重启，HVCI 兼容性存疑。用户确认放弃开源驱动方案，
  转向"DD 驱动自动获取"
- ✅ DdDriverService.StartAutoFetch 后台单飞自动获取：DD 官方发布渠道
  （github.com/ddxoft = 作者自己的发布页，程序只做下载器、不二次分发闭源驱动）
  → GitHub API 查最新 Release 7z 资产（限流/失败退回内置直链）→ 下载（3 分钟超时）
  → 随包 7zr.exe（7-Zip 独立版，LGPL，仅解 .7z，602KB）解包 → 优先取官方包
  1.simple 变体 dd63330.dll → 安装到 %APPDATA%\TheCelestialDiviner\drivers
  （既有探测位置，用户目录可写无需管理员）；临时目录 finally 清理；全程 Logger 留痕
- ✅ VM 接入（冷启动 / 热切换两条路径）：DD 模式缺驱动时先回退普通模式（不影响使用）
  + 后台自动获取；完成回调 AutoFetchCompleted 封送 UI 线程重试 EnsureReady 成功后
  经 KeyboardMode 属性自动升回 DD 模式（同步模拟器 + 落盘 + 日志）；
  用户中途手动切换模式即取消挂起（_ddAutoActivate 标志）
- ✅ 细节：自动获取跳过条件只认可加载的 x64 版（exe / APPDATA 的 dd63330.dll），
  Change Box 的 DD64.dll（32 位时间锁版，x64 必加载失败）不算"已有驱动"——
  本机实测命中过该误判；首次 DD 初始化成功后把 DD 自释放到 %TEMP% 的 dd63330.sys
  转存 APPDATA（KeepKernelDriverSys），后续启动预置内核服务不受 %TEMP% 清理影响；
  ResolveDriverSysPath 增加 APPDATA 候选
- ✅ csproj：7zr.exe（drivers/7zr.exe，入库，LGPL 允许再分发）随构建 / 发布包分发
- ✅ 端到端实测（独立 harness 引用主程序集）：下载 3657KB → 解包 → 安装成功，
  安装的 dd63330.dll SHA-256 与官方包内文件逐字节一致；临时目录清理确认
- ✅ README / drivers\README 同步（DD 自动获取为主、手动下载为兜底；7-Zip LGPL 署名）；
  构建 0 警告 0 错误
- ✅ 稳定性终案（用户确认"镜像到 Release 与直接内置无异"）：发布 zip **重新附带**
  dd63330.dll / dd63330.sys（用户开箱即用，无需任何下载）；源码仓库仍不入库
  （drivers/ gitignore 不变，二次开发按官网自取）；自动获取保留为兜底
  （源码构建 / 驱动被杀软误删场景）；README DD 节改为"随包附带 + 自动获取兜底"
- ✅ 版本 1.7.2（csproj Version/FileVersion 1.7.2.0；窗口标题仅主次版本仍为 v1.7）
- ✅ 版权合规复盘（用户提出 DeepSeek 意见）：核实 ddxoft/master 仓库**无任何许可文件**，
  默认保留所有权利 → 发布产物携带闭源二进制构成未经授权复制/分发，DeepSeek 判断成立；
  **恢复引导式获取**：发布产物移除 dd63330.dll/.sys（12 文件），静默自动获取改为
  **用户确认后**的引导流程（DdDriverFetchConfirmRequested 事件 + View Yes/No 确认框，
  下载由用户发起、程序仅做官方渠道下载器，法律地位更干净）；冷启动路径在
  InitializeRuntime（窗口可见后）弹确认，热切换路径即时弹确认；拒绝保持普通模式并
  日志提示手动途径；v1.7.2 双端附件同版本替换为不含驱动的包并同步说明
- ✅ 引导强调核心依赖 + 失败重试（用户需求）：确认框文案告知"本软件的键盘连发功能
  完全依赖 DD 驱动运作"以传达获取重要性，并承诺"下载失败自动重试直至成功"；
  RunAutoFetch 重构为重试循环——线性退避 5s 起步上限 60s 直至成功，
  NoRetryException 标记重试无意义的硬性失败（缺 7zr.exe / 上游包内容变化）直接终止，
  网络类失败（解析 / 下载 / 解包损坏）均重试，重试前清理解包残留目录；
  热切换时若获取已在途（IsAutoFetchRunning）不再重复弹确认，改提示在途与自动启用；
  README 注意事项 / DD 节同步"完全依赖 + 重试直至成功"表述
- ✅ 引导文案整合开发者难处（用户需求）：确认框与 README 说明"DD 驱动虽由作者
  免费提供，但它是闭源软件，我们无权将其打包分发，所以这一步需要由您来完成"，
  向用户解释为何需要下载而非直接内置
- ✅ README 强调使用范围声明（用户需求）：开头新增 ⚠️ 声明块——本软件仅供剑网三
  PVE 环境下的按键辅助使用，请勿用于 PVP 等违规环境，违规后果自负；
  注意事项列表将"仅限剑网三 PVE 环境"列为首条

## 上次变更（README 用户向重写 + DD 驱动出库（不入库 / 不入安装包））
- ✅ README 全面重写为**用户向**：下载运行、注意事项（管理员权限 / 杀软误报 / DD 驱动
  自行下载 / 游戏过滤 / 配置不丢失 / 联网点 / 托盘驻留）、功能一览、使用说明与触发方式
  速查表、更新方法、二次开发；移除开发者向的项目结构 / 构建细节；标题去掉版本号
- ✅ DD 驱动出库（闭源第三方组件，不再随仓库 / 安装包分发）：
  - git rm --cached drivers/dd63330.dll/.sys + .gitignore 忽略（本地保留供开发构建）
  - drivers/README.md 改为"从 ddxoft 官网 http://www.ddxoft.com/ 获取，放入 drivers/ 目录"
    的开发者指引（保留 sys 预置服务用途说明）
  - UpdateService 发版要求注释同步：安装包不含 DD 驱动，覆盖安装不删除已放置的驱动
  - v1.7.1 双端 Release 附件重打包替换（去除 dd63330.*，其余 11 个文件与布局不变），
    GitHub Release 说明与 Gitee Release 正文同步补充"不含 DD 驱动"提示
- ✅ 缺失驱动时程序行为不变：DD 模式自动回退普通注入并日志提示；已装用户升级后
  驱动文件保留（xcopy 覆盖不删除）

## 上次变更（切换方案热键语音 + 目标方案代号键帽）
- ✅ 切换方案热键触发并实际切换档位时播放“切换”提示语音：
  新增内嵌资源 Resources/Sounds/CycleVoice.mp3（源件 PR/切换.mp3，csproj
  Sounds\*.mp3 通配自动覆盖）；SoundCueService 新增独立 _cyclePlayer + PlayCycle()
  （独立播放器可与总开关语音叠加，MediaFailed 留痕）；UI 手动点档位不播（仅热键路径）
- ✅ 键帽可视化区域同步显示目标方案代号键帽（①②③④）：
  KeyVisualizerService.ShowCycleKeycap 走连发脉冲通道渲染（HoldPulse 80ms 一次
  完整按下弹起），重复触发复用同帽递增连击角标，停止触发 1.2s 后随活动巡检
  独立淡出移除（触发后键帽自然消失原则）
- ✅ 与状态提醒键帽同语义独立运行：无视键位可视化总开关（_globalEnabled）与
  模式过滤、不做物理按键快照登记——可视化总开关关闭时依然显示
- ✅ 其余档位均为空未切换时不播语音不显键帽（无目标代号可显示）；
  构建 0 警告 0 错误；反射核对 cyclevoice.mp3 已入 .g.resources
- ✅ 版本 1.7.1（csproj Version/FileVersion 1.7.1.0；窗口标题仅主次版本仍为 v1.7）
- ✅ 发版压缩包改为同名目录包裹（dist 与 Gitee Release 附件同步替换）：解压文件不再散落；
  自更新解包下钻逻辑天然兼容（payload 根级无 exe 且唯一子目录含 exe 时自动进入该目录）；
  旧版根级散文件 zip 布局同样兼容

## 上次变更（二级弹层间隙致慢速无法选入）
- ❌ 现象：左下角「选项」菜单的二级级联子菜单，只有极快划入才能选中，
  鼠标稍慢、划过分组项右侧时子菜单立即消失
- 🔍 根因（WPF 源码 + 最小复现实验双重定位）：二级弹层与分组项右侧有 2px 间隙，
  鼠标离开分组项、进入间隙期间仍悬停在本应用 Menu 上（未离开菜单捕获树），
  MenuItem.MouseLeaveInMenuMode → IsMouseOverSibling = true → 启动 MenuShowDelay
  关闭计时器；本机系统 MenuShowDelay = 0 → 反选、弹层立即关闭。快速划过时
  Leave→Enter 同帧，弹层 MouseEnter 会停掉关闭计时器方可幸免 —— 即“只有极快
  才能选中”。最小复现实验：复刻同款自绘菜单（Custom 定位 + 2px 间隙 +
  MenuShowDelay=0），程序化 1px/10ms 慢速右移，弹层在鼠标到达前约 1.5s 即关闭；
  间隙改 0 后同速穿越全程保持（对照验证）
- ✅ 修复：MenuPlacements.RightOfTarget 二级弹层定位 Gap 2 → 0（无缝贴齐分组项
  右侧）——鼠标离开分组项瞬间已命中弹层 HWND，IsMouseOverSibling 恒 false，
  关闭计时器不再启动；一级弹层（向上弹出，纵向路径鼠标离开按钮后立即离开
  Menu 区域，无此问题）保留 2px 呼吸间隙不变
- ✅ MainWindow.xaml 二级弹层注释同步；构建 0 警告 0 错误

## 上次变更（档位组上移 + 圆形勾选框）
- ✅ 方案档位标签组（方案①②③④）自面板底部移至最顶部：DockPanel 首个子元素 + Dock=Top，
  现自上而下 = 方案档位①②③④ → 连发时序档位（常规/极限） → 模式分段条 → 选择态提示 →
  方案列表 → 分区标签组（常规/轮转/双宏） → 「从左侧键盘管理方案」按钮；
  档位 Border 边距 0,6,0,0 → 0,0,0,8（顶部无需上边距）；分区标签组上边距移除、
  下边距 6 保留（直接贴列表），管理按钮注释同步更新
- ✅ 方案列表勾选框美化：App.xaml 新增 RoundCheckStyle 圆形勾选框模板（15×15），
  表头全选框 + 行启用勾选框 + 双宏首键勾选框三处共用（替换系统默认方框）：
  - 底色经新主题资源 ThemeRoundCheckFill 随主题切换（App.ApplyTheme 注入：
    日间 = 主题紫 #836899、夜间 = 主题金 #cea23a，色值唯一落点 Constants.cs）
  - 勾选符号恒白色圆头对勾；半选态（IsChecked=null，表头部分行启用）白色短横线
  - 悬停圆框描边提亮反馈；禁用整体 40% 透明；沿用 Focusable=False（不参与键盘焦点）
- ✅ 构建：0 警告 0 错误

## 上次变更（分区标签组移至管理按钮上方 + 方案列表表头底边框）
- ✅ 开关分区子标签组（常规/轮转/双宏，深灰选中 SectionTabStyle）自顶部堆栈（模式分段条与
  选择态提示条之间）移至方案面板底部堆栈：DockPanel 内声明在管理按钮之后 → 置于
  「从左侧键盘管理方案」按钮上方；底部堆栈自下而上 = 档位①②③④ → 管理按钮 → 分区标签组，
  间距统一 6px（管理按钮上边距 6 移除、分区标签组上下边距 6）
- ✅ 方案列表表头加底边框：表头 Grid 外包 Border（ThemeBorder 底边 1px，列表内容分隔线），
  表头文字与线之间留 3px，线与列表之间 2px
- ✅ 纯 XAML 布局调整，VM 零改动
- ✅ 构建通过（0 警告 0 错误）

## 上次变更（状态提醒按钮金色/常规色切换修复（缺 PropertyChanged））
- ❌ 现象：状态提醒按钮图标一直保持金色，关闭后不回落到常规按钮色
- 🔍 根因：MainViewModel.StatusReminderEnabled setter 未发 PropertyChanged —— 底栏图标的
  DataTrigger 绑定（StatusReminderEnabled → 金色/常规色）只在启动时读取一次初值
  （默认开启 → 金），此后点击切换 VM 状态变了但绑定不知情，触发器永不重评估
- ✅ 修复：setter 末尾补 OnPropertyChanged(nameof(StatusReminderEnabled))
  （导入配置路径此前已单独发通知，不受影响）
- ✅ 构建通过（0 警告 0 错误）

## 上次变更（导入/导出配置移至底栏 + 管理按钮移回方案面板底部）
- ✅ 导入配置 / 导出配置按钮自方案面板底部移至底栏，紧跟「选项」菜单键之后
  （[☰选项] [导入配置] [导出配置] [调整可视化位置]，IconButtonStyle 26px 高、
  8px 间距与底栏节奏一致）；作用于当前方案档位，命令绑定不变
- ✅ 「从左侧键盘管理方案」按钮自分区标签组上方移回方案面板底部——原导入/导出的位置
  （档位①②③④上方、ListBox 之下，Dock=Bottom + 上边距 6）；开关模式/按压模式切换区域
  恢复紧凑（模式分段条 → 分区子标签直接相接）
- ✅ 纯布局调整：ImportCommand / ExportCommand / StartPickingCommand 绑定与 VM 零改动
- ✅ 构建通过（0 警告 0 错误）

## 上次变更（键帽鼠标标签/图标 + 管理按钮移位 + 分区标签配色）
- ✅ 键帽可视化鼠标标签改版（仅悬浮层键帽；主界面图块与方案列表名称不变）：
  - 滚轮上/下 → 「↑滚」「↓滚」，侧键1/2 → 「X1」「X2」（纯文本，KeyVisualizerService
    CanonicalLabel + KeycapTargetName 双处同步——脉冲键帽与物理键帽标签必须同源）
  - 左键/中键/右键 → lucide 图标键帽：mouse-left / mouse-right / mouse（ISC 许可；
    mouse-left/right 为 0.573+ 新增，lucide-static@0.573.0 包未含、取自主分支）。
    键帽标识标签仍为「左键/中键/右键」（保证“同键触发连发”时脉冲与物理按键复用同帽），
    键帽面按标签查表（CapIcons）渲染 lucide 线稿描边（描边 = 配色文字色，线宽 2 随
    Stretch 等比缩放），修饰键组合目标（Ctrl+左键）仍为文本键帽
- ✅ 「从左侧键盘管理方案」按钮移至开关分区子标签组（常规/轮转/双宏）上方
- ✅ 分区子标签选中色紫 → 深灰：新增 SectionTabStyle（BasedOn ModeTabStyle 重写模板），
    选中 = ThemeButtonBg 底（同底栏按钮底色，不引入界面外新颜色）+ 主题色文字 + SemiBold，
    悬停保持 TabIdleBg；日/夜主题层次各自成阶梯：背景 < 悬停 < 选中（浅 #FAFAFA/#F0F0F0/#E8E8E8，
    深 #202020/#2B2B2B/#333333）。仅分区标签组换用，时序/档位/模式分段不受影响
- ✅ 构建通过（0 警告 0 错误）

## 上次变更（弹层定位修复（MenuDropAlignment）+ Tooltip 改注释区展示）
- ❌ 现象（截图取证）：主菜单弹层右对齐到「选项」按钮而非左对齐；二级弹层（键盘注入选项）
  翻到主菜单左侧展开
- 🔍 根因：系统“菜单右对齐”辅助选项 SystemParameters.MenuDropAlignment 被置位
  （部分中文软件会改此系统设置），WPF Popup 的 Top 摆位被镜像为右对齐、Right 摆位被
  翻转到左侧 —— 与此前“菜单向上/向右展开”的实机表现完全吻合
- ✅ 修复：弹层全部改用 Placement=Custom 显式坐标定位（Custom 不参与 MenuDropAlignment
  镜像），新增 Helpers/MenuPlacements.cs 静态回调：
  - AboveTarget：一级弹层左下角对齐按钮左上角（向上展开、左对齐，2px 间隙）
  - RightOfTarget：二级弹层左上角对齐分组项右上角（向右展开，2px 间隙）
  - XAML 经 x:Static 挂接（conv:MenuPlacements.*），任何系统设置下定位确定
- ✅ 菜单悬浮 Tooltip 改注释区展示：移除三个分组项的 ToolTip，悬停分组项 / 叶子项 /
  配色子项时在注释区（CommentText）展示所属分组说明（与分区标签悬停注释共用同一展示位
  与恢复逻辑 OnSectionTabMouseLeave）；分组项 XAML 接线，键盘注入/键帽可视化叶子项
  OnLoaded 循环接线，键帽配色子项经 ItemContainerStyle EventSetter；说明文案收录
  MenuComments 字典（键 = 分组标题，叶子项经 Parent 向上取）
- ✅ 构建通过（0 警告 0 错误）

## 上次变更（菜单对齐 / 悬停选中同款效果 / 底栏按钮统一间隙）
- ✅ 选项文本与菜单键按钮文本左对齐：二级菜单项左内边距 10 → 14（14 + ● 标记列 14 =
  选项文本起点 28px = 按钮文本起点 9 + menu 图标 13 + 间距 6）；一级弹层左缘与按钮左缘对齐
  （注：当时 Top 摆位实机受 MenuDropAlignment 镜像未生效，已在下一轮以 Custom 定位修复）
- ✅ 悬停效果升级为选中同款：悬停菜单选项 = 紫底白字（AccentPrimary），与选中态视觉一致；
  IsHighlighted（覆盖菜单展开路径高亮）与 IsMouseOver（纯命中测试，保证悬停必有反馈）
  双触发器同款视觉；选中（当前生效项）= 同款紫底白字 + 常驻白色 ● 区分
- ✅ 菜单键与「调整可视化位置」按钮之间补 8px 间隙（Menu 右边距，与底栏其余控件间距节奏一致）
- ✅ 子菜单向右展开（注：当时 Right 摆位实机受 MenuDropAlignment 镜像翻到左侧，
  已在下一轮以 Custom 定位修复）
- ✅ 构建通过（0 警告 0 错误）；注：上一轮编辑曾把触发器区块引号写成中文弯引号导致 XAML
  解析失败（MC3000），已修正

## 上次变更（菜单键按钮化 + 热键钮日间配色）
- ✅ 设置菜单键按钮化：顶层 MenuTopStyle 自绘模板改为按钮观感——lucide menu 图标（三条横线，
  v0.544，MenuIconPath）+ 文本「选项」（替换原「☰ 菜单」），配色对齐 IconButtonStyle
  （ThemeButtonBg 底 / ThemeBorder 边框 / 圆角 4 / ThemeFg 图标文字）
- ✅ 弹出方向：菜单位于窗口左下角 → 顶层子菜单 Placement=Top 向上弹出（弹层间隙移到底部）；
  二级子菜单保持 Placement=Right 向右展开
- ✅ 交互反馈：悬停 = TabIdleBg 灰底（原有）；新增选中态 = 当前生效项紫底白字 + 白色 ●
  （AccentPrimary，与全应用“选中 = 主题紫”语义一致）；触发器顺序置于悬停之后，
  当前项悬停时保持紫色不被灰底覆盖；菜单键展开（按下）态 = ThemeBorder，与 IconButtonStyle 按压一致
- ✅ 全局热键设置按钮（底栏）日间配色：原日间为近黑底（HotkeyBg #1F1F1F）白字，改为主题化
  HotkeySettingBg / HotkeySettingBorder / HotkeySettingFg —— 夜间沿用近黑底白字（#303030/#333333/White），
  日间复用普通按钮配色（= ThemeButtonBg / ThemeBorder / ThemeFg）；键鼠区总开关键图块的
  HotkeyBg 黑色高亮不受影响
- ✅ 构建通过（0 警告 0 错误）

## 上次变更（按钮去键盘焦点 + 底栏设置菜单）
- ✅ 全窗口按钮/单选钮/复选框不再参与键盘焦点（Focusable=False）：点击按钮后按 Space
  不再重复触发、方向键不再游走选中其他按钮（纯鼠标操作策略）：
  - IconButtonStyle（App.xaml 全局，覆盖底栏图标钮 / 方案行 eye·✕ / 导入导出 / 总开关键 /
    管理钮 / 调整模式确定钮等全部 Button）
  - ModeTabStyle / ModeSegmentStyle（模式·分区·时序·档位单选钮，ProfileTabStyle 继承）
  - 方案列表表头/行内三个 CheckBox + 方案列表 ListBox
  - TextBox（时序编辑）保留键盘输入；音量 Slider 未动
- ✅ 底栏左下角新增「☰ 菜单」设置菜单键（WPF Menu 级联）：键盘注入（普通/扫描码/消息/DD 驱动）、
  键帽配色（KeycapSchemes 全部预设动态生成）、键帽可视化（全部/修饰键和自定义键/自定义键，
  原「可视化」改名）；三个下拉框自底栏移除
- ✅ 菜单模板全部自绘主题化：系统默认 MenuItem 夜间模式为白底黑字不可用 →
  顶层 MenuTopStyle（点击开合，子菜单向下弹出）+ 二级 MenuSubStyle（叶子 / 级联组共用，
  Role 触发器画 ▸ 箭头，Popup 向右弹出自动避屏）；弹出层 ThemePanel/ThemeBorder；
  选中标记为自绘 ●（AccentPrimary，IsCheckable=False + 代码同步 IsChecked 实现单选，
  规避系统勾选符号硬编码配色）；菜单项 Focusable=False（悬停展开不受影响）
- ✅ VM 新增 SetKeyboardModeCommand / SetKeycapSchemeCommand / SetVisualizerModeCommand
  （参数沿用原下拉语义：索引 / 方案名）；KeycapSchemeName / VisualizerModeIndex setter
  补 OnPropertyChanged（此前不通知，菜单 ● 标记需要跟随刷新）
- ✅ MainWindow：移除 KeyboardModeBox 接线；SyncSettingsMenu 按 VM 状态回填 ● 标记
  （静态项直接遍历，键帽配色生成项经 ItemContainerGenerator 取容器），
  VM PropertyChanged（KeyboardMode / KeycapSchemeName / VisualizerModeIndex）+
  菜单展开 SubmenuOpened（覆盖按需生成的容器首展开时机）双路驱动同步
- ✅ 构建通过（0 警告 0 错误）

## 上次变更（调整区域可拖动 + 键帽恢复单行）
- ✅ 调整模式整个区域可点击拖动修复：WPF AllowsTransparency 窗口为分层窗口，OS 按
  Alpha 通道命中测试——纯透明（alpha=0）区域鼠标消息穿透到下层窗口，WPF
  MouseLeftButtonDown 收不到（此前只能拖住键帽本体 / 提示条，空白区拖不动）。
  修复：窗口 Background 垫 alpha=1 近透明画刷（#01000000，视觉不可见），整个 940×180
  区域均可按下 DragMove 拖拽，且不穿透到下层窗口
- ✅ 键帽布局恢复单行（用户偏好，撤销上一轮 WrapPanel 双行换行）：根容器 WrapPanel →
  StackPanel，键帽 Margin 底边距 8 → 0；调整模式右下 StackPanel 底边距保持 0，
  调整键帽与真实键帽同为窗口底边贴齐（顺带修正了原始实现 4px 的对齐偏差，逐像素对齐）
- ✅ 构建通过（0 警告 0 错误）

## 上次变更（可视化区域：防拖出屏幕 + 磁吸 + 禁最大化 + 双行换行）
- ✅ 新增 SnapIntoWorkArea(win, magnet) 静态辅助（KeycapOverlayWindow）：
  WinForms Screen.FromHandle 取所在屏幕工作区（物理像素）→ 按窗口当前 DPI（PerMonitorV2
  TransformToDevice）换算 DIP；magnet > 0 时四边独立磁吸（距边 ≤24px 拖拽结束贴齐），
  随后整窗（含宽高）强制钳回工作区（Compat.Clamp，防反向钳位取 Max 保护）；
  无 PresentationSource（未显示）时跳过，显示后首次定位再校正
- ✅ 调整模式窗口：拖拽结束（DragMove 返回后）磁吸 + 钳位 → 区域不可拖出屏幕
  （此前拖到屏幕边缘外时虚线框被屏幕边缘裁切，即"右下角显示残缺"的根因）；
  打开时也钳位一次（陈旧保存位置 / 显示器热插拔防御）
- ✅ 调整模式窗口 ResizeMode = NoResize：区域仅临时显示，禁用调整大小 / 最大化
  （含 Aero Snap / Win+↑），防窗口尺寸被改后虚线框 ≠ 真实区域 940×180 破坏 1:1 对齐
- ✅ 主悬浮层 PositionToSaved 也接入钳位（magnet = 0 静默）：保存位置越界时钳回工作区
- ✅ 键帽截断修复：悬浮层根容器 StackPanel → WrapPanel（窗口 940×180 本就为两行键帽预留，
  单行 ≈ 12 帽，超出一行自动换行到第二行而非截断）；键帽 Margin 加底边距 8 = 第二行行距
  （单行时键帽整体上移 8px，无功能影响）；调整模式右下 StackPanel 底边距 4 → 0，
  补齐原先 4px 的对齐偏差（现调整键帽与真实键帽同为窗口底边上方 8px，逐像素对齐）
- ✅ 虚线框内缩 1px → 2px（高分屏窗口边缘取整裁切防御）
- ✅ 构建通过（0 警告 0 错误）

## 上次变更（调整可视化位置模式）
- ✅ 调整模式虚拟键帽内容从"A"改为**应用图标**（复用提醒键帽的 LoadAppIcon，所见即所得）
- ✅ 确定按钮套用应用全局 IconButtonStyle（4px 圆角 + 主题配色 + 悬停反馈），
  局部 Padding 覆盖样式内边距不变
- ✅ 新增**可视化区域虚线边框**（Rectangle，AccentPrimary 主题色 SetResourceReference 随主题
  同步，1px 圆角虚线 4-3，内缩 1px 防描边被窗口边缘裁切）：标出悬浮窗 940×180 完整范围，
  键帽实际渲染在其右下角锚点；Transparent 填充使整个范围均可 DragMove 拖拽；
  提示文案同步改为「虚线框 = 键帽显示区域 · 拖动调整位置 · 确定保存 · Esc 取消」
- ✅ 构建通过（0 警告 0 错误）

## 上次变更（状态提醒按钮：金色激活态 + 默认激活）
- ✅ 按钮图标激活态改为**金色墨迹**（AccentGold，与夜间模式钮同源）；未激活恢复默认文字色
  （ThemeFg 满透明度），去掉原先的 0.4 暗淡 / 紫色高亮方案
- ✅ 修复上一版的触发器失效 bug：Stroke 原写在 Path 本地属性上，WPF 依赖属性优先级中
  本地值 > Style 触发器 → 激活态的颜色 Setter 从未生效（仅透明度变化）；现 Data / Stroke
  全部移入 Style Setter（与 eye / volume 图标"本地值会压过 DataTrigger"注释同源教训）
- ✅ 默认激活：AppConfig.StatusReminderEnabled 默认值 false → true；配置版本 v7 → v8，
  迁移把存量配置（含开发期落盘的关闭态）统一置为开启——迁移只执行一次（落盘 v8 后跳过），
  用户此后手动关闭不会被重置
- ✅ 构建通过（0 警告 0 错误）

## 上次变更（状态提醒键帽）
- ✅ 底部栏右侧新增“状态提醒”图标按钮（lucide message-square-dot，26×26 与夜间/音量钮同规格；
  激活 = 主题紫高亮，未激活 = 文字色 0.4 暗淡），开关随配置持久化（AppConfig v7 新增
  StatusReminderEnabled，缺省 false，Clone / 导入配置同步）
- ✅ 功能语义：按钮开启后，**应用总开关**（非键位可视化总开关）开启时，键帽悬浮区常驻一个
  键帽，内容为应用图标（Resources\app.ico 经 IconBitmapDecoder 取最大帧，加载失败退化为
  “启”字）；任一方案连发触发 → 键帽立即消失；全部连发停止 2.5s → 键帽恢复显示
  （即总开关正在开启的屏幕指示灯，连发时让位给真实连发键帽）
- ✅ TaskSchedulerService 新增 FiringChanged 事件：`_tasks.Any(t => t.Active)` 聚合态翻转时触发
  （去重，锁内调用）；HandleSourceDown / HandleSourceUp / SetMasterEnabled / ApplyConfig /
  StopAll 五个状态变更点末尾触发——任一任务 Active 即视为连发中（Toggle 被 Hold 暂停时
  Hold 必在连发，语义一致），因此连发开始于第一次注入前、恢复计时精确起于全部停止
- ✅ KeyVisualizerService 新增状态提醒状态机（SetMasterSwitch / SetReminderEnabled 入口，
  全部 UI 线程）：desired = 按钮开 && 总开关开 && 无连发 && 恢复倒计时未挂起；
  停发时 DispatcherTimer 2.5s 倒计时，期间再次连发则 Stop/Start 重置；悬浮层未创建前
  只记状态，AttachOverlay 末尾补应用（修复 VM 构造早于窗口创建的时序）
- ✅ KeycapOverlayWindow 新增 SetReminderVisible + 常驻 KeycapControl（isReminder: true）：
  - 不参与快照协调（UpdateKeys 两个分支跳过）、不参与空闲淡出（RetireIdleCaps 跳过），
    空快照 / 可视化总开关关闭均不清除它
  - 恒居键帽序列末位（普通键帽经 AddCap 插到它之前）：窗口右对齐 → 提醒键帽位置稳定不跳动
  - BeginAdjust 清场时一并移除，EndAdjust 后按期望态恢复（调整模式中状态变更只记 _reminderDesired）
- ✅ 导入配置后 StatusReminderEnabled 随导入还原（services 开关 + OnPropertyChanged 刷新按钮图标）
- ✅ 构建通过（0 警告 0 错误）

## 上次变更（悬停注释文案）
- ✅ MainWindow.xaml.cs SectionComments 文案更新（仅注释区显示文本，无逻辑改动）：
  - 轮转开关：「支持两个或多个轮转开关模式键位之间一键切换。」
  - 双宏开关：「首键位一键启停两个开关模式键位的轮流连发。」
- ✅ 第二勾选框说明并入双宏悬停注释（非功能改动，行为已有实现）：
  「取消勾选第二个勾选框，则首键位一键启停第二个开关模式键位的连发，不再轮转。」
  （行为：取消勾选 → Targets[0].Enabled=false，调度器过滤后仅剩次键连发）
- ✅ 构建通过（0 警告 0 错误）；注意运行入口：最新产物在
  src\TheCelestialDiviner\bin\Debug\net48\，dist\try-color 为旧拷贝（曾导致改完看不到变化）

## 上次变更（双宏页布局死循环修复）
- ❌ 现象：切到双宏开关分区后整机严重卡顿（UI 线程 100% 空转，截图/操作数秒才响应），
  方案列表每行只剩行首勾选框，键名 / 时序框 / eye / ✕ 全部不可见
- 🔍 根因（minidump 线程栈取证）：第二勾选框当时放在第二行的一个嵌套 Grid 里，且该嵌套
  Grid 既没有 Grid.Column（整体落进外层第 0 列）、首列又带 `SharedSizeGroup="SchemeColCheck"`
  —— 嵌套共享 + 星号列 + IsDual 行可见性切换三者叠加，触发 WPF 共享列宽（SharedSizeScope）
  无限布局循环：UI 线程在 PresentationCore 反复重排版（转储栈顶为 PresentationCore.ni 深层递归）；
  同时嵌套 Grid 的内容宽度（勾选框 + ⇄辅键名 ≈ 68px）被并进外层第 0 列的 Auto 宽度并经共享
  组放大，键名列（`*`）被挤瘦到裁掉 F11 尾字符，最坏时整行内容被推出面板外只剩勾选框
- ✅ 修复（仅 MainWindow.xaml 方案行模板）：删除嵌套 Grid，第二勾选框与 ⇄辅键名直接作为
  外层 Grid 第 1 行的单元格（列 0 = 勾选框列、列 1 = 键名列，天然与首行对齐，无需共享列宽），
  两者均绑定 IsDual → BoolToVis 控制显隐；方案行模板不再有任何嵌套共享尺寸参与者
- ✅ 实机回归：双宏页两行方案完整渲染（- + ⇄ = / F11 + ⇄ F12），UI 线程 CPU 由打满一核
  （2000ms/2s）降至 16ms/2s；常规 / 轮转 / 双宏三页切换均正常；第二勾选框取消 → 落盘
  Targets[0].Enabled=false、方案 Enabled 保持 true，复选后恢复（实测通过）

## 上次变更（双宏首键连发开关）
- ✅ 双宏方案列表勾选列新增第二个勾选框（辅键行首列，与首行启用勾选同列对齐）：
  - 勾选（默认）＝原行为：首键与次键 1-2-1-2 交替连发，首键启停
  - 取消＝首键仅作启停触发键：按首键启动 / 再按停止，次键单独连发不交替
    （等效于用首键位启停次键位的连发方案）
- ✅ 实现路径（模型零改动）：复用 TargetKeyConfig.Enabled（已随配置持久化），
  调度器 ApplyConfig 本就过滤未启用目标 —— 双宏任务仅剩次键时自动退化为
  单目标连发（phase 恒 0），首键触发源语义不变；首行启用勾选仍为方案级整体启停
- ✅ SchemeRowViewModel 新增 FirstKeyFiring 属性 + onFirstKeyFiringChanged 回调；
  MainViewModel 新增 SetDualFirstKeyFiring（写首目标键 Enabled → 重建任务 → 落盘 + 日志）
- ✅ MigrateLegacySchemes 不再强制双宏目标键 Enabled=true（否则重启覆盖用户取消的勾选）
- ✅ 新录入双宏方案两目标默认 Enabled=true，与勾选框默认态一致；构建通过（0 警告 0 错误）

## 上次变更（日/夜图标区分 + 键帽逐键帽独立淡出）
- ✅ 底栏主题按钮图标随模式切换：白天 = lucide sun（中心圆 + 八向光线，金色描边），
  夜间 = lucide eclipse（外圈大圆 + 月牙弧线）；DataTrigger 绑定 VM.NightMode，
  切换时 OnPropertyChanged(NightMode) 驱动图标刷新
- ✅ 键帽可视化“停止的键位不随时间消失”修复（根因：窗口级淡出计时器被发射中的
  脉冲键不断重置 —— HoldPulse 每次发射 Stop+Start，开关模式连发不停计时器永不触发，
  同屏已停止的键位只能陪跑到发射键也停止）：
  - 移除窗口级 _dismissTimer/BeginDismiss，改为 500ms 活动巡检（_activityTimer）
    + 逐键帽独立宽限/淡出：各键帽距最后活动（按压刷新 / 首次释放 MarkReleaseOnce 锚定 /
    最后一次脉冲 PulsePress 顺延）超过 2.5s 后各自 400ms 淡出移除；全部移除后隐藏窗口
  - IsRetirable 仅看最后活动时刻，不依赖释放标记（轮转开关自动停旧键无后续快照也能退出）；
    物理按住中 / 发射中（600ms 脉冲窗口）永不淘汰
  - 宽限内重按 / 重新发射（CancelRetire）取消淡出继续复用同名键帽，连击计数保留；
    已释放键帽不再立即从视觉树移除，改为抬起后进入宽限（快照中消失 → 宽限而非删除）
  - 效果：一个开关模式键位连发中，其它已停止键位 2.5s 后独立消失，互不影响
- ✅ 构建通过（0 警告 0 错误）

## 上次变更（角标修复 + 夜间模式）
- ✅ 键帽连击角标显示不全修复：角标向右上外凸 1/4 边长（6px）超出悬浮窗右缘被裁，
  Root 预留 0.25×size 右侧留白（Margin 方式，StackPanel 无 Padding）
- ✅ 连击角标计数上限 99：超过后停在 99 不再累加；角标 MinWidth 正圆 + Padding 2，
  两位数时宽度自适应呈横向药丸，数字不溢出圆边
- ✅ 底栏右下角新增夜间模式按钮（音量按钮左侧，26×26 与音量钮同规格）：
  图标 lucide eclipse（外圈大圆 + 月牙弧线，金色描边与主题金一致）；点击切换白天/夜间
- ✅ 主题切换链路：
  - AppConfig 新增 NightMode（缺省 false）与 ThemeFollowSystem（缺省 true 跟随系统）；
    Clone 深拷贝同步；手动切换一次后 ThemeFollowSystem = false（固定不再跟随系统）
  - MainViewModel.NightMode 属性：切换 → ApplyTheme + 落盘 + 日志；
    VM 构造时按配置应用主题（App.OnStartup 原无条件跟随系统的调用移除，避免覆盖）；
    导入配置后随配置还原主题
  - App.ApplyTheme 复用同一 ResourceDictionary 实例覆写键值（反复切换不再堆积字典）；
    同步 KeycapOverlayWindow.SetThemeDark（调整模式提示条深浅配色）
  - 夜间模式适配：紫（#836899）/ 金（#cea23a）不动，背景 #202020、面板 #2A2A2A、
    边框 #3D3D3D、按钮 #333333、文字 #E6E6E6；键位图块空态 #2F2F2F / 空字 #AAAAAA；
    已创建图块实例刷子切换时全量 RefreshTheme；调整模式提示条深底白字
- ✅ 夜间模式补适配：模式分段条轨道容器硬编码 Background="White" → 主题资源
  SegmentTrackBg（白天白轨道不变 / 夜间 #383838，比面板亮一档，保留紫/金胶囊与轨道层次）
- ✅ 构建通过（0 警告 0 错误）

## 上次变更（键帽校准收尾 + 档位归位 + 鼠标键简称）
- ✅ 对照 keyviz 源码（src/components/keycaps/lowprofile.tsx / press-count.tsx、
  src/components/settings/keycap.tsx、src/stores/key_style.ts）校准键帽复刻：
  - 边框色：不再固定黑色。keyviz 点选配色预设时执行 setBorderStyle({ color: scheme.secondary })，
    即边框 = 方案 secondary 色（Pansy → #4527a0）；KeycapScheme 增加 Border 属性（缺省 = Base），
    KeycapOverlayWindow.BorderColor 改用 scheme.Border
  - 连击角标：复刻 keyviz PressCount —— 0.75×size（24px）正圆、圆角 50%，
    底色 = 配色文字色、数字 = 配色面色（Pansy：金底紫字），显示裸数字（去掉 "×" 前缀），
    锚右上角向外凸出 1/4 边长（Margin -0.25×size）；角标弹跳动画保留
- ✅ 面板“档位”语义归位（修正前次误移）：
  - 方案①②③④是方案标签，放回面板最底部原位（导入/导出配置之下，
    Bottom 声明在先贴最底边，档位 Border 保持在导入导出 Grid 之前）
  - “档位标签” = 连发时序档位（常规档 26ms / 极限档 11ms），移到面板最顶部（模式分段条之上）
  - 面板顺序：时序档位 → 模式分段条 → 分区子标签 → 添加 → 方案列表 → 导入/导出 → 最底方案档位
- ✅ 键帽鼠标键简称：悬浮键帽“鼠标左键/鼠标中键/鼠标右键” → “左键/中键/右键”
  （KeyVisualizerService.CanonicalLabel 物理按下路径改简称 + 新增 KeycapTargetName 连发脉冲
  路径同源简称，保证“同键触发连发”时脉冲与物理按键复用同一键帽；
  滚轮/侧键保持原名，修饰键前缀顺序 Ctrl→Shift→Alt 不变）
- ✅ 构建通过（0 警告 0 错误）

## 上次变更（按压+间隔精简 + 方案④）
- ✅ 表头"按压+频率" → **"按压+间隔"**（tooltip 同步去掉"频率"措辞）
- ✅ 行内时序显示 "xxms + xxms" → **"xx + xxms"**：删去按压框后的 "ms" 文本列
  （SchemeColMs1 列整个移除，"+"、"ms"/eye/✕ 列号前移，表头 ColumnSpan 5 → 4），
  行模板与表头 SharedSizeGroup 同步对齐；周期语义不变（按压 + 间隔，日志与 tooltip 不变）
- ✅ 时序数字框（按压 / 间隔 TextBox）左右内边距收窄：Padding="-2,0"
  （App.xaml 全局 TextBox 模板 PART_ContentHost Margin=Padding，负值抵消 TextBoxView
  自带 ~2px 内缩，数字与虚影水印居中对齐、可用宽度更大）
- ✅ 方案档位 3 → 4 套（新增方案④）：
  - ProfileCount 3 → 4（ActiveProfile 钳位 / SyncProfileRuntime 补空档位自动跟随）；
    ProfileLabel 增加 2 → ③、默认 → ④
  - AppConfig.Profiles 初始化器补第 4 套空档位；ConfigService v3→v4 迁移补齐循环 < 3 → < 4
    （SyncProfileRuntime 加载后仍会补齐，纯一致性）；迁移日志"方案三档位" → "方案四档位"
  - MainWindow.xaml 档位 Border 三列 → 四列 + Profile4Button（Tag=3，Checked 回写 VM）；
    code-behind ProfileButtons 数组加入 Profile4Button
  - 配置无 schema 变更（Profiles 为列表，旧配置加载后自动补第 4 套空档位），版本号维持 v6

## 上次状态（方案管理模式：持续添加 / 右键取消 / 成对退出，2026-09-05）
- ✅ 按钮语义改造：面板按钮 "＋ 添加连发键" → **"从左侧键盘管理方案"**（空闲态）；
  点击进入方案管理模式（原一次性录入态），按钮变为 **"退出管理方案"**（仍可点击）
- ✅ 管理模式持续保持，不再点一个键就退出：
  - **左键单击键位 = 添加方案**（键盘按键直接录入保留；双宏分区仍为两步成对录入）
  - **右键单击键位 = 取消该键位方案**（新 RemovePickAt；键位自身无方案但为某双宏辅键时同样整对取消）
  - **Esc / 点击键鼠区空白处（含图块外任意区域与其它窗口）/ 点"退出管理方案"按钮 = 请求退出**
- ✅ 双宏成对录入配对语义：
  - 成对完成（偶数步）后管理模式保持，可继续成对添加下一组（提示条回到"选第 1 键"）
  - **偶数态才能退出**：成对录入中（已选第 1 键未选第 2 键）请求退出 → 先放弃暂存的第 1 键
    回到偶数态（AbandonDualFirstPick），再次请求才真正退出
  - **取消一个 = 取消一对**：右键双宏方案任一键位（开关键位或辅键）整对取消；
    DeleteScheme 检测双宏方案并在日志列出两个键位（方案列表 ✕ 删除同样生效）
  - 成对录入中右键暂存的第 1 键 = 放弃该键重新选择
- ✅ 双触发防线（钩子只观察不吞事件，同一次物理点击 / 按键会同时到达钩子路径与 WPF）：
  - 图块 WPF 左键 MouseBinding 改为**仅钩子未安装时兜底**（HandleSourceClicked 判 !HookInstalled），
    否则双宏两步录入会被同一次点击触发两次（第 1 键暂存后立刻又被当第 2 键校验报"两键相同"）
  - StartPicking / StartMasterKeyPicking 增加 **150ms 退出冷却**（_lastPickingExitTick）：
    "退出管理方案"点击先由钩子空白路径退出，同一物理点击随后作为陈旧 WPF 点击落回
    已恢复可用的按钮，冷却防止刚退出就被重进（同时修复旧版热键录入按钮的同类隐患）
  - View 增加 Esc 兜底（PreviewKeyDown，仅 !HookInstalled 时触发）：钩子失效时也能退出管理模式
- ✅ 提示文案：默认底部提示 / 提示条 / 日志全部更新为管理语义
  （s_defaultHint、s_managePrompt、s_dualManagePrompt；进入 / 退出 / 放弃第 1 键 / 无方案可取消 均有日志）
- ✅ 总开关键录入保持一次性录入语义不变（录完即退出，不受管理模式改造影响）

## 上次状态（方案列表表头 + 可视化开关迁位 + 档位仅影响新方案 + 按压时长时序，2026-09-05）
- ✅ 方案列表表头（面板 DockPanel 挂 IsSharedSizeScope，列宽经 SharedSizeGroup 与行模板对齐）：
  - 列 1 全选框：一键启用/停用全部连发键（SchemesAllEnabled 三态——全启用 true /
    全停用 false / 混合 null 半选；行内单行勾选联动刷新；空列表点击视觉回弹）
  - 列 2"按键"、列 3"按压+频率"（跨时序编辑框五列，tooltip 解释 周期=按压+间隔）
  - 列 4 可视化总开关：底栏 eye 按钮**迁入表头**（ToggleGlobalVisualCommand 复用，
    eye/eye-off 随 GlobalVisualEnabled 切换）；**底栏原按钮已删除**（音量按钮保留）
  - 列 5 删除勾选项（✕ 图标复用行删除按钮）：删除所有勾选（启用）的连发键，
    经 DeleteCheckedRequested 事件由 View 弹 MessageBox 确认（破坏性操作）后执行
- ✅ 时序档位逻辑修正：切换常规/极限**仅写入面板默认值**（之后添加的连发键使用），
  不再修改已有方案（tooltip / 日志同步更正）；行内编辑框仍可单键调整
- ✅ 连发时序重构：周期 = **按压时长 HoldMs（按下→弹起）+ 间隔 IntervalMs（弹起→下次按下）**；
  原实现按压 0ms（down/up 背靠背），逐帧轮询输入的游戏在帧窗口 ≥ 按压时长时
  按下状态采不到（丢发）、间隔过短时两次按压合并为一次持续按住
- ✅ 默认按压 26ms + 间隔 26ms（周期 52ms ≈ 19 发/秒）：按 40fps 最差帧窗口 25ms 校准
  （按压/间隔均须 ≥ 帧窗口才能零丢失），全 40~100 帧区间稳定登记；
  上限换算：100 帧 50 发/秒 > 任何游戏动作冷却，实际收益不受损
- ✅ 双独立 ±20% 抖动（Constants.TimingJitterPercent）：TaskLoop 每轮对按压与间隔
  各自独立取随机值（26ms → 20~31ms），消除恒定周期的机器指纹；SchedulerService
  新增 JitterMs（静态 Random + 锁，.NET Framework 无 Random.Shared）
- ✅ 注入层：InputSimulatorService.Click 增加 holdMs 参数（PressKey / ClickButton 在
  down/up 之间 Sleep，精度依赖 timeBeginPeriod(1)）；滚轮目标无按压语义不受影响
- ✅ 钩子线程保护：滚轮脉冲路径（Hold 模式滚轮源）原在钩子线程同步 Click，
  现含 Sleep 不得阻塞 → 投递 ThreadPool 并以配置实例为锁串行化（down/up 严格成对）
- ✅ 模型：TargetKeyConfig 新增 HoldMs（默认 26，钳位 10~200，缺失/非法 → 默认）；
  AppConfig 新增 DefaultHoldMs 面板默认值 + TimingPreset（0 常规/1 极限）；
  CurrentVersion 5 → 6（字段有默认值，仅推进版本号）；
  ConfigService.NormalizeIntervals → NormalizeTiming（间隔 + 按压一并归一化）
- ✅ UI：方案行新增"按压时长"编辑框（周期构成显示为 `[按压]ms + [间隔]ms`），
  复用间隔框的虚影水印 / 点击清空 / 回车失焦提交 / 点击外部自动提交全流程
  （SchemeRowViewModel.HoldEdit/CommitHoldEdit；MainWindow OnHoldBox* 处理器，
  GotMouseCapture 清空处理器更名 OnTimingBoxGotMouseCapture 共用）
- ✅ 理论依据：逐帧轮询输入的登记率上限 = 1 / 2×帧窗口（采样序列每 2 帧至多一次
  弹起→按下跳变），周期压破 2×帧窗口只会降低登记率，不存在"丢换快"的交易；
  极限档 11/11（帧率 ≥90 上限 ~45 发/秒，55 帧以下丢发、40 帧拍频混叠骤降 ~5 发/秒）
- ✅ 实测基线保留：EchoGuard 按 (vk, 方向) + 12ms 时间窗匹配，与按压时长无关，无需改动

## 上次状态（滑块几何协调 + 全局方框圆角化 + 行按钮间距，2026-09-05）
- ✅ 滑块几何协调：钮高 26 → 20（内容 Padding 0,2），衬底外扩 4px（圆角 14）；
  轨道 CornerRadius=16、Padding=4,6 → 衬底距容器边垂直 2px、水平 1px，不再超出容器；
  轮廓↔标签保持无缝（衬底图形），轮廓↔容器留 1~2px 呼吸
- ✅ 全局方框圆角化：IconButtonStyle 加 CornerRadius=4 模板（含悬停/按下/禁用反馈），
  覆盖导入/导出/热键/添加/调整位置/行内 eye/删除等全部按钮；新增 RoundedComboStyle
  圆角下拉模板（含弹层圆角与项高亮）应用于键盘注入 / 键帽配色下拉；TextBox 全局
  圆角模板（App.xaml）应用于间隔编辑框；键源图块圆角 3 → 4；横幅 / 底栏 / 日志面板
  补 CornerRadius=4 与外边距（10,6,10,4 / 10,0,10,6）
- ✅ 方案行可视化开关与删除按钮间距：-1px 重叠 → 4px 小间距（去掉 Panel.ZIndex）

## 上次状态（模式滑块轮廓改衬底图形 + 字体恒定加粗，2026-09-05）
- ✅ 字体恒定加粗：ModeSegmentStyle FontWeight=SemiBold（含未选中），去掉样式级
  IsChecked 触发器；悬停模板触发器的加粗 setter 一并移除（不再需要）
- ✅ 轮廓改绘制衬底：描边环（BorderThickness + 1px 间隙）有留白感，改为在钮背后
  垫灰色圆角矩形（Halo，负 Margin 外扩 5px、圆角 18），无缝紧贴钮边缘，
  视觉比 2px 描边大得多；颜色仍 TabIdleBg（与方案档位悬停同源）；
  透明 ↔ 灰切换零布局位移，外缘恰止于本列边界，不侵犯相邻钮

## 上次状态（模式滑块几何定稿，2026-09-05）
- ✅ 圆角拉满：钮高 26 / 圆角 13（胶囊），轨道 CornerRadius=16、Padding=3→5
- ✅ 钮缩小让位轮廓：钮 Margin=3,0（水平），内容 Padding 0,6 → 0,4
- ✅ 轮廓环改为负 Margin 外扩 3px（BorderThickness=2 + 间隙 1），环圆角 16 = 13 + 3：
  不占钮内部空间、外缘恰好止于本列边界，与相邻钮色底保持 3px 白隙，互不侵犯
- ✅ 轮廓颜色 #808080 → TabIdleBg（与方案①②③悬停色同源，深浅主题自适应）
- ✅ 环 BorderThickness 常驻占位、BorderBrush 透明 ↔ 灰切换，显隐零布局位移

## 上次状态（模式选择重做为滑块样式，2026-09-05）
- ✅ ModeSegmentStyle 重写：两枚独立 RadioButton（不再拼成一整条），
  底色仍常驻紫（AccentPrimary）/ 金（AccentGold），共同置于白色背景容器
  （CornerRadius=8 Padding=3）作滑块轨道；选中 / 悬停钮外围显灰色外轮廓
  （BorderThickness=1.5 常驻占位防布局位移，BorderBrush 透明 ↔ #808080 切换），
  轮廓圆角 7.5 = 内标签圆角 6 + 环厚 1.5；选中 / 悬停字体加粗，光标默认
- ✅ 删除 Helpers/SegmentCorner.cs（附加属性仅旧分段条使用，已无引用）

## 上次状态（方案档位移面板最底 + 底栏间距/等高统一，2026-09-05）
- ✅ 方案①②③档位标签移到右面板最底部（导入/导出配置之下、贴面板底边；
  DockPanel 内先声明的 Bottom 子元素贴最底边，档位 Border 声明在导入导出 Grid 之前）；
  面板顺序：模式分段条 → 分区子标签 → 添加 → 方案列表 → 导入/导出 → 最底方案档位
- ✅ 底栏"全局开关"文字改为"全局开关热键设置："
- ✅ 底栏间距统一 8px（注入下拉/配色下拉右侧 10px → 8px；eye 与音量间 10px → 8px；
  热键按钮右侧补 8px 间距，衔接最右 eye 组）
- ✅ 底栏等高：热键按钮、eye、音量、调整可视化位置统一 Height=26
  （eye/音量 22×20、24×20 → 26×26；热键 Padding 4,3 → 4,0 定高）

## 上次状态（分段条悬停加粗 + 底栏图标移最右，2026-09-05）
- ✅ ModeSegmentStyle：未选中半边悬停时字体加粗（模板触发器 IsMouseOver →
  TextElement.FontWeight；ContentPresenter 无 FontWeight 属性，须用 TextElement 附加属性），
  可点击性暗示；选中半边仍常驻加粗（样式级 IsChecked 触发器）
- ✅ 底栏重排：可视化总开关（eye）与音量按钮（volume）移到底栏最右边
  （新 StackPanel Dock=Right 声明在全局开关键组之前，贴最右边缘），
  底栏从左到右：键盘注入 → 键帽配色 → 调整位置 → 全局开关 → 最右 eye+音量

## 上次状态（金色定稿 #B39041 + 分段条字重 + 导入导出移底）
- ✅ 金色定稿 #B39041：AccentGold 资源 + 键盘区按压模式图块（KeySourceViewModel.s_holdBrush）
- ✅ ModeSegmentStyle 字重：未选中半边恢复正常（Normal），选中半边由
  Style.Triggers IsChecked 触发器常驻加粗（SemiBold）；底色仍常驻紫 / 金
- ✅ 导入 / 导出配置移到方案面板最底部：DockPanel 内声明于 ListBox 之前并
  Dock=Bottom（ListBox 为最后子元素占中间剩余空间），方案列表增长时按钮
  不会被推出面板；面板顺序：方案档位 → 模式分段条 → 分区子标签 → 添加 →
  方案列表 → 底部导入/导出
- ✅ 使用说明 v1.6.0 相应描述同步过时（布局描述），下次发版时更新

## 上次状态（金色统一 #FFCB00 + 分段条简化 + 方案列表去选中特效，2026-09-05）
- ✅ 金色统一改 #FFCB00：AccentGold 资源（App.ApplyTheme）+ 键盘区按压模式图块
  硬编码（KeySourceViewModel.s_holdBrush）；横幅深色文字 #1F1F1F 保留（亮金底对比度）
- ✅ ModeSegmentStyle 简化：两半文字统一白色加粗（去掉右侧深字 #1F1F1F）；
  去掉悬停加暗 / 按下内衬 / 选中暗化全部触发器；光标恢复默认（去掉 Hand）；
  选中态仅常驻加粗（底色本来就常驻紫 / 金）
- ✅ 方案列表 ListBox 加 ItemContainerStyle 重写模板（纯 ContentPresenter）：
  去掉 WPF 默认蓝色选中 / 悬停底色

## 上次状态（发版 v1.6.0，2026-09-05）
- ✅ 版本 1.6.0（csproj Version/FileVersion、窗口标题、托盘文本）
- ✅ 使用说明.txt 更新至 v1.6.0：方案面板布局调整（档位标签上移）、
  开关/按压分段条、键帽配色下拉、可视化悬浮窗位置调整、间隔编辑虚影水印
- ✅ Release 发布并打包：dist/TheCelestialDiviner-1.6.0-win64.zip（1.77 MB / 解压 6.44 MB），
  产物结构与 1.5.0 一致（exe + System.*.dll 依赖 + dd63330 驱动 + 使用说明）

## 上次状态（右面板布局 + 模式分段条）
- ✅ 方案①②③档位标签移到导入/导出配置按钮上方（原顺序：导入导出 → 档位 → 模式）
- ✅ 模式标签重做为分段条（ModeSegmentStyle）：开关/按压两半常驻底色拼成一整条
  （左半开关模式常驻主题紫 AccentPrimary，右半按压模式常驻金 AccentGold），
  选中半边加暗内衬（#26000000）+ 加粗呈按压选中态；未选中半边文字淡化 0.75、
  悬停轻微加暗；按下时更深内衬（#45000000）给出点击按压反馈；
  左右外侧圆角经新增 SegmentCorner.Rounding 附加属性逐半指定
  （Helpers/SegmentCorner.cs；模板内 RelativeSource=TemplatedParent 绑定读取）
- ✅ 金色底文字用 #1F1F1F（与全局开关横幅一致，保证对比度）

## 上次状态（v1.5.0 配色统一，2026-09-04）
- ✅ 全局配色统一：主题紫 #8A5CF5（模式标签选中 / 方案①②③选中 / 开关模式图块 /
  双宏图块 / 总开关键图块 / 横幅开启 / 录入提示条），金色 #D6A01D（按压模式图块 /
  横幅关闭）；硬编码色值提为主题资源（AccentPrimary / AccentGold / HotkeyBg / TabIdleBg），
  后续调色只改 ApplyTheme 一处
- ✅ 热键按钮与总开关键图块改黑色底白字（用户指定）
- ✅ 方案①②③ 内边距扩大呈正方形（ProfileTabStyle 26×26）
- ✅ 未选中模式标签 / 档位按钮增加灰色底（浅 #DFDFDF / 深 #3A3A3A），可点击性明确
- ✅ 行内可视化开关（eye）改用 IconButtonStyle（与删除按钮同款，视觉统一）
- ✅ 新增全局键位可视化总开关（音量右侧，eye 图标默认开启）：关闭时全部键位统一暂停显示
  （服务层闸门 + 列表 eye 图标整体暗淡禁用），各键位开关状态保留，恢复后延续；
  状态持久化（config v5 GlobalVisualEnabled 字段），导入配置同步还原
- ✅ 发版：使用说明更新至 v1.5.0（新增三档位 / 键位可视化说明），
  dist 打包 TheCelestialDiviner-1.5.0-win64.zip（1.76 MB / 解压 6.38 MB）

## 上次状态（v1.5.0 主体，GPL-3.0 许可证变更 + 键位可视化悬浮层）
- ✅ 许可证 MIT → GPL-3.0（键帽视觉复刻自 keyviz GPL 设计）
- ✅ 键帽可视化悬浮层（右下角点击穿透 + 置顶 + PBT 双层键帽 + 组合键并排 + 连击角标）
- ✅ 方案键位自动注册可视化，方案行 eye / eye-off 逐键开关（实时落盘）
- ✅ 修复：Geometry 资源类型不匹配 + 异常处理器弹窗重入渲染管线导致栈溢出闪退
  （Renderer 异常不再弹模态框，WER LocalDumps 已配置）

## 上次状态（v1.4.0，界面精简 + 方案三档位）
- ✅ 许可证 MIT → **GPL-3.0**（键帽视觉复刻自 keyviz 的 GPL 设计，衍生作品整体转 GPL；
  LICENSE 已替换为 GPL-3.0 全文，README 许可段落与出处声明同步）
- ✅ 新增键位可视化悬浮层（KeycapOverlayWindow）：屏幕右下角，点击穿透
  （WS_EX_TRANSPARENT + NOACTIVATE + TOOLWINDOW）+ 置顶 + 不抢焦点；
  键帽复刻 keyviz PBT 样式（外层深色底座 #1a1a1a + 内层浅色渐变按压面，
  按下内层下沉 0.15em / easeInOutExpo 100ms；组合键修饰键独立键帽并排；
  连击 ≥2 次右上角红色 ×N 角标弹跳显示）
- ✅ 显示语义：按下 → 显示并停在屏上（计时停止）；释放 → 2.5 秒后淡出隐藏；
  释放事件与当前显示组合匹配才抬键帽（防止按住 A 期间 B 的释放误抬 A）
- ✅ KeyVisualizerService：注册表（方案键位 → 显示开关）+ 钩子事件订阅 +
  UI Dispatcher 封送；修饰键状态用 GetAsyncKeyState 实时检测
- ✅ 有方案的键位自动注册可视化（默认全部开启）：启动 / 添加 / 删除 / 导入 /
  档位切换 / 总开关键迁移后 SyncVisualizerRegistry 全量同步
- ✅ 方案行新增可视化开关列（lucide eye / eye-off 图标，ISC 许可）：
  默认 eye；关闭后 eye-off 半透明；切换实时落盘（config v5 新增 VisualKeys 表，
  缺省视为开启），仅控制悬浮层显示、不影响连发本身
- ✅ 版本 1.5.0（csproj / 窗口标题 / 托盘文本）

## 上次状态（v1.4.0，界面精简 + 方案三档位）
- ✅ 删除顶部状态栏（定时器 / 权限 / 钩子 / 总开关状态文本行）；
  语音音量设置项移入右侧方案面板“全局开关热键”一行（热键后面，紧凑滑块）
- ✅ 底部栏“导出配置”后新增方案档位切换 ①②③（RadioButton，复用 ModeTabStyle 选中效果）：
  - 配置模型 v4：`AppConfig.Profiles`（固定 3 套方案字典）+ `ActiveProfile`（默认 0 = 方案①）
  - 运行期镜像约定：`Schemes` 与 `Profiles[ActiveProfile]` 同一实例，全部方案编辑直接落在活动档位
  - 切换档位：重建方案列表 + 刷新图块高亮 + 调度器 ApplyConfig + 实时落盘 + 日志
  - 按钮选中态由 code-behind 随 VM.ActiveProfile 同步（避开 RadioButton 组内互斥取消选中破坏绑定的 WPF 已知问题）
  - 迁移：v3 旧配置存量方案整体迁入档位①；v4 配置反序列化完整还原；导入配置后同步档位按钮选中态
- ✅ 小键盘句点图块改名：图标行显示省略的“Num”、名称行显示“.”（原“Num.”单行）
- ✅ 定时器 1ms 分辨率提升保留（连发间隔精度功能基础），仅去掉状态栏展示
- ✅ 版本 1.4.0（csproj Version/FileVersion 1.4.0.0 / 窗口标题 v1.4）

## 上次状态（v1.3.0 修订，连发间隔下限 10ms + 方案行内编辑）
- ✅ 连发间隔默认值与下限改为 **10ms**（Constants.DefaultIntervalMs = MinIntervalMs = 10，
  上限仍 100ms；TargetKeyConfig.IntervalMs 默认值同步 10ms）
- ✅ 配置兼容：ConfigService.NormalizeIntervals 在加载 / 导入时把 < 10ms 的存量间隔钳制到 10ms
- ✅ 删除方案面板顶部的全局"连发间隔"设置项（顶栏仅保留全局开关热键）
- ✅ 方案列表行内间隔改为可编辑文本框：新方案按 10ms 默认值生成，点击文本框可修改，
  回车 / 失焦提交，非法输入回退上次有效值，越界自动钳制到 10~100；
  修改后重建调度器任务实时生效并落盘（UpdateSchemeInterval）
- ✅ 录入流程不再读取全局间隔输入：新方案统一用 Constants.DefaultIntervalMs 生成

## 上次状态（v1.3.0，开关模式分区 + 顶栏合并）
- ✅ 方案面板顶部合并为一行：**全局开关热键 + 连发间隔**（热键在前、间隔在后）
  （v1.3.0 修订已移除间隔输入，顶栏仅保留热键）
- ✅ 开关模式新增三个分区子标签（常规开关 / 轮转开关 / 双宏开关；仅开关模式显示）：
  - **分区1 常规开关**：原来的正常开关模式，互不影响
  - **分区2 轮转开关**：Rotate 分区内互斥——开启某轮转键时自动停止该分区内正在运行的其他键
    （省去手动关闭；调度器 Activate 内遍历任务 Deactivate 其他 Rotate 激活任务）
  - **分区3 双宏开关**：两步连选两个键（头键 = 开关键位，仅头键行显示启用勾选框，两行一体）；
    按下头键 → 两键轮流触发（1-2-1-2…，间隔 = 录入的连发间隔），再按一次头键一起停止
- ✅ 模型：新增 `ToggleSection` 枚举（Normal/Rotate/Dual，JsonStringEnumConverter）+ `KeyScheme.Section`
  （默认 Normal，仅 Mode=Toggle 生效）；AppConfig.CurrentVersion 2 → 3；
  MigrateIfNeeded 改为 `fromVersion < 2` 才跑 v1→v2 逻辑（v2→v3 仅推进版本号）
- ✅ 调度器：RunningTask.Config 改 `Configs`（List<TargetKeyConfig>）+ Section；双宏 = 单任务双目标，
  TaskLoop 按 phase 轮流 Click，停止时 phase 归零（重启从第 1 键开始）；事件日志同步
- ✅ 录入流程：双宏两步录入（第 1 键暂存不落盘，提示条同步进度）；校验：滚轮拒、总开关占用拒、
  两键相同拒、任一方已注册方案拒、任一方已是其他双宏辅键拒；普通单键路径同样拒绝"已是双宏辅键"
- ✅ 列表：双宏行两行一体（第 1 行勾选 + 主键 + 间隔 + 删除，第 2 行仅辅键名占位对齐）；
  按模式 + 分区过滤；辅键图块在启用双宏方案下蓝色标记（"⇄ A · B"，KeySourceViewModel.MarkDualPartner）
- ✅ 版本 1.3.0（csproj Version/FileVersion 1.3.0.0 / 窗口标题 v1.3）

## 上次状态（v1.2.2，commit 见 git log）
- ✅ 键盘区按真实 ANSI 物理布局重排（KeyboardRowPanel 键宽单位自适应）
- ✅ 暗淡效果触发互换：总开关开启时控件暗淡、关闭时清晰 + 黄色横幅
- ✅ 修复构造阶段按钮晚于配置加载的初始暗淡状态不同步

## 上次状态（v1.2.1，commit b1743e5）
- ✅ 修复总开关语音无声：WPF MediaPlayer 不解析 pack://application 资源 URI
  （Media Foundation 管线静默失败），改为启动时解包 WAV 到 %TEMP%\TheCelestialDiviner\
  并以文件 URI 播放；挂 MediaFailed 日志钩子，失败必留痕
- ✅ 键盘注入默认模式改为 DD 驱动（AppConfig.DefaultKeyboardMode = 3）
- ✅ 实机验证：F9 注入 → 总开关切换日志正常，语音播放无 MediaFailed

## 上次状态（v1.2.0，commit 57a47b8）— .NET Framework 4.8 迁移
- ✅ 发布体积 147MB → 5.1MB（主 exe 0.4MB + System.Text.Json 等小依赖 + dd63330.dll 3.7MB）
- ✅ Win10 1903+ / Win11 零安装（4.8 系统内置；不用 4.8.1 因其覆盖面反而小）
- ✅ 新增 Helpers/CompilerShims.cs（init/required 特性垫片）、
  Helpers/Compatibility.cs（Compat.Clamp / CombineHashCodes / 集合扩展）
- ✅ 调用点替换：Math.Clamp → Compat.Clamp（7 处）、File.Move 三参 → Copy+Delete、
  HashCode.Combine → Compat.CombineHashCodes、KVP 解构扩展
- ✅ System.Text.Json 改 NuGet 包 8.0.5 + AutoGenerateBindingRedirects
- ⚠️ 分发为目录形式（exe + config + System.*.dll + dd 驱动），不再是单文件

## 全部文件清单
- 根：TheCelestialDiviner.sln / .gitignore / README.md / PROGRESS.md / tools/make_icon.py
- src/TheCelestialDiviner/：csproj / app.manifest / App.xaml(.cs)
- Helpers：Constants / Logger / NativeMethods / ThemeHelper / Converters / Compatibility /
  CompilerShims / KeyboardRowPanel（新）
- Models：AppConfig.cs（InputKind/MouseInput/TargetKind/TriggerMode/InputSource/
  TargetKeyConfig/KeyScheme/GlobalSwitchConfig+Clone/AppConfig）
- Services：InputHookService / InputSimulatorService / TaskSchedulerService / DdDriverService /
  ConfigService / TimerResolutionService / KeyRecorder / InputNameMapper / SoundCueService
- ViewModels：MainViewModel(partial: 核心 + Collections) / KeySourceViewModel（含 WidthUnits）/
  TargetKeyViewModel / RelayCommand
- Views：MainWindow.xaml(.cs) / SchemeDialog.xaml(.cs) / GlobalSwitchDialog.xaml(.cs)
- Resources：app.ico + Sounds/StartVoice.wav / StopVoice.wav（内嵌，播放时解包）

## 部署形态
- 目标框架：net48（.NET Framework 4.8，系统内置零安装）
- 发布：dotnet publish -c Release -o bin\Release\net48-publish（约 5.1MB 全包）
- 高分屏：app.manifest PerMonitorV2（Framework 下正统做法）
- 运行要求：管理员权限（全局钩子 + 注入）
