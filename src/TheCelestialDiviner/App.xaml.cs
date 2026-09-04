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

            // 服务组装（手动 DI，规模小无需容器）。
        _configService = new ConfigService();
        _hookService = new InputHookService();
        _scheduler = new TaskSchedulerService();
        _timerResolution = new TimerResolutionService();
        _vm = new MainViewModel(_configService, _hookService, _scheduler, _timerResolution);

        // 主题资源注入（跟随系统深浅模式）。
        ApplyTheme(ThemeHelper.IsDarkMode());

        _mainWindow = new MainWindow(_vm, this);
        _mainWindow.Show();

        // 全局兜底：后台线程异常记录日志（不打断主流程）。
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Logger.Error("未处理异常（AppDomain）。", args.ExceptionObject as Exception);
        System.Windows.Forms.Application.ThreadException += (_, args) =>
            Logger.Error("WinForms 线程异常（托盘）。", args.Exception);
    }

    /// <summary>按深浅模式写入主题资源字典（键名与 App.xaml / 控件样式约定）。</summary>
    public void ApplyTheme(bool isDark)
    {
        var fg = isDark ? "#E6E6E6" : "#1F1F1F";
        var bg = isDark ? "#202020" : "#FAFAFA";
        var panel = isDark ? "#2A2A2A" : "#F0F0F0";
        var border = isDark ? "#3D3D3D" : "#CCCCCC";
        var buttonBg = isDark ? "#333333" : "#E8E8E8";

        var dict = new ResourceDictionary();
        dict["ThemeFg"] = ToBrush(fg);
        dict["ThemeBg"] = ToBrush(bg);
        dict["ThemePanel"] = ToBrush(panel);
        dict["ThemeBorder"] = ToBrush(border);
        dict["ThemeButtonBg"] = ToBrush(buttonBg);

        // 强调色（全局统一：紫 = 主题主色，金 = 警示色，浅/深主题同值）。
        dict["AccentPrimary"] = ToBrush("#8A5CF5");
        dict["AccentGold"] = ToBrush("#D6A01D");
        // 热键按钮 / 总开关键图块底色（用户指定纯黑，两主题同值）。
        dict["HotkeyBg"] = ToBrush("#000000");
        // 未选中模式标签底色（比面板深一点点的灰，暗示可点击；比按钮底色稍浅区分层级）。
        dict["TabIdleBg"] = ToBrush(isDark ? "#3A3A3A" : "#DFDFDF");

        // 键源控件主题（静态刷子 + 实例刷新）。
        KeySourceViewModel.UpdateTheme(isDark);

        Resources.MergedDictionaries.Add(dict);
    }

    private static System.Windows.Media.SolidColorBrush ToBrush(string hex)
    {
        var brush = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    // ---------- 托盘 ----------
    /// <summary>创建托盘图标（16/32 双尺寸）与右键菜单。</summary>
    public void SetupTray(MainWindow window)
    {
        if (_trayIcon is not null) return;

        _trayIcon = new Forms.NotifyIcon
        {
            Text = "衍天高手 v1.5",
            Visible = true
        };
        try
        {
            // 从内嵌资源加载 32x32 图标；16x16 由系统自动缩放。
            var sfi = GetResourceStream(new Uri("pack://application:,,,/Resources/app.ico"));
            using var stream = sfi.Stream;
            _trayIcon.Icon = new Icon(stream, 32, 32);
        }
        catch
        {
            // 图标缺失时使用系统默认（不阻塞启动）。
        }

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示主界面", null, (_, _) => window.ShowFromTray());
        menu.Items.Add("总开关 开启/关闭", null, (_, _) => _vm.ToggleGlobalEnabled());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitFromTray());

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => window.ShowFromTray();
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
        // 兜底二次清理（正常路径走 ExitApp）。
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
