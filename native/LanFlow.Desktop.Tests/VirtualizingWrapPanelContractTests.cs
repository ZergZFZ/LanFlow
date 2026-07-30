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
