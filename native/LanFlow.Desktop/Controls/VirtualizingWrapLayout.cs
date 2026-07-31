using System;
using System.Windows;

namespace LanFlow.Desktop.Controls;

public sealed class VirtualizingWrapLayout
{
    private readonly double _itemWidth;
    private readonly double _itemHeight;
    private readonly double _horizontalSpacing;
    private readonly double _verticalSpacing;
    private readonly int _bufferRows;

    public VirtualizingWrapLayout(
        double itemWidth,
        double itemHeight,
        double horizontalSpacing,
        double verticalSpacing,
        int bufferRows)
    {
        _itemWidth = RequirePositiveFinite(itemWidth, nameof(itemWidth));
        _itemHeight = RequirePositiveFinite(itemHeight, nameof(itemHeight));
        _horizontalSpacing = RequireNonNegativeFinite(horizontalSpacing, nameof(horizontalSpacing));
        _verticalSpacing = RequireNonNegativeFinite(verticalSpacing, nameof(verticalSpacing));

        if (bufferRows < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bufferRows));
        }

        _bufferRows = bufferRows;
    }

    public Size CalculateExtent(int itemCount, double viewportWidth)
    {
        if (itemCount <= 0)
        {
            return new Size(0, 0);
        }

        int columns = CalculateColumns(viewportWidth);
        int rows = (itemCount + columns - 1) / columns;
        double width = (columns * _itemWidth) + ((columns - 1) * _horizontalSpacing);
        double height = (rows * _itemHeight) + ((rows - 1) * _verticalSpacing);
        return new Size(width, height);
    }

    public ViewportRange CalculateRange(
        int itemCount,
        double viewportWidth,
        double viewportHeight,
        double verticalOffset)
    {
        if (itemCount <= 0)
        {
            return ViewportRange.Empty;
        }

        int columns = CalculateColumns(viewportWidth);
        double rowPitch = _itemHeight + _verticalSpacing;
        double safeOffset = NormalizeNonNegative(verticalOffset);
        double safeViewportHeight = NormalizeNonNegative(viewportHeight);
        int rowCount = (itemCount + columns - 1) / columns;

        int firstVisibleRow = (int)Math.Floor(safeOffset / rowPitch);
        int lastVisibleRow = (int)Math.Floor((safeOffset + safeViewportHeight) / rowPitch);
        int firstRow = Math.Clamp(firstVisibleRow - _bufferRows, 0, rowCount - 1);
        int lastRow = Math.Clamp(lastVisibleRow + _bufferRows, firstRow, rowCount - 1);

        int firstIndex = firstRow * columns;
        int lastIndex = Math.Min(itemCount - 1, ((lastRow + 1) * columns) - 1);
        return new ViewportRange(firstIndex, lastIndex, columns);
    }

    public Rect GetItemRect(int index, int columns)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);

        int row = index / columns;
        int column = index % columns;
        return new Rect(
            column * (_itemWidth + _horizontalSpacing),
            row * (_itemHeight + _verticalSpacing),
            _itemWidth,
            _itemHeight);
    }

    public int IndexFromPoint(Point point, int itemCount, int columns)
    {
        if (itemCount <= 0)
        {
            return -1;
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);

        int rowCount = (itemCount + columns - 1) / columns;
        int column = NearestSlot(point.X, _itemWidth, _horizontalSpacing);
        int row = NearestSlot(point.Y, _itemHeight, _verticalSpacing);
        row = Math.Clamp(row, 0, rowCount - 1);
        column = Math.Clamp(column, 0, columns - 1);

        int rowStart = row * columns;
        int rowLastIndex = Math.Min(itemCount - 1, rowStart + columns - 1);
        return Math.Clamp(rowStart + column, rowStart, rowLastIndex);
    }

    public int MoveIndex(
        int index,
        NavigationDirection direction,
        int itemCount,
        int columns)
    {
        if (itemCount <= 0)
        {
            return -1;
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        index = Math.Clamp(index, 0, itemCount - 1);

        int column = index % columns;
        return direction switch
        {
            NavigationDirection.Left when column > 0 => index - 1,
            NavigationDirection.Right when column < columns - 1 && index + 1 < itemCount => index + 1,
            NavigationDirection.Up when index >= columns => index - columns,
            NavigationDirection.Down when index + columns < itemCount => index + columns,
            NavigationDirection.Down when ((index / columns) + 1) * columns < itemCount => itemCount - 1,
            _ => index
        };
    }

    private int CalculateColumns(double viewportWidth)
    {
        double safeWidth = NormalizeNonNegative(viewportWidth);
        double columnPitch = _itemWidth + _horizontalSpacing;
        return Math.Max(1, (int)Math.Floor((safeWidth + _horizontalSpacing) / columnPitch));
    }

    private static int NearestSlot(double coordinate, double itemSize, double spacing)
    {
        if (!double.IsFinite(coordinate) || coordinate <= 0)
        {
            return 0;
        }

        double pitch = itemSize + spacing;
        int slot = (int)Math.Floor(coordinate / pitch);
        double withinSlot = coordinate - (slot * pitch);
        if (withinSlot <= itemSize || spacing == 0)
        {
            return slot;
        }

        double distanceToCurrent = withinSlot - itemSize;
        double distanceToNext = pitch - withinSlot;
        return distanceToNext <= distanceToCurrent ? slot + 1 : slot;
    }

    private static double RequirePositiveFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return value;
    }

    private static double RequireNonNegativeFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return value;
    }

    private static double NormalizeNonNegative(double value) =>
        double.IsFinite(value) && value > 0 ? value : 0;
}
