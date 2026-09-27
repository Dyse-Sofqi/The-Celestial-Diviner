using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using TheCelestialDiviner.Helpers;

namespace TheCelestialDiviner.Views;

/// <summary>
/// 配色选择控件（「方案定制」三色选择用）：色块（点击展开预设色板）+ #RRGGBB 输入框。
/// 纯代码构建（样式的主题资源自适应）；非法输入不改动色块，合法输入即时回调。
/// </summary>
public sealed class ColorField : Grid
{
    /// <summary>预设色板（8 列 × 3 行）：灰阶 + 主题紫金 + 常用色相。</summary>
    private static readonly string[] Presets =
    [
        "#1F1F1F", "#404040", "#666666", "#808080", "#AAAAAA", "#D9D9D9", "#FFFFFF", "#000000",
        "#836899", "#A364EA", "#673AB7", "#4527A0", "#31CBB6", "#2BE9F6", "#66BB4D", "#43A047",
        "#CEA23A", "#FDDB27", "#F06292", "#D81B60", "#EF5350", "#C62828", "#2196F3", "#1976D2",
    ];

    private readonly Border _swatch;
    private readonly TextBox _hex;
    private readonly Popup _popup;
    private string _color = "#000000";
    private bool _loading;

    /// <summary>色值变更（用户操作导致；程序化赋值不触发）。</summary>
    public event Action? ColorChanged;

    public ColorField()
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        VerticalAlignment = VerticalAlignment.Center;

        _swatch = new Border
        {
            Width = 46,
            Height = 24,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "点击选择预设颜色",
        };
        _swatch.SetResourceReference(Border.BorderBrushProperty, "ThemeBorder");

        // 预设色板弹层（先于色块事件构建：闭包引用 _popup 时它已就绪）
        _popup = new Popup { PlacementTarget = _swatch, Placement = PlacementMode.Bottom,
            StaysOpen = false, AllowsTransparency = true, VerticalOffset = 4 };
        var panel = new WrapPanel { Width = 8 * 30, Margin = new Thickness(6) };
        foreach (var preset in Presets)
        {
            var tile = new Border
            {
                Width = 22, Height = 22, Margin = new Thickness(4),
                CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Background = (Brush)new BrushConverter().ConvertFromString(preset)!,
                ToolTip = preset,
            };
            tile.SetResourceReference(Border.BorderBrushProperty, "ThemeBorder");
            tile.MouseLeftButtonUp += (_, _) => { Hex = preset; _popup.IsOpen = false; };
            panel.Children.Add(tile);
        }
        var popupBorder = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = panel,
        };
        popupBorder.SetResourceReference(Border.BackgroundProperty, "ThemePanel");
        popupBorder.SetResourceReference(Border.BorderBrushProperty, "ThemeBorder");
        _popup.Child = popupBorder;

        _swatch.MouseLeftButtonUp += (_, _) => _popup.IsOpen = !_popup.IsOpen;
        SetColumn(_swatch, 0);
        Children.Add(_swatch);

        _hex = new TextBox { Width = 92, Height = 24, MaxLength = 7, VerticalContentAlignment = VerticalAlignment.Center };
        _hex.TextChanged += (_, _) => OnHexTyped();
        SetColumn(_hex, 1);
        Children.Add(_hex);

        Apply(_color);
    }

    /// <summary>当前色值（#RRGGBB；赋值即刷新色块与输入框，不触发 ColorChanged）。</summary>
    public string Hex
    {
        get => _color;
        set
        {
            var normalized = Constants.NormalizeHexColor(value);
            if (normalized.Length == 0) return;
            Apply(normalized);
        }
    }

    /// <summary>刷新色块与输入框（_loading 抑制输入框回环回调）。</summary>
    private void Apply(string normalized)
    {
        _loading = true;
        _color = normalized;
        _swatch.Background = (Brush)new BrushConverter().ConvertFromString(normalized)!;
        if (!string.Equals(_hex.Text, normalized, StringComparison.OrdinalIgnoreCase)) _hex.Text = normalized;
        _loading = false;
    }

    /// <summary>输入框手动输入：合法才生效并回调（非法保留原色，不打断输入过程）。</summary>
    private void OnHexTyped()
    {
        if (_loading) return;
        var normalized = Constants.NormalizeHexColor(_hex.Text);
        if (normalized.Length == 0) return;
        _color = normalized;
        _swatch.Background = (Brush)new BrushConverter().ConvertFromString(normalized)!;
        ColorChanged?.Invoke();
    }
}
