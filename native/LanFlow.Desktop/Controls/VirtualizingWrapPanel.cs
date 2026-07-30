using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace LanFlow.Desktop.Controls;

public sealed class VirtualizingWrapPanel : VirtualizingPanel, IScrollInfo
{
    public static readonly DependencyProperty ItemWidthProperty =
        DependencyProperty.Register(
            nameof(ItemWidth),
            typeof(double),
            typeof(VirtualizingWrapPanel),
            new FrameworkPropertyMetadata(100d, FrameworkPropertyMetadataOptions.AffectsMeasure, OnLayoutPropertyChanged),
            IsPositiveFinite);

    public static readonly DependencyProperty ItemHeightProperty =
        DependencyProperty.Register(
            nameof(ItemHeight),
            typeof(double),
            typeof(VirtualizingWrapPanel),
            new FrameworkPropertyMetadata(80d, FrameworkPropertyMetadataOptions.AffectsMeasure, OnLayoutPropertyChanged),
            IsPositiveFinite);

    public static readonly DependencyProperty HorizontalSpacingProperty =
        DependencyProperty.Register(
            nameof(HorizontalSpacing),
            typeof(double),
            typeof(VirtualizingWrapPanel),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsMeasure, OnLayoutPropertyChanged),
            IsNonNegativeFinite);

    public static readonly DependencyProperty VerticalSpacingProperty =
        DependencyProperty.Register(
            nameof(VerticalSpacing),
            typeof(double),
            typeof(VirtualizingWrapPanel),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsMeasure, OnLayoutPropertyChanged),
            IsNonNegativeFinite);

    public static readonly DependencyProperty BufferRowsProperty =
        DependencyProperty.Register(
            nameof(BufferRows),
            typeof(int),
            typeof(VirtualizingWrapPanel),
            new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsMeasure, OnLayoutPropertyChanged),
            value => value is int rows && rows >= 0);

    private readonly List<int> _realizedIndices = [];
    private Size _extent;
    private Size _viewport;
    private Point _offset;
    private int _columns = 1;

    public double ItemWidth
    {
        get => (double)GetValue(ItemWidthProperty);
        set => SetValue(ItemWidthProperty, value);
    }

    public double ItemHeight
    {
        get => (double)GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    public double HorizontalSpacing
    {
        get => (double)GetValue(HorizontalSpacingProperty);
        set => SetValue(HorizontalSpacingProperty, value);
    }

    public double VerticalSpacing
    {
        get => (double)GetValue(VerticalSpacingProperty);
        set => SetValue(VerticalSpacingProperty, value);
    }

    public int BufferRows
    {
        get => (int)GetValue(BufferRowsProperty);
        set => SetValue(BufferRowsProperty, value);
    }

    public ViewportRange RealizedRange { get; private set; } = ViewportRange.Empty;

    public IReadOnlyList<int> RealizedIndices => _realizedIndices;

    public event EventHandler<ViewportRange>? ViewportChanged;

    public bool CanHorizontallyScroll { get; set; }

    public bool CanVerticallyScroll { get; set; } = true;

    public double ExtentWidth => _extent.Width;

    public double ExtentHeight => _extent.Height;

    public double ViewportWidth => _viewport.Width;

    public double ViewportHeight => _viewport.Height;

    public double HorizontalOffset => _offset.X;

    public double VerticalOffset => _offset.Y;

    public ScrollViewer? ScrollOwner { get; set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        ItemsControl? owner = ItemsControl.GetItemsOwner(this);
        int itemCount = owner?.Items.Count ?? 0;
        Size viewport = NormalizeViewport(availableSize);
        var layout = CreateLayout();
        Size contentExtent = layout.CalculateExtent(itemCount, viewport.Width);
        Size extent = new(
            Math.Max(viewport.Width, contentExtent.Width),
            Math.Max(viewport.Height, contentExtent.Height));

        UpdateScrollMetrics(extent, viewport);
        ViewportRange range = layout.CalculateRange(
            itemCount,
            viewport.Width,
            viewport.Height,
            _offset.Y);

        if (owner is null || itemCount == 0)
        {
            RemoveAllChildren();
            UpdateRealizedRange(ViewportRange.Empty);
            return viewport;
        }

        IItemContainerGenerator generator = ItemContainerGenerator;
        RemoveOutsideRange(generator, range);
        RealizeRange(generator, range);

        var childSize = new Size(ItemWidth, ItemHeight);
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(childSize);
        }

        RebuildRealizedIndices(generator);
        UpdateRealizedRange(range);
        return viewport;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var layout = CreateLayout();
        IItemContainerGenerator generator = ItemContainerGenerator;

        for (int childIndex = 0; childIndex < InternalChildren.Count; childIndex++)
        {
            int itemIndex = generator.IndexFromGeneratorPosition(new GeneratorPosition(childIndex, 0));
            if (itemIndex < 0)
            {
                continue;
            }

            Rect logicalRect = layout.GetItemRect(itemIndex, _columns);
            logicalRect.Offset(-_offset.X, -_offset.Y);
            InternalChildren[childIndex].Arrange(logicalRect);
        }

        return finalSize;
    }

    public void LineUp() => SetVerticalOffset(VerticalOffset - RowPitch);

    public void LineDown() => SetVerticalOffset(VerticalOffset + RowPitch);

    public void LineLeft() => SetHorizontalOffset(HorizontalOffset - ColumnPitch);

    public void LineRight() => SetHorizontalOffset(HorizontalOffset + ColumnPitch);

    public void MouseWheelUp() => SetVerticalOffset(VerticalOffset - (RowPitch * 3));

    public void MouseWheelDown() => SetVerticalOffset(VerticalOffset + (RowPitch * 3));

    public void MouseWheelLeft() => SetHorizontalOffset(HorizontalOffset - (ColumnPitch * 3));

    public void MouseWheelRight() => SetHorizontalOffset(HorizontalOffset + (ColumnPitch * 3));

    public void PageUp() => SetVerticalOffset(VerticalOffset - ViewportHeight);

    public void PageDown() => SetVerticalOffset(VerticalOffset + ViewportHeight);

    public void PageLeft() => SetHorizontalOffset(HorizontalOffset - ViewportWidth);

    public void PageRight() => SetHorizontalOffset(HorizontalOffset + ViewportWidth);

    public void SetHorizontalOffset(double offset)
    {
        double next = CanHorizontallyScroll
            ? ClampOffset(offset, ExtentWidth, ViewportWidth)
            : 0;
        if (AreClose(next, _offset.X))
        {
            return;
        }

        _offset.X = next;
        ScrollOwner?.InvalidateScrollInfo();
        InvalidateMeasure();
    }

    public void SetVerticalOffset(double offset)
    {
        double next = CanVerticallyScroll
            ? ClampOffset(offset, ExtentHeight, ViewportHeight)
            : 0;
        if (AreClose(next, _offset.Y))
        {
            return;
        }

        _offset.Y = next;
        ScrollOwner?.InvalidateScrollInfo();
        InvalidateMeasure();
    }

    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        ItemsControl? owner = ItemsControl.GetItemsOwner(this);
        DependencyObject? container = FindDirectChild(visual);
        int index = owner is null || container is null
            ? -1
            : owner.ItemContainerGenerator.IndexFromContainer(container);
        if (index < 0)
        {
            return rectangle;
        }

        Rect itemRect = CreateLayout().GetItemRect(index, _columns);
        double targetOffset = VerticalOffset;
        if (itemRect.Top < VerticalOffset)
        {
            targetOffset = itemRect.Top;
        }
        else if (itemRect.Bottom > VerticalOffset + ViewportHeight)
        {
            targetOffset = itemRect.Bottom - ViewportHeight;
        }

        SetVerticalOffset(targetOffset);
        itemRect.Offset(-HorizontalOffset, -VerticalOffset);
        return itemRect;
    }

    private double RowPitch => ItemHeight + VerticalSpacing;

    private double ColumnPitch => ItemWidth + HorizontalSpacing;

    private VirtualizingWrapLayout CreateLayout() =>
        new(ItemWidth, ItemHeight, HorizontalSpacing, VerticalSpacing, BufferRows);

    private void RemoveOutsideRange(IItemContainerGenerator generator, ViewportRange range)
    {
        for (int childIndex = InternalChildren.Count - 1; childIndex >= 0; childIndex--)
        {
            int itemIndex = generator.IndexFromGeneratorPosition(new GeneratorPosition(childIndex, 0));
            if (range.Contains(itemIndex))
            {
                continue;
            }

            var position = new GeneratorPosition(childIndex, 0);
            if (generator is IRecyclingItemContainerGenerator recyclingGenerator)
            {
                recyclingGenerator.Recycle(position, 1);
            }
            else
            {
                generator.Remove(position, 1);
            }

            RemoveInternalChildRange(childIndex, 1);
        }
    }

    private void RealizeRange(IItemContainerGenerator generator, ViewportRange range)
    {
        if (range.FirstIndex < 0)
        {
            return;
        }

        GeneratorPosition startPosition = generator.GeneratorPositionFromIndex(range.FirstIndex);
        int childIndex = startPosition.Offset == 0
            ? startPosition.Index
            : startPosition.Index + 1;

        using IDisposable generation = generator.StartAt(
            startPosition,
            GeneratorDirection.Forward,
            allowStartAtRealizedItem: true);

        for (int itemIndex = range.FirstIndex; itemIndex <= range.LastIndex; itemIndex++, childIndex++)
        {
            if (generator.GenerateNext(out bool newlyRealized) is not UIElement child)
            {
                continue;
            }

            if (newlyRealized)
            {
                if (childIndex >= InternalChildren.Count)
                {
                    AddInternalChild(child);
                }
                else
                {
                    InsertInternalChild(childIndex, child);
                }

                generator.PrepareItemContainer(child);
            }
        }
    }

    private void RebuildRealizedIndices(IItemContainerGenerator generator)
    {
        _realizedIndices.Clear();
        for (int childIndex = 0; childIndex < InternalChildren.Count; childIndex++)
        {
            int itemIndex = generator.IndexFromGeneratorPosition(new GeneratorPosition(childIndex, 0));
            if (itemIndex >= 0)
            {
                _realizedIndices.Add(itemIndex);
            }
        }
    }

    private void RemoveAllChildren()
    {
        if (InternalChildren.Count == 0)
        {
            _realizedIndices.Clear();
            return;
        }

        IItemContainerGenerator generator = ItemContainerGenerator;
        var position = new GeneratorPosition(0, 0);
        if (generator is IRecyclingItemContainerGenerator recyclingGenerator)
        {
            recyclingGenerator.Recycle(position, InternalChildren.Count);
        }
        else
        {
            generator.Remove(position, InternalChildren.Count);
        }

        RemoveInternalChildRange(0, InternalChildren.Count);
        _realizedIndices.Clear();
    }

    private void UpdateRealizedRange(ViewportRange range)
    {
        if (RealizedRange == range)
        {
            return;
        }

        RealizedRange = range;
        ViewportChanged?.Invoke(this, range);
    }

    private void UpdateScrollMetrics(Size extent, Size viewport)
    {
        bool changed = !AreClose(_extent.Width, extent.Width)
            || !AreClose(_extent.Height, extent.Height)
            || !AreClose(_viewport.Width, viewport.Width)
            || !AreClose(_viewport.Height, viewport.Height);

        _extent = extent;
        _viewport = viewport;
        _columns = CreateLayout().CalculateRange(1, viewport.Width, viewport.Height, 0).Columns;
        _offset.X = CanHorizontallyScroll ? ClampOffset(_offset.X, ExtentWidth, ViewportWidth) : 0;
        _offset.Y = CanVerticallyScroll ? ClampOffset(_offset.Y, ExtentHeight, ViewportHeight) : 0;

        if (changed)
        {
            ScrollOwner?.InvalidateScrollInfo();
        }
    }

    private Size NormalizeViewport(Size availableSize)
    {
        double width = double.IsFinite(availableSize.Width)
            ? Math.Max(0, availableSize.Width)
            : Math.Max(ItemWidth, _viewport.Width);
        double height = double.IsFinite(availableSize.Height)
            ? Math.Max(0, availableSize.Height)
            : Math.Max(ItemHeight, _viewport.Height);
        return new Size(width, height);
    }

    private DependencyObject? FindDirectChild(DependencyObject visual)
    {
        DependencyObject? current = visual;
        while (current is not null && !ReferenceEquals(VisualTreeHelper.GetParent(current), this))
        {
            current = VisualTreeHelper.GetParent(current);
        }

        return current;
    }

    private static double ClampOffset(double offset, double extent, double viewport)
    {
        if (!double.IsFinite(offset))
        {
            return 0;
        }

        return Math.Clamp(offset, 0, Math.Max(0, extent - viewport));
    }

    private static bool AreClose(double left, double right) =>
        Math.Abs(left - right) < 0.01;

    private static bool IsPositiveFinite(object value) =>
        value is double number && double.IsFinite(number) && number > 0;

    private static bool IsNonNegativeFinite(object value) =>
        value is double number && double.IsFinite(number) && number >= 0;

    private static void OnLayoutPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs _)
    {
        var panel = (VirtualizingWrapPanel)dependencyObject;
        panel.InvalidateMeasure();
        panel.ScrollOwner?.InvalidateScrollInfo();
    }
}
