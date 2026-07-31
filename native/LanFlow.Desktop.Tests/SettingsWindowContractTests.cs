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
        Assert.Contains("<ColumnDefinition Width=\"200\" />", xaml);
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
        Assert.Contains("Tag=\"card\"", xaml);
        Assert.DoesNotContain("x:Name=\"ListLayoutRadio\"", xaml);
        Assert.Contains("列间距", xaml);
        Assert.DoesNotContain("项目间距", xaml);
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

        Assert.Contains("new SettingsWindow(session, _iconService.Clear", mainWindow);
    }

    [Fact]
    public void SettingsWindow_ThrottlesEveryContinuousSliderAndFlushesDragCompletion()
    {
        var xaml = File.ReadAllText(GetDesktopPath("Views", "SettingsWindow.xaml"));
        var codeBehind = File.ReadAllText(GetDesktopPath("Views", "SettingsWindow.xaml.cs"));

        Assert.Equal(
            12,
            System.Text.RegularExpressions.Regex.Matches(
                xaml,
                "primitives:Thumb.DragCompleted=\"ContinuousSlider_DragCompleted\"").Count);
        Assert.Contains("TimeSpan.FromMilliseconds(33)", codeBehind);
        Assert.Contains("PreviewThrottle<double>", codeBehind);
        Assert.Contains("FlushPreviewThrottles();", codeBehind);
    }

    [Fact]
    public void SettingsWindow_HoverDelaySliderUsesSharedPreviewChannel()
    {
        var xaml = File.ReadAllText(GetDesktopPath("Views", "SettingsWindow.xaml"));
        var codeBehind = File.ReadAllText(GetDesktopPath("Views", "SettingsWindow.xaml.cs"));
        var viewModel = File.ReadAllText(GetDesktopPath("Presentation", "SettingsWindowViewModel.cs"));

        Assert.Contains("x:Name=\"GroupHoverDelaySlider\"", xaml);
        Assert.Contains("Tag=\"groupHoverDelayMs\"", xaml);
        Assert.Contains("Minimum=\"0\" Maximum=\"500\"", xaml);
        Assert.Contains("\"groupHoverDelayMs\",", codeBehind);
        Assert.DoesNotContain("GroupHoverDelaySlider_ValueChanged", codeBehind);
        Assert.Contains("case \"groupHoverDelayMs\":", viewModel);
    }

    [Fact]
    public void SettingsWindow_ExposesConfigLocationActions()
    {
        var xaml = File.ReadAllText(GetDesktopPath("Views", "SettingsWindow.xaml"));
        var codeBehind = File.ReadAllText(GetDesktopPath("Views", "SettingsWindow.xaml.cs"));

        Assert.Contains("x:Name=\"ConfigPathText\"", xaml);
        Assert.Contains("Click=\"CopyConfig_Click\"", xaml);
        Assert.Contains("Click=\"OpenConfig_Click\"", xaml);
        Assert.Contains("Click=\"ChangeConfigLocation_Click\"", xaml);
        Assert.Contains("Click=\"RestoreConfigLocation_Click\"", xaml);
        Assert.Contains("x:Name=\"ConfigLocationStatusText\"", xaml);
        Assert.Contains("OpenFolderDialog", codeBehind);
        Assert.Contains("SettingsMaintenanceMessages.Describe", codeBehind);
        Assert.Contains("ConfigMigrationStatus.TargetContainsConfig", codeBehind);
    }

    [Fact]
    public void MainWindow_ResolvesConfigDirectoryFromLocator()
    {
        var mainWindow = File.ReadAllText(GetDesktopPath("MainWindow.xaml.cs"));
        var app = File.ReadAllText(GetDesktopPath("App.xaml.cs"));

        Assert.Contains("new ConfigLocationService()", mainWindow);
        Assert.Contains("location.Resolve().DirectoryPath", mainWindow);
        Assert.Contains("ResolveConfigLocationAtStartup", app);
    }

    [Fact]
    public void SettingsWindow_UsesExplicitThreeChoiceCloseFlowWithoutYesNoMessageBox()
    {
        var xaml = File.ReadAllText(GetDesktopPath("Views", "SettingsWindow.xaml"));
        var codeBehind = File.ReadAllText(GetDesktopPath("Views", "SettingsWindow.xaml.cs"));
        var mainWindow = File.ReadAllText(GetDesktopPath("MainWindow.xaml.cs"));

        Assert.Contains("Closing=\"Window_Closing\"", xaml);
        Assert.Contains("UnsavedCloseDecision.ApplyAndClose", codeBehind);
        Assert.Contains("UnsavedCloseDecision.Discard", codeBehind);
        Assert.Contains("UnsavedCloseDecision.KeepEditing", codeBehind);
        Assert.Contains("ShowUnsavedChangesDialog", codeBehind);
        Assert.DoesNotContain("MessageBox.Show", codeBehind);
        Assert.Contains("SettingsCloseFlow.TryComplete", mainWindow);
        Assert.Contains("settingsWindow.CloseDecision", mainWindow);
        Assert.DoesNotContain("var accepted = settingsWindow.ShowDialog() == true;", mainWindow);
    }

    [Fact]
    public void SettingsWindow_SaveActionClosesTheModelessWindow()
    {
        var codeBehind = File.ReadAllText(GetDesktopPath("Views", "SettingsWindow.xaml.cs"));
        int methodStart = codeBehind.IndexOf("private void Save_Click", StringComparison.Ordinal);
        int methodEnd = codeBehind.IndexOf("private void Window_Closing", methodStart, StringComparison.Ordinal);
        string method = codeBehind[methodStart..methodEnd];

        Assert.Contains("Close();", method, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsWindow_PlacesConfigLocationActionsInPerformancePanel()
    {
        var xaml = File.ReadAllText(GetDesktopPath("Views", "SettingsWindow.xaml"));
        int performanceStart = xaml.IndexOf("<StackPanel x:Name=\"PerformancePanel\"", StringComparison.Ordinal);
        int aboutStart = xaml.IndexOf("<StackPanel x:Name=\"AboutPanel\"", StringComparison.Ordinal);
        string performancePanel = xaml[performanceStart..aboutStart];
        string aboutPanel = xaml[aboutStart..];

        Assert.Contains("x:Name=\"ConfigPathText\"", performancePanel, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"ConfigPathText\"", aboutPanel, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsWindow_UsesAStretchNavigationListWithoutNegativeOverflow()
    {
        var xaml = File.ReadAllText(GetDesktopPath("Views", "SettingsWindow.xaml"));

        Assert.DoesNotContain("Width=\"184\"", xaml);
        Assert.DoesNotContain("Margin=\"-8,0\"", xaml);
        Assert.Contains("HorizontalAlignment=\"Stretch\"", xaml);
    }

    [Fact]
    public void SettingsWindow_ReservesEnoughSpaceForTheOpacityPercentageEditor()
    {
        var xaml = File.ReadAllText(GetDesktopPath("Views", "SettingsWindow.xaml"));

        Assert.Contains("x:Name=\"OpacityPercentBox\" Width=\"72\"", xaml);
        Assert.Contains("Padding=\"8,0\"", xaml);
        Assert.Contains("<ColumnDefinition Width=\"100\" />", xaml);
    }

    [Fact]
    public void MainWindow_UsesApplicationThemeResourcesForSecondaryWindows()
    {
        var mainWindow = File.ReadAllText(GetDesktopPath("MainWindow.xaml.cs"));

        Assert.Contains("Application.Current?.Resources ?? Resources", mainWindow);
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
