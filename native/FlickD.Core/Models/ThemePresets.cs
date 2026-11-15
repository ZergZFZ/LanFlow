namespace FlickD.Desktop.Models;

// 内置皮肤预设：配色取自界面美化预览稿 A/B/C/D，材质档见 SkinApplier。
// 固定 Id 供配置引用（Settings.ThemeProfileId）；自定义皮肤（CustomThemes）Id 留空、以 Name 为标识。
public static class ThemePresets
{
    public const string FluentLightId = "fluent-light";
    public const string FluentDarkId = "fluent-dark";
    public const string ContrastId = "contrast";
    public const string GlassId = "glass";

    public static ThemeProfile FluentLight { get; } = new()
    {
        Id = FluentLightId,
        Name = "Fluent 轻透",
        Material = "opaque",
        Opacity = 1.0,
        Colors = new ThemeColors
        {
            Panel = "#F6F7FB", PanelBorder = "#D7DCE8", Surface = "#FFFFFF", SurfaceBorder = "#E3E7EF",
            Footer = "#F0F2F7", TextPrimary = "#1E2533", TextSecondary = "#59657A",
            Accent = "#5B67D6", Hover = "#EEF0FA", IconSurface = "#E2E6ED",
        },
    };

    public static ThemeProfile FluentDark { get; } = new()
    {
        Id = FluentDarkId,
        Name = "Fluent 深色",
        Material = "opaque",
        Opacity = 1.0,
        Colors = new ThemeColors
        {
            Panel = "#161A26", PanelBorder = "#303850", Surface = "#1F2434", SurfaceBorder = "#28304A",
            Footer = "#12151F", TextPrimary = "#F2F5FB", TextSecondary = "#9AA3B8",
            Accent = "#7B86E8", Hover = "#262C3E", IconSurface = "#2A3040",
        },
    };

    public static ThemeProfile Contrast { get; } = new()
    {
        Id = ContrastId,
        Name = "高对比",
        Material = "opaque",
        Opacity = 1.0,
        Colors = new ThemeColors
        {
            Panel = "#FFFFFF", PanelBorder = "#D9DEE8", Surface = "#FFFFFF", SurfaceBorder = "#E6EAF1",
            Footer = "#F2F4F8", TextPrimary = "#171C28", TextSecondary = "#5A6578",
            Accent = "#5B67D6", Hover = "#EEF0FE", IconSurface = "#E9EDF3",
        },
    };

    public static ThemeProfile Glass { get; } = new()
    {
        Id = GlassId,
        Name = "Acrylic 玻璃",
        Material = "acrylic",
        Opacity = 0.68,
        Colors = new ThemeColors
        {
            Panel = "#181C28", PanelBorder = "#3A4258", Surface = "#222838", SurfaceBorder = "#2E3648",
            Footer = "#141826", TextPrimary = "#F4F6FC", TextSecondary = "#B7BFD2",
            Accent = "#8A94FF", Hover = "#2E3448", IconSurface = "#2A3040",
        },
    };

    public static IReadOnlyList<ThemeProfile> All { get; } = [FluentLight, FluentDark, Contrast, Glass];
}
