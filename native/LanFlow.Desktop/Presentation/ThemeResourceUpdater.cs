using System;
using System.Windows;
using System.Windows.Media;
using LanFlow.Desktop.Models;

namespace LanFlow.Desktop.Presentation;

public sealed class ThemeResourceUpdater
{
    public void Apply(ResourceDictionary resources, ThemeColors colors)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(colors);

        Set(resources, "WindowBackgroundBrush", colors.Panel);
        Set(resources, "SurfaceBrush", colors.Surface);
        Set(resources, "ItemHoverBrush", colors.Hover);
        Set(resources, "ItemSelectedBrush", colors.Accent);
        Set(resources, "PrimaryTextBrush", colors.TextPrimary);
        Set(resources, "SecondaryTextBrush", colors.TextSecondary);
        Set(resources, "FocusBorderBrush", colors.Accent);
        Set(resources, "GroupTabSelectedBrush", colors.Accent);
        Set(resources, "WindowBorderBrush", colors.PanelBorder);
        Set(resources, "DividerBrush", colors.SurfaceBorder);
        Set(resources, "MutedSurfaceBrush", colors.Footer);
        Set(resources, "IconSurfaceBrush", colors.IconSurface);
        Set(resources, "DragIndicatorBrush", colors.Accent);

        // Transitional aliases keep secondary windows and older local styles compatible.
        Set(resources, "PanelBrush", colors.Panel);
        Set(resources, "PanelBorderBrush", colors.PanelBorder);
        Set(resources, "SurfaceBorderBrush", colors.SurfaceBorder);
        Set(resources, "FooterBrush", colors.Footer);
        Set(resources, "TextPrimaryBrush", colors.TextPrimary);
        Set(resources, "TextSecondaryBrush", colors.TextSecondary);
        Set(resources, "AccentBrush", colors.Accent);
        Set(resources, "SelectedTileBrush", colors.Accent);
        Set(resources, "HoverBrush", colors.Hover);
    }

    private static void Set(ResourceDictionary resources, string key, string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        if (brush.CanFreeze)
        {
            brush.Freeze();
        }

        resources[key] = brush;
    }
}