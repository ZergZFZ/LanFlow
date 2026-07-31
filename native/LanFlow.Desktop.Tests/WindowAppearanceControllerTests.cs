using LanFlow.Desktop.Models;
using LanFlow.Desktop.Presentation;
using Xunit;

namespace LanFlow.Desktop.Tests;

public sealed class WindowAppearanceControllerTests
{
    [Theory]
    [InlineData(0.40)]
    [InlineData(0.85)]
    [InlineData(1.00)]
    public void WholeWindow_UsesOneOpacityForWindowAndOpaqueSurfaces(double opacity)
    {
        var state = WindowAppearanceController.Calculate(
            SettingsOptionValues.TransparencyWholeWindow,
            layeredOpacity: 0.85,
            wholeWindowOpacity: opacity);

        Assert.Equal(opacity, state.WindowOpacity, 3);
        Assert.Equal(255, state.SurfaceAlpha);
        Assert.Equal(1.0, state.ContentOpacity, 3);
    }

    [Theory]
    [InlineData(0.40, 102)]
    [InlineData(0.85, 217)]
    [InlineData(1.00, 255)]
    public void Layered_LeavesWindowAndContentOpaqueAndChangesSurfaceAlpha(double opacity, byte alpha)
    {
        var state = WindowAppearanceController.Calculate(
            SettingsOptionValues.TransparencyLayered,
            layeredOpacity: opacity,
            wholeWindowOpacity: 0.85);

        Assert.Equal(1.0, state.WindowOpacity, 3);
        Assert.Equal(alpha, state.SurfaceAlpha);
        Assert.Equal(1.0, state.ContentOpacity, 3);
    }

    [Theory]
    [InlineData(SettingsOptionValues.TransparencyLayered, 0.10, 2.00, 1.00, 102)]
    [InlineData(SettingsOptionValues.TransparencyWholeWindow, 0.10, 2.00, 1.00, 255)]
    [InlineData("unexpected", 0.85, 0.60, 1.00, 217)]
    public void Calculate_ClampsOpacityAndDefaultsUnknownModeToLayered(
        string mode,
        double layeredOpacity,
        double wholeWindowOpacity,
        double expectedWindowOpacity,
        byte expectedSurfaceAlpha)
    {
        var state = WindowAppearanceController.Calculate(mode, layeredOpacity, wholeWindowOpacity);

        Assert.Equal(expectedWindowOpacity, state.WindowOpacity, 3);
        Assert.Equal(expectedSurfaceAlpha, state.SurfaceAlpha);
        Assert.Equal(1.0, state.ContentOpacity, 3);
    }
}
