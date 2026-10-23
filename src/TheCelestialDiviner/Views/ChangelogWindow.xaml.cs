using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Navigation;
using TheCelestialDiviner.Services;

namespace TheCelestialDiviner.Views;

/// <summary>
/// 更新日志窗口（「选项 → 更新日志」）：把内嵌 CHANGELOG.md 渲染成可滚动、可复制的长文档。
/// 只支持 CHANGELOG 实际用到的 Markdown 子集（# / ## / ### / 无序列表 / **粗体** / `代码` / [链接](url)），
/// 不做通用解析——更新日志是本仓库自己维护的文件，格式可控。
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
                AppendInlines(vp, title);
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
                AppendInlines(p, line.Substring(2));
                doc.Blocks.Add(p);
                pending = null; list = null;
            }
            else if (line.StartsWith("### "))
            {
                var p = new Paragraph { FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 2) };
                AppendInlines(p, line.Substring(4));
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
                AppendInlines(p, line.Substring(2));
                list.ListItems.Add(new ListItem(p));
                pending = p;
            }
            else if (line.StartsWith("  ") && pending is not null)
            {
                // 续行（缩进两格起）：接回上一段 / 上一条列表项，不另起段落
                AppendInlines(pending, " " + line.Trim());
            }
            else
            {
                var p = new Paragraph { Margin = new Thickness(0, 0, 0, 4) };
                AppendInlines(p, line);
                doc.Blocks.Add(p);
                pending = p; list = null;
            }
        }

        Viewer.Document = doc;
    }

    /// <summary>
    /// 把一行文本拆成 Run / Bold / 行内代码 / 超链接塞进指定段落。
    /// 只认 **粗体**、`代码`、[文本](url) 三种行内标记（其余按原文输出）。
    /// </summary>
    private void AppendInlines(Paragraph target, string line)
    {
        var i = 0;
        while (i < line.Length)
        {
            var bold = line.IndexOf("**", i, StringComparison.Ordinal);
            var code = line.IndexOf('`', i);
            var link = line.IndexOf('[', i);
            var next = MinPositive(bold, code, link);
            if (next < 0)
            {
                target.Inlines.Add(new Run(line.Substring(i)));
                return;
            }
            if (next > i) target.Inlines.Add(new Run(line.Substring(i, next - i)));

            if (next == bold)
            {
                var end = line.IndexOf("**", next + 2, StringComparison.Ordinal);
                if (end < 0) { target.Inlines.Add(new Run(line.Substring(next))); return; }
                target.Inlines.Add(new Bold(new Run(line.Substring(next + 2, end - next - 2))));
                i = end + 2;
            }
            else if (next == code)
            {
                var end = line.IndexOf('`', next + 1);
                if (end < 0) { target.Inlines.Add(new Run(line.Substring(next))); return; }
                var run = new Run(line.Substring(next + 1, end - next - 1)) { FontFamily = new System.Windows.Media.FontFamily("Consolas, Cascadia Mono, monospace") };
                target.Inlines.Add(run);
                i = end + 1;
            }
            else
            {
                var close = line.IndexOf(']', next + 1);
                if (close < 0 || close + 1 >= line.Length || line[close + 1] != '(')
                {
                    target.Inlines.Add(new Run(line.Substring(next, 1)));
                    i = next + 1;
                    continue;
                }
                var urlEnd = line.IndexOf(')', close + 2);
                if (urlEnd < 0)
                {
                    target.Inlines.Add(new Run(line.Substring(next)));
                    return;
                }
                var label = line.Substring(next + 1, close - next - 1);
                var url = line.Substring(close + 2, urlEnd - close - 2);
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    var hyperlink = new Hyperlink(new Run(label)) { NavigateUri = uri };
                    hyperlink.SetResourceReference(TextElement.ForegroundProperty, "AccentPrimary");
                    hyperlink.RequestNavigate += OnLinkClick;
                    target.Inlines.Add(hyperlink);
                }
                else
                {
                    target.Inlines.Add(new Run(label));
                }
                i = urlEnd + 1;
            }
        }
    }

    /// <summary>取三个候选位置中最小且非负的一个（-1 = 未找到）。</summary>
    private static int MinPositive(int a, int b, int c)
    {
        var min = -1;
        foreach (var v in new[] { a, b, c })
            if (v >= 0 && (min < 0 || v < min)) min = v;
        return min;
    }

    /// <summary>行内链接：交给系统默认浏览器打开（与主界面注释区一致）。</summary>
    private void OnLinkClick(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch
        {
            // 打不开就算了：链接不是窗口的主要功能，不弹错误框打扰。
        }
        e.Handled = true;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
