using System.Windows;
using System.Windows.Documents;
using TheCelestialDiviner.Helpers;
using TheCelestialDiviner.Services;

namespace TheCelestialDiviner.Views;

/// <summary>
/// 更新确认框（检查更新 / 底栏「有更新」共用，ShowDialog 返回 true = 确认安装）：
/// 版本号 + 发布说明按 Markdown 渲染 → 确认后由调用方走"下载 → 解包校验 → 重启"。
/// 说明渲染与「更新日志」窗口同源（<see cref="InlineMarkdown"/>：# / ## / ### 标题、
/// 列表、**粗体**、`代码`、[链接](url)），全文可滚动，不再截断。
/// </summary>
public partial class UpdateConfirmWindow : Window
{
    private readonly UpdateCheck _check;

    public UpdateConfirmWindow(UpdateCheck check)
    {
        InitializeComponent();
        _check = check;
        HeaderText.Text = $"发现新版本 {check.TagName}（当前 v{UpdateService.CurrentVersion.ToString(3)}），是否下载并安装？";
        var doc = new FlowDocument
        {
            FontSize = 12,
            PagePadding = new Thickness(0),
            // 默认会按窗口宽度自动分栏：正无穷 = 始终单列
            ColumnWidth = double.PositiveInfinity
        };
        doc.SetResourceReference(TextElement.ForegroundProperty, "ThemeFg");
        InlineMarkdown.AppendBlocks(doc, check.Notes?.Trim() ?? "");
        if (doc.Blocks.Count == 0)
            doc.Blocks.Add(new Paragraph(new Run("（本次更新没有附带更新说明。）")));
        Viewer.Document = doc;
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
