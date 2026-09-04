# 衍天高手（The Celestial Diviner）开发进度交接

更新：2026-09-04 — **主题紫金配色统一 + 全局可视化总开关（v1.5.0）**

## 本次变更（主题色统一 + 全局可视化总开关）
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
