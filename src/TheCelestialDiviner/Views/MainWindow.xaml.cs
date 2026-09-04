using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using TheCelestialDiviner.Helpers;
using TheCelestialDiviner.Models;
using TheCelestialDiviner.Services;
using TheCelestialDiviner.ViewModels;

namespace TheCelestialDiviner.Views;

/// <summary>
/// 主窗口：状态栏/横幅/鼠标区/键盘区/底部栏/日志面板；
/// 钩子事件 Dispatcher 封送、音量滑块、关闭到托盘、方案与全局开关对话框。
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly App _app;

    public MainWindow(MainViewModel vm, App app)
    {
        InitializeComponent();
        _vm = vm;
        _app = app;
        DataContext = _vm;

        Loaded += OnLoaded;
    }

    /// <summary>底部栏方案档位按钮（①②③，选中态由 VM.ActiveProfile 驱动同步）。</summary>
    private RadioButton[] ProfileButtons => new[] { Profile1Button, Profile2Button, Profile3Button };

    /// <summary>方案列表间隔框：回车提交（提交绑定并清除焦点）。</summary>
    private void OnIntervalBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (sender is TextBox tb)
        {
            var expr = tb.GetBindingExpression(TextBox.TextProperty);
            expr?.UpdateSource();
            Keyboard.ClearFocus();
        }
        e.Handled = true;
    }

    // ---------- 初始化 ----------
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _app.SetupTray(this);

        // 键盘注入模式下拉框：加载时回填，切换时双向同步到 VM（热切换 + 自动保存）。
        KeyboardModeBox.SelectedIndex = _vm.KeyboardMode;
        KeyboardModeBox.SelectionChanged += (_, _) =>
            _vm.KeyboardMode = KeyboardModeBox.SelectedIndex;
        // VM 侧导入配置后同步回 UI。
        _vm.KeyboardModeChanged += mode => KeyboardModeBox.SelectedIndex = mode;

        // 音量滑块初值回填（绑定双向，拖动后写 VM 并防抖落盘）。
        SoundVolumeSlider.Value = _vm.SoundVolume;

        // 方案档位按钮：初值回填；后续随 VM.ActiveProfile 变更同步选中态。
        // 不用 IsChecked 双向绑定：同组互斥取消选中会破坏绑定（WPF 已知问题）。
        SyncProfileButtons(_vm.ActiveProfile);
        _vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.ActiveProfile))
                Dispatcher.BeginInvoke(() => SyncProfileButtons(_vm.ActiveProfile));
        };

        // 键位可视化悬浮层（钩子事件 → 悬浮层；UI 线程窗口创建后接线）。
        _vm.Visualizer.AttachOverlay();

        // 钩子事件 → UI 封送（调度器本身线程安全，但日志与 UI 属性需封送）。
        _app.Hooks.SourceDown += src =>
            Dispatcher.BeginInvoke(() => _vm.HandleHookDown(src));
        _app.Hooks.SourceUp += src =>
            Dispatcher.BeginInvoke(() => _vm.HandleHookUp(src));

        // VM 弹窗请求 + 键位录入选择态的鼠标命中测试。
        _vm.ImportRequested += ImportConfig;
        _vm.ExportRequested += ExportConfig;
        _vm.PickingMouseReceived += OnPickingMouseReceived;

        // 日志面板常驻展开：新日志追加后自动滚动到最新（首条）。
        _vm.Logs.CollectionChanged += (_, _) =>
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                if (_vm.Logs.Count > 0) LogList.ScrollIntoView(_vm.Logs[0]);
            });

        // 运行时初始化（定时器分辨率提升 + 钩子安装 + 权限状态回填）。
        _vm.InitializeRuntime(IsElevated());
    }

    /// <summary>方案档位按钮选中态同步（仅驱动选中项，不回写 VM）。</summary>
    private void SyncProfileButtons(int active)
    {
        for (var i = 0; i < ProfileButtons.Length; i++)
        {
            var target = i == active;
            if (ProfileButtons[i].IsChecked != target)
                ProfileButtons[i].IsChecked = target;
        }
    }

    /// <summary>当前进程是否以管理员身份运行。</summary>
    private static bool IsElevated()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(identity)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    // ---------- 键位录入选择态 ----------
    /// <summary>模式标签切换（XAML 解析期 IsChecked 即触发，此时 VM 未注入，需判空）。</summary>
    private void OnModeTabChecked(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        if (sender is RadioButton { Tag: string tag })
            _vm.SelectedMode = tag == "Hold" ? TriggerMode.Hold : TriggerMode.Toggle;
    }

    /// <summary>开关分区子标签切换（常规 / 轮转 / 双宏；同样需判空防 XAML 解析期触发）。</summary>
    private void OnSectionTabChecked(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        if (sender is RadioButton { Tag: string tag } &&
            Enum.TryParse<ToggleSection>(tag, out var section))
            _vm.SelectedSection = section;
    }

    /// <summary>
    /// 底部栏方案档位切换（①②③；XAML 解析期 IsChecked 即触发，需判空）。
    /// 同组 RadioButton 互斥，点击选中项只触发一次 Checked；VM 侧同步重建方案列表、
    /// 调度器任务并实时落盘。
    /// </summary>
    private void OnProfileChecked(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        if (sender is RadioButton { Tag: string tag } &&
            int.TryParse(tag, out var index))
            _vm.ActiveProfile = index;
    }

    /// <summary>
    /// 选择态中的鼠标按下：光标落在键鼠图块上 → 录入该键；
    /// 落在键鼠区外（含本窗口其余区域与其他窗口）→ 退出选择态。
    /// </summary>
    private void OnPickingMouseReceived(InputSource _)
    {
        var screen = System.Windows.Forms.Cursor.Position;
        var source = FindTileAt(KeyboardArea, screen) ?? FindTileAt(MouseArea, screen);
        if (source is null) _vm.CancelPicking();
        else _vm.CompletePick(source);
    }

    /// <summary>命中测试容器内指定屏幕坐标处的键鼠图块，返回其输入源（占位空白忽略）。</summary>
    private static InputSource? FindTileAt(UIElement container, System.Drawing.Point screen)
    {
        var rel = container.PointFromScreen(new Point(screen.X, screen.Y));
        if (container.InputHitTest(rel) is not DependencyObject hit) return null;

        var d = hit;
        while (d is not null)
        {
            if (d is FrameworkElement { DataContext: KeySourceViewModel tile }
                && !tile.IsSpacer && tile.Source is not null
                && !(tile.Source.Kind == InputKind.Keyboard && tile.Source.VirtualKey == 0))
            {
                return tile.Source;
            }
            d = System.Windows.Media.VisualTreeHelper.GetParent(d);
        }
        return null;
    }

    // ---------- 导入 / 导出 ----------
    private void ImportConfig()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "导入配置",
            Filter = "配置文件 (*.json)|*.json|所有文件 (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var json = System.IO.File.ReadAllText(dlg.FileName);
            _vm.ImportFromJson(json);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"读取文件失败：{ex.Message}", "衍天高手",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportConfig()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出配置",
            Filter = "配置文件 (*.json)|*.json",
            FileName = "TheCelestialDiviner-config.json"
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            System.IO.File.WriteAllText(dlg.FileName, _vm.ExportToJson());
            _vm.AddLog($"配置已导出到 {dlg.FileName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"写入文件失败：{ex.Message}", "衍天高手",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ---------- 交互 ----------
    // “全部停止”按钮已取消：其职责与全局总开关重叠，停止能力由总开关承担。

    // ---------- 托盘 ----------
    /// <summary>隐藏到托盘。</summary>
    public void HideToTray()
    {
        Hide();
        WindowState = WindowState.Minimized;
        ShowInTaskbar = false;
    }

    /// <summary>从托盘恢复显示。</summary>
    public void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        ShowInTaskbar = true;
        Activate();
    }

    /// <summary>关闭 → 隐藏到托盘（不退出）；托盘菜单退出才真正退出。</summary>
    private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_app.IsExiting)
        {
            e.Cancel = true;
            _app.RequestExitFromClose(this);
        }
    }
}
