using System.Collections.ObjectModel;
using System.Windows;
using TheCelestialDiviner.Helpers;
using TheCelestialDiviner.Models;
using TheCelestialDiviner.Services;
using TheCelestialDiviner.ViewModels;

namespace TheCelestialDiviner.Views;

/// <summary>
/// 方案设置对话框：绑定目标键表格、添加表单（录制 5 秒超时）、清空/取消/确认。
/// 确认时把编辑结果通过回调交回主 VM 落盘；取消不产生任何修改。
/// </summary>
public partial class SchemeDialog : Window
{
    private readonly InputSource _source;
    private readonly InputHookService _hooks;
    private readonly Action<InputSource, KeyScheme> _onConfirm;
    private readonly KeyRecorder _recorder;

    /// <summary>编辑中的目标键行集合（DataGrid 绑定源）。</summary>
    public ObservableCollection<TargetKeyViewModel> Targets { get; } = new();

    public SchemeDialog(Window owner, string sourceName, InputSource source,
        KeyScheme? current, InputHookService hooks, Action<InputSource, KeyScheme> onConfirm)
    {
        InitializeComponent();
        Owner = owner;
        Title = $"设置方案 — {sourceName}";
        _source = source;
        _hooks = hooks;
        _onConfirm = onConfirm;
        _recorder = new KeyRecorder(hooks);

        // 载入当前方案（深拷贝，取消不影响原配置）。
        if (current is not null)
            foreach (var t in current.Targets)
                Targets.Add(new TargetKeyViewModel(t.Clone()));

        TargetsGrid.ItemsSource = Targets;
        Closed += (_, _) => _recorder.Stop();
    }

    // ---------- 添加表单 ----------
    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        AddFormPanel.Visibility = Visibility.Visible;
        AddButton.Visibility = Visibility.Collapsed;
    }

    /// <summary>录制目标键：等待键盘或鼠标按下（5 秒超时）。</summary>
    private void OnRecordClick(object sender, RoutedEventArgs e)
    {
        RecordButton.IsEnabled = false;
        RecordedTextBox.Text = "请按下任意键…（5 秒内）";

        _recorder.Start(
            onCaptured: src =>
            {
                // 仅捕获键盘 / 鼠标按键（滚轮不作为目标键录入，可后续按需扩展）。
                if (src.Kind == InputKind.Mouse && src.Mouse is MouseInput.WheelUp or MouseInput.WheelDown)
                {
                    RecordedTextBox.Text = "滚轮不能作为目标键，请按其他键";
                    RecordButton.IsEnabled = true;
                    return;
                }

                _captured = src;
                RecordedTextBox.Text = InputNameMapper.GetSourceName(src);
                RecordButton.IsEnabled = true;
            },
            onTimeout: () =>
            {
                RecordedTextBox.Text = "录制超时，请重试";
                RecordButton.IsEnabled = true;
            });
    }

    private InputSource? _captured;

    /// <summary>确认添加：校验录入并追加一行。</summary>
    private void OnConfirmAddClick(object sender, RoutedEventArgs e)
    {
        if (_captured is null)
        {
            MessageBox.Show(this, "请先录制目标键。", "衍天高手",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(IntervalBox.Text, out var interval))
            interval = Constants.DefaultIntervalMs;

        var target = new TargetKeyConfig
        {
            Kind = _captured.Kind == InputKind.Keyboard ? TargetKind.Keyboard : TargetKind.Mouse,
            VirtualKey = _captured.VirtualKey,
            Extended = _captured.Extended,
            Mouse = _captured.Mouse,
            Mode = ModeCombo.SelectedIndex == 1 ? TriggerMode.Hold : TriggerMode.Toggle,
            IntervalMs = Compat.Clamp(interval, Constants.MinIntervalMs, Constants.MaxIntervalMs),
            // 实验功能：附带修饰键（Ctrl/Shift/Alt 勾选状态持久化到目标键配置）。
            ModCtrl = ModCtrlBox.IsChecked == true,
            ModShift = ModShiftBox.IsChecked == true,
            ModAlt = ModAltBox.IsChecked == true,
            Enabled = true
        };

        Targets.Add(new TargetKeyViewModel(target));
        _captured = null;
        RecordedTextBox.Text = "点击“录制”后按下键盘或鼠标按键";
        AddFormPanel.Visibility = Visibility.Collapsed;
        AddButton.Visibility = Visibility.Visible;
    }

    // ---------- 表格操作 ----------
    private void OnDeleteTargetClick(object sender, RoutedEventArgs e)
    {
        if ((sender as System.Windows.Controls.Button)?.DataContext is TargetKeyViewModel tvm)
            Targets.Remove(tvm);
    }

    private void OnClearAllClick(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(this, "确定清空该输入源的所有目标键吗？", "衍天高手",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes) Targets.Clear();
    }

    // ---------- 确认 / 取消 ----------
    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        var scheme = new KeyScheme { Enabled = true };
        foreach (var t in Targets)
            scheme.Targets.Add(t.Config.Clone());

        _onConfirm(_source, scheme);
        DialogResult = true;
    }
}
