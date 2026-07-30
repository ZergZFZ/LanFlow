using System;
using LanFlow.Desktop.Models;

namespace LanFlow.Desktop.Presentation;

public static class LegacySettingsControlMapper
{
    public static void ApplyLayoutToggle(Settings settings, bool cardEnabled, bool isExplicitLayoutChange)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!isExplicitLayoutChange) return;
        settings.LayoutMode = cardEnabled
            ? SettingsOptionValues.CardLayout
            : SettingsOptionValues.GridLayout;
    }

    public static void ApplyOpacity(Settings settings, double opacity)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.TransparencyMode == SettingsOptionValues.TransparencyWholeWindow)
        {
            settings.WholeWindowOpacity = opacity;
        }
        else
        {
            settings.LayeredOpacity = opacity;
        }

        settings.Opacity = opacity;
    }
}
