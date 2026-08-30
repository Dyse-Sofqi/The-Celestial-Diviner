using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TheCelestialDiviner.Helpers;
using TheCelestialDiviner.Models;
using TheCelestialDiviner.Services;
using TheCelestialDiviner.ViewModels;

namespace TheCelestialDiviner.Views;

/// <summary>
/// 主窗口：状态栏/横幅/鼠标区/键盘区/底部栏/日志面板；
/// 钩子事件 Dispatcher 封送、Ctrl+点击全部停止、关闭到托盘、方案与全局开关对话框。
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

    // ---------- 初始化 ----------
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _app.SetupTray(this);

        // 钩子事件 → UI 封送（调度器本身线程安全，但日志与 UI 属性需封送）。
        _app.Hooks.SourceDown += src =>
            Dispatcher.BeginInvoke(() => _vm.HandleHookDown(src));
        _app.Hooks.SourceUp += src =>
            Dispatcher.BeginInvoke(() => _vm.HandleHookUp(src));

        // VM 弹窗请求。
        _vm.SchemeEditRequested += OpenSchemeDialog;
        _vm.GlobalSwitchSetupRequested += OpenGlobalSwitchDialog;
        _vm.ImportRequested += ImportConfig;
        _vm.ExportRequested += ExportConfig;

        // 运行时初始化（定时器/钩子安装 + 状态栏回填）。
        _vm.InitializeRuntime(IsElevated());
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

    // ---------- 对话框 ----------
    private void OpenSchemeDialog(KeySourceViewModel svm)
    {
        if (svm.IsSpacer || svm.Source is null) return;
        var owner = this;
        var current = App.Instance.ViewModel.CurrentConfig.Schemes
            .GetValueOrDefault(TaskSchedulerService.BuildSourceKey(svm.Source))?.Clone();

        var dialog = new SchemeDialog(owner, svm.Name, svm.Source, current,
            App.Instance.Hooks, (inputSource, scheme) =>
            {
                // 对话框确认回调：写回配置。
                _vm.CommitScheme(svm, scheme);
            });
        dialog.Owner = owner;
        dialog.ShowDialog();
    }

    private void OpenGlobalSwitchDialog()
    {
        var dialog = new GlobalSwitchDialog(this, _app.Hooks,
            App.Instance.ViewModel.CurrentConfig.GlobalSwitch.Clone(),
            _vm.IsGlobalKeyConflicting, key => _vm.SetGlobalSwitchKey(key));
        dialog.ShowDialog();
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
    /// <summary>“全部停止”按钮 Ctrl+点击防误触。</summary>
    private void OnStopAllButtonDown(object sender, MouseButtonEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            _vm.StopAllCommand.Execute(null);
        }
        else
        {
            _vm.AddLog("请按住 Ctrl 再点击“全部停止”（防误触）。");
        }
        e.Handled = true;
    }

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
