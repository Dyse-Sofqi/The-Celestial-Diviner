# 衍天高手 The Celestial Diviner v1.0

键盘 / 鼠标连发（自动点击）工具。全局钩子监听物理输入，`SendInput` 模拟连发，
.NET 8 + WPF 单文件 exe，需管理员权限运行。

## 功能

- **全局输入监听**：`WH_KEYBOARD_LL` + `WH_MOUSE_LL` 低级钩子，专用线程 + 消息泵，回调零耗时
- **多目标绑定**：每个输入源（键盘键 / 鼠标 7 键 / 滚轮）可绑定多个目标键，独立配置：
  - 目标键（键盘键 / 鼠标键 / 滚轮）
  - 模式：**开关 Toggle**（按一次启动 / 再按停止）或 **按压 Hold**（按住连发，松开即停）
  - 间隔：1 ~ 100ms，默认 **5ms**（`timeBeginPeriod(1)` 保证定时精度）
- **暂停规则**：任一 Hold 目标键激活时，暂停所有 Toggle；Hold 全部释放后恢复（以目标键为单位）
- **全局开关键**：自定义键盘热键一键启用/停用全部方案；停用即停所有连发；恢复不自动重启
- **全部停止**：状态栏按钮 **Ctrl+点击**（防误触）
- **配置持久化**：`%APPDATA%\TheCelestialDiviner\config.json`，变更自动保存，损坏自动备份重建
- **导入 / 导出**：底部按钮导出 / 导入 JSON 配置
- **托盘**：关闭窗口 → 最小化到托盘；托盘右键：显示主界面 / 全局启用停用 / 全部停止 / 退出；双击显示/隐藏
- **界面**：简体中文；深浅主题跟随系统；Per-Monitor V2 DPI 感知
- 状态颜色：未注册浅灰 / Toggle 蓝 / Hold 橙；全局停用时全部控件半透明 + 黄色横幅

## 运行环境

- Windows 10 (1903+) / Windows 11，x64
- 无需安装 .NET（自包含单文件发布）；开发构建需 .NET 8 SDK
- **必须以管理员身份运行**（全局钩子 + 向高权限窗口发送输入）

## 使用说明

1. 右键 `TheCelestialDiviner.exe` → **以管理员身份运行**（UAC 弹窗选"是"）
2. 左键点击任意按键（键盘区或鼠标区）→ 弹出"设置方案"对话框
3. 对话框内"+ 添加" → "录制"（5 秒内按下目标键）→ 选模式与间隔 → "确认添加" → "确认"
4. 右键按键可：编辑方案 / 启用停用方案 / 清空方案
5. "设置全局开关" → "录制"按下一个键盘键作为总开关热键
6. 触发：按下注册的输入源 → 绑定的目标键按配置模式连发
7. 关闭窗口不会退出，程序驻留托盘；托盘右键 → 退出

### 模式说明

| 模式 | 行为 |
|---|---|
| 开关 Toggle | 按一次注册源启动连发，再按一次停止 |
| 按压 Hold | 按住注册源连发，松开即停 |
| 混合 | Hold 激活期间所有 Toggle 暂停，Hold 全部释放后恢复 |
| 滚轮源 | 每滚动一格 = 一次触发（Hold 触发一次点击 / Toggle 翻转状态） |

## 杀毒软件 / SmartScreen 白名单指引

本工具使用全局键盘钩子与 `SendInput` 模拟输入，属于游戏辅助 / 自动化工具的常规技术手段，
**可能被杀毒软件或 SmartScreen 拦截或误报**。这是行为特征（Hook + 注入输入）触发的通用规则，并非病毒。

- **SmartScreen**：首次运行若弹出"已保护你的电脑"→ 点击"更多信息" → "仍要运行"
- **Windows Defender**：若文件被隔离，在"病毒和威胁防护"→"保护历史记录"→ 还原并添加排除
  - 路径示例：`Windows 安全中心 → 病毒和威胁防护设置 → 排除项 → 添加文件夹`（把 exe 所在目录加入排除）
- **第三方杀软**（火绒 / 360 / 腾讯管家等）：在拦截提示中选择"信任本程序"，或把 exe 加入白名单
- 本程序为绿色单文件，不写注册表、不驻留服务、不联网；退出即完全清理

## 开发 / 构建

```powershell
# 安装 .NET 8 SDK 后：
dotnet build src\TheCelestialDiviner\TheCelestialDiviner.csproj -c Release

# 发布单文件 exe（自包含，154MB；框架依赖发布可换 --self-contained false，约 1MB）
dotnet publish src\TheCelestialDiviner\TheCelestialDiviner.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

产物：`src\TheCelestialDiviner\bin\Release\net8.0-windows\win-x64\publish\TheCelestialDiviner.exe`

### 项目结构

```
src/TheCelestialDiviner/
├── Views/            MainWindow / SchemeDialog / GlobalSwitchDialog（XAML + cs）
├── ViewModels/       MainViewModel(partial) / KeySourceViewModel / TargetKeyViewModel / RelayCommand
├── Models/           InputSource / TargetKeyConfig / KeyScheme / GlobalSwitchConfig / AppConfig
├── Services/         InputHookService / InputSimulatorService / TaskSchedulerService
│                     ConfigService / TimerResolutionService / KeyRecorder / InputNameMapper
├── Helpers/          NativeMethods / Constants / Logger / ThemeHelper / Converters
├── Resources/        app.ico（16~256 多尺寸）
├── app.manifest      requireAdministrator + PerMonitorV2 DPI
└── App.xaml(.cs)     服务组装 / 主题注入 / 托盘 / 退出时序
```

## 异常处理

- 钩子安装失败 → 状态栏红色提示"请以管理员身份运行"
- 连发任务异常 → 记录日志并自动恢复循环
- 配置损坏 → 自动备份为 `config.json.bak` 并生成新配置
- 退出时序：停止所有任务 → 卸载钩子 → `timeEndPeriod(1)` → 保存配置

## 许可

见 LICENSE。
