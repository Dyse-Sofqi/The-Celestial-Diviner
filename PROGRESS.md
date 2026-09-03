# 衍天高手（The Celestial Diviner）开发进度交接

更新：2026-09-03 23:55 (Asia/Shanghai) — **UI 拟真布局 + 暗淡语义互换（v1.2.2）**

## 本次变更（键盘物理布局 + 暗淡互换）
- ✅ 键盘区按真实 ANSI 物理布局重排：
  - 自定义 `Helpers/KeyboardRowPanel`：按"键宽单位"等比排列（普通键 = 4 格），单位宽度随窗口自适应
  - 功能键行：Esc 后大间隔，F1–F4 / F5–F8 / F9–F12 三段分组（真实分组间隔）
  - 主键区拟真键宽：Backspace 2u、Tab/\ 1.5u、Caps 1.75u、Enter 2.25u、
    LShift 2.25u、RShift 2.75u、空格 6.25u、底部修饰键 1.25u
  - 导航簇右置：Ins/Home/PgUp 与 Del/End/PgDn 两行对齐主键区右侧；
    方向键倒 T 形（↑ 独占上排居中，←↓→ 下排）
  - 全行 90 单位对齐（占位空白补齐），各列按键与实体键盘位置一一对应
- ✅ 暗淡效果触发互换（用户反馈：关闭时通常在设置方案，开启时不看面板）：
  - 总开关**开启时**全部输入源控件暗淡（Opacity 0.5，面板仅供观察）
  - **关闭时**恢复清晰（便于左键编辑方案）+ 黄色横幅提示
  - 顺带修复：构造阶段按钮晚于配置加载创建导致的初始暗淡状态不同步
- ✅ 版本 1.2.2（csproj / 窗口标题同步）

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
