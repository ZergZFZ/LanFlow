using System.IO;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using LanFlow.Desktop.Controls;
using Xunit;

namespace LanFlow.Desktop.Tests;

public sealed class VirtualizingWrapPanelContractTests
{
    [Fact]
    public void Panel_ExposesRequiredLayoutAndViewportContract()
    {
        Assert.True(typeof(VirtualizingPanel).IsAssignableFrom(typeof(VirtualizingWrapPanel)));
        Assert.True(typeof(IScrollInfo).IsAssignableFrom(typeof(VirtualizingWrapPanel)));
        Assert.NotNull(VirtualizingWrapPanel.ItemWidthProperty);
        Assert.NotNull(VirtualizingWrapPanel.ItemHeightProperty);
        Assert.NotNull(VirtualizingWrapPanel.HorizontalSpacingProperty);
        Assert.NotNull(VirtualizingWrapPanel.VerticalSpacingProperty);
        Assert.NotNull(VirtualizingWrapPanel.BufferRowsProperty);
        Assert.NotNull(typeof(VirtualizingWrapPanel).GetEvent(nameof(VirtualizingWrapPanel.ViewportChanged)));
        Assert.NotNull(typeof(VirtualizingWrapPanel).GetProperty(nameof(VirtualizingWrapPanel.RealizedRange)));
        Assert.NotNull(typeof(VirtualizingWrapPanel).GetProperty(nameof(VirtualizingWrapPanel.RealizedIndices)));
    }

    [Fact]
    public void MeasureOverride_GuardsUnavailableGeneratorBeforeRealization()
    {
        string source = File.ReadAllText(GetPanelSourcePath());
        int measureOverride = source.IndexOf("protected override Size MeasureOverride", StringComparison.Ordinal);
        int generatorRead = FindAfter(source, "ItemContainerGenerator;", measureOverride);
        int nullGuard = FindAfter(source, "generator is null", generatorRead);
        int realization = FindAfter(source, "RealizeRange(generator, range);", generatorRead);
        int arrangeOverride = source.IndexOf("protected override Size ArrangeOverride", StringComparison.Ordinal);
        int arrangeGeneratorRead = FindAfter(source, "ItemContainerGenerator;", arrangeOverride);
        int arrangeNullGuard = FindAfter(source, "generator is null", arrangeGeneratorRead);
        int arrangeGeneratorUse = FindAfter(source, "generator.IndexFromGeneratorPosition", arrangeGeneratorRead);

        Assert.True(generatorRead >= 0, "MeasureOverride must read the panel item container generator.");
        Assert.True(nullGuard > generatorRead, "MeasureOverride must handle a temporarily unavailable generator.");
        Assert.True(realization > nullGuard, "The measure null guard must execute before item realization.");
        Assert.True(arrangeGeneratorRead >= 0, "ArrangeOverride must read the panel item container generator.");
        Assert.True(arrangeNullGuard > arrangeGeneratorRead, "ArrangeOverride must handle a temporarily unavailable generator.");
        Assert.True(arrangeGeneratorUse > arrangeNullGuard, "The arrange null guard must execute before using the generator.");
    }

    [Fact]
    public void MainWindow_UsesRecyclingVirtualizationWithoutPlainWrapPanel()
    {
        string xaml = File.ReadAllText(GetMainWindowXamlPath());

        Assert.Contains("controls:VirtualizingWrapPanel", xaml);
        Assert.Contains("VirtualizingPanel.VirtualizationMode=\"Recycling\"", xaml);
        Assert.Contains("ScrollViewer.CanContentScroll=\"True\"", xaml);
        Assert.DoesNotContain("<WrapPanel", xaml);
    }

    [Fact]
    public void MainWindow_DefinesVirtualizedWrapAndListPanelTemplates()
    {
        string xaml = File.ReadAllText(GetMainWindowXamlPath());

        Assert.Contains("x:Key=\"VirtualizingWrapItemsPanel\"", xaml);
        Assert.Contains("x:Key=\"VirtualizingListItemsPanel\"", xaml);
        Assert.Contains("VirtualizingStackPanel", xaml);
    }

    private static int FindAfter(string source, string value, int startIndex) =>
        startIndex >= 0
            ? source.IndexOf(value, startIndex, StringComparison.Ordinal)
            : -1;

    private static string GetPanelSourcePath() =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "LanFlow.Desktop",
            "Controls",
            "VirtualizingWrapPanel.cs"));

    private static string GetMainWindowXamlPath() =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "LanFlow.Desktop",
            "MainWindow.xaml"));
}
