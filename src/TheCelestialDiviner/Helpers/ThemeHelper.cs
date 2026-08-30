using Microsoft.Win32;

namespace TheCelestialDiviner.Helpers;

/// <summary>系统深色 / 浅色主题检测（注册表 AppsUseLightTheme）。</summary>
public static class ThemeHelper
{
    /// <summary>当前应用主题是否为深色（读取失败时按浅色处理）。</summary>
    public static bool IsDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            // AppsUseLightTheme：1 = 浅色，0 = 深色。
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int i && i == 0;
        }
        catch
        {
            return false;
        }
    }
}
