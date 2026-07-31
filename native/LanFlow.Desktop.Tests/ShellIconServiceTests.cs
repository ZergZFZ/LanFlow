using System.Collections.Concurrent;
using System.Windows.Media;
using LanFlow.Desktop.Services;
using Xunit;

namespace LanFlow.Desktop.Tests;

public sealed class ShellIconServiceTests
{
    [Fact]
    public async Task SameKeyConcurrentRequests_AreExtractedOnce()
    {
        var extractor = new FakeIconExtractor();
        await using var service = new ShellIconService(extractor, capacity: 256, workerCount: 2);

        var first = service.GetIconAsync("tool.exe", 48, IconLoadPriority.Viewport, default).AsTask();
        var second = service.GetIconAsync("tool.exe", 48, IconLoadPriority.Viewport, default).AsTask();
        await extractor.WaitForCallsAsync(1);
        extractor.CompleteNext(CreateFrozenImage());

        await Task.WhenAll(first, second);
        Assert.Equal(1, extractor.CallCount);
    }

    [Fact]
    public async Task Invalidate_ForcesUpdatedFileToBeExtractedAgain()
    {
        var extractor = new FakeIconExtractor(autoComplete: true);
        await using var service = new ShellIconService(extractor, capacity: 256, workerCount: 2);

        await service.GetIconAsync("tool.exe", 48, IconLoadPriority.Viewport, default);
        service.Invalidate("tool.exe");
        await service.GetIconAsync("tool.exe", 48, IconLoadPriority.Viewport, default);

        Assert.Equal(2, extractor.CallCount);
    }

    [Fact]
    public async Task ViewportRequest_RunsBeforeQueuedIdleRequest()
    {
        var extractor = new FakeIconExtractor();
        await using var service = new ShellIconService(extractor, capacity: 256, workerCount: 1);

        var blocker = service.GetIconAsync("blocker.exe", 48, IconLoadPriority.Viewport, default).AsTask();
        await extractor.WaitForCallsAsync(1);
        var idle = service.GetIconAsync("idle.exe", 48, IconLoadPriority.Idle, default).AsTask();
        var viewport = service.GetIconAsync("viewport.exe", 48, IconLoadPriority.Viewport, default).AsTask();

        extractor.CompleteNext(CreateFrozenImage());
        await extractor.WaitForCallsAsync(2);
        Assert.Equal("viewport.exe", extractor.Paths[1]);
        extractor.CompleteNext(CreateFrozenImage());
        await extractor.WaitForCallsAsync(3);
        extractor.CompleteNext(CreateFrozenImage());

        await Task.WhenAll(blocker, idle, viewport);
        Assert.Equal(new[] { "blocker.exe", "viewport.exe", "idle.exe" }, extractor.Paths);
    }

    [Fact]
    public async Task CancelledRequest_DoesNotPopulateCache()
    {
        var extractor = new FakeIconExtractor();
        await using var service = new ShellIconService(extractor, capacity: 256, workerCount: 1);
        using var cancellation = new CancellationTokenSource();

        var cancelled = service.GetIconAsync("tool.exe", 48, IconLoadPriority.Viewport, cancellation.Token).AsTask();
        await extractor.WaitForCallsAsync(1);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);

        var retried = service.GetIconAsync("tool.exe", 48, IconLoadPriority.Viewport, default).AsTask();
        await extractor.WaitForCallsAsync(2);
        extractor.CompleteNext(CreateFrozenImage());
        await retried;

        Assert.Equal(2, extractor.CallCount);
    }

    [Fact]
    public async Task CapacityOverflow_EvictsLeastRecentlyUsedEntry()
    {
        var extractor = new FakeIconExtractor(autoComplete: true);
        await using var service = new ShellIconService(extractor, capacity: 256, workerCount: 2);

        for (var index = 0; index < 257; index++)
        {
            await service.GetIconAsync($"tool-{index}.exe", 48, IconLoadPriority.Viewport, default);
        }

        await service.GetIconAsync("tool-0.exe", 48, IconLoadPriority.Viewport, default);
        Assert.Equal(258, extractor.CallCount);
    }

    private static ImageSource CreateFrozenImage()
    {
        var image = new DrawingImage();
        image.Freeze();
        return image;
    }

    private sealed class FakeIconExtractor(bool autoComplete = false) : IIconExtractor
    {
        private readonly ConcurrentQueue<TaskCompletionSource<ImageSource?>> _pending = new();
        private readonly SemaphoreSlim _calls = new(0);
        private readonly object _pathsGate = new();
        private readonly List<string> _paths = [];
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public IReadOnlyList<string> Paths
        {
            get
            {
                lock (_pathsGate) return _paths.ToArray();
            }
        }

        public ValueTask<ImageSource?> ExtractAsync(string path, int pixelSize, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            lock (_pathsGate) _paths.Add(path);
            _calls.Release();

            if (autoComplete)
            {
                return ValueTask.FromResult<ImageSource?>(CreateFrozenImage());
            }

            var completion = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            _ = completion.Task.ContinueWith(
                _ => registration.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            _pending.Enqueue(completion);
            return new ValueTask<ImageSource?>(completion.Task);
        }

        public void CompleteNext(ImageSource? image)
        {
            while (_pending.TryDequeue(out var completion))
            {
                if (completion.TrySetResult(image)) return;
            }

            Assert.Fail("No icon extraction is waiting for completion.");
        }

        public async Task WaitForCallsAsync(int count)
        {
            while (CallCount < count)
            {
                await _calls.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
    }
}
