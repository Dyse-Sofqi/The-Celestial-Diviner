using System.Windows;
using System.Windows.Media;
using TheCelestialDiviner.Services;

namespace TheCelestialDiviner.Views;

/// <summary>
/// 「该装哪种驱动」的选择窗口（系统导引的一次性入口）。
/// 只负责展示 <see cref="SystemDriverAdvisor.Advice"/> 并回传用户选择，不碰驱动、不改配置。
/// 返回值：4 = Interception / 3 = DD / 0 = 暂用普通模式 / -1 = 直接关窗（等同于暂不）。
/// </summary>
public partial class DriverChoiceDialog : Window
{
    /// <summary>用户选择（见类型注释；未选择时为 -1）。</summary>
    public int Choice { get; private set; } = -1;

    private readonly SystemDriverAdvisor.Advice _advice;

    public DriverChoiceDialog(SystemDriverAdvisor.Advice advice)
    {
        InitializeComponent();
        _advice = advice;
        Build();
    }

    private void Build()
    {
        SubtitleText.Text = "这台机器适合用哪条物理级注入路线，取决于系统版本与内核完整性开关。"
                            + "程序已按当前环境给出建议，你也可以自行改选。";

        EnvText.Text =
            $"系统：{_advice.OsName}（Build {_advice.Build}）\n"
            + $"内核完整性：内存完整性 {( _advice.HvciOn ? "开" : "关")} · "
            + $"硬件堆栈保护 {(_advice.ShadowStacksOn ? "开" : "关")} · "
            + $"Smart App Control {(_advice.SmartAppControlOn ? "开" : "关")}\n"
            + $"键盘过滤链：{(_advice.KeyboardFilters.Count == 0 ? "读不到" : string.Join(" → ", _advice.KeyboardFilters))}\n"
            + $"现有驱动：Interception {(_advice.InterceptionPresent ? "已安装" : "未安装")} · "
            + $"DD 库 {( _advice.DdAvailable ? "在位" : "不在位")}";

        RecommendTitle.Text = $"建议：{_advice.RecommendedLabel}";
        ReasonText.Text = string.Join("\n", _advice.Reasons);
        WarningText.Text = _advice.Warnings.Count == 0 ? "" : string.Join("\n", _advice.Warnings);
        WarningText.Visibility = _advice.Warnings.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        // 主按钮 = 推荐路线；次按钮 = 另一条物理级路线（推荐为普通模式时给两条都留口子）
        PrimaryButton.Content = _advice.RecommendedMode switch
        {
            4 => "使用 Interception（推荐）",
            3 => "使用 DD 驱动（推荐）",
            _ => "仍要试 Interception"
        };

        var alternative = _advice.RecommendedMode switch
        {
            4 => 3,
            3 => 4,
            _ => 3
        };
        AlternativeButton.Content = alternative == 3 ? "改用 DD 驱动" : "改用 Interception 驱动";
        AlternativeButton.Tag = alternative;

        if (_advice.RecommendedMode == 0)
        {
            // 两条都不推荐时，主按钮仍给 Interception（用户明确要试），次按钮给 DD。
            PrimaryButton.Tag = 4;
        }
        else
        {
            PrimaryButton.Tag = _advice.RecommendedMode;
        }

        if (_advice.RecommendedMode == 4)
        {
            // 推荐 Interception 时把它做视觉强调（不用主题资源，跟随系统强调色即可）
            PrimaryButton.FontWeight = FontWeights.SemiBold;
        }
    }

    private void OnPrimary(object sender, RoutedEventArgs e) => Finish(PrimaryButton.Tag);

    private void OnAlternative(object sender, RoutedEventArgs e) => Finish(AlternativeButton.Tag);

    private void OnSkip(object sender, RoutedEventArgs e) => Finish(0);

    private void Finish(object? tag)
    {
        Choice = tag is int mode ? mode : 0;
        DialogResult = true;
        Close();
    }
}
