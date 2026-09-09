using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using TheCelestialDiviner.Services;
using TheCelestialDiviner.ViewModels;

namespace TheCelestialDiviner.Views;

/// <summary>
/// 语音设置模态框：为总开关开启 / 关闭与方案切换三种提示音导入自定义音频
/// （导入即复制到语音目录并立即生效），支持试听与重置回内嵌默认音频。
/// </summary>
public partial class VoiceSettingsWindow : Window
{
    private readonly MainViewModel _vm;

    /// <summary>每行的当前音频名文本与重置按钮（导入 / 重置后刷新）。</summary>
    private readonly Dictionary<SoundCue, (TextBlock Text, Button Reset)> _rows = new();

    public VoiceSettingsWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        AddRow(SoundCue.Start, "开启语音");
        AddRow(SoundCue.Stop, "关闭语音");
        AddRow(SoundCue.Cycle, "方案切换语音");
        foreach (var cue in _rows.Keys) RefreshRow(cue);
    }

    /// <summary>构建一行：用途标题 + 当前音频名 + 导入 / 试听 / 重置按钮。</summary>
    private void AddRow(SoundCue cue, string title)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);

        var current = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Opacity = 0.8,
            Margin = new Thickness(10, 0, 10, 0),
            ToolTip = "当前音频（导入后为复制到语音目录的文件名）"
        };
        Grid.SetColumn(current, 1);
        grid.Children.Add(current);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        var import = MakeButton("导入");
        import.Click += (_, _) => OnImport(cue);
        var preview = MakeButton("试听");
        preview.Click += (_, _) => _vm.PreviewVoiceCue(cue);
        var reset = MakeButton("重置");
        reset.Click += (_, _) => OnReset(cue);
        buttons.Children.Add(import);
        buttons.Children.Add(preview);
        buttons.Children.Add(reset);
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);

        RowsPanel.Children.Add(grid);
        _rows[cue] = (current, reset);
    }

    private static Button MakeButton(string text) => new()
    {
        Content = text,
        Height = 26,
        MinWidth = 54,
        Margin = new Thickness(6, 0, 0, 0),
        Style = (Style)Application.Current.Resources["IconButtonStyle"]
    };

    /// <summary>导入：选择音频文件 → VM 复制到语音目录并热应用；失败弹出原因。</summary>
    private void OnImport(SoundCue cue)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = $"选择{MainViewModel.VoiceCueName(cue)}语音",
            Filter = "音频文件|*.mp3;*.wav;*.m4a;*.aac;*.wma|所有文件|*.*",
            CheckFileExists = true
        };
        if (dlg.ShowDialog(this) != true) return;

        var error = _vm.ImportVoiceCue(cue, dlg.FileName);
        if (error is not null)
        {
            MessageBox.Show(this, $"导入失败：{error}", "语音设置",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        RefreshRow(cue);
    }

    /// <summary>重置：删除自定义音频并恢复默认（未设置时按钮禁用）。</summary>
    private void OnReset(SoundCue cue)
    {
        if (!_vm.HasCustomVoice(cue)) return;
        _vm.ResetVoiceCue(cue);
        RefreshRow(cue);
    }

    /// <summary>刷新某行的当前音频名与重置按钮可用态。</summary>
    private void RefreshRow(SoundCue cue)
    {
        if (!_rows.TryGetValue(cue, out var row)) return;
        row.Text.Text = _vm.VoiceCueDisplayName(cue);
        row.Reset.IsEnabled = _vm.HasCustomVoice(cue);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
