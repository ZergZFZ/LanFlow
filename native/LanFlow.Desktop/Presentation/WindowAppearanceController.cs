using System;
using System.Windows;
using System.Windows.Media;
using LanFlow.Desktop.Models;

namespace LanFlow.Desktop.Presentation;

public sealed class WindowAppearanceController
{
    private static readonly string[] LayeredSurfaceBrushKeys =
    [
        "WindowBackgroundBrush",
        "SurfaceBrush",
        "MutedSurfaceBrush",
    ];

    public static WindowAppearanceState Calculate(
        string? mode,
        double layeredOpacity,
        double wholeWindowOpacity)
    {
        layeredOpacity = Math.Clamp(layeredOpacity, 0.40, 1.00);
        wholeWindowOpacity = Math.Clamp(wholeWindowOpacity, 0.40, 1.00);

        return string.Equals(
            mode,
            SettingsOptionValues.TransparencyWholeWindow,
            StringComparison.Ordinal)
            ? new WindowAppearanceState(wholeWindowOpacity, byte.MaxValue, 1.0)
            : new WindowAppearanceState(
                1.0,
                (byte)Math.Round(layeredOpacity * byte.MaxValue, MidpointRounding.AwayFromZero),
                1.0);
    }

    public void Apply(
        Window window,
        FrameworkElement surfaceRoot,
        FrameworkElement contentRoot,
        Settings settings)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(surfaceRoot);
        ArgumentNullException.ThrowIfNull(contentRoot);
        ArgumentNullException.ThrowIfNull(settings);

        var state = Calculate(
            settings.TransparencyMode,
            settings.LayeredOpacity,
            settings.WholeWindowOpacity);

        window.Opacity = state.WindowOpacity;
        surfaceRoot.Opacity = 1.0;
        contentRoot.Opacity = state.ContentOpacity;

        foreach (var key in LayeredSurfaceBrushKeys)
        {
            if (window.TryFindResource(key) is not SolidColorBrush source)
            {
                continue;
            }

            surfaceRoot.Resources[key] = CreateBrush(source.Color, state.SurfaceAlpha);
            contentRoot.Resources[key] = CreateBrush(source.Color, state.SurfaceAlpha);
        }
    }

    private static SolidColorBrush CreateBrush(Color source, byte alpha)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, source.R, source.G, source.B));
        if (brush.CanFreeze)
        {
            brush.Freeze();
        }

        return brush;
    }
}
