using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace LanFlow.Desktop.Tests;

public sealed class ThemeResourceContractTests
{
    private static readonly string[] RequiredSemanticKeys =
    [
        "WindowBackgroundBrush",
        "SurfaceBrush",
        "ItemHoverBrush",
        "ItemSelectedBrush",
        "PrimaryTextBrush",
        "SecondaryTextBrush",
        "FocusBorderBrush",
        "GroupTabSelectedBrush",
        "WindowBorderBrush",
        "MutedSurfaceBrush",
        "DividerBrush",
        "DangerBrush",
        "DragIndicatorBrush",
    ];

    private static readonly string[] RequiredComponentStyles =
    [
        "CommandButtonStyle",
        "PrimaryButtonStyle",
        "CompactTextBoxStyle",
        "GroupTabItemStyle",
        "LauncherItemContainerStyle",
        "SettingsSectionHeaderStyle",
    ];

    private static readonly HashSet<string> LegacyMainWindowColors = new(StringComparer.OrdinalIgnoreCase)
    {
        "#171B28",
        "#343B50",
        "#22283A",
        "#38425B",
        "#1D2231",
        "#F5F7FC",
        "#ADB5C7",
        "#35405E",
        "#5A00B7C3",
        "#2B3247",
        "#2A3040",
        "#CC1F1F1F",
        "#8994AA",
        "#F1BD68",
        "#57627A",
        "#88000000",
        "#000000",
        "#00FFFFFF",
    };

    [Theory]
    [InlineData("Color.Neutral.000")]
    [InlineData("Color.Neutral.050")]
    [InlineData("Color.Neutral.100")]
    [InlineData("Color.Neutral.700")]
    [InlineData("Color.Neutral.900")]
    [InlineData("Color.Accent.500")]
    [InlineData("Color.Danger.500")]
    [InlineData("Space.1")]
    [InlineData("Space.2")]
    [InlineData("Space.3")]
    [InlineData("Space.4")]
    [InlineData("Radius.Small")]
    [InlineData("Radius.Medium")]
    [InlineData("Motion.Fast")]
    [InlineData("Font.Size.Body")]
    [InlineData("Font.Size.Caption")]
    [InlineData("Icon.Size.Command")]
    public void BaseDictionary_DefinesRequiredToken(string key)
    {
        var xaml = File.ReadAllText(GetDesktopPath("Themes", "Tokens.Base.xaml"));

        Assert.Contains($"x:Key=\"{key}\"", xaml);
    }

    [Fact]
    public void SemanticDictionary_DefinesRequiredKeys()
    {
        var xaml = File.ReadAllText(GetDesktopPath("Themes", "Tokens.Semantic.xaml"));

        Assert.All(RequiredSemanticKeys, key => Assert.Contains($"x:Key=\"{key}\"", xaml));
    }

    [Fact]
    public void ComponentDictionary_DefinesRequiredStyles()
    {
        var xaml = File.ReadAllText(GetDesktopPath("Themes", "Components.xaml"));

        Assert.All(RequiredComponentStyles, key => Assert.Contains($"x:Key=\"{key}\"", xaml));
    }

    [Fact]
    public void App_MergesBaseSemanticAndComponentDictionariesInOrder()
    {
        var xaml = File.ReadAllText(GetDesktopPath("App.xaml"));
        var baseIndex = xaml.IndexOf("Tokens.Base.xaml", StringComparison.Ordinal);
        var semanticIndex = xaml.IndexOf("Tokens.Semantic.xaml", StringComparison.Ordinal);
        var componentIndex = xaml.IndexOf("Components.xaml", StringComparison.Ordinal);

        Assert.True(baseIndex >= 0);
        Assert.True(baseIndex < semanticIndex);
        Assert.True(semanticIndex < componentIndex);
    }

    [Fact]
    public void MainWindow_DoesNotIntroduceColorsOutsideMigrationBaseline()
    {
        var xaml = File.ReadAllText(GetDesktopPath("MainWindow.xaml"));
        var colors = Regex.Matches(xaml, "#[0-9A-Fa-f]{6,8}")
            .Select(match => match.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        Assert.All(colors, color => Assert.Contains(color, LegacyMainWindowColors));
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