using System.Windows;
using System.Windows.Documents;
using TheCelestialDiviner.Helpers;
using TheCelestialDiviner.Services;

namespace TheCelestialDiviner.Views;

/// <summary>
/// 更新日志窗口（「选项 → 更新日志」）：把内嵌 CHANGELOG.md 渲染成可滚动、可复制的长文档。
/// 只支持 CHANGELOG 实际用到的 Markdown 子集（# / ## / ### / 无序列表 / **粗体** / `代码` / [链接](url)），
/// 行内标记解析共用 <see cref="InlineMarkdown"/>；不做通用解析——更新日志是本仓库自己维护的文件，格式可控。
/// </summary>
public partial class ChangelogWindow : Window
{
    public ChangelogWindow()
    {
        InitializeComponent();
        Render();
    }

    /// <summary>把 CHANGELOG 文本逐行构建成 FlowDocument。</summary>
    private void Render()
    {
        var text = ChangelogService.Load();
        Subtitle.Text = $"按版本倒序（最新在上）；当前运行版本 v{UpdateService.DisplayVersion}";

        var doc = new FlowDocument
        {
            FontSize = 12,
            PagePadding = new Thickness(0),
            // 默认会按窗口宽度自动分栏：正无穷 = 始终单列（长条目不该被拆到两栏）
            ColumnWidth = double.PositiveInfinity
        };
        doc.SetResourceReference(TextElement.ForegroundProperty, "ThemeFg");

        if (text.Length == 0)
        {
            doc.Blocks.Add(new Paragraph(new Run("未能读取更新日志（内嵌资源缺失）。")));
            Viewer.Document = doc;
            return;
        }

        Paragraph? pending = null;   // 续行追加目标（当前段落 / 当前列表项内容）
        List? list = null;           // 连续 "- " 行归入同一个列表
        var skipUnreleased = false;  // 「## 未发布」是开发中的条目，面向用户的窗口里整段跳过

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd();

            if (line.StartsWith("## "))
            {
                var title = line.Substring(3).Trim();
                skipUnreleased = title.StartsWith("未发布", StringComparison.Ordinal);
                pending = null;
                list = null;
                if (skipUnreleased) continue;

                // 版本标题：主题紫 + 上间距，作为长文档的视觉分隔
                var vp = new Paragraph { FontSize = 14, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 14, 0, 4) };
                vp.SetResourceReference(TextElement.ForegroundProperty, "AccentPrimary");
                InlineMarkdown.Append(vp, title);
                doc.Blocks.Add(vp);
                continue;
            }
            if (skipUnreleased) continue;

            if (line.Length == 0 || line.Trim() == "---")
            {
                pending = null;
                list = null;
                continue;
            }

            if (line.StartsWith("# "))
            {
                var p = new Paragraph { FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) };
                InlineMarkdown.Append(p, line.Substring(2));
                doc.Blocks.Add(p);
                pending = null; list = null;
            }
            else if (line.StartsWith("### "))
            {
                var p = new Paragraph { FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 2) };
                InlineMarkdown.Append(p, line.Substring(4));
                doc.Blocks.Add(p);
                pending = null; list = null;
            }
            else if (line.StartsWith("- "))
            {
                if (list is null)
                {
                    list = new List { MarkerStyle = TextMarkerStyle.Disc, Margin = new Thickness(16, 0, 0, 4), Padding = new Thickness(0) };
                    doc.Blocks.Add(list);
                }
                var p = new Paragraph { Margin = new Thickness(0, 0, 0, 3) };
                InlineMarkdown.Append(p, line.Substring(2));
                list.ListItems.Add(new ListItem(p));
                pending = p;
            }
            else if (line.StartsWith("  ") && pending is not null)
            {
                // 续行（缩进两格起）：接回上一段 / 上一条列表项，不另起段落
                InlineMarkdown.Append(pending, " " + line.Trim());
            }
            else
            {
                var p = new Paragraph { Margin = new Thickness(0, 0, 0, 4) };
                InlineMarkdown.Append(p, line);
                doc.Blocks.Add(p);
                pending = p; list = null;
            }
        }

        Viewer.Document = doc;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
