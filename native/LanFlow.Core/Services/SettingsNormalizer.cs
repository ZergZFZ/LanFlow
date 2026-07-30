using LanFlow.Desktop.Models;

namespace LanFlow.Desktop.Services;

public static class SettingsNormalizer
{
    public static void ClampPreviewValues(Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.GroupLabelSize = Math.Clamp(settings.GroupLabelSize, 28, 52);
        settings.GroupLabelFontSize = Math.Clamp(settings.GroupLabelFontSize, 11, 18);
        settings.GroupNavigationWidth = Math.Clamp(settings.GroupNavigationWidth, 96, 280);
        settings.LayeredOpacity = Math.Clamp(settings.LayeredOpacity, 0.40, 1.00);
        settings.WholeWindowOpacity = Math.Clamp(settings.WholeWindowOpacity, 0.40, 1.00);
    }
}
