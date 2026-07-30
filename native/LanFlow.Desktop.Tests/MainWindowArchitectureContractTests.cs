using System.IO;
using System.Reflection;
using LanFlow.Desktop.Models;
using LanFlow.Desktop.Presentation;
using Xunit;

namespace LanFlow.Desktop.Tests;

public sealed class MainWindowArchitectureContractTests
{
    [Fact]
    public void ProductionCode_DoesNotContainLegacyMainWindowPaths()
    {
        var mainWindow = ReadDesktopFile("MainWindow.xaml.cs");
        var settingsWindow = ReadDesktopFile("Views", "SettingsWindow.xaml.cs");
        var productionCode = mainWindow + settingsWindow;

        foreach (var legacyPath in new[]
                 {
                     "ApplyItemMetrics",
                     "RefreshGroupTabs",
                     "LoadIcons()",
                     "ItemList.UpdateLayout()",
                     "ShellIconService.GetIcon",
                     "Clone(Settings",
                 })
        {
            Assert.DoesNotContain(legacyPath, productionCode, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void MainWindow_DoesNotDeclarePlatformOrExtractedBusinessState()
    {
        var mainWindow = ReadDesktopFile("MainWindow.xaml.cs");

        foreach (var extractedMember in new[]
                 {
                     "[DllImport",
                     "GetWindowLong",
                     "SetWindowLong",
                     "SetWindowPos",
                     "DwmExtendFrameIntoClientArea",
                     "struct MARGINS",
                     "_dragSourceGroup",
                     "_dragSourceIndex",
                     "_dragStartedWhileFiltering",
                     "_previewTargetItem",
                     "_previewInsertIndex",
                     "_iconLru",
                     "_hoverTimer",
                     "CalculateOpacity",
                     "SurfaceAlpha",
                 })
        {
            Assert.DoesNotContain(extractedMember, mainWindow, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SettingsCoordinator_ExposesSingleApplyCoreAndThreeIntentMethods()
    {
        var type = typeof(MainWindowSettingsCoordinator);

        AssertPublicInstanceMethod(type, "Preview");
        AssertPublicInstanceMethod(type, "Apply");
        AssertPublicInstanceMethod(type, "Restore");
        Assert.Single(type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(method => method.Name == "ApplyCore"));

        var code = ReadDesktopFile("Presentation", "MainWindowSettingsCoordinator.cs");
        Assert.Equal(1, CountOccurrences(code, "_persistSettings("));
    }

    [Fact]
    public void DragDropCoordinator_ExposesLifecycleAndOwnsDragState()
    {
        var type = typeof(LauncherDragDropCoordinator);

        AssertPublicInstanceMethod(type, "Begin");
        AssertPublicInstanceMethod(type, "Update");
        AssertPublicInstanceMethod(type, "Drop");
        AssertPublicInstanceMethod(type, "Cancel");

        var code = ReadDesktopFile("Presentation", "LauncherDragDropCoordinator.cs");
        Assert.Contains("VirtualizingWrapLayout", code, StringComparison.Ordinal);
        Assert.Contains("_sourceGroup", code, StringComparison.Ordinal);
        Assert.Contains("_sourceIndex", code, StringComparison.Ordinal);
        Assert.Contains("_generation", code, StringComparison.Ordinal);
        Assert.Contains("MapVisibleInsertIndexToSource", code, StringComparison.Ordinal);
    }

    [Fact]
    public void DragDropCoordinator_DropAllowsMovingIntoFrequencySortedGroup()
    {
        var item = new LauncherItem { Name = "Source" };
        var source = new Group { Name = "Source", SortMode = "custom" };
        var target = new Group { Name = "Target", SortMode = "frequency" };
        source.Items.Add(item);
        var saveCount = 0;
        var coordinator = new LauncherDragDropCoordinator(
            () => { },
            () => saveCount++,
            _ => { });
        long generation = coordinator.Begin(item, source, isFiltering: false);

        bool dropped = coordinator.Drop(generation, target, target.Items, 0);

        Assert.True(dropped);
        Assert.Empty(source.Items);
        Assert.Same(item, Assert.Single(target.Items));
        Assert.Equal(1, saveCount);
    }

    [Fact]
    public void MainWindow_ComposesAndUsesBothCoordinators()
    {
        var mainWindow = ReadDesktopFile("MainWindow.xaml.cs");

        Assert.Contains("MainWindowSettingsCoordinator", mainWindow, StringComparison.Ordinal);
        Assert.Contains("LauncherDragDropCoordinator", mainWindow, StringComparison.Ordinal);
        Assert.Contains("_settingsCoordinator.Preview", mainWindow, StringComparison.Ordinal);
        Assert.Contains("_settingsCoordinator.Apply", mainWindow, StringComparison.Ordinal);
        Assert.Contains("_settingsCoordinator.Restore", mainWindow, StringComparison.Ordinal);
        Assert.Contains("_dragDropCoordinator.Begin", mainWindow, StringComparison.Ordinal);
        Assert.Contains("_dragDropCoordinator.Update", mainWindow, StringComparison.Ordinal);
        Assert.Contains("_dragDropCoordinator.Drop", mainWindow, StringComparison.Ordinal);
        Assert.Contains("_dragDropCoordinator.Cancel", mainWindow, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = text.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }

    private static void AssertPublicInstanceMethod(Type type, string name) =>
        Assert.Contains(
            type.GetMethods(BindingFlags.Instance | BindingFlags.Public),
            method => method.Name == name);

    private static string ReadDesktopFile(params string[] parts) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "LanFlow.Desktop",
            Path.Combine(parts))));
}
