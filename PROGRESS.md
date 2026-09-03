# 衍天高手（The Celestial Diviner）开发进度交接

更新：2026-09-03 21:40 (Asia/Shanghai) — **取消全部停止；全局开关升级为按键总开关**

## 本次变更（总开关 + 提示语音 + 音量）
- ✅ **取消「全部停止」功能**（与全局开关用途重叠）：删除状态栏按钮（含 Ctrl+点击防误触）、
  VM StopAllCommand、托盘菜单项；停止能力统一由总开关承担
  （TaskSchedulerService.StopAll 保留，仅退出时序内部调用）
- ✅ **总开关 = 按键总开关**：默认关闭；默认键 F9（0x78），可自定义
  （底部「设置全局开关」对话框录制 / 清除，冲突校验不变）
- ✅ 开启播「启动」、关闭播「关闭」语音：新增 Services/SoundCueService.cs（MediaPlayer，
  内嵌 WAV；Resources/Sounds/StartVoice.wav、StopVoice.wav，Windows SAPI 中文语音
  Huihui 生成，44.1kHz 16bit mono）
- ✅ **音量调节滑块**：顶部状态栏右侧（原全部停止按钮位置），0~100%，默认 70%；
  VM SoundVolume 属性 + 500ms 防抖落盘；拖动即时生效（含拖动中切换的语音）
- ✅ AppConfig 升 v2：GlobalSwitch 默认 HasKey=true/VK=F9/Enabled=false；新增 SoundVolume；
  ConfigService.MigrateIfNeeded 做 v1→v2 迁移（仅当 F9 未被存量方案占用时设默认键；
  已自定义热键的保留用户键；导入配置走同一迁移）
- ✅ 编辑总开关键时旧键方案自动迁移到新键（RelocateSchemeFromMasterKey，避免键位重叠吞键）
- ✅ 热键录制期间总开关键不触发切换（KeyRecorder.IsAnyRecording 静态标记，防录方案时误切）
- ✅ 状态栏新增「总开关键：F9」显示；横幅文案改为「总开关已关闭」；托盘菜单文案「总开关 开启/关闭」
- ✅ 实测（本机 Win11 x64 管理员）：Debug 构建运行，日志确认「配置 v1 已迁移到 v2
  （总开关默认关闭、默认键 F9）」，钩子安装成功、5 个连发任务正常应用
- ✅ dotnet build Debug / Release：0 error（仅原有 WFAC010 警告）

## 上次变更（DD 驱动模式）
- ✅ Services/DdDriverService.cs：加载 ddxoft DD 虚拟驱动 x64 DLL（dd63330.dll），
  DD_btn(0) 初始化、DD_todc VK→DD 码映射（缓存）、DD_key 按下/抬起注入
- ✅ InputSimulatorService.KeyboardMode=3：键盘走 DD，鼠标/滚轮仍走 SendInput
- ✅ UI 下拉框第 4 项「DD 驱动（物理级）」，切换失败自动回退普通模式
- ⚠️ DD 键码表覆盖 100 个 VK；F13+ 不支持；DD 鼠标注入未做端到端确认

## 全部文件清单
- 根：TheCelestialDiviner.sln / .gitignore / README.md / PROGRESS.md / tools/make_icon.py
- src/TheCelestialDiviner/：csproj / app.manifest / App.xaml(.cs)
- Helpers：Constants（含 DefaultMasterKeyVk=F9、DefaultSoundVolume=70）/ Logger /
  NativeMethods / ThemeHelper / Converters
- Models：AppConfig.cs（InputSource/TargetKeyConfig/KeyScheme/GlobalSwitchConfig(v2 默认值)/
  AppConfig(Version=2+SoundVolume)）
- Services：InputNameMapper / ConfigService（+MigrateIfNeeded）/ TimerResolutionService /
  InputSimulatorService / InputHookService / TaskSchedulerService / KeyRecorder（+IsAnyRecording）/
  **SoundCueService（新）**
- ViewModels：RelayCommand / TargetKeyViewModel / KeySourceViewModel / MainViewModel.cs(partial：
  +SoundVolume+SoundCueMuteForExit，-StopAllCommand) / MainViewModel.Collections.cs
  （+RelocateSchemeFromMasterKey）
- Views：MainWindow.xaml(.cs)（-全部停止按钮 +音量滑块 +总开关键状态）/ SchemeDialog.xaml(.cs) /
  GlobalSwitchDialog.xaml(.cs)
- Resources：app.ico + **Sounds/StartVoice.wav、StopVoice.wav（新）**
- drivers/dd63330.dll（DD 模式依赖）

## 已知妥协（如需打磨）
- KeySourceViewModel 部分注释被乱码修复脚本替换成'。'（纯注释，无功能影响）
- 提示语音为 SAPI 合成音（Huihui），音质一般；如需更好音质可换真人录音替换 WAV 文件
- 托盘图标用单 32x32 加载；WFAC010 警告保留（manifest DPI 声明为需求要求）

## 关键设计约定
- 注入事件 dwExtraInfo=InputHookService.InjectMagic；钩子回调里 magic 命中直接放行
- 源键 K:VK:EXT / M:MouseInput；滚轮源=脉冲
- 总开关：scheduler.SetMasterEnabled(false) 即停所有连发；恢复开启不自动重启
- 总开关键与方案注册源互斥（双向冲突校验）；编辑总开关键自动迁移旧键方案
- 配置 Version 字段启用：Load 与 ImportFromJson 都走 MigrateIfNeeded
- 关窗→托盘（App.IsExiting 区分）；托盘退出走 ExitApp 时序：
  静音语音→StopAll→Dispose hook→Dispose timer→SaveConfig
