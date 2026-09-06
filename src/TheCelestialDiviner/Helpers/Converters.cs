using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TheCelestialDiviner.Helpers;

/// <summary>bool → Opacity 转换器：true → 0.5（全局停用半透明），false → 1。</summary>
public sealed class BoolToDisabledOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? 0.5 : 1.0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>bool → Visibility：true → Visible，false → Collapsed。</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 连发间隔框水印转换器：仅在编辑框被点击清空（编辑文本为空）时显示已提交间隔数值虚影；
/// 键入任意内容（含非法中间态）都不再显示水印，避免与输入重叠。
/// values[0] = IntervalEdit（编辑文本），values[1] = IntervalMs（已提交值，int）。
/// </summary>
public sealed class IntervalWatermarkConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return "";
        var edit = values[0] as string ?? "";
        if (edit.Length > 0) return "";
        return values[1] is int i ? i.ToString(culture) : "";
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>string → Visibility：空/空字符串 → Collapsed。</summary>
public sealed class EmptyToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
