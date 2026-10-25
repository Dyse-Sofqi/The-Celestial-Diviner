using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Navigation;

namespace TheCelestialDiviner.Helpers;

/// <summary>
/// Markdown 子集 → FlowDocument 渲染（更新日志窗口 / 更新确认框共用）。
/// 只认 CHANGELOG.md 与 Release 说明实际用到的标记，不做通用解析——
/// 两个消费方都是本仓库自己维护的文件，格式可控。
/// 行内：**粗体**、`代码`、[文本](url)；块级：# / ## / ### 标题、"- " 列表、两格缩进续行。
/// </summary>
public static class InlineMarkdown
{
    /// <summary>
    /// 把一行文本拆成 Run / Bold / 行内代码 / 超链接塞进指定段落。
    /// 行内标记不识别时按原文输出（如落单的 "**"）；链接交给系统默认浏览器打开。
    /// </summary>
    public static void Append(Paragraph target, string line)
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

    /// <summary>
    /// 按块级结构渲染整段 Markdown 文本到文档（Release 说明用）：# / ## / ### 标题、
    /// "- " 列表（两格缩进的 "  - " 二级项在同一列表内缩进渲染）、两格缩进续行接回
    /// 上一段 / 上一条列表项、空行与 "---" 分段。行内标记经 <see cref="Append"/> 解析。
    /// </summary>
    public static void AppendBlocks(FlowDocument doc, string text)
    {
        Paragraph? pending = null;   // 续行追加目标（当前段落 / 当前列表项内容）
        List? list = null;           // 连续 "- " 行归入同一个列表
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Length == 0 || line.Trim() == "---")
            {
                pending = null;
                list = null;
                continue;
            }

            if (line.StartsWith("## "))
            {
                var p = new Paragraph { FontSize = 14, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 12, 0, 4) };
                p.SetResourceReference(TextElement.ForegroundProperty, "AccentPrimary");
                Append(p, line.Substring(3).Trim());
                doc.Blocks.Add(p);
                pending = null; list = null;
            }
            else if (line.StartsWith("### "))
            {
                var p = new Paragraph { FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 2) };
                Append(p, line.Substring(4).Trim());
                doc.Blocks.Add(p);
                pending = null; list = null;
            }
            else if (line.StartsWith("# "))
            {
                var p = new Paragraph { FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) };
                Append(p, line.Substring(2));
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
                Append(p, line.Substring(2));
                list.ListItems.Add(new ListItem(p));
                pending = p;
            }
            else if (line.StartsWith("  ") && list is not null && line.TrimStart().StartsWith("- "))
            {
                // 二级列表项：WPF 嵌套 List 样式过重，同一列表内缩进一级即可读
                var p = new Paragraph { Margin = new Thickness(18, 0, 0, 3) };
                Append(p, line.TrimStart().Substring(2));
                list.ListItems.Add(new ListItem(p));
                pending = p;
            }
            else if (line.StartsWith("  ") && pending is not null)
            {
                // 续行（缩进两格起）：接回上一段 / 上一条列表项，不另起段落
                Append(pending, " " + line.Trim());
            }
            else
            {
                var p = new Paragraph { Margin = new Thickness(0, 0, 0, 4) };
                Append(p, line);
                doc.Blocks.Add(p);
                pending = p; list = null;
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
    private static void OnLinkClick(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch
        {
            // 打不开就算了：链接不是主要功能，不弹错误框打扰。
        }
        e.Handled = true;
    }
}
