# 衍天高手 The Celestial Diviner v1.1

键盘 / 鼠标连发（自动点击）工具。全局钩子监听物理输入，`SendInput` 模拟连发，
.NET Framework 4.8 + WPF（Win10 1903+ / Win11 系统内置运行时，零安装，全包 < 6MB），需管理员权限运行。

## 功能

- **全局输入监听**：`WH_KEYBOARD_LL` + `WH_MOUSE_LL` 低级钩子，专用线程 + 消息泵，回调零耗时
- **键盘注入 4 模式**（底部下拉框热切换）：
  - **普通（SendInput）**：虚拟键码 + 扫描码双填
  - **扫描码（DirectInput）**：`KEYEVENTF_SCANCODE`，兼容 DirectInput / RawInput 游戏
  - **消息（PostMessage）**：直投前台窗口消息，仅游戏前台时有效
  - **DD 驱动（物理级）**：经 DD 虚拟驱动注入，**不带 `LLKHF_INJECTED` 标记**，
    对游戏表现为真实物理键盘，可解决前三种模式均被过滤的场景
    （需程序目录有 `dd63330.dll`，官方免费版加载时需联网授权；失败自动回退普通模式）
- **多目标绑定**：每个输入源（键盘键 / 鼠标 7 键 / 滚轮）可绑定多个目标键，独立配置：
  - 目标键（键盘键 / 鼠标键 / 滚轮）
  - 模式：**开关 Toggle**（按一次启动 / 再按停止）或 **按压 Hold**（按住连发，松开即停）
  - 间隔：1 ~ 100ms，默认 **5ms**（`timeBeginPeriod(1)` 保证定时精度）
- **暂停规则**：任一 Hold 目标键激活时，暂停所有 Toggle；Hold 全部释放后恢复（以目标键为单位）
- **总开关（全局开关键）**：默认键 **F9**（可自定义），作为所有方案的按键总开关：
  - **默认关闭**：启动后按总开关键开启/关闭；开启时语音提示“启动”，关闭时提示“关闭”
  - 关闭状态下所有方案不响应输入，正在连发的任务立即停止；恢复开启不自动重启（方案保持待触发）
  - 开关键不能与已注册方案的输入源相同；编辑总开关键时原键上的方案自动迁移到新键
  - 顶部状态栏显示当前总开关状态与键位；底部“设置全局开关”按钮录制/清除自定义键位
- **提示语音音量**：顶部状态栏右侧滑块（0 ~ 100%，默认 70%），拖动自动保存；0 为静音
- **配置持久化**：`%APPDATA%\TheCelestialDiviner\config.json`，变更自动保存，损坏自动备份重建（v1 旧配置自动迁移：总开关默认关闭、默认键 F9）
- **导入 / 导出**：底部按钮导出 / 导入 JSON 配置（旧版本导出文件导入时自动迁移）
- **托盘**：关闭窗口 → 最小化到托盘；托盘右键：显示主界面 / 总开关 开启关闭 / 退出；双击显示/隐藏
- **界面**：简体中文；深浅主题跟随系统；Per-Monitor V2 DPI 感知
- 状态颜色：未注册浅灰 / Toggle 蓝 / Hold 橙；全局停用时全部控件半透明 + 黄色横幅

## 运行环境

- Windows 10 (1903+) / Windows 11，x64
- 无需安装任何运行时（.NET Framework 4.8 已内置，若系统关闭了该功能可在“启用或关闭 Windows 功能”中开启 .NET Framework 4.8 高级服务）
- **必须以管理员身份运行**（全局钩子 + 向高权限窗口发送输入）

## 使用说明

1. 右键 `TheCelestialDiviner.exe` → **以管理员身份运行**（UAC 弹窗选"是"）
2. 左键点击任意按键（键盘区或鼠标区）→ 弹出"设置方案"对话框
3. 对话框内"+ 添加" → "录制"（5 秒内按下目标键）→ 选模式与间隔 → "确认添加" → "确认"
4. 右键按键可：编辑方案 / 启用停用方案 / 清空方案
5. "设置全局开关" → "录制"按下一个键盘键作为总开关键（默认 F9，无需设置即可用）
6. **按 F9（总开关键）开启总开关**（听到“启动”语音）→ 按下注册的输入源 → 绑定的目标键按配置模式连发；再按 F9 关闭（“关闭”语音）
7. 关闭窗口不会退出，程序驻留托盘；托盘右键 → 退出

### 模式说明

| 键盘注入模式 | 行为 |
|---|---|
| 普通（SendInput） | 虚拟键码 + 扫描码双填，兼容面最广，但带注入标记 |
| 扫描码（DirectInput） | 只以扫描码语义注入，兼容 DirectInput / RawInput 游戏 |
| 消息（PostMessage） | 直投前台窗口消息，不进系统输入流，仅游戏前台时有效 |
| DD 驱动（物理级） | DD 虚拟驱动注入，无注入标记、表现同物理键盘（游戏兼容性最强） |

### DD 驱动模式说明

- 依赖 [ddxoft DD 虚拟鼠标键盘驱动](https://github.com/ddxoft/master) 官方 **2026 x64 免费版**
  （`dd63330.dll`），随发布包放在程序目录即可；也支持放到
  `%APPDATA%\TheCelestialDiviner\drivers\dd63330.dll`
- 免费版 DLL 加载时会进行**在线授权校验**，请保持网络可用；初始化失败时程序自动回退普通模式并在日志提示
- 仅接管**键盘**注入（本工具核心场景）；鼠标 / 滚轮连发仍走 SendInput
- 修改注入模式立即生效，配置自动保存并随导入 / 导出同步

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

## FAQ：为什么在部分游戏里键盘连发不生效？

| 测试项 | 结果 | 结论 |
|---|---|---|
| 鼠标注入（SendInput） | **生效** | 保护组件未整体拦截注入事件 |
| 键盘注入（SendInput：虚拟键码 / 扫描码 / 双字段均试） | 不生效 | 保护组件按系统 `LLKHF_INJECTED` 注入标记**选择性过滤键盘** |
| 键盘消息模式（PostMessage `WM_KEYDOWN/UP`） | 聊天框**生效**、技能键不触发 | 聊天框（UI 文本）读窗口消息；战斗输入走 DirectInput / RawInput 直读硬件级输入流，不读消息队列 |

**结论**：此类游戏的战斗键盘输入读取硬件级输入流，且对应用层注入的键盘事件按注入标记过滤。
`LLKHF_INJECTED` 标记由操作系统在用户态打上、无法摘除；PostMessage 不进输入流但游戏不读。
鼠标连发（含滚轮）不受影响，可正常使用。

## 开发 / 构建

```powershell
# 开发构建（需 .NET Framework 4.8 开发者包 / targeting pack，VS2022 默认含）
dotnet build TheCelestialDiviner.sln -c Release

# 发布（net48 无需 -r / --self-contained；产物约 1.5MB + dd 驱动 3.7MB）
dotnet publish src\TheCelestialDiviner\TheCelestialDiviner.csproj -c Release -o dist
```

产物：exe + System.Text.Json 等小依赖 DLL + dd63330.dll，合计约 5.1MB（主 exe 0.4MB）。

### 项目结构

```
src/TheCelestialDiviner/
├── Views/            MainWindow / SchemeDialog / GlobalSwitchDialog（XAML + cs）
├── ViewModels/       MainViewModel(partial) / KeySourceViewModel / TargetKeyViewModel / RelayCommand
├── Models/           InputSource / TargetKeyConfig / KeyScheme / GlobalSwitchConfig / AppConfig
├── Services/         InputHookService / InputSimulatorService / TaskSchedulerService
│                     ConfigService / TimerResolutionService / KeyRecorder / InputNameMapper
│                     SoundCueService（总开关提示语音）
├── Helpers/          NativeMethods / Constants / Logger / ThemeHelper / Converters
│                     Compatibility（net48 垫片：Clamp/哈希/集合扩展） / CompilerShims（init/required）
├── Resources/        app.ico（16~256 多尺寸）+ Sounds/（总开关提示语音 WAV）
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
