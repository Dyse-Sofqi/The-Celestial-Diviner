using System.Drawing;
using System.IO;
using System.Windows;
using TheCelestialDiviner.Helpers;
using TheCelestialDiviner.Models;
using TheCelestialDiviner.Services;
using TheCelestialDiviner.ViewModels;
using TheCelestialDiviner.Views;
using Forms = System.Windows.Forms;
using WinApp = System.Windows.Application;

namespace TheCelestialDiviner;

/// <summary>应用入口：服务组装、主题注入、托盘、全局异常兜底、退出时序。</summary>
public partial class App : Application
{
    private ConfigService _configService = null!;
    private InputHookService _hookService = null!;
    private TaskSchedulerService _scheduler = null!;
    private TimerResolutionService _timerResolution = null!;
    private MainViewModel _vm = null!;
    private MainWindow _mainWindow = null!;
    private Forms.NotifyIcon? _trayIcon;

    /// <summary>是否正在退出（区分“关窗到托盘”与“真正退出”）。</summary>
    public bool IsExiting { get; private set; }

    /// <summary>服务定位（对话框需要录制器 / 冲突校验时使用）。</summary>
    public static App Instance => (App)Current;

    /// <summary>主视图模型引用（对话框共用）。</summary>
    public MainViewModel ViewModel => _vm;

    /// <summary>钩子服务引用（录制器事件源）。</summary>
    public InputHookService Hooks => _hookService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 单实例保护（必须早于一切初始化）：多实例会让两个进程互删 DD 驱动服务，
        // 使 CreateServiceW 返回 1072，进而触发 DD 自己的「DD install error.」模态框；
        // 并造成按键重复注入、config.json 互相覆盖。详见 SingleInstanceGuard 注释。
        if (!SingleInstanceGuard.TryAcquire())
        {
            // 窗口已在前台就不再打扰；激活失败（如窗口在别的虚拟桌面）才提示一句，
            // 避免用户以为程序没启动而反复双击。
            if (!SingleInstanceGuard.ActivateExisting())
            {
                MessageBox.Show("衍天高手已在运行，请勿重复启动。", "衍天高手",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            Shutdown();
            return;
        }

        // 关机 / 注销：系统会直接结束进程，来不及走 ExitApp。这里撤掉崩溃熔断标记，
        // 否则每次正常关机都会被下次启动误判成"上次异常退出"，白白退回普通模式。
        // 直接删标记而不走 DdDriverService.Shutdown()：后者要拿驱动锁，关机时不该被它拖住。
        SessionEnding += (_, _) =>
        {
            try { DdCrashGuard.MarkCleanExit(); }
            catch (Exception ex) { Logger.Warn($"关机时序清理 DD 会话标记失败：{ex.Message}"); }
        };

        // 渲染模式：**不要**在进程级强制 SoftwareOnly。
        // 键帽悬浮层是 AllowsTransparency=True 的分层窗口，WPF 对它本身就是"离屏位图 +
        // UpdateLayeredWindow 上传"，与进程渲染模式无关——那条 0.5~3.9s 的 GPU 排队问题
        // 由它自己的透明窗口路径解决。
        // 而进程级 SoftwareOnly 会把**主窗口**也拖进 CPU 光栅化：1000×620 的窗口每次拖动 /
        // 重绘都在 CPU 上合成，表现就是"拖动发涩、界面不跟手"（用户实测反馈）。
        // 取舍：主窗口回到 GPU 渲染（流畅）；代价是游戏前台把 GPU 队列打满时主窗口会排队——
        // 但那时用户并不在操作界面（连发跑起来后不需要看窗口），优先级明确。
        // （历史：曾在此处设 ProcessRenderMode = SoftwareOnly，现已移除。）

        // 诊断开关（排查"拖动发涩"用，正常运行不要带）：
        //   --no-mouse-hook → 不安装 WH_MOUSE_LL（鼠标类方案不可用）
        //   --no-overlay    → 不创建键帽悬浮层（含渲染时钟激励器）
        //   --no-dd         → 不加载 DD 驱动（判断是否 DD 内核驱动引入拖动延迟）
        //   --no-intercept  → 不加载 Interception 驱动（同上，针对第二条注入路线）
        var args = Environment.GetCommandLineArgs();
        if (args.Any(a => string.Equals(a, "--no-mouse-hook", StringComparison.OrdinalIgnoreCase)))
            InputHookService.SuppressMouseHook = true;
        // 注：曾临时默认置 SuppressMouseHook=true 验证"拖动发涩是否由 WH_MOUSE_LL 引入"，
        // 结论：是。鼠标检测已改为 Raw Input（见 InputHookService），不再需要这个开关。
        if (args.Any(a => string.Equals(a, "--no-overlay", StringComparison.OrdinalIgnoreCase)))
            KeyVisualizerService.SuppressOverlay = true;
        if (args.Any(a => string.Equals(a, "--no-dd", StringComparison.OrdinalIgnoreCase)))
            DdDriverService.SuppressDriver = true;
        if (args.Any(a => string.Equals(a, "--no-intercept", StringComparison.OrdinalIgnoreCase)))
            InterceptionDriverService.SuppressDriver = true;

        // 渲染能力（一次性记录）：Tier 0 = 没有硬件加速（WPF 全程软件光栅化，拖动必然发涩）。
        // 混合显卡 / 4K / 远程桌面等环境容易落到 Tier 0，排查拖动问题时先看这一行。
        Logger.Info($"WPF 渲染能力：Tier={System.Windows.Media.RenderCapability.Tier >> 16}，"
                    + $"ProcessRenderMode={System.Windows.Media.RenderOptions.ProcessRenderMode}"
                    + $"（Tier 0 = 无硬件加速；鼠标钩子={(!InputHookService.SuppressMouseHook ? "启用" : "已禁用")}）。");

        // 服务组装（手动 DI，规模小无需容器）。
        _configService = new ConfigService();
        _hookService = new InputHookService();
        _scheduler = new TaskSchedulerService();
        _timerResolution = new TimerResolutionService();
        _vm = new MainViewModel(_configService, _hookService, _scheduler, _timerResolution);

        // 主题资源注入：由 MainViewModel 构造时按配置应用（夜间模式 > 跟随系统），
        // 此处不再重复调用，避免覆盖用户手动选择的夜间/白天模式。

        _mainWindow = new MainWindow(_vm, this);
        _mainWindow.Show();

        // 全局兜底：后台线程异常记录日志（不打断主流程）。
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Logger.Error("未处理异常（AppDomain）。", args.ExceptionObject as Exception);
        System.Windows.Forms.Application.ThreadException += (_, args) =>
            Logger.Error("WinForms 线程异常（托盘）。", args.Exception);
    }

    /// <summary>主题字典（首次创建后复用同一实例，避免反复切换时堆积字典）。</summary>
    private ResourceDictionary? _themeDict;

    /// <summary>按深浅模式写入主题资源字典（键名与 App.xaml / 控件样式约定）。
    /// 三个强调色槽位取自当前档位的方案定制外观（主题预设 + 默认主题自定义覆盖），
    /// 界面各处的 {DynamicResource AccentPrimary / AccentGold} 随之整体换肤。</summary>
    public void ApplyTheme(bool isDark, ResolvedAppearance look)
    {
        var fg = isDark ? "#E6E6E6" : "#1F1F1F";
        var bg = isDark ? "#202020" : "#FAFAFA";
        var panel = isDark ? "#2A2A2A" : "#F0F0F0";
        var border = isDark ? "#3D3D3D" : "#CCCCCC";
        var buttonBg = isDark ? "#333333" : "#E8E8E8";

        // 首次创建并挂入合并字典；后续仅覆写键值（同字典实例变更会触发 DynamicResource 刷新）。
        if (_themeDict is null)
        {
            _themeDict = new ResourceDictionary();
            Resources.MergedDictionaries.Add(_themeDict);
        }
        var dict = _themeDict;
        dict["ThemeFg"] = ToBrush(fg);
        dict["ThemeBg"] = ToBrush(bg);
        dict["ThemePanel"] = ToBrush(panel);
        dict["ThemeBorder"] = ToBrush(border);
        dict["ThemeButtonBg"] = ToBrush(buttonBg);

        // 强调色（全局统一：主色 = 开关模式，警示色 = 按压模式；浅/深主题同值；
        // 色值唯一落点在 Helpers/Constants.cs，主题槽位与自定义覆盖在 Models/ProfileAppearance.cs）。
        dict["AccentPrimary"] = ToBrush(look.AccentPrimaryHex);
        dict["AccentGold"] = ToBrush(look.AccentGoldHex);
        // 方案列表圆形勾选框底色（日间 = 主色，夜间 = 警示色，勾选符号恒白）。
        dict["ThemeRoundCheckFill"] = ToBrush(isDark ? look.AccentGoldHex : look.AccentPrimaryHex);
        // 热键按钮 / 总开关键图块底色（近黑但不过分扎眼；浅/深主题分别取值）。
        dict["HotkeyBg"] = ToBrush(isDark ? "#303030" : "#1F1F1F");
        // 切换方案热键 / 独立关闭热键图块底色：中灰——与总开关键的近黑底作**灰度区分**，
        // 一眼看出该键位已被其它热键占用（比黑底浅两档，白字仍清晰）。
        dict["HotkeyAuxBg"] = ToBrush(isDark ? "#555555" : "#6E6E6E");
        // 全局热键设置按钮（底栏）：夜间沿用近黑底白字；日间复用普通按钮风格
        //（与 ThemeButtonBg / ThemeBorder / ThemeFg 同值；键鼠区总开关键图块的 HotkeyBg 高亮不受影响）。
        dict["HotkeySettingBg"] = ToBrush(isDark ? "#303030" : buttonBg);
        dict["HotkeySettingBorder"] = ToBrush(isDark ? "#333333" : border);
        dict["HotkeySettingFg"] = ToBrush(isDark ? "#FFFFFF" : fg);
        // 未选中模式标签底色（只比浅色背景深一点点 / 深色背景亮一点点，暗示可点击）。
        dict["TabIdleBg"] = ToBrush(isDark ? "#2B2B2B" : "#F0F0F0");
        // 模式分段条轨道容器底色（白天白轨道衬紫/金胶囊；夜间用比面板亮一档的灰，
        // 保留胶囊色钮与轨道的层次对比）。
        dict["SegmentTrackBg"] = ToBrush(isDark ? "#383838" : "#FFFFFF");

        // 键源控件主题（静态刷子 + 实例刷新）。
        KeySourceViewModel.UpdateTheme(isDark, look);
        // 键帽悬浮层主题（调整模式提示条配色）。
        KeycapOverlayWindow.SetThemeDark(isDark);
    }
    private static System.Windows.Media.SolidColorBrush ToBrush(string hex)
    {
        var brush = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    // ---------- 托盘 ----------
    // 双状态托盘图标（启动时各合成一次；状态切换只换引用，不重复分配 GDI 资源）。
    private Icon? _trayIconOn;
    private Icon? _trayIconOff;

    /// <summary>
    /// 托盘图标状态合成：以 app.ico 为底，开启态在右下角叠加纯绿圆点
    /// （无描边，直径 15px，圆心压到图标右下边缘，越界部分裁切）；关闭态用原图标。
    /// </summary>
    private static Icon ComposeTrayIcon(Icon baseIcon, bool enabled)
    {
        if (!enabled) return (Icon)baseIcon.Clone();

        const int size = 32;
        using var bmp = new System.Drawing.Bitmap(size, size);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.DrawIcon(baseIcon, new System.Drawing.Rectangle(0, 0, size, size));

        // 右下角纯绿圆点（单色实心，无描边）：直径 15px，圆心压到图标右下边缘（越界部分裁切）。
        using var dotBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0x00, 0xC8, 0x53));
        g.FillEllipse(dotBrush, 16, 16, 15, 15);

        return Icon.FromHandle(bmp.GetHicon());
    }

    /// <summary>创建托盘图标（16/32 双尺寸）与右键菜单。</summary>
    public void SetupTray(MainWindow window)
    {
        if (_trayIcon is not null) return;

        _trayIcon = new Forms.NotifyIcon
        {
            Text = TrayText(_vm.CurrentLook, enabled: false),
            Visible = true
        };
        BuildTrayIcons(_vm.CurrentLook);
        RefreshTrayIcon();

        // 全局开关状态 → 托盘图标绿点 / 提示文字联动（VM 事件在 UI 线程触发，直接订阅）。
        _vm.GlobalStateChanged += _ => RefreshTrayIcon();

        // 档位外观变更（切换方案 / 方案定制 / 导入配置）→ 托盘图标与提示文字随档位重建。
        _vm.AppearanceChanged += look =>
        {
            BuildTrayIcons(look);
            RefreshTrayIcon();
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示主界面", null, (_, _) => window.ShowFromTray());
        menu.Items.Add("总开关 开启/关闭", null, (_, _) => _vm.ToggleGlobalEnabled());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitFromTray());

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => window.ShowFromTray();
    }

    /// <summary>托盘提示文字（"{应用名} v{版本}"，开启态追加"已开启"；应用名随档位方案定制）。</summary>
    private string TrayText(ResolvedAppearance look, bool enabled) =>
        enabled
            ? $"{look.AppName} v{UpdateService.DisplayVersion} — 已开启"
            : $"{look.AppName} v{UpdateService.DisplayVersion}";

    /// <summary>按当前档位外观合成双状态托盘图标（启动时 / 切换档位或方案定制时各一次；
    /// 旧图标先释放，避免反复切换堆积 GDI 句柄）。</summary>
    private void BuildTrayIcons(ResolvedAppearance look)
    {
        try
        {
            // 从内嵌资源加载 32x32 图标；16x16 由系统自动缩放。
            var sfi = GetResourceStream(new Uri(look.AppIconUri));
            using var stream = sfi.Stream;
            using var baseIcon = new Icon(stream);

            var off = (Icon)baseIcon.Clone();
            var on = ComposeTrayIcon(baseIcon, enabled: true);
            _trayIconOff?.Dispose();
            _trayIconOn?.Dispose();
            _trayIconOff = off;
            _trayIconOn = on;
        }
        catch
        {
            // 图标缺失时使用系统默认（不阻塞启动）。
        }
    }

    /// <summary>按当前总开关状态刷新托盘图标与提示文字。</summary>
    private void RefreshTrayIcon()
    {
        if (_trayIcon is null) return;
        _trayIcon.Icon = _vm.GloballyEnabled ? _trayIconOn : _trayIconOff;
        _trayIcon.Text = TrayText(_vm.CurrentLook, _vm.GloballyEnabled);
    }

    /// <summary>关闭窗口 → 隐藏到托盘（不退出），首次给出提示日志。</summary>
    public bool RequestExitFromClose(MainWindow window)
    {
        window.HideToTray();
        _vm.AddLog("已最小化到托盘（托盘右键可退出）。");
        return false; // false = 拦截关闭，仅隐藏
    }

    /// <summary>托盘菜单退出：置位退出标记并执行退出时序。</summary>
    private void ExitFromTray()
    {
        IsExiting = true;
        ExitApp();
    }

    /// <summary>自更新重启：置位退出标记并走完整退出时序（自更新脚本等待进程退出后覆盖安装目录并重启）。</summary>
    public void ExitForUpdateRestart()
    {
        IsExiting = true;
        ExitApp();
    }

    /// <summary>
    /// 退出时序（按需求顺序）：停止所有连发任务 → 卸载钩子 → 恢复定时器分辨率 → 保存配置 → 移除托盘。
    /// </summary>
    public void ExitApp()
    {
        // 退出时静音提示语音，避免结束时还播报。
        _vm.SoundCueMuteForExit();
        try { _scheduler.StopAll(); } catch (Exception ex) { Logger.Error("停止任务失败。", ex); }
        try { _hookService.Dispose(); } catch (Exception ex) { Logger.Error("卸载钩子失败。", ex); }
        try { _timerResolution.Dispose(); } catch (Exception ex) { Logger.Error("恢复定时器失败。", ex); }
        try { _configService.Save(_vm.CurrentConfig); } catch (Exception ex) { Logger.Error("退出保存配置失败。", ex); }

        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        Shutdown();
    }

    /// <summary>UI 线程异常兜底：记录日志并阻止崩溃（可继续运行）。</summary>
    private bool _handlingDispatcherException;

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        // 渲染/布局管线内的异常若弹模态框会重入渲染循环，反复排版直到栈溢出
        // （dwrite.dll / TextShaping.dll 0xC00000FD 闪退）。首次异常弹窗提示，
        // 若异常源于布局/渲染阶段则仅记录日志并标记已处理，避免重入。
        if (_handlingDispatcherException ||
            e.Exception.StackTrace?.Contains("MeasureOverride") == true ||
            e.Exception.StackTrace?.Contains("UpdateLayout") == true ||
            e.Exception.StackTrace?.Contains("RenderMessageHandler") == true)
        {
            Logger.Error("布局/渲染管线异常（防重入，不弹窗）。", e.Exception);
            e.Handled = true;
            return;
        }
        _handlingDispatcherException = true;
        try
        {
            Logger.Error("UI 线程未处理异常。", e.Exception);
            Logger.Error("异常堆栈：" + (e.Exception.StackTrace ?? "(无堆栈)"));
            MessageBox.Show($"发生内部错误：{e.Exception.Message}", "衍天高手",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _handlingDispatcherException = false;
        }
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 兜底二次清理（正常路径走 ExitApp）：
        // 无论从哪里退出，都要撤掉 DD 崩溃熔断标记，否则下次启动会把这次正常退出
        // 当成"异常退出"而退回普通模式。Shutdown 内部幂等。
        try { DdDriverService.Shutdown(); }
        catch (Exception ex) { Logger.Warn($"退出时序清理 DD 会话标记失败：{ex.Message}"); }

        // Interception 上下文：释放设备句柄（不卸载驱动 —— 驱动是系统级类过滤驱动，
        // 卸载要单独走官方安装器 /uninstall，退出程序不该动它）。
        try { InterceptionDriverService.Shutdown(); }
        catch (Exception ex) { Logger.Warn($"退出时序释放 Interception 上下文失败：{ex.Message}"); }

        _trayIcon?.Dispose();
        // 释放单实例互斥体（与 TryAcquire 同为 UI 线程，可安全 Release）。
        SingleInstanceGuard.Release();
        base.OnExit(e);
    }
}
