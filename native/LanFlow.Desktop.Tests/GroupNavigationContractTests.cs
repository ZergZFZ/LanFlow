using System.IO;
using LanFlow.Desktop.Controls;
using Xunit;

namespace LanFlow.Desktop.Tests;

public sealed class GroupNavigationContractTests
{
    [Fact]
    public void MainWindow_ContainsExactlyOneGroupNavigationControl()
    {
        var xaml = File.ReadAllText(GetDesktopPath("MainWindow.xaml"));

        Assert.Equal(1, CountOccurrences(xaml, "controls:GroupNavigationControl"));
        Assert.DoesNotContain("x:Name=\"GroupTabs\"", xaml);
        Assert.DoesNotContain("x:Name=\"TopGroupTabs\"", xaml);
    }

    [Fact]
    public void MainWindow_NoLongerRebuildsGroupButtons()
    {
        var source = File.ReadAllText(GetDesktopPath("MainWindow.xaml.cs"));

        Assert.DoesNotContain("RefreshGroupTabs", source);
        Assert.DoesNotContain("GroupTabs.Children.Clear", source);
        Assert.DoesNotContain("GroupTabs.Children.Add", source);
    }

    [Fact]
    public void Control_ExposesDataSelectionLayoutAndSizingProperties()
    {
        Assert.NotNull(GroupNavigationControl.ItemsSourceProperty);
        Assert.NotNull(GroupNavigationControl.SelectedItemProperty);
        Assert.NotNull(GroupNavigationControl.GroupLayoutProperty);
        Assert.NotNull(GroupNavigationControl.GroupLabelSizeProperty);
        Assert.NotNull(GroupNavigationControl.GroupLabelFontSizeProperty);
        Assert.NotNull(GroupNavigationControl.GroupNavigationWidthProperty);
    }

    [Theory]
    [InlineData(nameof(GroupNavigationControl.GroupInvoked), nameof(GroupNavigationControl.GroupInvokedEvent))]
    [InlineData(nameof(GroupNavigationControl.GroupHovered), nameof(GroupNavigationControl.GroupHoveredEvent))]
    [InlineData(nameof(GroupNavigationControl.GroupDragHovered), nameof(GroupNavigationControl.GroupDragHoveredEvent))]
    [InlineData(nameof(GroupNavigationControl.GroupDropped), nameof(GroupNavigationControl.GroupDroppedEvent))]
    public void Control_ExposesRequiredRoutedEvents(string eventName, string routedEventFieldName)
    {
        Assert.NotNull(typeof(GroupNavigationControl).GetEvent(eventName));
        Assert.NotNull(typeof(GroupNavigationControl).GetField(routedEventFieldName));
    }

    [Fact]
    public void Control_UsesOneListBoxWithVirtualizedTopAndLeftPanels()
    {
        var xaml = File.ReadAllText(GetDesktopPath("Controls", "GroupNavigationControl.xaml"));

        Assert.Equal(1, CountOccurrences(xaml, "<ListBox "));
        Assert.Contains("VirtualizingStackPanel", xaml);
        Assert.Contains("GroupLabelFontSize", xaml);
        Assert.Contains("TextTrimming=\"CharacterEllipsis\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"{Binding Name}\"", xaml);
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
