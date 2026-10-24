# 方案：OBS 干净录制（游戏画面 1:1 + 键帽可捕获）

> 状态：**方案已被 Web 叠加源取代**（v1.8.6 实际实现）。两轮实测否决了窗口捕获路线：
> ① OBS 窗口捕获不支持透明通道，不透明底板观感不可接受；② OBS 窗口列表只列可见窗口，
> 键帽空闲时悬浮窗隐藏、列表里选不到。最终实现 = 本机 HTTP+WebSocket 服务
> （WebOverlayService）+ OBS 浏览器源（CEF 透明背景，WebOverlayPage.Html 复刻键帽视觉），
> 屏幕观感完全不变、录制画面键帽透明浮动。见 CHANGELOG v1.8.6 与 docs/obs-recording.md。
> 本文仅作设计过程存档，不再反映现状。

## 背景与目标

用户用 OBS 录制游戏（剑网3，衍天高手连发工具），存在两个问题：

1. **标题栏黑边**：游戏用"窗口最大化"模式，窗口捕获的画面含标题栏，与 OBS 画布不完全匹配，
   顶部有黑条。（已确认：黑条本身**不影响性能**，只是画质/画面匹配问题，不需要独占全屏。）
2. **键帽捕获不到**：键帽悬浮层（KeycapOverlayWindow）是逐像素 alpha 分层窗口
   （AllowsTransparency=True，内存位图 + UpdateLayeredWindow 上屏），OBS 窗口捕获
   （BitBlt / WGC）会把透明区域合成到不透明底上，得到黑底黑框，无法干净叠加。

目标：一次录制直接出成片——游戏画面 1:1 无黑边 + 键帽画面可被 OBS 干净捕获并自由摆放。

## 总体结论

- **问题一不改代码**，靠"游戏切无边框全屏 + OBS 配置"解决（见文末使用指南，随本功能落一份
  `docs/obs-recording.md`）。
- **问题二改代码**：给键帽悬浮层加"**录制模式**"开关——开启后悬浮层重建为**不透明深色底条的
  普通窗口**（非分层窗口），OBS 窗口捕获即可干净捕获；关闭恢复现状透明悬浮。

### 为什么是"不透明底条"而不是别的

- OBS 的窗口捕获源不支持按 alpha 合成到画布（源一律不透明），透明悬浮窗永远捕不出干净结果；
  色度键（绿幕/品红底）有抗锯齿杂边问题，不可靠。不透明深色底是唯一干净方案。
- **性能不劣化**：非分层窗口不走 UpdateLayeredWindow 整面 CPU 上传（恰是现状里最贵的呈现路径，
  见 KeycapOverlayWindow.xaml.cs:601 注释），DWM 重定向表面按脏区更新，呈现成本 ≤ 现状；
  事件驱动机制（80ms 呈现节流、按需 Pacer、空内容隐藏）全部原样保留。OBS 侧多抓一个
  560×100 逻辑像素的小窗，相对 1080p 游戏帧是零头。
- **WYSIWYG**：屏幕上看到什么（深色条上的键帽），录出来就是什么。

---

## 实现规格

### 1. KeycapOverlayWindow.xaml — 移除两处窗口属性

`AllowsTransparency="True"` 与 `Background="Transparent"` 从 Window 根元素**移除**（XAML 属性
会在 InitializeComponent 时覆盖代码先行设置的值），改由构造函数按模式设置。其余属性不动。

### 2. KeycapOverlayWindow.xaml.cs — 模式静态量 + 双风格构造 + 干净销毁

**(a) 新增静态模式量与底色**（放在 `_scheme` 静态量附近，同款注释风格）：

```csharp
/// <summary>录制模式（主线程写 / 悬浮窗线程读，窗口构造时消费）：true = 不透明深色底条的
/// 普通窗口（非分层窗口，OBS 窗口捕获 BitBlt/WGC 均可干净捕获）；false = 逐像素 alpha
/// 分层悬浮（现状观感，OBS 捕获得到黑底）。AllowsTransparency 只能在句柄创建前设置，
/// 运行期切换由 KeyVisualizerService 销毁重建窗口（RebuildOverlayForModeSwitch）。</summary>
private static volatile bool _recordingMode;

/// <summary>录制模式底条色：中性深色，与全部键帽配色方案兼容；不透明是捕获干净的前提。</summary>
private static readonly Color RecordingBackdropColor = Color.FromRgb(0x21, 0x20, 0x27);

public static bool RecordingMode
{
    get => _recordingMode;
    set => _recordingMode = value;
}
```

**(b) 构造函数开头**（现有 `InitializeComponent();` 之前）按模式设置窗口风格：

```csharp
public KeycapOverlayWindow()
{
    // 允许透明的窗口风格必须在句柄创建前定死（AllowsTransparency 运行期不可改）；
    // XAML 已移除这两个属性，这里按模式设置后 InitializeComponent 不会覆盖。
    WindowStyle = WindowStyle.None;   // AllowsTransparency 的前置约束，先显式设好
    if (RecordingMode)
    {
        Background = new SolidColorBrush(RecordingBackdropColor);
    }
    else
    {
        AllowsTransparency = true;
        Background = Brushes.Transparent;
    }
    InitializeComponent();
    // ……以下全部保持现状……
}
```

**(c) 穿透样式按模式去掉 TOOLWINDOW**：OBS 的窗口选择列表会过滤 WS_EX_TOOLWINDOW
（各版本行为不一），录制模式窗口必须出现在列表里才能被选为捕获源。代价仅是 Alt-Tab
多一项（ShowInTaskbar=False 保证任务栏干净）。改 `ApplyClickThrough` / `MakeClickThrough`
（KeycapOverlayWindow.xaml.cs:490-508）加一个参数：

```csharp
private void ApplyClickThrough()
{
    var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
    if (hwnd == IntPtr.Zero) return;
    MakeClickThrough(hwnd, toolWindow: !_recordingMode);   // 录制模式不隐藏于 OBS 窗口列表
}

/// <summary>给任意 HWND 加穿透 + 不抢焦点（+ 可选工具窗口样式）。
/// toolWindow=false 供录制模式：OBS 窗口列表过滤 WS_EX_TOOLWINDOW，录制窗口必须可选。</summary>
private static void MakeClickThrough(IntPtr hwnd, bool toolWindow)
{
    if (hwnd == IntPtr.Zero) return;
    var ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
    var flags = NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_NOACTIVATE;
    if (toolWindow) flags |= NativeMethods.WS_EX_TOOLWINDOW;
    _ = NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, ex | flags);
    NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
        NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER
        | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);
}
```

调用点两处：`ApplyClickThrough`（如上）与 Pacer 的 `SourceInitialized`（KeycapOverlayWindow.xaml.cs:603）
——Pacer 保持 `toolWindow: true`（1×1 心跳窗口，永远不需要被捕获）。

**(d) 干净销毁 + 关停后早退**：重建窗口需要销毁旧窗。旧窗 Close 后，已排队但未执行的封送调用
（DispatchSnapshot / OnTargetFired 等）仍可能落到旧实例上，`Show()` 一个已 Close 的窗口会抛异常。
加 `_shutdown` 关停标记 + 销毁方法；公共入口早退：

```csharp
private bool _shutdown;   // 销毁关停：此后所有公共入口早退（已排队的封送调用落到旧窗上不再渲染/Show）

/// <summary>悬浮窗是否处于调整模式（重建前需先退出，见 KeyVisualizerService.RebuildOverlayForModeSwitch）。</summary>
public bool IsAdjusting => _adjustWindow is not null;

/// <summary>不保存直接退出调整模式（重建前清场用；与 Esc 取消同语义）。</summary>
public void CancelAdjust() => EndAdjust();

/// <summary>销毁悬浮窗（模式切换重建时由 KeyVisualizerService 在悬浮窗线程调用）：
/// 停常驻计时器与激励器、关调整窗、Close 本窗。Close 后 Dispatcher 继续运行
/// （新窗口在同一线程重建），线程不退出。</summary>
public void ShutdownOverlay()
{
    _shutdown = true;
    _activityTimer.Stop();
    _applyTimer.Stop();
    DestroyPacer();
    _adjustWindow?.Close();
    _adjustWindow = null;
    Close();
}
```

在 `UpdateKeys` / `HoldPulse` / `SetReminderVisible` / `ShowOverlay` / `BeginAdjust` / `RefreshAppIcon`
入口各加一行 `if (_shutdown) return;`。

### 3. KeyVisualizerService.cs — 模式设置 + 悬浮窗线程销毁重建

新增字段与方法（放在 `SetOpacity` 附近，同款注释风格）：

```csharp
/// <summary>键帽录制模式（VM 调用）：AllowsTransparency 运行期不可改，切换 = 悬浮窗线程
/// 上销毁重建窗口。窗口未创建时只写静态量（AttachOverlay 构造时消费）。</summary>
public void SetRecordingMode(bool enabled)
{
    KeycapOverlayWindow.RecordingMode = enabled;
    if (_dispatcher is null) return;   // 悬浮窗尚未创建：启动路径按静态量构造
    _dispatcher.BeginInvoke(RebuildOverlayForModeSwitch);
}

/// <summary>悬浮窗线程：按当前模式销毁重建悬浮窗。位置 / 透明度 / 提醒键帽期望态 /
/// 当前按住快照全部重放，切换对用户无感。重建失败（极端）= 键帽停显，主功能不受影响。</summary>
private void RebuildOverlayForModeSwitch()
{
    var old = _overlay;
    if (old is null) return;
    try
    {
        if (old.IsAdjusting) old.CancelAdjust();   // 调整模式中：先退出（不保存位置）
        var position = old.PositionOverride;       // 保存位置（null = 默认右下角，新窗同样默认）
        var opacityPercent = old.Opacity * 100;    // 窗口整体透明度随迁

        old.ShutdownOverlay();

        var overlay = new KeycapOverlayWindow();   // 构造读静态 RecordingMode
        overlay.Dispatcher.UnhandledException += (_, e) =>
        {
            Logger.Error("键帽悬浮窗线程未处理异常。", e.Exception);
            e.Handled = true;
        };
        _overlay = overlay;                        // 同线程同 Dispatcher，_dispatcher 不变
        if (position is { } pos) overlay.SetSavedPosition(pos.X, pos.Y);
        overlay.SetOpacity(opacityPercent);
        overlay.PreloadHandle();                   // 仍在应用前台时建立置顶/穿透样式
        _reminderShown = false;                    // 提醒键帽期望态重放到新窗口
        RefreshReminder();
        DispatchSnapshot(bumpLabel: null);         // 当前按住键位立即重绘（连击视觉归位）

        Logger.Info(KeycapOverlayWindow.RecordingMode
            ? "键帽悬浮层已切换为录制模式（不透明底条，OBS 窗口捕获可选 KeycapOverlay）。"
            : "键帽悬浮层已切换为常规模式（透明悬浮）。");
    }
    catch (Exception ex)
    {
        Logger.Error("键帽悬浮层模式切换重建失败（键帽停显，重启应用恢复）。", ex);
    }
}
```

说明：
- 悬浮窗线程与 Dispatcher **不重建**（窗口 Close 不终止 Dispatcher.Run），`_dispatcher` 引用不变；
  排队中的封送闭包引用旧 `_overlay` 也安全（`_shutdown` 早退兜底）。
- 状态提醒相关字段（`_reminderShown` / `_reminderResumeTimer` / `_firing`）在服务里，不受换窗影响；
  配色 / 图标走静态量（`_scheme` / `_appIconUri`），新窗自动继承。
- 启动时序与 `SetOpacity` 一致：VM 构造先于 `AttachOverlay`（MainWindow.xaml.cs:293），
  此时 `_dispatcher` 为 null，只写静态量即可，首次构造即按模式创建。

### 4. AppConfig.cs — 新字段 + 版本 17 → 18

- 版本注释（AppConfig.cs:294-299）追加 `v18：键帽录制模式；` 并 `CurrentVersion = 18`。
- 新字段（放 `VisualizerOpacity` 附近）：

```csharp
/// <summary>键帽录制模式（底栏按钮）：开启后键帽悬浮层为不透明深色底条，
/// 供 OBS 窗口捕获干净画面；关闭为透明悬浮。默认关闭。</summary>
public bool KeycapRecordingMode { get; set; }
```

- `Clone()`（AppConfig.cs:459-490）补 `KeycapRecordingMode = KeycapRecordingMode,`。
- **无需迁移逻辑**：System.Text.Json 反序列化缺省字段即默认 false，ConfigService 现有
  版本号赋值流程（ConfigService.cs:73/127）自动覆盖。

### 5. MainViewModel.cs — 启动还原 + 命令 + 属性

- **启动接线**（MainViewModel.cs:217 `_visualizer.SetReminderEnabled(...)` 之后）：

```csharp
_visualizer.SetRecordingMode(_config.KeycapRecordingMode);   // 键帽录制模式（悬浮窗未创建时只写静态量）
```

- **命令注册**（MainViewModel.cs:252 附近，模仿 `ToggleStatusReminderCommand`）：

```csharp
ToggleKeycapRecordingCommand = RelayCommand.Create(() => KeycapRecordingMode = !KeycapRecordingMode);
```

同区域声明 `public ICommand ToggleKeycapRecordingCommand { get; }`。

- **属性**（模仿 `StatusReminderEnabled`，MainViewModel.cs:1537-1551，同款持久化与日志）：

```csharp
/// <summary>键帽录制模式（底栏按钮）：开启后键帽悬浮层重建为不透明深色底条，
/// OBS「窗口捕获」选择 KeycapOverlay 即可把键帽画面干净叠加进录制/直播；
/// 关闭恢复透明悬浮。位置 / 透明度 / 状态提醒 / 可视化过滤不受影响。</summary>
public bool KeycapRecordingMode
{
    get => _config.KeycapRecordingMode;
    set
    {
        if (_config.KeycapRecordingMode == value) return;
        _config.KeycapRecordingMode = value;
        _visualizer.SetRecordingMode(value);
        SaveConfig();
        AddLog(value
            ? "键帽录制模式已开启：悬浮层切换为不透明底条，OBS「窗口捕获」选择 KeycapOverlay 窗口即可干净录入键帽。"
            : "键帽录制模式已关闭：悬浮层恢复透明悬浮。");
        OnPropertyChanged(nameof(KeycapRecordingMode));   // 底栏按钮图标随开关切换金色 ↔ 常规色
    }
}
```

### 6. MainWindow.xaml — 底栏按钮（复刻状态提醒按钮形态）

- 图标资源（MainWindow.xaml:36 附近的 Geometry 资源区，lucide video）：

```xml
<!-- lucide video（ISC 许可，lucide-static v0.544；圆角机身 + 右侧镜头）。键帽录制模式按钮图标 -->
<Geometry x:Key="VideoIconPath">M4,6 L14,6 A2,2 0 0 1 16,8 L16,16 A2,2 0 0 1 14,18 L4,18 A2,2 0 0 1 2,16 L2,8 A2,2 0 0 1 4,6 Z M16,9.2 L21,6.2 A0.5,0.5 0 0 1 21.75,6.63 L21.75,17.37 A0.5,0.5 0 0 1 21,17.8 L16,14.8</Geometry>
```

- 按钮（插在 `StatusReminderButton` 之后、夜间模式按钮之前，MainWindow.xaml:577 后；
  结构逐行复刻 StatusReminderButton，仅换名 / 换图标 / 换绑定）：

```xml
<!-- 键帽录制模式按钮（lucide video）：开启后键帽悬浮层重建为不透明深色底条，
     OBS「窗口捕获」选择 KeycapOverlay 即可干净捕获键帽并叠加进录制画面；
     激活态 = 金色墨迹（与状态提醒钮同源），未激活 = 默认文字色；默认关闭。
     悬停说明不在 Tooltip：经 MouseEnter 显示于左下注释区（OnBottomBarCommentEnter） -->
<Button x:Name="RecordingModeButton" Width="26" Height="26" Padding="0"
        Style="{StaticResource IconButtonStyle}"
        Command="{Binding ToggleKeycapRecordingCommand}"
        MouseEnter="OnBottomBarCommentEnter" MouseLeave="OnCommentMouseLeave"
        Margin="0,0,6,0">
    <Grid Background="Transparent">
        <Path StrokeThickness="1.8" Fill="Transparent"
              StrokeStartLineCap="Round" StrokeEndLineCap="Round"
              StrokeLineJoin="Round"
              Width="15" Height="15" Stretch="Uniform">
            <Path.Style>
                <Style TargetType="Path">
                    <Setter Property="Data" Value="{StaticResource VideoIconPath}"/>
                    <Setter Property="Stroke" Value="{DynamicResource ThemeFg}"/>
                    <Style.Triggers>
                        <DataTrigger Binding="{Binding KeycapRecordingMode}" Value="True">
                            <Setter Property="Stroke" Value="{DynamicResource AccentGold}"/>
                        </DataTrigger>
                    </Style.Triggers>
                </Style>
            </Path.Style>
        </Path>
    </Grid>
</Button>
```

### 7. MainWindow.xaml.cs — 悬停注释

`BottomBarComments` 字典（MainWindow.xaml.cs:562）加一项：

```csharp
["RecordingModeButton"] = "键帽录制模式：开启后键帽悬浮层切换为不透明深色底条，OBS「窗口捕获」选择 KeycapOverlay 窗口即可把键帽画面干净地叠加进录制/直播；关闭恢复透明悬浮。",
```

### 8. 用户指南（新建 docs/obs-recording.md）

内容即本方案文末"使用指南"一节（游戏无边框 + OBS 三种捕获方案 + 键帽叠加步骤 + 输出参数建议）。
README 的功能列表可各加一行（可选）。

### 9. 发版收尾（仓库惯例）

- csproj：`<Version>1.8.5</Version>` → `1.8.6`，`<FileVersion>1.8.5.0</FileVersion>` → `1.8.6.0`。
- CHANGELOG.md 顶部新增 `## v1.8.6 — 日期` 段（格式仿 v1.8.5，标题建议
  `### 新增 · 键帽录制模式（OBS 干净捕获键帽）`），附 docs/obs-recording.md 链接。
- docs/release-notes/v1.8.6-release-notes.md（仿现有同目录文件）。

---

## 边界情况（实现时自检）

1. **切换时正在连发 / 按住键**：旧窗销毁丢失瞬态键帽，`DispatchSnapshot(null)` 重绘当前按住键，
   脉冲键帽随下一发脉冲重现——一次视觉复位，可接受。
2. **切换时正在调整模式**：先 `CancelAdjust()`（不保存位置）再重建。
3. **状态提醒开启 + 录制模式**：提醒键帽常驻 → 深色条常驻屏幕（也是录制画面的一部分，WYSIWYG）。
   不想要常驻条就关状态提醒。**这一点写进用户指南**。
4. **透明度滑块**：作用于窗口整体（含底条），录制模式同样生效；捕获画面随窗口透明度变化，属预期。
5. **`--no-overlay` 诊断开关**：无悬浮窗，`SetRecordingMode` 只写静态量，无副作用。
6. **重建失败**：try/catch 记日志，键帽停显但主线程 / 钩子 / 连发不受影响（独立线程隔离的既有价值）。
7. **Pacer**：录制模式窗口同样受益于渲染时钟激励器，逻辑不动；Pacer 自身仍是 1×1 工具窗，不会被捕获。
8. **游戏前台时切换**：销毁/重建的窗口管理器调用在悬浮窗线程执行，最坏情况只是键帽视觉短暂停滞
   （既有取舍，见 KeyVisualizerService.cs:106-118 注释）。

## 验收清单

1. 构建通过；启动后默认关闭：悬浮层观感与 v1.8.5 完全一致（透明、悬浮、点击穿透）。
2. 底栏按钮切换：开 = 屏幕出现深色底条 + 键帽照常显示；关 = 恢复透明悬浮。位置、透明度、
   状态提醒、键帽配色在反复切换后保持。
3. OBS（Win10 1903+）：添加「窗口捕获」→ 窗口列表中出现 KeycapOverlay（录制模式开启时）；
   捕获画面为不透明深色底 + 键帽，无黑边杂色；可缩放摆放到画布任意位置，与屏幕实际位置无关。
4. 游戏前台（剑网3 无边框）：连发 + 键帽脉冲 + 连击角标在录制模式正常渲染；主窗口无卡死。
5. 配置持久化：切换后重启应用，模式还原；导出/导入配置携带该字段。
6. 回归：调整模式拖拽保存位置两模式各验一次（位置语义不变）；`--no-overlay` 正常。

## 明确不做

- 不做色度键 / 绿幕底（抗锯齿杂边不可靠）。
- 不做"镜像双窗"（透明悬浮 + 捕获副本并存）：双倍呈现成本、位置漂移，收益不成比例。
- 不改游戏捕获方案本身（问题一是 OBS/游戏侧配置，代码无需改动）。
- 底条色不随键帽配色方案联动（固定中性深色；如需联动是后续小改进）。

---

## 使用指南（随实现落到 docs/obs-recording.md）

### 问题一：游戏画面 1:1 无黑边（不改 OBS 之外的任何东西）

按优先级三选一：

1. **游戏内显示设置切"无边框窗口"**（推荐）：客户区铺满整屏、无标题栏，OBS 画布分辨率设成
   游戏分辨率即 1:1。Win10/11 全屏优化下与独占全屏性能几乎无差。
2. **OBS 用「游戏捕获」源**（hook 模式）：直接抓游戏渲染帧，无视窗口边框，开销还最低；
   游戏保持窗口最大化也可以。
3. **兜底**：保持现状「窗口捕获」+ 对源按住 Alt 拖边裁掉标题栏。

**不要用独占全屏**：窗口捕获（WGC）会黑屏。

OBS 输出建议：画布 = 输出 = 游戏分辨率，60fps，编码器 NVENC/AMF，码率控制 CQP 18–22
（或 CBR 15000–40000 kbps）。

### 问题二：键帽叠加进录制画面

1. 衍天高手底栏点亮「录制模式」按钮（摄像机图标，激活为金色）：键帽悬浮层变为不透明深色底条。
   （状态提醒开着的话底条会常驻——不想要就先关状态提醒。）
2. OBS 添加第二个源：「窗口捕获」→ 窗口选 **KeycapOverlay** → 方法 Auto 即可。
3. 把该源缩放（建议 50%~75%）并拖到画布任意位置（与它在屏幕上的实际位置无关），
   叠在游戏源之上——单文件直出成片，无需后期。
4. 录完把「录制模式」关掉，悬浮层恢复透明悬浮。
