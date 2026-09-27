using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using TheCelestialDiviner.Services;
using TheCelestialDiviner.ViewModels;

namespace TheCelestialDiviner.Views;

/// <summary>
/// 语音设置模态框：为总开关开启 / 关闭与方案切换三种提示音选择音频
/// （下拉列表 = 内嵌默认 + 程序目录里的候选音频 + 当前已导入的自定义音频；
/// 也可用「导入…」从任意位置选择文件）——选择即复制到语音目录并立即生效，
/// 支持试听与重置回内嵌默认音频。
/// </summary>
public partial class VoiceSettingsWindow : Window
{
    private readonly MainViewModel _vm;

    /// <summary>下拉项类型：默认音频 / 程序目录候选音频 / 已导入的自定义音频。</summary>
    private enum CueOptionKind
    {
        Default,
        Candidate,
        Imported,
    }

    /// <summary>下拉项：显示名 + 类型 + 候选文件路径（仅 Candidate 非空）。
    /// ToString 返回显示名 —— 即便模板 / DisplayMemberPath 缺失也只会显示短名，绝不落到路径。</summary>
    private sealed record CueOption(string Label, CueOptionKind Kind, string? CandidatePath = null)
    {
        public override string ToString() => Label;
    }

    /// <summary>每行的下拉与重置按钮（选择 / 导入 / 重置后刷新）。</summary>
    private readonly Dictionary<SoundCue, (ComboBox Picker, Button Reset)> _rows = new();

    /// <summary>正在程序化刷新下拉（抑制 SelectionChanged 回调，防"刷新 → 选择变更 → 再刷新"递归）。</summary>
    private bool _refreshing;

    public VoiceSettingsWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        AddRow(SoundCue.Start, "开启语音");
        AddRow(SoundCue.Stop, "关闭语音");
        AddRow(SoundCue.Cycle, "方案切换语音");
        foreach (var cue in _rows.Keys) RefreshRow(cue);
    }

    /// <summary>构建一行：用途标题 + 音频下拉列表 + 导入 / 试听 / 重置按钮。</summary>
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

        // 音频下拉列表：默认（内嵌）/ 程序目录候选音频 / 当前已导入的自定义音频。
        var picker = new ComboBox
        {
            Height = 26,
            MinWidth = 200,
            MaxWidth = 260,          // 短名显示；超长名截断，不撑宽对话框
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 10, 0),
            Style = (Style)Application.Current.Resources["RoundedComboStyle"],
            ToolTip = "可选音频：内嵌默认音频、程序目录里的候选音频（把音频文件放到程序目录即可出现）、"
                      + "以及已导入的自定义音频；选择即生效并复制到语音目录",
        };
        picker.SelectionChanged += (_, _) => OnPick(cue);
        Grid.SetColumn(picker, 1);
        grid.Children.Add(picker);

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
        _rows[cue] = (picker, reset);
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

    /// <summary>刷新某行的下拉列表（默认音频 + 程序目录候选 + 当前自定义）与重置按钮可用态。
    /// 显示名一律精简：去路径、去扩展名、去用途前缀（改用途前缀匹配仍用完整文件名）。</summary>
    private void RefreshRow(SoundCue cue)
    {
        if (!_rows.TryGetValue(cue, out var row)) return;
        var stored = _vm.HasCustomVoice(cue) ? _vm.VoiceCueDisplayName(cue) : "";
        var original = SoundCueService.OriginalFileName(cue, stored);   // "{前缀}_原文件名" → 原文件名

        var options = new List<CueOption> { new("默认音频", CueOptionKind.Default) };
        var selected = 0;
        foreach (var path in SoundCueService.ScanCandidates())
        {
            options.Add(new CueOption(SoundCueService.ShortDisplayName(path), CueOptionKind.Candidate, path));
            // 已导入项：按原文件名回填候选选中态（存储名 = "{用途前缀}_{原文件名}"）
            if (original.Length > 0 &&
                string.Equals(Path.GetFileName(path), original, StringComparison.OrdinalIgnoreCase))
                selected = options.Count - 1;
        }
        if (original.Length > 0 && selected == 0)
        {
            // 手动导入且来源不在程序目录：单独成项（短名 + （已导入））
            options.Add(new CueOption($"{SoundCueService.ShortDisplayName(original)}（已导入）", CueOptionKind.Imported));
            selected = options.Count - 1;
        }

        _refreshing = true;
        try
        {
            row.Picker.ItemsSource = options;
            row.Picker.DisplayMemberPath = nameof(CueOption.Label);   // 必须显式指定：缺失会落到 ToString/路径
            row.Picker.SelectedIndex = selected;
        }
        finally
        {
            _refreshing = false;
        }
        row.Reset.IsEnabled = _vm.HasCustomVoice(cue);
    }

    /// <summary>
    /// 下拉选择：默认项 → 重置回内嵌音频；候选音频 → 与「导入…」同效（复制到语音目录并热应用）；
    /// 「已导入」项 → 已是当前音频，不做动作。失败（文件被占用等）弹提示并回到刷新后的选中态。
    /// </summary>
    private void OnPick(SoundCue cue)
    {
        if (_refreshing) return;   // 程序化刷新期间的选择变更不当作用户操作
        if (!_rows.TryGetValue(cue, out var row)) return;
        if (row.Picker.SelectedItem is not CueOption option) return;

        switch (option.Kind)
        {
            case CueOptionKind.Default:
                if (!_vm.HasCustomVoice(cue)) return;
                _vm.ResetVoiceCue(cue);
                break;

            case CueOptionKind.Candidate:
                var error = _vm.ImportVoiceCue(cue, option.CandidatePath!);
                if (error is not null)
                    MessageBox.Show(this, $"选择失败：{error}", "语音设置",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                break;

            default:
                return;   // 「已导入」项：当前即是，无需处理
        }
        RefreshRow(cue);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
