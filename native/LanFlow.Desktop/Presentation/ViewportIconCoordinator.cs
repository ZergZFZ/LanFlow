using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LanFlow.Desktop.Controls;
using LanFlow.Desktop.Models;
using LanFlow.Desktop.Services;

namespace LanFlow.Desktop.Presentation;

public readonly record struct IconViewportRequest(
    int Index,
    IconLoadPriority Priority);

public sealed class ViewportIconCoordinator : IDisposable
{
    private readonly IIconService _iconService;
    private readonly object _gate = new();
    private CancellationTokenSource? _foregroundCancellation;
    private CancellationTokenSource? _preheatCancellation;
    private long _generation;
    private bool _foregroundActive;
    private bool _disposed;
    private int _activePixelSize;
    private string _activeThemeVariant = string.Empty;
    private string? _lastCurrentGroupId;

    public ViewportIconCoordinator(IIconService iconService)
    {
        ArgumentNullException.ThrowIfNull(iconService);
        _iconService = iconService;
    }

    public static IReadOnlyList<IconViewportRequest> BuildRequests(
        int itemCount,
        ViewportRange viewport,
        int bufferItemCount)
    {
        if (itemCount < 0) throw new ArgumentOutOfRangeException(nameof(itemCount));
        if (bufferItemCount < 0) throw new ArgumentOutOfRangeException(nameof(bufferItemCount));
        if (itemCount == 0) return [];

        var requests = new List<IconViewportRequest>(itemCount);
        var assigned = new bool[itemCount];
        var hasViewport = viewport.FirstIndex <= viewport.LastIndex &&
                          viewport.LastIndex >= 0 &&
                          viewport.FirstIndex < itemCount;

        if (hasViewport)
        {
            var first = Math.Clamp(viewport.FirstIndex, 0, itemCount - 1);
            var last = Math.Clamp(viewport.LastIndex, 0, itemCount - 1);
            AddRange(first, last, IconLoadPriority.Viewport);

            var bufferFirst = Math.Max(0, first - bufferItemCount);
            var bufferLast = Math.Min(itemCount - 1, last + bufferItemCount);
            AddRange(bufferFirst, first - 1, IconLoadPriority.Buffer);
            AddRange(last + 1, bufferLast, IconLoadPriority.Buffer);
        }

        AddRange(0, itemCount - 1, IconLoadPriority.Idle);
        return requests;

        void AddRange(int first, int last, IconLoadPriority priority)
        {
            for (var index = first; index <= last; index++)
            {
                if (index < 0 || index >= itemCount || assigned[index]) continue;
                assigned[index] = true;
                requests.Add(new IconViewportRequest(index, priority));
            }
        }
    }

    public Task RefreshAsync(
        IReadOnlyList<LauncherItem> items,
        ViewportRange viewport,
        int pixelSize,
        string themeVariant,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (pixelSize <= 0) throw new ArgumentOutOfRangeException(nameof(pixelSize));
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedTheme = NormalizeThemeVariant(themeVariant);
        CancellationTokenSource batchCancellation;
        long generation;

        lock (_gate)
        {
            ThrowIfDisposed();
            generation = ++_generation;
            CancelAndDispose(ref _foregroundCancellation);
            CancelAndDispose(ref _preheatCancellation);
            batchCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _foregroundCancellation = batchCancellation;
            _foregroundActive = true;
            _activePixelSize = pixelSize;
            _activeThemeVariant = normalizedTheme;
        }

        var visibleCount = viewport.FirstIndex <= viewport.LastIndex
            ? Math.Max(1, viewport.LastIndex - viewport.FirstIndex + 1)
            : 1;
        var requests = BuildRequests(items.Count, viewport, visibleCount);
        return RefreshCoreAsync(
            items,
            requests,
            pixelSize,
            normalizedTheme,
            generation,
            batchCancellation.Token);
    }

    public Task PreheatAsync(
        IReadOnlyList<Group> groups,
        string? currentGroupId,
        int pixelSize,
        string themeVariant,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(groups);
        if (pixelSize <= 0) throw new ArgumentOutOfRangeException(nameof(pixelSize));
        cancellationToken.ThrowIfCancellationRequested();

        CancellationTokenSource preheatCancellation;
        string? recentlyVisitedGroupId;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_foregroundActive || groups.Count == 0) return Task.CompletedTask;

            recentlyVisitedGroupId = _lastCurrentGroupId;
            _lastCurrentGroupId = currentGroupId;
            CancelAndDispose(ref _preheatCancellation);
            preheatCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _preheatCancellation = preheatCancellation;
        }

        _ = NormalizeThemeVariant(themeVariant);
        var selectedGroups = SelectPreheatGroups(groups, currentGroupId, recentlyVisitedGroupId);
        return PreheatCoreAsync(selectedGroups, pixelSize, preheatCancellation.Token);
    }

    public void CancelPending()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _generation++;
            _foregroundActive = false;
            CancelAndDispose(ref _foregroundCancellation);
            CancelAndDispose(ref _preheatCancellation);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _generation++;
            _foregroundActive = false;
            CancelAndDispose(ref _foregroundCancellation);
            CancelAndDispose(ref _preheatCancellation);
        }
    }

    private async Task RefreshCoreAsync(
        IReadOnlyList<LauncherItem> items,
        IReadOnlyList<IconViewportRequest> requests,
        int pixelSize,
        string themeVariant,
        long generation,
        CancellationToken cancellationToken)
    {
        try
        {
            var pending = new List<Task>(requests.Count);
            foreach (var request in requests)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = items[request.Index];
                var requestVersion = ++item.IconRequestVersion;
                var path = item.Path;
                pending.Add(LoadAndApplyAsync(
                    item,
                    requestVersion,
                    path,
                    pixelSize,
                    themeVariant,
                    generation,
                    request.Priority,
                    cancellationToken));
            }

            await Task.WhenAll(pending);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            lock (_gate)
            {
                if (!_disposed && generation == _generation)
                {
                    _foregroundActive = false;
                }
            }
        }
    }

    private async Task LoadAndApplyAsync(
        LauncherItem item,
        int requestVersion,
        string? path,
        int pixelSize,
        string themeVariant,
        long generation,
        IconLoadPriority priority,
        CancellationToken cancellationToken)
    {
        try
        {
            var image = await _iconService.GetIconAsync(
                path,
                pixelSize,
                priority,
                cancellationToken);

            lock (_gate)
            {
                if (_disposed ||
                    cancellationToken.IsCancellationRequested ||
                    generation != _generation ||
                    requestVersion != item.IconRequestVersion ||
                    !string.Equals(path, item.Path, StringComparison.Ordinal) ||
                    pixelSize != _activePixelSize ||
                    !string.Equals(themeVariant, _activeThemeVariant, StringComparison.Ordinal))
                {
                    return;
                }

                item.IconImage = image;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Keep the previous image. A failed or stale request must not cause visible flicker.
        }
    }

    private async Task PreheatCoreAsync(
        IReadOnlyList<Group> groups,
        int pixelSize,
        CancellationToken cancellationToken)
    {
        try
        {
            var pending = new List<Task>();
            foreach (var group in groups)
            {
                foreach (var item in group.Items)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    pending.Add(LoadForCacheAsync(item.Path, pixelSize, cancellationToken));
                }
            }

            await Task.WhenAll(pending);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task LoadForCacheAsync(
        string? path,
        int pixelSize,
        CancellationToken cancellationToken)
    {
        try
        {
            await _iconService.GetIconAsync(
                path,
                pixelSize,
                IconLoadPriority.Idle,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Preheating is opportunistic and must not affect the visible UI.
        }
    }

    private static IReadOnlyList<Group> SelectPreheatGroups(
        IReadOnlyList<Group> groups,
        string? currentGroupId,
        string? recentlyVisitedGroupId)
    {
        var currentIndex = -1;
        for (var index = 0; index < groups.Count; index++)
        {
            if (string.Equals(groups[index].Id, currentGroupId, StringComparison.Ordinal))
            {
                currentIndex = index;
                break;
            }
        }

        if (currentIndex < 0) return groups.Take(1).ToArray();

        var selected = new List<Group>(3);
        AddGroupById(recentlyVisitedGroupId);
        if (currentIndex > 0) AddGroup(groups[currentIndex - 1]);
        if (currentIndex + 1 < groups.Count) AddGroup(groups[currentIndex + 1]);
        return selected;

        void AddGroupById(string? groupId)
        {
            if (string.IsNullOrEmpty(groupId) || string.Equals(groupId, currentGroupId, StringComparison.Ordinal))
            {
                return;
            }

            var group = groups.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, groupId, StringComparison.Ordinal));
            if (group is not null) AddGroup(group);
        }

        void AddGroup(Group group)
        {
            if (selected.All(candidate => !string.Equals(candidate.Id, group.Id, StringComparison.Ordinal)))
            {
                selected.Add(group);
            }
        }
    }

    private static string NormalizeThemeVariant(string? themeVariant) =>
        string.IsNullOrWhiteSpace(themeVariant) ? "default" : themeVariant.Trim();

    private static void CancelAndDispose(ref CancellationTokenSource? cancellation)
    {
        cancellation?.Cancel();
        cancellation?.Dispose();
        cancellation = null;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
