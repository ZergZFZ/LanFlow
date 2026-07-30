using System.Collections.Concurrent;
using System.Windows.Media;
using LanFlow.Desktop.Controls;
using LanFlow.Desktop.Models;
using LanFlow.Desktop.Presentation;
using LanFlow.Desktop.Services;
using Xunit;

namespace LanFlow.Desktop.Tests;

public sealed class ViewportIconCoordinatorTests
{
    [Fact]
    public void BuildRequests_AssignsViewportBufferAndIdlePriorities()
    {
        var requests = ViewportIconCoordinator.BuildRequests(
            itemCount: 20,
            viewport: new ViewportRange(5, 9, 5),
            bufferItemCount: 5);

        Assert.All(
            requests.Where(request => request.Index is >= 5 and <= 9),
            request => Assert.Equal(IconLoadPriority.Viewport, request.Priority));
        Assert.All(
            requests.Where(request => request.Index is >= 0 and <= 4 or >= 10 and <= 14),
            request => Assert.Equal(IconLoadPriority.Buffer, request.Priority));
        Assert.All(
            requests.Where(request => request.Index >= 15),
            request => Assert.Equal(IconLoadPriority.Idle, request.Priority));
    }

    [Fact]
    public void BuildRequests_OrdersEachClampedIndexExactlyOnce()
    {
        var requests = ViewportIconCoordinator.BuildRequests(
            itemCount: 8,
            viewport: new ViewportRange(-4, 12, 3),
            bufferItemCount: 2);

        Assert.Equal(Enumerable.Range(0, 8), requests.Select(request => request.Index));
        Assert.All(requests, request => Assert.Equal(IconLoadPriority.Viewport, request.Priority));
    }

    [Fact]
    public void BuildRequests_OrdersViewportThenBufferThenIdle()
    {
        var requests = ViewportIconCoordinator.BuildRequests(
            itemCount: 12,
            viewport: new ViewportRange(4, 6, 3),
            bufferItemCount: 2);

        Assert.Equal([4, 5, 6], requests.Take(3).Select(request => request.Index));
        Assert.All(requests.Take(3), request => Assert.Equal(IconLoadPriority.Viewport, request.Priority));
        Assert.Equal([2, 3, 7, 8], requests.Skip(3).Take(4).Select(request => request.Index));
        Assert.All(requests.Skip(3).Take(4), request => Assert.Equal(IconLoadPriority.Buffer, request.Priority));
        Assert.Equal([0, 1, 9, 10, 11], requests.Skip(7).Select(request => request.Index));
        Assert.All(requests.Skip(7), request => Assert.Equal(IconLoadPriority.Idle, request.Priority));
        Assert.Equal(12, requests.Select(request => request.Index).Distinct().Count());
    }

    [Fact]
    public void BuildRequests_EmptyCollectionReturnsEmpty()
    {
        Assert.Empty(ViewportIconCoordinator.BuildRequests(
            itemCount: 0,
            viewport: ViewportRange.Empty,
            bufferItemCount: 5));
    }

    [Fact]
    public async Task RefreshAsync_DoesNotApplyResultsFromPreviousGeneration()
    {
        await using var icons = new ControlledIconService(ignoreCancellation: true);
        using var coordinator = new ViewportIconCoordinator(icons);
        var item = new LauncherItem { Path = "old.exe" };

        var oldRefresh = coordinator.RefreshAsync(
            [item],
            new ViewportRange(0, 0, 1),
            48,
            "dark",
            default);
        await icons.WaitForCallsAsync(1);

        item.Path = "new.exe";
        var newRefresh = coordinator.RefreshAsync(
            [item],
            new ViewportRange(0, 0, 1),
            48,
            "dark",
            default);
        await icons.WaitForCallsAsync(2);

        var newImage = CreateFrozenImage();
        var oldImage = CreateFrozenImage();
        icons.Complete("new.exe", 48, newImage);
        icons.Complete("old.exe", 48, oldImage);

        await Task.WhenAll(oldRefresh, newRefresh);

        Assert.Same(newImage, item.IconImage);
    }

    [Fact]
    public async Task RefreshAsync_ChangedPixelSizeAndThemeRejectsOldResult()
    {
        await using var icons = new ControlledIconService(ignoreCancellation: true);
        using var coordinator = new ViewportIconCoordinator(icons);
        var item = new LauncherItem { Path = "same.exe" };

        var oldRefresh = coordinator.RefreshAsync(
            [item],
            new ViewportRange(0, 0, 1),
            32,
            "light",
            default);
        await icons.WaitForCallsAsync(1);

        var newRefresh = coordinator.RefreshAsync(
            [item],
            new ViewportRange(0, 0, 1),
            64,
            "dark",
            default);
        await icons.WaitForCallsAsync(2);

        var newImage = CreateFrozenImage();
        icons.Complete("same.exe", 64, newImage);
        icons.Complete("same.exe", 32, CreateFrozenImage());

        await Task.WhenAll(oldRefresh, newRefresh);

        Assert.Same(newImage, item.IconImage);
    }

    [Fact]
    public async Task RefreshAsync_CancellationDoesNotClearNewerImage()
    {
        await using var icons = new ControlledIconService(ignoreCancellation: false);
        using var coordinator = new ViewportIconCoordinator(icons);
        var item = new LauncherItem { Path = "tool.exe" };
        var existing = CreateFrozenImage();
        item.IconImage = existing;
        using var cancellation = new CancellationTokenSource();

        var refresh = coordinator.RefreshAsync(
            [item],
            new ViewportRange(0, 0, 1),
            48,
            "dark",
            cancellation.Token);
        await icons.WaitForCallsAsync(1);
        cancellation.Cancel();
        await refresh;

        Assert.Same(existing, item.IconImage);
    }

    [Fact]
    public async Task PreheatAsync_IncludesRecentlyVisitedAndAdjacentGroupsWithoutDuplicates()
    {
        await using var icons = new ControlledIconService(ignoreCancellation: false);
        using var coordinator = new ViewportIconCoordinator(icons);
        var groups = new[]
        {
            CreateGroup("a", "a.exe"),
            CreateGroup("b", "b.exe"),
            CreateGroup("c", "c.exe"),
            CreateGroup("d", "d.exe"),
        };

        var firstPreheat = coordinator.PreheatAsync(groups, "b", 48, "dark", default);
        await icons.WaitForCallsAsync(2);
        icons.Complete("a.exe", 48, CreateFrozenImage());
        icons.Complete("c.exe", 48, CreateFrozenImage());
        await firstPreheat;

        var secondPreheat = coordinator.PreheatAsync(groups, "d", 48, "dark", default);
        await icons.WaitForCallsAsync(4);
        var secondBatch = icons.Calls.Skip(2).ToArray();

        Assert.Equal(["b.exe", "c.exe"], secondBatch.Select(call => call.Path));
        Assert.All(secondBatch, call => Assert.Equal(IconLoadPriority.Idle, call.Priority));
        icons.Complete("b.exe", 48, CreateFrozenImage());
        icons.Complete("c.exe", 48, CreateFrozenImage());
        await secondPreheat;
    }

    [Fact]
    public async Task PreheatAsync_UsesIdleAndIsCancelledByForegroundRefresh()
    {
        await using var icons = new ControlledIconService(ignoreCancellation: false);
        using var coordinator = new ViewportIconCoordinator(icons);
        var groups = new[]
        {
            CreateGroup("left", "left.exe"),
            CreateGroup("current", "current.exe"),
            CreateGroup("right", "right.exe"),
        };

        var preheat = coordinator.PreheatAsync(groups, "current", 48, "dark", default);
        await icons.WaitForCallsAsync(2);

        Assert.All(icons.Calls, call => Assert.Equal(IconLoadPriority.Idle, call.Priority));
        Assert.DoesNotContain(icons.Calls, call => call.Path == "current.exe");

        var foregroundItem = new LauncherItem { Path = "foreground.exe" };
        var foreground = coordinator.RefreshAsync(
            [foregroundItem],
            new ViewportRange(0, 0, 1),
            48,
            "dark",
            default);
        await icons.WaitForCallsAsync(3);
        icons.Complete("foreground.exe", 48, CreateFrozenImage());

        await Task.WhenAll(preheat, foreground);

        Assert.Contains(
            icons.Calls,
            call => call.Path == "foreground.exe" && call.Priority == IconLoadPriority.Viewport);
        Assert.True(icons.Calls.Where(call => call.Priority == IconLoadPriority.Idle).All(call => call.WasCancelled));
    }

    private static Group CreateGroup(string id, string path)
    {
        var group = new Group { Id = id, Name = id };
        group.Items.Add(new LauncherItem { Path = path, Name = id });
        return group;
    }

    private static ImageSource CreateFrozenImage()
    {
        var image = new DrawingImage();
        image.Freeze();
        return image;
    }

    private sealed class ControlledIconService(bool ignoreCancellation) : IIconService
    {
        private readonly ConcurrentQueue<PendingRequest> _pending = new();
        private readonly SemaphoreSlim _callSignal = new(0);
        private readonly object _callsGate = new();
        private readonly List<IconCall> _calls = [];

        public IReadOnlyList<IconCall> Calls
        {
            get
            {
                lock (_callsGate) return _calls.ToArray();
            }
        }

        public ValueTask<ImageSource?> GetIconAsync(
            string? path,
            int pixelSize,
            IconLoadPriority priority,
            CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var call = new IconCall(path ?? string.Empty, pixelSize, priority);
            lock (_callsGate) _calls.Add(call);
            _callSignal.Release();

            CancellationTokenRegistration registration = default;
            if (!ignoreCancellation)
            {
                registration = cancellationToken.Register(() =>
                {
                    call.WasCancelled = true;
                    completion.TrySetCanceled(cancellationToken);
                });
            }

            _ = completion.Task.ContinueWith(
                _ => registration.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            _pending.Enqueue(new PendingRequest(call, completion));
            return new ValueTask<ImageSource?>(completion.Task);
        }

        public void Complete(string path, int pixelSize, ImageSource? image)
        {
            var deferred = new List<PendingRequest>();
            while (_pending.TryDequeue(out var request))
            {
                if (request.Call.Path == path && request.Call.PixelSize == pixelSize && request.Completion.TrySetResult(image))
                {
                    foreach (var pending in deferred) _pending.Enqueue(pending);
                    return;
                }

                deferred.Add(request);
            }

            foreach (var pending in deferred) _pending.Enqueue(pending);
            Assert.Fail($"No pending icon request matched {path} at {pixelSize}px.");
        }

        public async Task WaitForCallsAsync(int count)
        {
            while (Calls.Count < count)
            {
                Assert.True(await _callSignal.WaitAsync(TimeSpan.FromSeconds(5)));
            }
        }

        public void Invalidate(string? path)
        {
        }

        public void Clear()
        {
        }

        public ValueTask DisposeAsync()
        {
            while (_pending.TryDequeue(out var request))
            {
                request.Completion.TrySetCanceled();
            }

            _callSignal.Dispose();
            return ValueTask.CompletedTask;
        }

        private sealed record PendingRequest(IconCall Call, TaskCompletionSource<ImageSource?> Completion);
    }

    private sealed record IconCall(string Path, int PixelSize, IconLoadPriority Priority)
    {
        public bool WasCancelled { get; set; }
    }
}
