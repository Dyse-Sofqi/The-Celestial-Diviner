# 衍天高手（The Celestial Diviner）开发进度交接

更新：2026-09-03 23:30 (Asia/Shanghai) — **语音无声修复 + 默认模式改 DD（v1.2.1）**

## 本次变更
- 🐛 **修复总开关切换无声**（用户实测反馈）：
  - 根因：WPF `MediaPlayer` 的媒体管线（Media Foundation）**不解析 pack://application 资源 URI**，
    `Open` 静默失败（HasAudio=false、无异常、Play 无声）——pack URI 只对
    `Application.GetResourceStream` 等资源解析器有效，MF 解不开
  - 取证：反射确认 WAV 已内嵌（g.resources 含 startvoice/stopvoice）；STA 脚本验证
    本机 MF 播本地文件正常 → 问题锁定在 pack URI 解析；pack Open 后 Duration=Automatic/HasAudio=False 复现
  - 修复：`SoundCueService` 启动时把两个 WAV 解包到 `%TEMP%\TheCelestialDiviner\`，
    改用**文件 URI** 播放（音量滑块照常）；挂 `MediaFailed` 日志钩子，失败不再静默
  - 端到端：net48 发布版实机 keybd_event 注入 F9 两次，日志出现启用/停用，无 MediaFailed
- ⚙️ **键盘注入默认模式改为 DD 驱动**（用户要求，物理级注入）：
  - `AppConfig.DefaultKeyboardMode = 3`；旧配置无 KeyboardMode 字段时按 DD 处理
  - 冷启动驱动未就绪（缺 dll / 非管理员 / 授权失败）仍静默回退普通模式
  - 注意：用户现存配置已是 KeyboardMode=3，此变更只影响无配置/新机器
- 版本号 1.2.1

## 验证记录
- dotnet build Debug：0 error 0 warning（net48）
- dotnet publish Release → bin\Release\net48-publish\（约 5.1MB，主 exe 0.4MB）
- 实机：启动解包 WAV 到 %TEMP%、钩子/定时器正常、F9 切换日志正常、无播放报错

## 上次状态（v1.2.0，commit 57a47b8）
- ✅ 迁移到 .NET Framework 4.8：零安装（Win10 1903+/Win11 内置），发布体积 147MB→5.1MB
- ✅ PerMonitorV2 高分屏适配不变（manifest 声明，net48 正统做法，WFAC010 警告消失）
- ✅ net48 兼容垫片：CompilerShims.cs（init/required）、Compatibility.cs（Clamp/哈希/集合扩展）
- ✅ System.Text.Json 改 NuGet 包 + AutoGenerateBindingRedirects
- ✅ 总开关改造（v1.1.0，commit 6dc5ed1）：取消"全部停止"、默认关闭、默认键 F9 可自定义、
  开关语音（启动/关闭）、音量滑块、配置 v1→v2 迁移、换键方案自动迁移、录制期防误触

## 全部文件清单
- 根：TheCelestialDiviner.sln / .gitignore / README.md / PROGRESS.md / drivers/dd63330.dll
- src/TheCelestialDiviner/：csproj（net48 + System.Text.Json 包）/ app.manifest / App.xaml(.cs)
- Helpers：Constants / Logger / NativeMethods / ThemeHelper / Converters /
  Compatibility（net48 垫片）/ CompilerShims（init/required 垫片）
- Models：AppConfig.cs（InputKind/MouseInput/TargetKind/TriggerMode/InputSource/TargetKeyConfig/
  KeyScheme/GlobalSwitchConfig+Clone/AppConfig+迁移；DefaultKeyboardMode=3）
- Services：InputHookService / InputSimulatorService（4 模式）/ TaskSchedulerService /
  ConfigService（v2 迁移）/ TimerResolutionService / KeyRecorder（IsAnyRecording）/
  InputNameMapper / DdDriverService / SoundCueService（临时文件解包播放）
- ViewModels：MainViewModel(.cs/.Collections.cs) / KeySourceViewModel / TargetKeyViewModel / RelayCommand
- Views：MainWindow / SchemeDialog / GlobalSwitchDialog（XAML + cs）
- Resources：app.ico / Sounds/StartVoice.wav + StopVoice.wav（SAPI Huihui 44.1k 16bit 单声道）

## 已知限制 / 注意事项
- Framework 版不再是单文件：分发需整个目录（exe + .exe.config + System.*.dll + dd63330.dll）
- WPF 媒体管线不认 pack://application URI（本次无声根因）；如需纯资源播放可改
  `SoundCueService` 用 `MediaPlayer.Open(文件URI)` 之外的方案（如 NAudio），当前解包方案够用
- DD 模式冷启动静默回退普通模式是刻意设计（初始化期日志面板未显示，避免误导）；
  热切换失败才有日志提示
- 暂不支持 F13+ 键的 DD 注入（DD_todc 返回 -1）
- DD 鼠标注入未做端到端确认，鼠标连发仍走 SendInput
- net8.0-windows 目标已移除；如需找回见 git 历史 v1.1.0（6dc5ed1）
