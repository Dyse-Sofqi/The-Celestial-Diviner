using System;
using System.Windows;
using System.Windows.Controls;

namespace TheCelestialDiviner.Helpers;

/// <summary>
/// 键盘行自定义面板：按“键宽单位”排列子项，模拟真实键盘的按键宽度比例
/// （普通键 = 4 单位，Tab/\ = 6，Caps = 7，Enter = 9，Shift = 9/11，空格 = 25 等）。
/// 每个子项通过附加属性 <see cref="UnitsProperty"/> 声明宽度单位；
/// 面板把可用宽度按总单位数等分，单位宽度随窗口自适应缩放。
/// </summary>
public class KeyboardRowPanel : Panel
{
    /// <summary>子项宽度单位（附加属性；默认 4 = 一个普通键位）。</summary>
    public static readonly DependencyProperty UnitsProperty = DependencyProperty.RegisterAttached(
        "Units", typeof(double), typeof(KeyboardRowPanel), new PropertyMetadata(4.0));

    public static void SetUnits(DependencyObject obj, double value) => obj.SetValue(UnitsProperty, value);

    public static double GetUnits(DependencyObject obj) => (double)obj.GetValue(UnitsProperty);

    protected override Size MeasureOverride(Size availableSize)
    {
        // 先按总单位数算出单位宽，再以每个子项的实际比例宽度测量。
        // 若用整行宽度测量，图块内的 ViewBox 会把内容放大到整行宽（键名超大且被裁切）。
        double totalUnits = 0;
        foreach (UIElement child in InternalChildren)
            totalUnits += GetUnits(child);
        double unitWidth = totalUnits > 0 ? availableSize.Width / totalUnits : 0;

        double maxChildHeight = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(GetUnits(child) * unitWidth, availableSize.Height));
            maxChildHeight = Math.Max(maxChildHeight, child.DesiredSize.Height);
        }
        // 行宽吃满可用宽度（单位宽 = 可用宽 / 总单位数，在 Arrange 中计算）。
        return new Size(availableSize.Width, maxChildHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double totalUnits = 0;
        foreach (UIElement child in InternalChildren)
            totalUnits += GetUnits(child);

        if (totalUnits <= 0 || InternalChildren.Count == 0)
            return finalSize;

        double unitWidth = finalSize.Width / totalUnits;
        double x = 0;
        foreach (UIElement child in InternalChildren)
        {
            double width = GetUnits(child) * unitWidth;
            child.Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width;
        }
        return finalSize;
    }
}
