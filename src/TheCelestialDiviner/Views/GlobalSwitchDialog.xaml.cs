using System.Windows;
using TheCelestialDiviner.Helpers;
using TheCelestialDiviner.Models;
using TheCelestialDiviner.Services;

namespace TheCelestialDiviner.Views;

/// <summary>
/// 全局开关键设置对话框：录制键盘热键 + 冲突校验（不能与已注册方案注册源相同）。
/// 确认回调把候选键交回主 VM 落盘；清除传入 null。
/// </summary>
public partial class GlobalSwitchDialog : Window
{
    private readonly InputHookService _hooks;
    private readonly Func<InputSource, bool> _conflictChecker;
    private readonly Action<InputSource?> _onConfirm;
    private readonly KeyRecorder _recorder;
    private InputSource? _captured;

    public GlobalSwitchDialog(Window owner, InputHookService hooks,
        GlobalSwitchConfig current, Func<InputSource, bool> conflictChecker,
        Action<InputSource?> onConfirm)
    {
        InitializeComponent();
        Owner = owner;
        _hooks = hooks;
        _conflictChecker = conflictChecker;
        _onConfirm = onConfirm;
        _recorder = new KeyRecorder(hooks);

        if (current.HasKey)
        {
            KeyBox.Text = InputNameMapper.GetKeyName(current.VirtualKey) +
                          (current.Extended ? " (扩展)" : "");
            _captured = new InputSource
            {
                Kind = InputKind.Keyboard,
                VirtualKey = current.VirtualKey,
                Extended = current.Extended
            };
        }

        Closed += (_, _) => _recorder.Stop();
    }

    /// <summary>录制热键（仅键盘；5 秒超时）。</summary>
    private void OnRecordClick(object sender, RoutedEventArgs e)
    {
        RecordButton.IsEnabled = false;
        KeyBox.Text = "请按下键盘按键…（5 秒内）";

        _recorder.Start(
            onCaptured: src =>
            {
                // 全局开关键仅支持键盘按键。
                if (src.Kind != InputKind.Keyboard)
                {
                    KeyBox.Text = "仅支持键盘按键，请重试";
                    RecordButton.IsEnabled = true;
                    return;
                }

                // 冲突验证：不能与任何已注册方案的注册源相同。
                if (_conflictChecker(src))
                {
                    KeyBox.Text = $"[{InputNameMapper.GetSourceName(src)}] 已被方案占用，请另选";
                    _captured = null;
                    RecordButton.IsEnabled = true;
                    return;
                }

                _captured = src;
                KeyBox.Text = InputNameMapper.GetSourceName(src);
                RecordButton.IsEnabled = true;
            },
            onTimeout: () =>
            {
                KeyBox.Text = "录制超时，请重试";
                RecordButton.IsEnabled = true;
            });
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        _captured = null;
        KeyBox.Text = "未设置";
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        _onConfirm(_captured);
        DialogResult = true;
    }
}
