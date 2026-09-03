# 衍天高手（The Celestial Diviner）开发进度交接

更新：2026-09-03 22:15 (Asia/Shanghai) — **迁移到 .NET Framework 4.8（net48）**

## 本次变更（net48 迁移，v1.2.0）

### 背景与决策
- 用户不可接受 .NET 8 自包含单文件的 147MB 体积（内嵌压缩后仍 65MB，WPF 运行时下限压不进 50MB）
- 选型对比后确定迁移 .NET Framework 4.8：Win10 1903+ / Win11 系统内置运行时，
  **零安装 + 全包 5.1MB（主 exe 0.4MB）**，且保留全部 WPF XAML 与业务代码
- 高分屏适配不受影响：WPF PerMonitorV2 由 app.manifest 声明，Framework 4.6.2+ 完整支持（原 net8 的 WFAC010 警告消失）

### 技术要点
- csproj：TargetFramework net48；System.Text.Json 改 NuGet 包 8.0.5；
  AutoGenerateBindingRedirects（生成 .exe.config 重定向 Unsafe 版本）
- 新增 Helpers/CompilerShims.cs：net48 缺失的 IsExternalInit / RequiredMemberAttribute /
  CompilerFeatureRequiredAttribute（支撑现有 C# 9 init / C# 11 required 语法）
- 新增 Helpers/Compatibility.cs：Compat.Clamp（Math.Clamp 等价）、
  Compat.CombineHashCodes（HashCode.Combine 等价，FNV-1a）、
  Dictionary.GetValueOrDefault 与 KeyValuePair.Deconstruct 扩展
- 调用点改造：Math.Clamp → Compat.Clamp（7 个文件）；HashCode.Combine → Compat.CombineHashCodes；
  File.Move(overwrite:) → File.Copy + Delete（net48 无三参 Move）
- 代码语法零降级：记录类 init/required、模式匹配、switch 表达式全部保留

### 验证
- ✅ Debug/Release 构建：0 错误 0 警告
- ✅ 实机运行（net48-publish）：配置加载正常、钩子安装成功、5 个连发任务运行、定时器 1ms 生效
- ✅ 体积：主 exe 0.4MB + 依赖 DLL 约 1MB + dd63330.dll 3.68MB ≈ 5.1MB
- ⚠️ 分发注意：需整个发布目录（exe + .exe.config + System.*.dll + dd63330.dll），不能只拷 exe
- 已清理 net8.0-windows 旧产物目录（用户如果还需要 .NET 8 版可从 git 历史找 v1.1.0）

## 上次变更（2026-09-03 21:40 前后，v1.1.0，总开关改造）
- 取消「全部停止」（状态栏按钮 / Ctrl+点击 / 托盘项），停止能力由总开关承担
- 总开关：默认关闭、默认键 F9 可自定义（配置 v1→v2 迁移）、方案注册源双向冲突校验、
  换键时方案自动迁移、录制期间暂停热键响应
- 开关切换播报「启动」/「关闭」语音（SAPI Huihui 离线合成 WAV，内嵌资源，SoundCueService 播放）
- 音量滑块（顶部状态栏右侧 0~100% 默认 70%，防抖 500ms 落盘）

## 全部文件清单
- 根：TheCelestialDiviner.sln / .gitignore / README.md / PROGRESS.md / tools/make_icon.py
- src/TheCelestialDiviner/：csproj / app.manifest / App.xaml(.cs)
- Helpers：Constants / Logger / NativeMethods（SendInput 返回 uint 已修正）/ ThemeHelper /
  Converters（4 个转换器）/ Compatibility（net48 垫片）/ CompilerShims（init/required 垫片）
- Models：AppConfig.cs（InputKind/MouseInput/TargetKind/TriggerMode/InputSource/TargetKeyConfig/
  KeyScheme/GlobalSwitchConfig+Clone/AppConfig v2 含 Version/SoundVolume）
- Services：InputHookService / InputSimulatorService / TaskSchedulerService / ConfigService
  （v1→v2 迁移）/ TimerResolutionService / KeyRecorder / InputNameMapper / DdDriverService /
  SoundCueService（总开关提示语音）
- ViewModels：MainViewModel（核心状态/命令/钩子接入/总开关切换）+ MainViewModel.Collections
  （集合/方案/导入导出/迁移）/ KeySourceViewModel / TargetKeyViewModel / RelayCommand
- Views：MainWindow / SchemeDialog / GlobalSwitchDialog（XAML + cs）
- Resources：app.ico（16~256 多尺寸）/ Sounds/（StartVoice.wav + StopVoice.wav，SAPI 合成）
