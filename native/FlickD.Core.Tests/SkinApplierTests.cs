using FlickD.Desktop.Models;
using FlickD.Desktop.Services;

namespace FlickD.Core.Tests;

public sealed class SkinApplierTests
{
    [Fact]
    public void Resolve_MatchesBuiltInPresetById()
    {
        Assert.Same(ThemePresets.Glass, SkinApplier.Resolve(ThemePresets.GlassId, []));
        Assert.Same(ThemePresets.FluentLight, SkinApplier.Resolve(ThemePresets.FluentLightId, []));
    }

    [Fact]
    public void Resolve_MatchesCustomSkinByIdOrName()
    {
        var custom = new ThemeProfile { Name = "我的皮肤", Material = "opaque", Opacity = 1.0 };
        var customWithId = new ThemeProfile { Id = "mine", Name = "带 Id 皮肤" };

        Assert.Same(customWithId, SkinApplier.Resolve("mine", [custom, customWithId]));
        Assert.Same(custom, SkinApplier.Resolve("我的皮肤", [custom]));
        Assert.Null(SkinApplier.Resolve("missing", [custom]));
        Assert.Null(SkinApplier.Resolve(null, [custom]));
        Assert.Null(SkinApplier.Resolve("", [custom]));
    }

    [Fact]
    public void Apply_WritesColorsCloneIdentityAndMaterial()
    {
        var settings = new Settings();
        var skin = ThemePresets.Glass;

        SkinApplier.Apply(settings, skin);

        Assert.Equal(skin.Name, settings.ThemeProfile);
        Assert.Equal(ThemePresets.GlassId, settings.ThemeProfileId);
        Assert.Equal("dark", settings.Theme);
        Assert.Equal(SettingsOptionValues.TransparencyAcrylic, settings.TransparencyMode);
        Assert.Equal(0.68, settings.LayeredOpacity, 3);
        Assert.Equal(0.68, settings.Opacity, 3);
        // 配色必须是克隆：后续改色不得反向污染皮肤源
        Assert.NotSame(skin.Colors, settings.ThemeColors);
        Assert.Equal(skin.Colors.Accent, settings.ThemeColors.Accent);
        settings.ThemeColors.Accent = "#123456";
        Assert.Equal("#8A94FF", skin.Colors.Accent);
    }

    [Fact]
    public void ApplyMaterial_MapsEachMaterialToTransparencyFields()
    {
        var opaque = new Settings();
        SkinApplier.ApplyMaterial(opaque, "opaque", null);
        Assert.Equal(SettingsOptionValues.TransparencyLayered, opaque.TransparencyMode);
        Assert.Equal(1.0, opaque.LayeredOpacity, 3);
        Assert.Equal(1.0, opaque.Opacity, 3);

        var layered = new Settings();
        SkinApplier.ApplyMaterial(layered, "layered", 0.8);
        Assert.Equal(SettingsOptionValues.TransparencyLayered, layered.TransparencyMode);
        Assert.Equal(0.8, layered.LayeredOpacity, 3);

        var whole = new Settings();
        SkinApplier.ApplyMaterial(whole, SettingsOptionValues.TransparencyWholeWindow, 0.9);
        Assert.Equal(SettingsOptionValues.TransparencyWholeWindow, whole.TransparencyMode);
        Assert.Equal(0.9, whole.WholeWindowOpacity, 3);

        // null 材质（旧自定义皮肤）视为不透明
        var legacy = new Settings { TransparencyMode = SettingsOptionValues.TransparencyWholeWindow };
        SkinApplier.ApplyMaterial(legacy, null, null);
        Assert.Equal(SettingsOptionValues.TransparencyLayered, legacy.TransparencyMode);
        Assert.Equal(1.0, legacy.LayeredOpacity, 3);
    }

    [Fact]
    public void ApplyMaterial_ClampsOutOfRangeOpacity()
    {
        var settings = new Settings();
        SkinApplier.ApplyMaterial(settings, "acrylic", 5.0);
        Assert.Equal(1.0, settings.LayeredOpacity, 3);

        SkinApplier.ApplyMaterial(settings, "acrylic", 0.1);
        Assert.Equal(0.40, settings.LayeredOpacity, 3);
    }

    [Theory]
    [InlineData("#F6F7FB", "light")]
    [InlineData("#FFFFFF", "light")]
    [InlineData("#161A26", "dark")]
    [InlineData("#171B28", "dark")]
    [InlineData("#171B28CC", "dark")]
    [InlineData("not-a-color", "dark")]
    [InlineData(null, "dark")]
    public void DeriveTheme_FallsBackToDarkOnInvalidPanel(string? panel, string expected)
    {
        var colors = new ThemeColors { Panel = panel! };
        Assert.Equal(expected, SkinApplier.DeriveTheme(colors));
    }

    [Fact]
    public void TryParseHex_HandlesSixAndEightDigitForms()
    {
        Assert.True(SkinApplier.TryParseHex("#5B67D6", out var r, out var g, out var b));
        Assert.Equal((byte)0x5B, r);
        Assert.Equal((byte)0x67, g);
        Assert.Equal((byte)0xD6, b);

        Assert.True(SkinApplier.TryParseHex("#CC5B67D6", out r, out g, out b));
        Assert.Equal((byte)0x5B, r);

        Assert.False(SkinApplier.TryParseHex("#12345", out _, out _, out _));
        Assert.False(SkinApplier.TryParseHex("zzzzzz", out _, out _, out _));
    }
}
