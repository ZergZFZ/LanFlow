using System.IO;
using Xunit;

namespace LanFlow.Desktop.Tests;

public sealed class SettingsWindowContractTests
{
    [Fact]
    public void SettingsWindow_UsesCategoryContentAndFixedFooterRegions()
    {
        var xaml = File.ReadAllText(GetDesktopPath("Views", "SettingsWindow.xaml"));

        Assert.Contains("x:Name=\"CategoryList\"", xaml);
        Assert.Contains("Width=\"184\"", xaml);
        Assert.Contains("x:Name=\"CategoryContentScrollViewer\"", xaml);
        Assert.Contains("x:Name=\"SettingsFooter\"", xaml);
        Assert.Contains("Grid.Row=\"1\"", xaml);
        Assert.Contains("Grid.ColumnSpan=\"2\"", xaml);
        Assert.DoesNotContain("<TabControl", xaml);
    }

    [Fact]
    public void SettingsWindow_ExposesAllApprovedCategoryPanelsAndCompactFieldRows()
    {
        var xaml = File.ReadAllText(GetDesktopPath("Views", "SettingsWindow.xaml"));

        foreach (var panelName in new[]
                 {
                     "AppearancePanel",
                     "LayoutPanel",
                     "GroupsPanel",
                     "TransparencyPanel",
                     "InteractionPanel",
                     "StartupPanel",
                     "PerformancePanel",
                     "AboutPanel",
                 })
        {
            Assert.Contains($"x:Name=\"{panelName}\"", xaml);
        }

        Assert.Contains("Style=\"{StaticResource SettingsFieldRowStyle}\"", xaml);
        Assert.Contains("x:Name=\"SaveButton\"", xaml);
        Assert.Contains("IsEnabled=\"{Binding HasChanges}\"", xaml);
        Assert.Contains("x:Name=\"ResetOpacityButton\"", xaml);
        Assert.Contains("x:Name=\"ClearIconCacheButton\"", xaml);
    }

    [Fact]
    public void SettingsWindow_OffersEveryLayoutTransparencyAndAnimationChoice()
    {
        var xaml = File.ReadAllText(GetDesktopPath("Views", "SettingsWindow.xaml"));

        Assert.Contains("Tag=\"grid\"", xaml);
        Assert.Contains("Tag=\"list\"", xaml);
        Assert.Contains("Tag=\"card\"", xaml);
        Assert.Contains("Tag=\"layered\"", xaml);
        Assert.Contains("Tag=\"wholeWindow\"", xaml);
        Assert.Contains("Tag=\"system\"", xaml);
        Assert.Contains("Tag=\"on\"", xaml);
        Assert.Contains("Tag=\"off\"", xaml);
    }

    [Fact]
    public void ComponentDictionary_DefinesSettingsLayoutStyles()
    {
        var xaml = File.ReadAllText(GetDesktopPath("Themes", "Components.xaml"));

        Assert.Contains("x:Key=\"SettingsCategoryItemStyle\"", xaml);
        Assert.Contains("x:Key=\"SettingsSectionStyle\"", xaml);
        Assert.Contains("x:Key=\"SettingsFieldRowStyle\"", xaml);
        Assert.Contains("x:Key=\"SettingsFooterStyle\"", xaml);
    }

    [Fact]
    public void SettingsWindow_UsesViewModelUpdatesInsteadOfLegacyBulkControlCopy()
    {
        var codeBehind = File.ReadAllText(GetDesktopPath("Views", "SettingsWindow.xaml.cs"));

        Assert.Contains("private readonly SettingsWindowViewModel _viewModel", codeBehind);
        Assert.DoesNotContain("private readonly SettingsPreviewSession _session", codeBehind);
        Assert.DoesNotContain("private void SettingsChanged", codeBehind);
    }

    [Fact]
    public void MainWindow_ConnectsTheIconCacheActionToSettings()
    {
        var mainWindow = File.ReadAllText(GetDesktopPath("MainWindow.xaml.cs"));

        Assert.Contains("new SettingsWindow(session, _iconService.Clear)", mainWindow);
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
