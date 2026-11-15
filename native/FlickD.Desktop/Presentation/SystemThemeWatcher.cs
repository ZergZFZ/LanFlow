using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace FlickD.Desktop.Presentation;

// 监听 Windows「应用模式」深浅色（AppsUseLightTheme 注册表值）。
// 深浅色切换通过 WM_SETTINGCHANGE 广播，.NET 封装为 UserPreferenceChanged(General)。
public sealed class SystemThemeWatcher : IDisposable
{
    private const string PersonalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightThemeValue = "AppsUseLightTheme";

    // true = 应用模式为浅色。
    public event Action<bool>? AppsLightThemeChanged;

    public SystemThemeWatcher()
    {
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    // 注册表缺失（如策略禁用个性化）时默认浅色，与 Windows 首次安装行为一致。
    public static bool IsAppsLightTheme() => ReadAppsUseLightTheme() ?? true;

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General)
        {
            return;
        }
        AppsLightThemeChanged?.Invoke(ReadAppsUseLightTheme() ?? true);
    }

    private static bool? ReadAppsUseLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath);
            return key?.GetValue(AppsUseLightThemeValue) is int value ? value != 0 : null;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}
