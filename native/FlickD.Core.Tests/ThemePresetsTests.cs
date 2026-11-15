using FlickD.Desktop.Models;
using FlickD.Desktop.Services;

namespace FlickD.Core.Tests;

public sealed class ThemePresetsTests
{
    [Fact]
    public void All_ContainsExactlyFourBuiltInSkinsWithUniqueIds()
    {
        Assert.Equal(4, ThemePresets.All.Count);
        Assert.All(ThemePresets.All, skin => Assert.False(string.IsNullOrWhiteSpace(skin.Id)));
        Assert.Equal(
            ThemePresets.All.Select(s => s.Id).Distinct().Count(),
            ThemePresets.All.Count);
        Assert.Equal(
            [ThemePresets.FluentLightId, ThemePresets.FluentDarkId, ThemePresets.ContrastId, ThemePresets.GlassId],
            ThemePresets.All.Select(s => s.Id).ToArray());
    }

    [Fact]
    public void All_SkinsCarryTenValidColors()
    {
        foreach (var skin in ThemePresets.All)
        {
            Assert.True(SkinApplier.TryParseHex(skin.Colors.Panel, out _, out _, out _), skin.Name + " Panel");
            Assert.True(SkinApplier.TryParseHex(skin.Colors.PanelBorder, out _, out _, out _), skin.Name + " PanelBorder");
            Assert.True(SkinApplier.TryParseHex(skin.Colors.Surface, out _, out _, out _), skin.Name + " Surface");
            Assert.True(SkinApplier.TryParseHex(skin.Colors.SurfaceBorder, out _, out _, out _), skin.Name + " SurfaceBorder");
            Assert.True(SkinApplier.TryParseHex(skin.Colors.Footer, out _, out _, out _), skin.Name + " Footer");
            Assert.True(SkinApplier.TryParseHex(skin.Colors.TextPrimary, out _, out _, out _), skin.Name + " TextPrimary");
            Assert.True(SkinApplier.TryParseHex(skin.Colors.TextSecondary, out _, out _, out _), skin.Name + " TextSecondary");
            Assert.True(SkinApplier.TryParseHex(skin.Colors.Accent, out _, out _, out _), skin.Name + " Accent");
            Assert.True(SkinApplier.TryParseHex(skin.Colors.Hover, out _, out _, out _), skin.Name + " Hover");
            Assert.True(SkinApplier.TryParseHex(skin.Colors.IconSurface, out _, out _, out _), skin.Name + " IconSurface");
        }
    }

    [Fact]
    public void All_SkinsCarryValidMaterialAndOpacity()
    {
        foreach (var skin in ThemePresets.All)
        {
            Assert.True(
                skin.Material is "opaque" or "layered" or "acrylic" or "wholeWindow",
                skin.Name + " material");
            Assert.True(skin.Opacity is null or >= 0.40 and <= 1.00, skin.Name + " opacity");
        }
        Assert.Equal("acrylic", ThemePresets.Glass.Material);
        Assert.Equal(0.68, ThemePresets.Glass.Opacity);
    }

    [Theory]
    [InlineData(ThemePresets.FluentLightId, "light")]
    [InlineData(ThemePresets.FluentDarkId, "dark")]
    [InlineData(ThemePresets.ContrastId, "light")]
    [InlineData(ThemePresets.GlassId, "dark")]
    public void BuiltInSkinsDeriveThemeFromPanelLuminance(string id, string expected)
    {
        var skin = ThemePresets.All.Single(s => s.Id == id);
        Assert.Equal(expected, SkinApplier.DeriveTheme(skin.Colors));
    }
}
