using LanFlow.Desktop.Models;

namespace LanFlow.Core.Tests;

public sealed class SettingsCloneTests
{
    [Fact]
    public void Clone_CopiesEveryInteractionAndTransparencySetting()
    {
        var source = new Settings
        {
            GroupSwitchMode = SettingsOptionValues.GroupSwitchHover,
            GroupLabelSize = 44,
            GroupLabelFontSize = 16,
            GroupNavigationWidth = 220,
            TransparencyMode = SettingsOptionValues.TransparencyWholeWindow,
            LayeredOpacity = 0.63,
            WholeWindowOpacity = 0.91,
            AnimationMode = SettingsOptionValues.AnimationOff,
        };

        var clone = source.Clone();

        Assert.NotSame(source, clone);
        Assert.Equal(source.GroupSwitchMode, clone.GroupSwitchMode);
        Assert.Equal(source.GroupLabelSize, clone.GroupLabelSize);
        Assert.Equal(source.GroupLabelFontSize, clone.GroupLabelFontSize);
        Assert.Equal(source.GroupNavigationWidth, clone.GroupNavigationWidth);
        Assert.Equal(source.TransparencyMode, clone.TransparencyMode);
        Assert.Equal(source.LayeredOpacity, clone.LayeredOpacity);
        Assert.Equal(source.WholeWindowOpacity, clone.WholeWindowOpacity);
        Assert.Equal(source.AnimationMode, clone.AnimationMode);
    }
}
