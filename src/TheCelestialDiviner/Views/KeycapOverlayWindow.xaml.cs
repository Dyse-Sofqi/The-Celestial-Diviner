using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TheCelestialDiviner.Helpers;

namespace TheCelestialDiviner.Views;

/// <summary>
/// 键帽可视化悬浮层：屏幕右下角，点击穿透 + 置顶 + 不抢焦点。
/// 键帽视觉复刻 keyviz 的 lowprofile 样式（双层结构：外层深色底座 + 内层浅色按压面，
/// 按下时内层下沉 0.25 倍字号、easeInOutExpo 缓动），连击时右上角显示 PressCount 正圆角标。
/// 显示语义：按下 → 显示并停在屏上；释放/停发 → 各键帽独立 1.2s 宽限后自行淡出移除
/// （逐键帽计时，发射中的键帽不影响其它键帽的淡出）。
/// </summary>
public sealed partial class KeycapOverlayWindow : Window
{
    // ---------- lowprofile 键帽视觉参数（精确复刻 keyviz lowprofile 默认值，size=32） ----------
    private const double TextSize = 32;                          // text.size
    private const double CapHeight = TextSize * 2.25;            // 键帽面 / 底座高度（72）
    private const double ContainerHeight = TextSize * 2.5;       // 容器高度（80，底部露 0.25×size 底座）
    private const double NormalMinWidth = TextSize * 2.25;       // 普通键最小宽（72）
    private const double ModifierMinWidth = TextSize * 2.5;      // 修饰键最小宽（80）
    private const double BorderRadius = 0.5 * TextSize * 1.25;   // border.radius × size×1.25（20）
    private const double BorderWidth = 2;                        // border.width（黑边）
    private const double PressOffset = TextSize * 0.25;          // 按下下沉 0.25×size（8，盖住底座）
    // 当前配色（默认 Pansy；由 MainViewModel 随配置加载 / 下拉切换调用 ApplyScheme）。
    private static KeycapScheme _scheme = KeycapSchemes.Resolve(null);

    // 从当前配色解析的颜色（键帽面 / 底座 / 文字 / 边框）。
    private static Color FaceColor => ParseHex(_scheme.Face);
    private static Color BaseColor => ParseHex(_scheme.Base);
    private static Color TextColor => ParseHex(_scheme.Text);
    // 边框：keyviz 点选配色预设时 setBorderStyle({ color: scheme.secondary })，即边框 = 方案底座色。
    private static Color BorderColor => ParseHex(_scheme.Border);

    private static Color ParseHex(string hex) =>
        (Color)ColorConverter.ConvertFromString(hex);
    private static readonly TimeSpan FadeIn = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan FadeOut = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan DismissAfter = TimeSpan.FromSeconds(1.2);

    /// <summary>按修饰键宽度渲染的标签（对应 keyviz event.isModifier() 的最小宽度加成）。</summary>
    private static readonly HashSet<string> ModifierLabels = new(StringComparer.OrdinalIgnoreCase)
        { "Ctrl", "Alt", "Shift", "Win", "Meta", "Cmd", "Option" };

    /// <summary>键帽活动巡检：每 250ms 检查各键帽空闲时长，到期独立淡出移除。</summary>
    private readonly DispatcherTimer _activityTimer;
    private readonly List<KeycapControl> _caps = new();
    private readonly Panel _host;

    /// <summary>调整模式窗口（独立普通窗口，从不设置穿透；拖拽用 DragMove 系统级可靠）。</summary>
    private Window? _adjustWindow;

    // ---------- 状态提醒键帽（常驻：总开关开启时显示应用图标，连发期间由服务隐藏） ----------

    /// <summary>状态提醒键帽的标识标签（与真实键名不冲突；图标加载失败时兜底显示）。</summary>
    private const string ReminderLabel = "启";

    /// <summary>状态提醒键帽（null = 未显示）。不参与快照协调与空闲淡出，恒居键帽序列末位。</summary>
    private KeycapControl? _reminderCap;

    /// <summary>提醒键帽期望显示态（调整模式临时清场后据此恢复）。</summary>
    private bool _reminderDesired;

    /// <summary>应用图标缓存（提醒键帽内容；懒加载，取 ico 中分辨率最大的帧）。</summary>
    private static ImageSource? _appIcon;

    // ---------- 鼠标键帽矢量图标（lucide mouse-left / mouse-right / mouse，ISC 许可；
    // mouse-left / mouse-right 为 0.573+ 新增图标） ----------
    // 键帽标识标签仍为“左键/中键/右键”（保证“同键触发连发”时脉冲与物理按键复用同帽），
    // 键帽面按标签查表渲染 lucide 线稿（描边 = 配色文字色）；滚轮 ↑滚/↓滚 与侧键 X1/X2 为纯文本改名。
    private static readonly Geometry MouseLeftIcon = Geometry.Parse(
        "M12,7.318 L12,10 M5,10 L5,15 A7,7 0 0,0 19,15 L19,9 C19,5.473 16.392,2.485 13,2 " +
        "M7,2 A2,2 0 1,1 7,6 A2,2 0 1,1 7,2 Z");
    private static readonly Geometry MouseRightIcon = Geometry.Parse(
        "M12,7.318 L12,10 M19,10 L19,15 A7,7 0 0,1 5,15 L5,9 C5,5.473 7.608,2.485 11,2 " +
        "M17,2 A2,2 0 1,1 17,6 A2,2 0 1,1 17,2 Z");
    private static readonly Geometry MouseMiddleIcon = Geometry.Parse(
        "M12,2 A7,7 0 0,1 19,9 L19,15 A7,7 0 0,1 12,22 A7,7 0 0,1 5,15 L5,9 A7,7 0 0,1 12,2 Z M12,6 L12,10");

    /// <summary>键帽标识标签 → lucide 图标（鼠标左/中/右键）。</summary>
    private static readonly Dictionary<string, Geometry> CapIcons = new()
    {
        ["左键"] = MouseLeftIcon,
        ["中键"] = MouseMiddleIcon,
        ["右键"] = MouseRightIcon,
    };

    private static ImageSource? LoadAppIcon()
    {
        if (_appIcon is not null) return _appIcon;
        try
        {
            var decoder = IconBitmapDecoder.Create(
                new Uri("pack://application:,,,/Resources/app.ico"),
                BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            _appIcon = decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
        }
        catch (Exception ex)
        {
            Logger.Warn($"应用图标加载失败，状态提醒键帽退化为文字显示：{ex.Message}");
        }
        return _appIcon;
    }

    /// <summary>
    /// 显示 / 隐藏状态提醒键帽（KeyVisualizerService 按总开关 + 连发状态驱动；UI 线程）。
    /// 常驻键帽：不参与快照协调与空闲淡出；隐藏 = 直接移除（连发即刻开始，随后的脉冲键帽
    /// 自然接管窗口显示）。
    /// </summary>
    public void SetReminderVisible(bool visible)
    {
        _reminderDesired = visible;
        if (_adjustWindow is not null) return;   // 调整模式中：EndAdjust 后按期望态恢复

        if (visible)
        {
            if (_reminderCap is not null) return;
            var cap = new KeycapControl(ReminderLabel, LoadAppIcon(), isReminder: true);
            _reminderCap = cap;
            _caps.Add(cap);
            _host.Children.Add(cap);   // 末位：窗口右对齐，右缘位置稳定不跳动
            PositionToSaved();
            ShowOverlay();
            return;
        }

        if (_reminderCap is null) return;
        var removed = _reminderCap;
        _reminderCap = null;
        _caps.Remove(removed);
        _host.Children.Remove(removed);
        if (_caps.Count == 0) Hide();
    }

    /// <summary>新增普通键帽（插入到提醒键帽之前：窗口右对齐，提醒键帽恒居末位位置稳定）。</summary>
    private void AddCap(KeycapControl cap)
    {
        _caps.Add(cap);
        if (_reminderCap is not null)
            _host.Children.Insert(_host.Children.Count - 1, cap);
        else
            _host.Children.Add(cap);
    }

    /// <summary>用户保存的悬浮窗位置（Left/Top；null = 默认屏幕右下角）。</summary>
    public Point? PositionOverride { get; set; }

    /// <summary>设置保存位置（配置加载 / 位置确定时调用；下一次显示即生效）。</summary>
    public void SetSavedPosition(double left, double top)
    {
        PositionOverride = new Point(left, top);
    }

    /// <summary>设置键帽悬浮层整体透明度（0~100 百分比；0 = 完全透明，100 = 完全不透明）。
    /// 只作用于窗口整体，键帽自身的淡出动画（KeycapControl.Opacity）不受影响。</summary>
    public void SetOpacity(double percent)
    {
        Opacity = Compat.Clamp(percent, 0, 100) / 100.0;
    }

    public KeycapOverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyClickThrough();

        _host = Root;
        // 右侧预留一个角标外凸量（0.25×size）：连击角标向右上外凸 1/4 边长，
        // 不预留会被窗口右缘裁掉（显示不全）。StackPanel 无 Padding，用 Margin 留白。
        Root.Margin = new Thickness(0, 0, TextSize * 0.25, 0);
        // 逐键帽独立淡出：每 250ms 巡检一次，各键帽释放/停发 1.2s 后独立淡出移除。
        // 修复：原窗口级淡出计时器被发射中的脉冲键持续重置，导致已停止的键位永远不消失。
        _activityTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _activityTimer.Tick += (_, _) => RetireIdleCaps();
        _activityTimer.Start();
    }

    /// <summary>应用配色方案（悬浮窗与键帽控件均为静态引用；重建当前显示以立即生效）。</summary>
    public static void ApplyScheme(KeycapScheme scheme)
    {
        _scheme = scheme;
    }

    private static bool _isDarkTheme;   // 当前主题深浅（调整模式提示条配色用）

    /// <summary>同步主题深浅（App.ApplyTheme 调用；调整模式提示条随主题换色）。</summary>
    public static void SetThemeDark(bool isDark) => _isDarkTheme = isDark;

    // ---------- 调整模式：独立普通窗口承载虚拟键帽 A（拖拽 DragMove，按钮原生可点） ----------

    /// <summary>
    /// 进入调整模式：弹出独立普通窗口（无穿透标志），内含应用图标虚拟键帽 + 圆角确定按钮 +
    /// 可视化区域虚线边框，窗口尺寸与主悬浮层一致（940×180），键帽渲染位置与真实键帽完全对齐，
    /// 因此确认时的窗口 Left/Top 即为保存位置。拖拽用 DragMove（系统级，绝对可靠）。
    /// NoResize：区域仅用于临时显示，不可调整大小 / 不可最大化（防 Aero Snap 破坏 1:1 对齐）；
    /// 拖拽结束时磁吸贴齐屏幕工作区边缘并强制钳回工作区内（区域不可拖出屏幕）。
    /// </summary>
    public void BeginAdjust(Action<double, double> onConfirm)
    {
        if (_adjustWindow is not null) return;   // 已在调整模式
        Hide();                                   // 主悬浮层隐藏，避免重叠混淆
        _host.Children.Clear();
        _caps.Clear();
        _reminderCap = null;                      // 提醒键帽一并清场，EndAdjust 后按期望态恢复

        // 虚拟键帽用应用图标内容：调整时看到的就是提醒键帽的真实观感。
        var cap = new KeycapControl(ReminderLabel, LoadAppIcon());
        var confirm = new Button
        {
            Content = "确定",
            FontSize = 12,
            Padding = new Thickness(14, 3, 14, 4),
            Cursor = System.Windows.Input.Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 6),
            Style = (Style)Application.Current.Resources["IconButtonStyle"]   // 应用圆角主题样式
        };
        var hint = new TextBlock
        {
            Text = "虚线框 = 键帽显示区域 · 拖动调整位置 · 确定保存 · Esc 取消",
            FontSize = 12,
            Foreground = new SolidColorBrush(_isDarkTheme
                ? Color.FromRgb(0xE6, 0xE6, 0xE6)
                : Color.FromRgb(0x1F, 0x1F, 0x1F)),
            Background = new SolidColorBrush(_isDarkTheme
                ? Color.FromArgb(0xCC, 0x20, 0x20, 0x20)
                : Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
            Padding = new Thickness(8, 4, 8, 4),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(8)
        };

        // 可视化区域虚线边框：标出悬浮窗完整范围（键帽实际渲染在右下角锚点），
        // 便于用户判断可显示区；Transparent 填充让整个范围均可拖拽。
        var bounds = new System.Windows.Shapes.Rectangle
        {
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            StrokeDashCap = PenLineCap.Round,
            RadiusX = 4,
            RadiusY = 4,
            Fill = Brushes.Transparent,
            Margin = new Thickness(2)   // 内缩 2px：1px 描边不被窗口边缘裁掉（高分屏取整防御）
        };
        bounds.SetResourceReference(System.Windows.Shapes.Rectangle.StrokeProperty, "AccentPrimary");

        // 右下角排列：确定按钮在键帽上方（与真实键帽渲染位置对齐；键帽无纵向边距，
        // StackPanel 也不留底边距 → 调整键帽与真实键帽同为窗口底边贴齐，逐像素对齐）。
        var right = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 8, 0)
        };
        right.Children.Add(confirm);
        right.Children.Add(cap);

        var root = new Grid { Width = Width, Height = Height };
        root.Children.Add(bounds);   // 最底层：虚线区域框
        root.Children.Add(hint);
        root.Children.Add(right);

        var win = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            // 分层窗口（AllowsTransparency）按 OS 层 Alpha 命中测试：纯透明（alpha=0）区域的
            // 鼠标消息会穿透到下层窗口，WPF 的 MouseLeftButtonDown 收不到 → 空白区域拖不动。
            // 垫一层 alpha=1 的近透明画刷（视觉不可见），使整个 940×180 区域都可按下拖动。
            Background = new SolidColorBrush(Color.FromArgb(0x01, 0, 0, 0)),
            SizeToContent = SizeToContent.WidthAndHeight,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            // 区域仅临时显示：禁止调整大小 / 最大化（含 Aero Snap），保证虚线框 = 真实区域 1:1。
            ResizeMode = ResizeMode.NoResize,
            Content = root
        };

        // 初始位置与主悬浮层完全一致（保存位置或默认右下角），确保键帽位置 1:1 对齐。
        if (PositionOverride is { } pos)
        {
            win.Left = pos.X;
            win.Top = pos.Y;
        }
        else
        {
            var work = SystemParameters.WorkArea;
            win.Left = work.Right - Width - 24;
            win.Top = work.Bottom - Height - 24;
        }

        // 确认：以调整窗口坐标（= 主悬浮层坐标）保存。
        confirm.Click += (_, _) =>
        {
            var left = win.Left;
            var top = win.Top;
            EndAdjust();
            onConfirm(left, top);
        };

        // 拖拽：系统级 DragMove（按下即整体拖动窗口；Button 会自行处理按下事件，
        // 不会触发 DragMove，因此按钮点击不受影响）。
        // 拖拽结束：磁吸贴齐工作区边缘 + 强制钳回工作区内（区域不可拖出屏幕）。
        win.MouseLeftButtonDown += (_, e) =>
        {
            try { win.DragMove(); } catch { /* 非拖拽状态时 DragMove 可能抛异常，忽略 */ }
            SnapIntoWorkArea(win, magnet: 24);
            e.Handled = true;
        };
        // Esc 取消：不保存直接退出。
        win.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape) EndAdjust();
        };

        _adjustWindow = win;
        win.Show();
        AssertTopmost(win);   // 调整窗口同样显式置顶，避免被游戏窗口盖住导致拖拽无从下手
        SnapIntoWorkArea(win, magnet: 0);   // 陈旧保存位置 / 显示器变更防御：打开时钳回工作区
    }

    /// <summary>退出调整模式（关闭调整窗口；不影响主悬浮层穿透配置）。</summary>
    private void EndAdjust()
    {
        _adjustWindow?.Close();
        _adjustWindow = null;
        // 调整模式清场时移除的提醒键帽按期望态恢复（普通键帽由下一次快照 / 脉冲重建）。
        if (_reminderDesired) SetReminderVisible(true);
    }

    /// <summary>定位到保存的位置（无保存位置时回退屏幕右下角）。</summary>
    private void PositionToSaved()
    {
        if (PositionOverride is { } pos)
        {
            Left = pos.X;
            Top = pos.Y;
            // 陈旧保存位置 / 显示器热插拔防御：钳回当前屏幕工作区（磁吸关闭，静默钳位）。
            SnapIntoWorkArea(this, magnet: 0);
        }
        else
        {
            PositionBottomRight();
        }
    }

    /// <summary>
    /// 磁吸 + 钳位：把窗口留在所在屏幕的工作区内。距任一工作区边缘 magnet 逻辑像素内
    /// → 贴齐该边（磁吸，仅调整模式拖拽结束时启用）；随后整窗强制钳回工作区。
    /// 工作区为物理像素，按窗口当前 DPI 换算成 DIP（PerMonitorV2）。
    /// 窗口尚未显示（无 PresentationSource）时跳过——显示后的首次定位会再次校正。
    /// </summary>
    private static void SnapIntoWorkArea(Window win, double magnet)
    {
        var source = PresentationSource.FromVisual(win);
        if (source?.CompositionTarget is null) return;
        // 预创建句柄（EnsureHandle）后首次定位可能早于布局完成：ActualWidth/Height 还是 0 / NaN，
        // 此时钳位会算出 NaN 抛异常。布局未就绪直接跳过，显示后的首次定位会再次校正。
        if (!(win.ActualWidth > 0) || !(win.ActualHeight > 0)) return;
        var scale = source.CompositionTarget.TransformToDevice.M11;
        if (scale <= 0) scale = 1;
        var work = System.Windows.Forms.Screen.FromHandle(
            new System.Windows.Interop.WindowInteropHelper(win).Handle).WorkingArea;
        double left = work.Left / scale, top = work.Top / scale;
        double right = work.Right / scale, bottom = work.Bottom / scale;

        if (magnet > 0)
        {
            // 磁吸：四边独立判定，接近哪条边贴齐哪条（拖拽结束后一次校正）。
            if (Math.Abs(win.Left - left) <= magnet) win.Left = left;
            if (Math.Abs(win.Top - top) <= magnet) win.Top = top;
            if (Math.Abs(win.Left + win.ActualWidth - right) <= magnet) win.Left = right - win.ActualWidth;
            if (Math.Abs(win.Top + win.ActualHeight - bottom) <= magnet) win.Top = bottom - win.ActualHeight;
        }
        // 兜底钳位：整窗（含宽高）必须完整留在工作区内。
        win.Left = Compat.Clamp(win.Left, left, Math.Max(left, right - win.ActualWidth));
        win.Top = Compat.Clamp(win.Top, top, Math.Max(top, bottom - win.ActualHeight));
    }

    /// <summary>WS_EX_TRANSPARENT + WS_EX_NOACTIVATE + WS_EX_TOOLWINDOW：点击与焦点行为完全穿透。
    /// 调整模式使用独立普通窗口，主悬浮层始终全穿透，无需动态切换。</summary>
    private void ApplyClickThrough()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        _ = NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
            ex | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);
        // SetWindowLong 只改样式位，不重算窗口框架缓存；补一次 SWP_FRAMECHANGED 让穿透 / 不抢焦点立即生效。
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER
            | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);
    }

    /// <summary>
    /// 启动时预创建 HWND（不显示）：让置顶 / 穿透样式在游戏抢前台之前就建立，
    /// 同时避免“第一次按键才创建分层窗口”带来的首键卡顿。句柄已存在时安全。
    /// </summary>
    public void PreloadHandle()
    {
        _ = new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
    }

    /// <summary>
    /// 重新声明置顶。WPF 只在窗口首次创建时应用 Topmost；悬浮层无键帽时频繁 Hide、按键时再 Show，
    /// 重新显示后可能掉到其他置顶窗口之下（现象：键帽不浮在最上层，切一下应用才出现）。
    /// SWP_NOACTIVATE 保证不抢焦点，SWP_SHOWWINDOW 兼顾首次显示。
    /// </summary>
    private static void AssertTopmost(Window win)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(win).Handle;
        if (hwnd == IntPtr.Zero) return;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE
            | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    /// <summary>显示悬浮层并重新声明置顶（所有显示路径统一走这里）。</summary>
    private void ShowOverlay()
    {
        Show();
        AssertTopmost(this);
    }

    /// <summary>
    /// 快照协调渲染：显示集 = 当前按住键位全量快照，逐键独立更新（多键同按互不顶替）。
    /// 新键入组 → 追加键帽（bumpLabel 递增连击角标）；快照中消失 → 抬起并进入 1.2s 独立宽限；
    /// 各键帽宽限到期后独立淡出移除（活动计时器巡检），发射中的脉冲键帽不受影响。
    /// </summary>
    public void UpdateKeys(IReadOnlyList<string> snapshot, string? bumpLabel)
    {
        // 调整模式中忽略按键显示：主悬浮层已隐藏，且调整结束后再恢复显示。
        if (_adjustWindow is not null) return;

        if (snapshot.Count == 0)
        {
            // 全部释放：抬起所有键帽，各自进入 1.2s 独立宽限（活动计时器到期淡出移除）。
            // 脉冲键帽一并抬起：空快照即无任何物理按键，发射未停时下一个脉冲会立即重新按下；
            // 若跳过，触发键与目标同键时（如"6"触发连发"6"），停止连发的最后一次按压
            // 落在 600ms 脉冲保护窗口内，释放被吞掉，键帽将卡死在按下态。
            foreach (var cap in _caps)
            {
                if (cap.IsReminder) continue;   // 提醒键帽不受快照管辖（无视可视化总开关常驻）
                cap.SetPressed(false);
                if (!cap.IsPulseActive) cap.MarkReleaseOnce();   // 脉冲键帽宽限锚定最后一次脉冲
            }
            return;
        }

        // 1) 已释放键帽不再立即移除：抬起视觉并标记释放，进入 1.2s 独立宽限
        //    （到期由活动计时器淡出移除；宽限锚定首次释放时刻，不被后续快照重置）。
        //    发射中的脉冲键帽（开关模式目标键）不受快照管辖：长按修饰键的自动重复
        //    每秒 ~30 次快照更新，否则会把脉冲键帽移除 → 下一脉冲又重建、角标清零，
        //    修饰键帽与脉冲键帽来回互抢而非并排显示。
        foreach (var cap in _caps)
        {
            if (cap.IsReminder || snapshot.Contains(cap.Label) || cap.IsPulseActive) continue;
            cap.SetPressed(false);
            cap.MarkReleaseOnce();
        }

        // 2) 追加新按下的键帽（顺序 = 快照顺序：修饰键在前，其余按按下先后）；
        //    宽限/淡出中的同名键帽（重按）取消淡出继续复用，连击计数保留。
        foreach (var label in snapshot)
        {
            var existing = _caps.FirstOrDefault(c => c.Label == label);
            if (existing is not null) { existing.CancelRetire(); continue; }
            AddCap(new KeycapControl(label));
        }

        // 3) 按压态收敛：快照内的键全部按住态（脉冲键帽的按压由 PulsePress 驱动）。
        foreach (var cap in _caps)
            if (snapshot.Contains(cap.Label))
                cap.SetPressed(true);

        // 4) 本次按下的键帽递增连击角标（连击计数随键帽销毁重置）。
        if (bumpLabel is not null)
            foreach (var cap in _caps.Where(c => c.Label == bumpLabel).ToList())
                cap.BumpPressCount();

        PositionToSaved();
        ShowOverlay();
    }

    /// <summary>
    /// 连发脉冲渲染：目标键每发射一次 → 键帽执行一次完整按下弹起（按压 HoldMs 后瞬间弹起）+ 连击角标递增。
    /// 不参与快照协调（目标键是注入脉冲，非物理按住）；停发后键帽停在弹起态，1.2s 独立淡出。
    /// </summary>
    public void HoldPulse(string label, int holdMs, int repeat = 1)
    {
        if (_adjustWindow is not null) return;
        if (repeat < 1) repeat = 1;
        if (holdMs < 10) holdMs = 10;

        var cap = _caps.FirstOrDefault(c => c.Label == label);
        if (cap is null)
        {
            cap = new KeycapControl(label);
            AddCap(cap);
        }
        cap.CancelRetire();   // 宽限/淡出中重新发射：取消淡出继续复用
        cap.PulsePress(holdMs);
        for (var i = 0; i < repeat; i++)
            cap.BumpPressCount();

        PositionToSaved();
        ShowOverlay();
    }

    /// <summary>锚定屏幕右下角（主屏工作区，边距 24px）。</summary>
    private void PositionBottomRight()
    {
        var work = SystemParameters.WorkArea;
        Left = work.Right - Width - 24;
        Top = work.Bottom - Height - 24;
    }

    /// <summary>
    /// 逐键帽独立淡出（活动计时器巡检）：物理按住中 / 发射中的键帽永不淘汰；
    /// 其余键帽距最后活动（按压刷新 / 首次释放 / 最后一次脉冲）超过 1.2s 后各自
    /// 300ms 淡出移除；全部移除后隐藏窗口。
    /// </summary>
    private void RetireIdleCaps()
    {
        if (_adjustWindow is not null || !IsVisible || _caps.Count == 0) return;
        var now = Environment.TickCount;
        var grace = (int)DismissAfter.TotalMilliseconds;
        foreach (var cap in _caps.ToList())
        {
            if (cap.IsReminder) continue;   // 提醒键帽常驻，永不淘汰
            if (!cap.IsRetirable(now, grace)) continue;
            cap.BeginRetire(() =>
            {
                _host.Children.Remove(cap);
                _caps.Remove(cap);
                if (_caps.Count == 0) Hide();
            });
        }
    }

    /// <summary>颜色加深（近似 keyviz 的 oklch darken：RGB 各通道 × (1 - amount)）。</summary>
    private static Color Darken(Color c, double amount) =>
        Color.FromRgb((byte)(c.R * (1 - amount)), (byte)(c.G * (1 - amount)), (byte)(c.B * (1 - amount)));

    // ---------- 键帽控件（lowprofile 双层结构：上层键帽面 + 下层锚底底座） ----------
    /// <summary>
    /// 单个键帽：底层深色底座（锚底，按下时被面盖住）+ 上层配色面键帽（边框 = 方案边框色）
    /// （未按下时底部露 0.25×size 深色边，按下时面下移盖住）+ 连击角标。
    /// </summary>
    private sealed class KeycapControl : Border
    {
        private readonly Border _face;       // 上层键帽面（按下时整体下移）
        private readonly Border? _badge;       // 连击角标（右上角，keyviz PressCount 正圆）
        private readonly TextBlock? _badgeText; // 角标内数字（圆内双轴居中）
        private readonly TranslateTransform _press;
        private readonly DispatcherTimer _pulsePop;  // 脉冲弹起定时器（HoldMs 后瞬间弹起）
        private bool _physHeld;              // 物理按住态：为真时脉冲不抢视觉
        private int _lastPulseTick;          // 最近一次脉冲时刻（Environment.TickCount，发射中判定用）
        private int _pressCount;
        private int _lastActivityTick = Environment.TickCount; // 最后活动（按压/脉冲/释放）时刻
        private bool _released;              // 已释放（进入 1.2s 独立宽限）
        private bool _retiring;              // 淡出动画进行中（防重复触发）

        public string Label { get; }

        /// <summary>状态提醒键帽（常驻）：不参与快照协调与空闲淡出（调用方负责跳过）。</summary>
        public bool IsReminder { get; }

        /// <summary>连发发射中（600ms 内有脉冲，覆盖最慢周期 200+100ms + 抖动）：
        /// 键帽由脉冲驱动，快照协调不得移除 / 抬起 / 按压。</summary>
        public bool IsPulseActive => Environment.TickCount - _lastPulseTick < 600;

        public KeycapControl(string label, ImageSource? icon = null, bool isReminder = false)
        {
            Label = label;
            IsReminder = isReminder;

            // 容器：高 2.5×size，普通键宽 2.25×size，修饰键 2.5×size。
            // 左 6 = 键帽水平间隔；单行布局，无纵向边距。
            Height = ContainerHeight;
            MinWidth = ModifierLabels.Contains(label) ? ModifierMinWidth : NormalMinWidth;
            Margin = new Thickness(6, 0, 0, 0);

            // 上层键帽面：方案面色 + 方案边框色圆角，垂直顶对齐；按下时下移 PressOffset 盖住底座。
            _press = new TranslateTransform();
            _pulsePop = new DispatcherTimer();
            _pulsePop.Tick += (_, _) =>
            {
                _pulsePop.Stop();
                _press.BeginAnimation(TranslateTransform.YProperty, null);
                _press.Y = 0;
            };
            // 键帽面内容：状态提醒键帽显示应用图标，鼠标左/中/右键显示 lucide 线稿图标，其余显示键名文字。
            _face = new Border
            {
                Height = CapHeight,
                VerticalAlignment = VerticalAlignment.Top,
                CornerRadius = new CornerRadius(BorderRadius),
                Background = new SolidColorBrush(FaceColor),
                BorderBrush = new SolidColorBrush(BorderColor),
                BorderThickness = new Thickness(BorderWidth),
                Padding = new Thickness(TextSize * 0.5, TextSize * 0.4, TextSize * 0.5, TextSize * 0.4),
                Child = icon is not null ? CreateIconFace(icon)
                        : CapIcons.TryGetValue(label, out var capIcon) ? CreateVectorFace(capIcon)
                        : new TextBlock
                        {
                            Text = label,
                            FontSize = TextSize,
                            FontWeight = FontWeights.Medium,
                            Foreground = new SolidColorBrush(TextColor),
                            VerticalAlignment = VerticalAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Center
                        },
                RenderTransform = _press
            };

            // 下层底座：深色圆角，锚定容器底部，未按下时底部露出 0.25×size。
            var baseLayer = new Border
            {
                Height = CapHeight,
                VerticalAlignment = VerticalAlignment.Bottom,
                CornerRadius = new CornerRadius(BorderRadius),
                Background = new SolidColorBrush(BaseColor),
                BorderBrush = new SolidColorBrush(BorderColor),
                BorderThickness = new Thickness(BorderWidth)
            };

            // 连击角标：复刻 keyviz PressCount —— 0.75×size（24px）正圆（圆角 50%），
            // 底色 = 配色文字色、数字 = 配色面色，显示裸数字，向右上角外凸 1/4 边长。
            // 宽度自适应（MinWidth 保证单数字正圆），计数上限 99 时两位数也不溢出圆边。
            _badgeText = new TextBlock
            {
                FontSize = TextSize * 0.4,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(FaceColor),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            _badge = new Border
            {
                MinWidth = TextSize * 0.75,
                Height = TextSize * 0.75,
                CornerRadius = new CornerRadius(TextSize * 0.375),   // 圆角 50% → 正圆
                Padding = new Thickness(2, 0, 2, 0),
                Background = new SolidColorBrush(TextColor),
                Child = _badgeText,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -TextSize * 0.25, -TextSize * 0.25, 0),
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };

            Child = new Grid();
            ((Grid)Child).Children.Add(baseLayer);
            ((Grid)Child).Children.Add(_face);
            ((Grid)Child).Children.Add(_badge);
        }

        /// <summary>图标键帽面内容（状态提醒键帽）：应用图标居中，高质量缩放。</summary>
        private static Image CreateIconFace(ImageSource icon)
        {
            var image = new Image
            {
                Source = icon,
                Width = TextSize * 1.25,
                Height = TextSize * 1.25,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            return image;
        }

        /// <summary>矢量图标键帽面（鼠标左/中/右键）：lucide 线稿描边渲染，
        /// 描边 = 配色文字色；StrokeThickness 2 与 lucide 24 视窗线宽一致（随 Stretch 等比放大）。</summary>
        private static System.Windows.Shapes.Path CreateVectorFace(Geometry icon) => new()
        {
            Data = icon,
            Stroke = new SolidColorBrush(TextColor),
            StrokeThickness = 2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Width = TextSize * 0.8,
            Height = TextSize * 0.8,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        /// <summary>按压态：按下时键帽面整体下移 0.25×size 盖住底座（easeInOutExpo 近似，50ms），松开瞬间弹起。</summary>
        public void SetPressed(bool pressed)
        {
            _physHeld = pressed;
            _pulsePop.Stop();   // 物理态变化取消未决的脉冲弹起，避免抢走按压视觉
            if (pressed)
            {
                _lastActivityTick = Environment.TickCount;
                _released = false;
                var anim = new DoubleAnimation(PressOffset, TimeSpan.FromMilliseconds(50))
                {
                    EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseInOut }
                };
                _press.BeginAnimation(TranslateTransform.YProperty, anim, HandoffBehavior.SnapshotAndReplace);
                return;
            }
            // 松开：清除动画并立即复位到基准位（淡出宽限由 MarkReleaseOnce 锚定）。
            _press.BeginAnimation(TranslateTransform.YProperty, null);
            _press.Y = 0;
        }

        /// <summary>首次释放时锚定宽限起点（后续快照不重置；宽限到期由巡检淡出移除）。</summary>
        public void MarkReleaseOnce()
        {
            if (_released) return;
            _released = true;
            _lastActivityTick = Environment.TickCount;
        }

        /// <summary>重新按下 / 重新发射：取消宽限与淡出，继续复用（连击计数保留）。</summary>
        public void CancelRetire()
        {
            _released = false;
            if (!_retiring) return;
            _retiring = false;
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
        }

        /// <summary>是否可淘汰：非物理按住、非发射中、且已过 1.2s 空闲宽限。
        /// 仅看最后活动时刻（按压 / 释放 / 脉冲），不依赖释放标记 —— 轮转开关自动停旧键时
        /// 可能没有后续快照事件，靠时间判定才能退出。</summary>
        public bool IsRetirable(int nowTick, int graceMs)
        {
            if (_physHeld || _retiring || IsPulseActive) return false;
            return nowTick - _lastActivityTick >= graceMs;
        }

        /// <summary>独立淡出（300ms）后回调移除（宿主从视觉树与集合中移除并按需隐藏窗口）。</summary>
        public void BeginRetire(Action onRemoved)
        {
            if (_retiring) return;
            _retiring = true;
            var anim = new DoubleAnimation(1, 0, FadeOut) { FillBehavior = FillBehavior.Stop };
            anim.Completed += (_, _) => onRemoved();
            BeginAnimation(OpacityProperty, anim);
        }

        /// <summary>
        /// 连发脉冲：一次完整按键视觉 —— 按下（min(50, holdMs) 缓动）→ holdMs 后瞬间弹起，
        /// 与真实注入节奏（按压 HoldMs + 间隔 IntervalMs）同步；新脉冲取消未决弹起重开一轮。
        /// </summary>
        public void PulsePress(int holdMs)
        {
            _lastPulseTick = Environment.TickCount;
            _lastActivityTick = _lastPulseTick;   // 脉冲即活动：宽限持续顺延，停发后才开始倒计时
            if (_physHeld) return;   // 物理按住中：键帽已处于按下态，脉冲只计角标

            _pulsePop.Stop();
            var anim = new DoubleAnimation(PressOffset, TimeSpan.FromMilliseconds(Math.Min(50, holdMs)))
            {
                EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseInOut }
            };
            _press.BeginAnimation(TranslateTransform.YProperty, anim, HandoffBehavior.SnapshotAndReplace);
            _pulsePop.Interval = TimeSpan.FromMilliseconds(holdMs);
            _pulsePop.Start();
        }

        /// <summary>连击角标计数上限（超过后停在 99，不再累加）。</summary>
        private const int MaxPressCount = 99;

        /// <summary>连击角标递增（≥2 次显示；角标弹跳一次；上限 99）。</summary>
        public void BumpPressCount()
        {
            _pressCount++;
            if (_pressCount > MaxPressCount) _pressCount = MaxPressCount;   // 上限 99
            if (_pressCount < 2 || _badge is null || _badgeText is null) return;
            _badgeText.Text = _pressCount.ToString();   // keyviz PressCount：裸数字
            if (_badge.Visibility != Visibility.Visible)
                _badge.Visibility = Visibility.Visible;
            // 角标弹跳（复刻 keyviz press-count 提示）。
            var scale = new ScaleTransform(1, 1, 0.5, 0.5);
            var pop = new DoubleAnimation(1.2, 1.0, TimeSpan.FromMilliseconds(150));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            _badge.LayoutTransform = scale;
        }
    }
}
