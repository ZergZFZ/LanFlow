using FlickD.Desktop.Models;

namespace FlickD.Desktop.Services;

// 皮肤应用逻辑：把 ThemeProfile（内置预设或自定义皮肤）写入 Settings 的现有主题/透明字段，
// 刷色管线（ThemeResourceUpdater）与外观控制器（WindowAppearanceController）无需感知皮肤概念。
public static class SkinApplier
{
    // 皮肤标识：内置皮肤用固定 Id，自定义皮肤用 Name。
    public static string ResolveSkinId(ThemeProfile skin) =>
        string.IsNullOrWhiteSpace(skin.Id) ? skin.Name : skin.Id;

    // 按标识解析皮肤：先内置预设，再自定义列表（Id 或 Name 匹配）。
    public static ThemeProfile? Resolve(string? id, IReadOnlyList<ThemeProfile> customThemes)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }
        var preset = ThemePresets.All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));
        if (preset != null)
        {
            return preset;
        }
        return customThemes.FirstOrDefault(t =>
            string.Equals(t.Id, id, StringComparison.Ordinal)
            || string.Equals(t.Name, id, StringComparison.Ordinal));
    }

    // 应用皮肤：配色为克隆（后续改色不影响皮肤源），材质写入现有透明字段。
    public static void Apply(Settings settings, ThemeProfile skin)
    {
        settings.ThemeColors = skin.Colors.Clone();
        settings.ThemeProfile = skin.Name;
        settings.ThemeProfileId = ResolveSkinId(skin);
        settings.Theme = DeriveTheme(skin.Colors);
        ApplyMaterial(settings, skin.Material, skin.Opacity);
    }

    // 材质档 → 现有透明三字段。opaque 用分层模式 + 100% 浓度表示（不触发整窗透明）。
    public static void ApplyMaterial(Settings settings, string? material, double? opacity)
    {
        switch (material)
        {
            case "layered":
                settings.TransparencyMode = SettingsOptionValues.TransparencyLayered;
                settings.LayeredOpacity = ClampOpacity(opacity, 1.0);
                break;
            case SettingsOptionValues.TransparencyAcrylic:
                settings.TransparencyMode = SettingsOptionValues.TransparencyAcrylic;
                settings.LayeredOpacity = ClampOpacity(opacity, 0.68);
                break;
            case SettingsOptionValues.TransparencyWholeWindow:
                settings.TransparencyMode = SettingsOptionValues.TransparencyWholeWindow;
                settings.WholeWindowOpacity = ClampOpacity(opacity, 0.85);
                break;
            case "opaque":
            default:
                settings.TransparencyMode = SettingsOptionValues.TransparencyLayered;
                settings.LayeredOpacity = 1.0;
                break;
        }
        settings.Opacity = settings.TransparencyMode == SettingsOptionValues.TransparencyWholeWindow
            ? settings.WholeWindowOpacity
            : settings.LayeredOpacity;
    }

    // 按面板底色亮度判定 light/dark，供图标深浅变体与配置归一使用。
    public static string DeriveTheme(ThemeColors colors)
    {
        if (!TryParseHex(colors.Panel, out var r, out var g, out var b))
        {
            return "dark";
        }
        var luminance = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;
        return luminance > 0.5 ? "light" : "dark";
    }

    public static bool TryParseHex(string? hex, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }
        var value = hex.Trim().TrimStart('#');
        if (value.Length == 8)
        {
            value = value[2..]; // 忽略 alpha 通道，取 RGB 部分
        }
        if (value.Length != 6
            || !byte.TryParse(value.AsSpan(0, 2), System.Globalization.NumberStyles.HexNumber, null, out r)
            || !byte.TryParse(value.AsSpan(2, 2), System.Globalization.NumberStyles.HexNumber, null, out g)
            || !byte.TryParse(value.AsSpan(4, 2), System.Globalization.NumberStyles.HexNumber, null, out b))
        {
            r = g = b = 0;
            return false;
        }
        return true;
    }

    private static double ClampOpacity(double? opacity, double fallback) =>
        Math.Clamp(opacity ?? fallback, 0.40, 1.00);
}
