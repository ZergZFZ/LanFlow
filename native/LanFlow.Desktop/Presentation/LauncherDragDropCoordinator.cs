using System;
using System.Collections.Generic;
using System.Windows;
using LanFlow.Desktop.Controls;
using LanFlow.Desktop.Models;

namespace LanFlow.Desktop.Presentation;

public readonly record struct LauncherDragUpdate(long Generation, int TargetIndex, bool OrderChanged);

public sealed class LauncherDragDropCoordinator
{
    private readonly Action _refreshVisibleItems;
    private readonly Action _save;
    private readonly Action<string> _setStatus;
    private Group? _sourceGroup;
    private LauncherItem? _item;
    private int _sourceIndex = -1;
    private int _previewInsertIndex = -1;
    private bool _startedWhileFiltering;
    private long _generation;

    public LauncherDragDropCoordinator(
        Action refreshVisibleItems,
        Action save,
        Action<string> setStatus)
    {
        _refreshVisibleItems = refreshVisibleItems ?? throw new ArgumentNullException(nameof(refreshVisibleItems));
        _save = save ?? throw new ArgumentNullException(nameof(save));
        _setStatus = setStatus ?? throw new ArgumentNullException(nameof(setStatus));
    }

    public long Generation => _generation;
    public bool IsActive => _item is not null && _sourceGroup is not null && _sourceIndex >= 0;
    public bool StartedWhileFiltering => _startedWhileFiltering;

    public long Begin(LauncherItem item, Group sourceGroup, bool isFiltering)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(sourceGroup);

        Cancel(_generation);
        _generation++;
        _item = item;
        _sourceGroup = sourceGroup;
        _sourceIndex = sourceGroup.Items.IndexOf(item);
        _previewInsertIndex = -1;
        _startedWhileFiltering = isFiltering;
        return _generation;
    }

    public LauncherDragUpdate Update(
        long generation,
        Group targetGroup,
        IReadOnlyList<LauncherItem> visibleItems,
        int visibleInsertIndex)
    {
        ArgumentNullException.ThrowIfNull(targetGroup);
        ArgumentNullException.ThrowIfNull(visibleItems);

        if (!CanUpdate(generation, targetGroup))
        {
            return new LauncherDragUpdate(_generation, -1, false);
        }

        int targetIndex = MapVisibleInsertIndexToSource(targetGroup, visibleItems, visibleInsertIndex);
        _previewInsertIndex = targetIndex;
        if (!ReferenceEquals(_sourceGroup, targetGroup) || _item is null)
        {
            return new LauncherDragUpdate(_generation, targetIndex, false);
        }

        int currentIndex = targetGroup.Items.IndexOf(_item);
        if (currentIndex < 0)
        {
            return new LauncherDragUpdate(_generation, targetIndex, false);
        }

        if (currentIndex < targetIndex)
        {
            targetIndex--;
        }

        targetIndex = Math.Clamp(targetIndex, 0, Math.Max(0, targetGroup.Items.Count - 1));
        if (currentIndex == targetIndex)
        {
            _previewInsertIndex = targetIndex;
            return new LauncherDragUpdate(_generation, targetIndex, false);
        }

        targetGroup.Items.Move(currentIndex, targetIndex);
        _previewInsertIndex = targetIndex;
        _refreshVisibleItems();
        return new LauncherDragUpdate(_generation, targetIndex, true);
    }

    public LauncherDragUpdate Update(
        long generation,
        Group targetGroup,
        IReadOnlyList<LauncherItem> visibleItems,
        Point logicalPoint,
        VirtualizingWrapLayout layout,
        int columns)
    {
        ArgumentNullException.ThrowIfNull(layout);
        int visibleIndex = layout.IndexFromPoint(logicalPoint, visibleItems.Count, Math.Max(1, columns));
        if (visibleIndex >= 0)
        {
            Rect itemRect = layout.GetItemRect(visibleIndex, Math.Max(1, columns));
            if (logicalPoint.X > itemRect.Left + (itemRect.Width / 2))
            {
                visibleIndex++;
            }
        }

        return Update(generation, targetGroup, visibleItems, visibleIndex);
    }

    public bool Drop(
        long generation,
        Group targetGroup,
        IReadOnlyList<LauncherItem> visibleItems,
        int visibleInsertIndex)
    {
        ArgumentNullException.ThrowIfNull(targetGroup);
        ArgumentNullException.ThrowIfNull(visibleItems);

        if (!CanUpdate(generation, targetGroup) || _item is null || _sourceGroup is null)
        {
            return false;
        }

        int targetIndex = _previewInsertIndex >= 0
            ? _previewInsertIndex
            : MapVisibleInsertIndexToSource(targetGroup, visibleItems, visibleInsertIndex);

        if (ReferenceEquals(_sourceGroup, targetGroup) && _previewInsertIndex >= 0)
        {
            _refreshVisibleItems();
        }
        else
        {
            RestorePreviewOrder();
            MoveItem(_item, _sourceGroup, targetGroup, targetIndex);
        }

        _save();
        _setStatus(ReferenceEquals(_sourceGroup, targetGroup)
            ? "\u9879\u76EE\u987A\u5E8F\u5DF2\u66F4\u65B0"
            : $"\u5DF2\u79FB\u81F3\u201C{targetGroup.Name}\u201D");
        ClearState();
        return true;
    }

    public void Cancel(long generation)
    {
        if (generation != _generation)
        {
            return;
        }

        RestorePreviewOrder();
        ClearState();
    }

    private bool CanUpdate(long generation, Group targetGroup) =>
        generation == _generation &&
        IsActive &&
        !_startedWhileFiltering &&
        (!ReferenceEquals(_sourceGroup, targetGroup) ||
         !string.Equals(targetGroup.SortMode, "frequency", StringComparison.Ordinal));

    private static int MapVisibleInsertIndexToSource(
        Group targetGroup,
        IReadOnlyList<LauncherItem> visibleItems,
        int visibleInsertIndex)
    {
        if (visibleInsertIndex >= visibleItems.Count)
        {
            return targetGroup.Items.Count;
        }

        visibleInsertIndex = Math.Clamp(visibleInsertIndex, 0, Math.Max(0, visibleItems.Count - 1));
        LauncherItem visibleItem = visibleItems[visibleInsertIndex];
        int sourceIndex = targetGroup.Items.IndexOf(visibleItem);
        return sourceIndex >= 0 ? sourceIndex : targetGroup.Items.Count;
    }

    private void RestorePreviewOrder()
    {
        if (_item is null || _sourceGroup is null || _sourceIndex < 0)
        {
            return;
        }

        int currentIndex = _sourceGroup.Items.IndexOf(_item);
        int targetIndex = Math.Clamp(_sourceIndex, 0, Math.Max(0, _sourceGroup.Items.Count - 1));
        if (currentIndex >= 0 && currentIndex != targetIndex)
        {
            _sourceGroup.Items.Move(currentIndex, targetIndex);
            _refreshVisibleItems();
        }

        _previewInsertIndex = -1;
    }

    private void MoveItem(LauncherItem item, Group sourceGroup, Group targetGroup, int targetIndex)
    {
        int sourceIndex = sourceGroup.Items.IndexOf(item);
        if (sourceIndex < 0)
        {
            return;
        }

        if (ReferenceEquals(sourceGroup, targetGroup) && sourceIndex < targetIndex)
        {
            targetIndex--;
        }

        sourceGroup.Items.Remove(item);
        targetIndex = Math.Clamp(targetIndex, 0, targetGroup.Items.Count);
        targetGroup.Items.Insert(targetIndex, item);
        _refreshVisibleItems();
    }

    private void ClearState()
    {
        _item = null;
        _sourceGroup = null;
        _sourceIndex = -1;
        _previewInsertIndex = -1;
        _startedWhileFiltering = false;
    }
}
