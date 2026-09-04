using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TheCelestialDiviner.Helpers;

namespace TheCelestialDiviner.Views;

/// <summary>
/// 键帽可视化悬浮层：屏幕右下角，点击穿透 + 置顶 + 不抢焦点。
/// 键帽视觉复刻 keyviz 的 PBT 样式（双层结构：外层深色底座 + 内层浅色按压面，
/// 按下时内层下沉 0.15 倍字号、easeInOutExpo 缓动），连击时右上角显示次数角标。
/// 显示语义：按下 → 显示并停在屏上（计时暂停）；释放 → 2.5 秒后淡出隐藏。
/// </summary>
public sealed partial class KeycapOverlayWindow : Window
{
    // ---------- PBT 键帽视觉参数（复刻 keyviz 默认值，字号 32） ----------
    private const double TextSize = 32;                        // text.size
    private const double BorderRadius = 0.5 * TextSize * 1.25; // border.radius × size×1.25
    private const double PressOffset = TextSize * 0.15;        // 按下下沉 0.15em
    private static readonly Color CapColor = Rgb(0x1A1A1A);      // secondaryColor（外层底座）
    private static readonly Color CapFaceColor = Rgb(0xFFFFFF);  // color.color（内层按压面）
    private static readonly Color TextColor = Rgb(0x000000);     // text.color
    private static readonly TimeSpan FadeIn = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan FadeOut = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan DismissAfter = TimeSpan.FromSeconds(2.5);

    private readonly DispatcherTimer _dismissTimer;
    private readonly List<KeycapControl> _caps = new();
    private readonly Panel _host;

    public KeycapOverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyClickThrough();

        _host = Root;
        _dismissTimer = new DispatcherTimer { Interval = DismissAfter };
        _dismissTimer.Tick += (_, _) => BeginDismiss();
    }

    /// <summary>WS_EX_TRANSPARENT + WS_EX_NOACTIVATE：点击与焦点行为完全穿透。</summary>
    private void ApplyClickThrough()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        _ = NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
            ex | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);
    }

    /// <summary>显示一组按键（组合键 → 多个键帽并排；相同组合复用现有键帽并递增连击角标）。</summary>
    public void ShowKeys(IReadOnlyList<string> labels, bool pressed)
    {
        if (labels.Count == 0) return;

        if (pressed)
        {
            // 标签与当前键帽一致 → 复用（连击 / 持按场景只更新按压态与角标）。
            if (!MatchesCurrent(labels))
                RebuildCaps(labels);
            foreach (var cap in _caps)
            {
                cap.SetPressed(true);
                cap.BumpPressCount();
            }
            PositionBottomRight();
            Show();
            _dismissTimer.Stop();   // 按住 / 连发期间不淡出
        }
        else
        {
            // 释放：仅当释放的组合与当前显示一致才结束按压动画
            //（避免按住 A 期间 B 的释放事件误抬 A 的键帽）。
            if (MatchesCurrent(labels))
                foreach (var cap in _caps)
                    cap.SetPressed(false);
            _dismissTimer.Stop();
            _dismissTimer.Start();  // 释放后 2.5s 淡出
        }
    }

    /// <summary>当前键帽序列是否与标签一致（复用判断）。</summary>
    private bool MatchesCurrent(IReadOnlyList<string> labels)
        => _caps.Count == labels.Count &&
           !_caps.Where((cap, i) => cap.Label != labels[i]).Any();

    /// <summary>重建键帽序列（组合键 → 多个键帽并排）。</summary>
    private void RebuildCaps(IReadOnlyList<string> labels)
    {
        _host.Children.Clear();
        _caps.Clear();
        foreach (var label in labels)
        {
            var cap = new KeycapControl(label);
            _host.Children.Add(cap);
            _caps.Add(cap);
        }
    }

    /// <summary>锚定屏幕右下角（主屏工作区，边距 24px）。</summary>
    private void PositionBottomRight()
    {
        var work = SystemParameters.WorkArea;
        Left = work.Right - Width - 24;
        Top = work.Bottom - Height - 24;
    }

    /// <summary>淡出后隐藏（清空视觉树，下次按键重建）。</summary>
    private void BeginDismiss()
    {
        _dismissTimer.Stop();
        var anim = new DoubleAnimation(1, 0, FadeOut) { FillBehavior = FillBehavior.Stop };
        anim.Completed += (_, _) =>
        {
            Opacity = 1;
            Hide();
            _host.Children.Clear();
            _caps.Clear();
        };
        BeginAnimation(OpacityProperty, anim);
    }

    private static Color Rgb(int rgb) =>
        Color.FromRgb((byte)(rgb >> 16 & 0xFF), (byte)(rgb >> 8 & 0xFF), (byte)(rgb & 0xFF));

    /// <summary>颜色加深（近似 keyviz 的 oklch darken：RGB 各通道 × (1 - amount)）。</summary>
    private static Color Darken(Color c, double amount) =>
        Color.FromRgb((byte)(c.R * (1 - amount)), (byte)(c.G * (1 - amount)), (byte)(c.B * (1 - amount)));

    // ---------- 键帽控件（PBT 双层结构） ----------
    /// <summary>单个键帽：外层底座（深）+ 内层按压面（浅渐变，按下下沉）+ 连击角标。</summary>
    private sealed class KeycapControl : Border
    {
        private readonly Border _inner;      // 内层按压面（按下时下沉）
        private readonly TextBlock? _badge;  // 连击角标（外层右上角）
        private readonly TranslateTransform _press;
        private int _pressCount;

        public string Label { get; }

        public KeycapControl(string label)
        {
            Label = label;

            // 外层底座：深色纯色 + 3px 内边距 + 圆角（视觉上充当键帽侧壁）。
            CornerRadius = new CornerRadius(BorderRadius);
            Background = new SolidColorBrush(CapColor);
            Padding = new Thickness(3);
            Margin = new Thickness(3, 0, 0, 0);
            VerticalAlignment = VerticalAlignment.Bottom;

            // 内层按压面：浅色横向渐变（左端 darken 10% → 右端原色），高 2.2 倍字号。
            _press = new TranslateTransform();
            var face = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0.5),
                EndPoint = new Point(1, 0.5)
            };
            face.GradientStops.Add(new GradientStop(Darken(CapFaceColor, 0.10), 0));
            face.GradientStops.Add(new GradientStop(CapFaceColor, 1));

            var text = new TextBlock
            {
                Text = label,
                FontSize = TextSize,
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(TextColor),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, TextSize * 0.06) // keyviz borderBottom 0.06em 的近似
            };

            _inner = new Border
            {
                CornerRadius = new CornerRadius(BorderRadius),
                Background = face,
                Child = text,
                RenderTransform = _press,
                Height = TextSize * 2.2,
                MinWidth = TextSize * 2,
                Padding = new Thickness(TextSize * 0.5, TextSize * 0.4, TextSize * 0.5, TextSize * 0.4)
            };

            // 连击角标：外层右上角（keyviz PressCount 的近似），与内层叠放。
            _badge = new TextBlock
            {
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Rgb(0xE53935)),
                Padding = new Thickness(5, 1, 5, 2),
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 2, 2, 0),
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };

            Child = new Grid();
            ((Grid)Child).Children.Add(_inner);
            ((Grid)Child).Children.Add(_badge);
        }

        /// <summary>按压态：内层下沉 0.15em（easeInOutExpo 近似，100ms）。</summary>
        public void SetPressed(bool pressed)
        {
            var anim = new DoubleAnimation(pressed ? PressOffset : 0, TimeSpan.FromMilliseconds(100))
            {
                EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseInOut }
            };
            _press.BeginAnimation(TranslateTransform.YProperty, anim, HandoffBehavior.SnapshotAndReplace);
        }

        /// <summary>连击角标递增（≥2 次显示；角标弹跳一次）。</summary>
        public void BumpPressCount()
        {
            _pressCount++;
            if (_pressCount < 2 || _badge is null) return;
            _badge.Text = $"×{_pressCount}";
            if (_badge.Visibility != Visibility.Visible)
                _badge.Visibility = Visibility.Visible;
            // 角标弹跳（复刻 keyviz press-count 提示）。
            var scale = new ScaleTransform(1, 1, 0.5, 0.5);
            var pop = new DoubleAnimation(1.2, 1.0, TimeSpan.FromMilliseconds(150));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            _badge.LayoutTransform = scale;
        }
    }
}
