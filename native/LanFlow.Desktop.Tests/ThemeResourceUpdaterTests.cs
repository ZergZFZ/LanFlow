using System.Windows;
using System.Windows.Media;
using LanFlow.Desktop.Models;
using LanFlow.Desktop.Presentation;
using Xunit;

namespace LanFlow.Desktop.Tests;

public sealed class ThemeResourceUpdaterTests
{
    [Fact]
    public void Apply_MapsThemeColorsToSemanticBrushes()
    {
        var resources = new ResourceDictionary();
        var colors = new ThemeColors
        {
            Panel = "#FF101112",
            PanelBorder = "#FF202122",
            Surface = "#FF303132",
            SurfaceBorder = "#FF404142",
            Footer = "#FF505152",
            TextPrimary = "#FF606162",
            TextSecondary = "#FF707172",
            Accent = "#FF808182",
            Hover = "#FF909192",
            IconSurface = "#FFA0A1A2",
        };

        new ThemeResourceUpdater().Apply(resources, colors);

        AssertBrush(resources, "WindowBackgroundBrush", colors.Panel);
        AssertBrush(resources, "SurfaceBrush", colors.Surface);
        AssertBrush(resources, "ItemHoverBrush", colors.Hover);
        AssertBrush(resources, "ItemSelectedBrush", colors.Accent);
        AssertBrush(resources, "PrimaryTextBrush", colors.TextPrimary);
        AssertBrush(resources, "SecondaryTextBrush", colors.TextSecondary);
        AssertBrush(resources, "FocusBorderBrush", colors.Accent);
        AssertBrush(resources, "GroupTabSelectedBrush", colors.Accent);
        AssertBrush(resources, "WindowBorderBrush", colors.PanelBorder);
        AssertBrush(resources, "DividerBrush", colors.SurfaceBorder);
        AssertBrush(resources, "MutedSurfaceBrush", colors.Footer);
        AssertBrush(resources, "IconSurfaceBrush", colors.IconSurface);
    }

    [Fact]
    public void Apply_FreezesCreatedBrushes()
    {
        var resources = new ResourceDictionary();

        new ThemeResourceUpdater().Apply(resources, ThemeColors.Dark());

        Assert.All(
            resources.Values.OfType<SolidColorBrush>(),
            brush => Assert.True(brush.IsFrozen));
    }

    private static void AssertBrush(ResourceDictionary resources, string key, string expected)
    {
        var brush = Assert.IsType<SolidColorBrush>(resources[key]);
        Assert.Equal((Color)ColorConverter.ConvertFromString(expected), brush.Color);
    }
}