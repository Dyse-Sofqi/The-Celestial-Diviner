namespace TheCelestialDiviner.Helpers;

/// <summary>
/// 键帽配色方案（复刻 keyviz 设置页 colorSchemes 预设）：
/// Face = 键帽面（primary）、Base = 底座（secondary）、Text = 文字色、Border = 边框色
/// （keyviz 点选预设时执行 setBorderStyle({ color: scheme.secondary })，即边框 = secondary）。
/// </summary>
public sealed class KeycapScheme
{
    public KeycapScheme(string name, string face, string @base, string text)
        : this(name, face, @base, text, @base)
    {
    }

    public KeycapScheme(string name, string face, string @base, string text, string border)
    {
        Name = name;
        Face = face;
        Base = @base;
        Text = text;
        Border = border;
    }

    /// <summary>方案名（持久化 / 下拉显示）。</summary>
    public string Name { get; }

    /// <summary>键帽面颜色（keyviz primary）。</summary>
    public string Face { get; }

    /// <summary>底座颜色（keyviz secondary）。</summary>
    public string Base { get; }

    /// <summary>文字颜色（keyviz text）。</summary>
    public string Text { get; }

    /// <summary>边框颜色（keyviz border.color；选预设时 = 方案 secondary 色）。</summary>
    public string Border { get; }
}

/// <summary>全部预设配色（与 keyviz src/components/settings/keycap.tsx colorSchemes 一致）。</summary>
public static class KeycapSchemes
{
    public static readonly KeycapScheme[] All =
    [
        new("Silver",     "#f8f8f8", "#dcdcdc", "#000000"),
        new("Blue",       "#2196f3", "#1976d2", "#ffffff"),
        new("Yellow",     "#FDDB27", "#dfc019", "#000000"),
        new("Green",      "#66bb6a", "#43a047", "#ffffff"),
        new("Pink",       "#f06292", "#d81b60", "#ffffff"),
        new("Red",        "#ef5350", "#c62828", "#ffffff"),
        new("Pansy",      "#673ab7", "#4527a0", "#ffc107"),
        new("Bumblebee",  "#404040", "#2e2e2e", "#FDDB27"),
    ];

    /// <summary>全部方案名（下拉列表绑定）。</summary>
    public static string[] Names { get; } = [.. All.Select(s => s.Name)];

    /// <summary>按名称查找（忽略大小写；未命中回退 Pansy）。</summary>
    public static KeycapScheme Resolve(string? name) =>
        All.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) ??
        All.First(s => s.Name == "Pansy");
}
