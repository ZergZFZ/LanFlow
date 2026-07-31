using System.IO;
using LanFlow.Desktop.Models;
using LanFlow.Desktop.Presentation;
using Xunit;

namespace LanFlow.Desktop.Tests;

public sealed class ContentTransitionControllerTests
{
    [Theory]
    [InlineData(SettingsOptionValues.AnimationSystem, true, true, true)]
    [InlineData(SettingsOptionValues.AnimationSystem, false, true, false)]
    [InlineData(SettingsOptionValues.AnimationOn, false, true, true)]
    [InlineData(SettingsOptionValues.AnimationOff, true, true, false)]
    [InlineData(SettingsOptionValues.AnimationOn, true, false, false)]
    [InlineData(SettingsOptionValues.AnimationSystem, true, false, false)]
    [InlineData("unexpected", true, true, true)]
    public void ShouldAnimate_RequiresPreferenceAndCacheHit(
        string mode,
        bool systemEnabled,
        bool cacheHit,
        bool expected)
    {
        Assert.Equal(
            expected,
            ContentTransitionController.ShouldAnimate(mode, systemEnabled, cacheHit));
    }
    [Fact]
    public void Transition_UsesFixedOpacityAndTranslationWithoutLayoutAnimation()
    {
        var source = File.ReadAllText(GetDesktopPath("Presentation", "ContentTransitionController.cs"));
        var xaml = File.ReadAllText(GetDesktopPath("MainWindow.xaml"));
        var combined = source + xaml;

        Assert.Contains("TimeSpan.FromMilliseconds(100)", source);
        Assert.Contains("DoubleAnimation(0.92, 1.0", source);
        Assert.Contains("DoubleAnimation(4.0, 0.0", source);
        Assert.Contains("<TranslateTransform />", xaml);
        Assert.DoesNotContain("ScaleTransform", combined);
        Assert.DoesNotContain("BounceEase", combined);
        Assert.DoesNotContain("WidthProperty", source);
        Assert.DoesNotContain("HeightProperty", source);
        Assert.DoesNotContain("MarginProperty", source);
    }

    private static string GetDesktopPath(params string[] parts) =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "LanFlow.Desktop",
            Path.Combine(parts)));
}
