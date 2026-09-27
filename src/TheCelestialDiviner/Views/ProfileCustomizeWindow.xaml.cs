using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TheCelestialDiviner.Helpers;
using TheCelestialDiviner.Models;
using TheCelestialDiviner.ViewModels;

namespace TheCelestialDiviner.Views;

/// <summary>
/// 方案定制模态框：「选项」菜单首项。四个方案档位各自可命名并绑定主题预设
/// （默认主题 / 衍天高手 / 莫问高手）；绑定「默认主题」的档位可继续定制
/// 应用图标（22 个可选图标画廊）/ 应用名（窗口名，版本号不变）/ 键帽配色 / 三种配色。
/// 确定后由 MainViewModel.SetProfileCustomization 归一化（限长、校验图标键 / 配色名 / 色值）并落盘，
/// 当前档位立即整套换肤。预设主题下定制区不展示（预设决定外观）。
/// </summary>
public partial class ProfileCustomizeWindow : Window
{
    private readonly MainViewModel _vm;

    /// <summary>编辑中的四个档位名称与外观副本（确定时才写回 VM）。</summary>
    private readonly List<string> _names = [];
    private readonly List<ProfileAppearance> _looks = [];

    /// <summary>档位页签（① ② ③ ④）与画廊按钮（键 = 图标键）。</summary>
    private readonly List<RadioButton> _tabs = [];
    private readonly Dictionary<string, Button> _iconButtons = new(StringComparer.Ordinal);

    private ColorField _primaryField = null!;
    private ColorField _goldField = null!;
    private ColorField _dualField = null!;

    private int _index;
    private string _iconKey = "";
    private bool _loading;

    public ProfileCustomizeWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        for (var i = 0; i < MainViewModel.ProfileCount; i++)
        {
            _names.Add(vm.ProfileName(i));
            _looks.Add(vm.ProfileAppearances[i].Clone());
        }

        BuildTabs();
        BuildIconGallery();
        ThemeBox.ItemsSource = SectThemes.All.Select(t => t.DisplayName).ToArray();
        SchemeBox.ItemsSource = KeycapSchemes.Names;
        AddColorRow("开关模式配色", out _primaryField);
        AddColorRow("按压模式配色", out _goldField);
        AddColorRow("双宏第二配色", out _dualField);

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            Confirm();
        };

        SelectProfile(vm.ActiveProfile);   // 默认停在与主界面一致的档位
    }

    /// <summary>档位页签：① ② ③ ④（已命名的显示自定义名），点击切换编辑对象。</summary>
    private void BuildTabs()
    {
        var style = (Style)FindResource("DialogTabStyle");
        for (var i = 0; i < MainViewModel.ProfileCount; i++)
        {
            var tab = new RadioButton
            {
                Style = style,
                GroupName = "CustomizeProfile",
                Content = ProfileTabLabel(i),
                Tag = i,
            };
            tab.Checked += (_, _) =>
            {
                if (_loading || tab.Tag is not int target || target == _index) return;
                Capture();                 // 切页签前先收下当前档位的编辑内容
                SelectProfile(target);
            };
            _tabs.Add(tab);
            TabPanel.Children.Add(tab);
        }
    }

    /// <summary>档位页签文本（未命名 = "①"；已命名 = "① 名称"，限长避免撑宽对话框）。</summary>
    private string ProfileTabLabel(int index)
    {
        var name = _names[index];
        var label = MainViewModel.ProfileLabel(index);
        if (string.IsNullOrWhiteSpace(name)) return label;
        return name.Length > 6 ? $"{label} {name.Substring(0, 6)}…" : $"{label} {name}";
    }

    /// <summary>图标画廊：22 个可选应用图标（程序自带衍天 / 莫问 + 20 个门派心法图标）。</summary>
    private void BuildIconGallery()
    {
        foreach (var icon in SectIcons.All)
        {
            var button = new Button
            {
                Width = 40,
                Height = 40,
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(0),
                Style = (Style)Application.Current.Resources["IconButtonStyle"],
                ToolTip = icon.DisplayName,
                Tag = icon.Key,
                Content = new Image
                {
                    Source = LoadIcon(icon.AppIconUri),
                    Width = 26,
                    Height = 26,
                    Stretch = Stretch.Uniform,
                },
            };
            button.Click += (_, _) => { _iconKey = icon.Key; RefreshIconSelection(); };
            _iconButtons[icon.Key] = button;
            IconGallery.Children.Add(button);
        }
    }

    /// <summary>加载图标（ico 取最大帧；失败返回 null → 键帽按钮显示空白但不影响选择）。</summary>
    private static ImageSource? LoadIcon(string uri)
    {
        try
        {
            var frames = BitmapDecoder.Create(new Uri(uri),
                BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames;
            return frames.OrderByDescending(f => f.PixelWidth).First();
        }
        catch (Exception ex)
        {
            Logger.Warn($"方案定制图标加载失败（{uri}）：{ex.Message}");
            return null;
        }
    }

    /// <summary>新增一行配色选择（标签 + ColorField）。</summary>
    private void AddColorRow(string title, out ColorField field)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var label = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(label, 0);
        row.Children.Add(label);
        field = new ColorField();
        Grid.SetColumn(field, 1);
        row.Children.Add(field);
        ColorRowsPanel.Children.Add(row);
    }

    /// <summary>切换到某个档位：刷新页签选中态与各输入控件（程序化赋值期间抑制改动回调）。</summary>
    private void SelectProfile(int index)
    {
        _loading = true;
        _index = index;
        for (var i = 0; i < _tabs.Count; i++) _tabs[i].IsChecked = i == index;

        var look = _looks[index];
        var theme = SectThemes.ByIndex(look.Theme);
        NameBox.Text = _names[index];
        ThemeBox.SelectedIndex = SectThemes.IndexOf(theme);

        // 应用图标：自定义键优先，否则用主题预设图标对应的键
        _iconKey = string.IsNullOrEmpty(look.IconKey) ? PresetIconKey(theme) : look.IconKey;
        RefreshIconSelection();
        AppNameBox.Text = look.AppName;
        SchemeBox.SelectedItem = KeycapSchemes.IsKnown(look.KeycapScheme) ? look.KeycapScheme : theme.KeycapScheme;

        // 三色：显示解析后的最终色（自定义或主题预设）——所见即所得，确定时写回具体色值
        var resolved = look.Resolve();
        _primaryField.Hex = resolved.AccentPrimaryHex;
        _goldField.Hex = resolved.AccentGoldHex;
        _dualField.Hex = resolved.AccentDualHex;

        UpdateThemeDependentUi(theme);
        _loading = false;
    }

    /// <summary>主题预设图标对应的图标键（默认主题 → 衍天图标；衍天高手 → 太玄经；莫问高手 → 莫问）。</summary>
    private static string PresetIconKey(SectTheme theme) =>
        SectIcons.All.FirstOrDefault(i => string.Equals(i.AppIconUri, theme.AppIconUri, StringComparison.OrdinalIgnoreCase))
            ?.Key ?? SectIcons.Diviner.Key;

    /// <summary>主题相关界面：定制区仅「默认主题」展示（预设主题下不展示定制项）；提示文案同步。</summary>
    private void UpdateThemeDependentUi(SectTheme theme)
    {
        CustomPanel.Visibility = theme.Customizable ? Visibility.Visible : Visibility.Collapsed;
        ThemeHint.Text = theme.Customizable ? "可定制下方外观" : "预设主题：外观由主题决定";
        AppNameHint.Text = $"留空 = {theme.DefaultAppName}";
        NameHint.Text = "留空 = 默认序号";
    }

    /// <summary>画廊选中态：选中项描边 = 主题强调色。</summary>
    private void RefreshIconSelection()
    {
        foreach (var (key, button) in _iconButtons)
        {
            var selected = string.Equals(key, _iconKey, StringComparison.OrdinalIgnoreCase);
            button.BorderThickness = new Thickness(selected ? 2 : 1);
            if (selected) button.SetResourceReference(Control.BorderBrushProperty, "AccentPrimary");
            else button.SetResourceReference(Control.BorderBrushProperty, "ThemeBorder");
            button.Opacity = selected ? 1 : 0.85;
        }
    }

    /// <summary>主题下拉变更：刷新定制区显隐（不改动已填的定制值，切回默认主题即恢复）。</summary>
    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        UpdateThemeDependentUi(SectThemes.ByIndex(Math.Max(0, ThemeBox.SelectedIndex)));
    }

    /// <summary>把界面上的编辑内容收进当前档位的副本（预设主题下不动定制项，切回默认主题可恢复）。</summary>
    private void Capture()
    {
        _names[_index] = NameBox.Text;
        var look = _looks[_index];
        var themeIndex = Math.Max(0, ThemeBox.SelectedIndex);
        look.Theme = themeIndex;
        if (!SectThemes.ByIndex(themeIndex).Customizable) return;

        look.IconKey = _iconKey;
        look.AppName = AppNameBox.Text;
        look.KeycapScheme = SchemeBox.SelectedItem as string ?? "";
        look.AccentPrimary = _primaryField.Hex;
        look.AccentGold = _goldField.Hex;
        look.AccentDual = _dualField.Hex;
    }

    /// <summary>确定：收下当前档位 → 写回 VM（归一化 + 落盘 + 立即换肤）→ 关闭。</summary>
    private void OnConfirmClick(object sender, RoutedEventArgs e) => Confirm();

    private void Confirm()
    {
        Capture();
        _vm.SetProfileCustomization(_names, _looks);
        Close();
    }

    /// <summary>恢复默认（当前档位）：默认主题 + 清空自定义项与名称（仍需点确定才落盘）。</summary>
    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        _names[_index] = "";
        _looks[_index] = new ProfileAppearance();
        SelectProfile(_index);
    }

    /// <summary>取消：不改动配置直接关闭。</summary>
    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();
}
