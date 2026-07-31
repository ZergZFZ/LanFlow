using System.Windows;
using LanFlow.Desktop.Controls;
using Xunit;

namespace LanFlow.Desktop.Tests;

public sealed class VirtualizingWrapLayoutTests
{
    private static readonly VirtualizingWrapLayout Layout = new(
        itemWidth: 100,
        itemHeight: 80,
        horizontalSpacing: 8,
        verticalSpacing: 10,
        bufferRows: 1);

    [Fact]
    public void CalculateRange_IncludesOneBufferRowAroundViewport()
    {
        var range = Layout.CalculateRange(
            itemCount: 100,
            viewportWidth: 440,
            viewportHeight: 180,
            verticalOffset: 180);

        Assert.Equal(4, range.Columns);
        Assert.Equal(4, range.FirstIndex);
        Assert.Equal(23, range.LastIndex);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(3, 324, 0)]
    [InlineData(4, 0, 90)]
    public void GetItemRect_MapsLogicalIndexToStableCoordinates(
        int index,
        double x,
        double y)
    {
        Assert.Equal(
            new Rect(x, y, 100, 80),
            Layout.GetItemRect(index, columns: 4));
    }

    [Theory]
    [InlineData(5, NavigationDirection.Left, 4)]
    [InlineData(5, NavigationDirection.Right, 6)]
    [InlineData(5, NavigationDirection.Up, 1)]
    [InlineData(5, NavigationDirection.Down, 9)]
    public void MoveIndex_UsesLogicalGridRatherThanRealizedContainers(
        int index,
        NavigationDirection direction,
        int expected)
    {
        Assert.Equal(
            expected,
            Layout.MoveIndex(index, direction, itemCount: 20, columns: 4));
    }

    [Fact]
    public void CalculateRange_EmptyCollectionReturnsEmptyRange()
    {
        Assert.Equal(
            ViewportRange.Empty,
            Layout.CalculateRange(0, 440, 180, 0));
    }

    [Fact]
    public void CalculateRange_NarrowViewportStillUsesOneColumn()
    {
        var range = Layout.CalculateRange(10, 20, 80, 0);

        Assert.Equal(1, range.Columns);
        Assert.Equal(0, range.FirstIndex);
    }

    [Theory]
    [InlineData(18, NavigationDirection.Down, 18)]
    [InlineData(19, NavigationDirection.Down, 19)]
    [InlineData(19, NavigationDirection.Right, 19)]
    public void MoveIndex_ClampsAtIncompleteLastRow(
        int index,
        NavigationDirection direction,
        int expected)
    {
        Assert.Equal(
            expected,
            Layout.MoveIndex(index, direction, itemCount: 20, columns: 6));
    }

    [Theory]
    [InlineData(106, 20, 1)]
    [InlineData(107, 20, 1)]
    [InlineData(105, 85, 5)]
    [InlineData(500, 500, 19)]
    [InlineData(-50, -50, 0)]
    public void IndexFromPoint_MapsSpacingToNearestLegalItem(
        double x,
        double y,
        int expected)
    {
        Assert.Equal(
            expected,
            Layout.IndexFromPoint(new Point(x, y), itemCount: 20, columns: 4));
    }

    [Fact]
    public void CalculateExtent_UsesLogicalRowsAndSpacing()
    {
        Assert.Equal(
            new Size(424, 260),
            Layout.CalculateExtent(itemCount: 10, viewportWidth: 440));
    }

    [Fact]
    public void GetItemRect_AppliesIndependentHorizontalAndVerticalSpacing()
    {
        var layout = new VirtualizingWrapLayout(100, 80, 8, 12, 0);

        Assert.Equal(new Rect(108, 0, 100, 80), layout.GetItemRect(1, columns: 4));
        Assert.Equal(new Rect(0, 92, 100, 80), layout.GetItemRect(4, columns: 4));
    }

    [Theory]
    [InlineData(0, 80, 8, 10, 1)]
    [InlineData(100, 0, 8, 10, 1)]
    [InlineData(100, 80, -1, 10, 1)]
    [InlineData(100, 80, 8, -1, 1)]
    [InlineData(100, 80, 8, 10, -1)]
    public void Constructor_RejectsInvalidGeometry(
        double itemWidth,
        double itemHeight,
        double horizontalSpacing,
        double verticalSpacing,
        int bufferRows)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new VirtualizingWrapLayout(
                itemWidth,
                itemHeight,
                horizontalSpacing,
                verticalSpacing,
                bufferRows));
    }
}
