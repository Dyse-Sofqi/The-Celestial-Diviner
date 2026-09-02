# 衍天高手（The Celestial Diviner）开发进度交接

更新：2026-09-03 03:00 (Asia/Shanghai) — **新增 DD 驱动模式（键盘物理级注入）**

## 本次变更（DD 驱动模式）
- ✅ 新增 `Services/DdDriverService.cs`：加载 ddxoft DD 虚拟驱动 x64 DLL（dd63330.dll），
  `DD_btn(0)` 初始化、`DD_todc` VK→DD 码运行时映射（带缓存）、`DD_key` 按下/抬起注入
- ✅ `InputSimulatorService.KeyboardMode=3`：键盘走 DD，鼠标/滚轮仍走 SendInput（InjectMagic 防回环不变）
- ✅ `AppConfig` 新增 `KeyboardMode` int 字段（旧配置回退 `UseScanCodes` 语义），导入/导出同步
- ✅ UI 下拉框新增第 4 项"DD 驱动（物理级）"，切换失败自动回退普通模式并日志提示
- ✅ 仓库新增 `drivers/dd63330.dll`（官方 2026 x64 免费版，来源 github.com/ddxoft/master），
  csproj 以 CopyToOutputDirectory 随构建/发布分发（单文件发布内嵌原生 DLL 自解压运行）
- ✅ 实测（本机 x64 Win11 管理员）：DD_btn(0)=1 初始化成功；DD 注入键盘事件在低级钩子中
  flags 无 LLKHF_INJECTED（0x10）位——物理级效果达成；端到端 F6→ScrollLock 30ms 连发验证通过
- ⚠️ DD 键码表覆盖 100 个 VK（数字/字母/导航/小键盘/F1-F12/标点）；F13+ 不支持（todc=-1）
- ⚠️ 本机 Change Box 自带 DD64.dll 为 32 位 DD202x 时间锁版（签名 2025-03 过期），DD_todc 恒返 -1，
  已被 ProbeDll 排除；若用户机器只有该版本需另行获取 x64 免费版
- ⚠️ DD 鼠标注入（DD_btn 掩码/DD_whl）未做端到端确认（本进程 LL 鼠标钩子未捕获到注入事件，
  疑似走 Raw Input 路径），故本版本鼠标连发不切 DD，后续如需可再加

## 上次状态（v1.0）
- ✅ dotnet build Debug：0 error（仅 WFAC010 警告，保留 manifest DPI 声明为需求要求）
- ✅ dotnet publish Release 单文件：`src\TheCelestialDiviner\bin\Release\net8.0-windows\win-x64\publish\TheCelestialDiviner.exe`
- ✅ README.md 完成

## 全部文件清单
- 根：TheCelestialDiviner.sln / .gitignore / README.md / PROGRESS.md / tools/make_icon.py
- src/TheCelestialDiviner/：csproj / app.manifest / App.xaml(.cs)
- Helpers：Constants / Logger / NativeMethods（SendInput 返回 uint 已修正）/ ThemeHelper / Converters（4 个转换器）
- Models：AppConfig.cs（InputKind/MouseInput/TargetKind/TriggerMode/InputSource/TargetKeyConfig/KeyScheme/GlobalSwitchConfig+Clone/AppConfig）
- Services：InputNameMapper / ConfigService / TimerResolutionService(+IsHighResolution) / InputSimulatorService / InputHookService / TaskSchedulerService / KeyRecorder
- ViewModels：RelayCommand / TargetKeyViewModel / KeySourceViewModel（乱码修复过：中文注释全是'。'结尾但代码逻辑完好）/ MainViewModel.cs(partial 核心) / MainViewModel.Collections.cs(集合+方案操作)
- Views：MainWindow.xaml(.cs) / SchemeDialog.xaml(.cs) / GlobalSwitchDialog.xaml(.cs)
- Resources/app.ico：16/24/32/48/64/128/256 七尺寸

## 已知妥协（如需打磨）
- KeySourceViewModel 部分注释被乱码修复脚本替换成'。'（纯注释，无功能影响）
- 键盘布局第7行导航区/方向键未含全部104键（Ins/Home/PgUp/↑←→/End/PgDn/↓/Del 已含，PrintScreen 等未含）
- 托盘图标用单 32x32 加载（系统自动缩放 16x16）
- WFAC010 警告：WinForms 建议用 ApplicationHighDpiMode 属性替代 manifest 声明；保留 manifest 因为需求指定

## 关键设计约定
- 注入事件 dwExtraInfo=InputHookService.InjectMagic；钩子回调里 magic 命中直接放行
- 源键 K:VK:EXT / M:MouseInput
- 滚轮源=脉冲：Hold→单次点击；Toggle→每格翻转
- 全局停用：scheduler.SetMasterEnabled(false) 即 StopAllCore；恢复不自动重启
- 全部停止按钮：Ctrl+点击（OnStopAllButtonDown 判定）
- 关窗→托盘（App.IsExiting 区分）；托盘退出走 ExitApp 时序：StopAll→Dispose hook→Dispose timer→SaveConfig
