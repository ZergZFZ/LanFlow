using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Channels;
using System.Windows.Media;

namespace LanFlow.Desktop.Services;

public enum IconLoadPriority
{
    Viewport = 0,
    Buffer = 1,
    Idle = 2,
}

public interface IIconService : IAsyncDisposable
{
    ValueTask<ImageSource?> GetIconAsync(
        string? path,
        int pixelSize,
        IconLoadPriority priority,
        CancellationToken cancellationToken);

    void Invalidate(string? path);
    void Clear();
}

public sealed class ShellIconService : IIconService
{
    private const string DefaultThemeVariant = "default";

    private readonly IIconExtractor _extractor;
    private readonly int _capacity;
    private readonly Channel<IconRequest>[] _queues;
    private readonly SemaphoreSlim _queueSignal = new(0);
    private readonly ConcurrentDictionary<IconCacheKey, InflightEntry> _inflight = new();
    private readonly object _cacheGate = new();
    private readonly Dictionary<IconCacheKey, LinkedListNode<CacheEntry>> _cache = new();
    private readonly LinkedList<CacheEntry> _lru = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task[] _workers;
    private int _disposeState;

    public ShellIconService()
        : this(new ShellIconExtractor(), capacity: 256, workerCount: 2)
    {
    }

    public ShellIconService(IIconExtractor extractor, int capacity = 256, int workerCount = 2)
    {
        ArgumentNullException.ThrowIfNull(extractor);
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (workerCount <= 0) throw new ArgumentOutOfRangeException(nameof(workerCount));

        _extractor = extractor;
        _capacity = capacity;
        _queues =
        [
            CreateQueue(),
            CreateQueue(),
            CreateQueue(),
        ];
        _workers = Enumerable.Range(0, workerCount)
            .Select(_ => Task.Run(ProcessQueueAsync))
            .ToArray();
    }

    public ValueTask<ImageSource?> GetIconAsync(
        string? path,
        int pixelSize,
        IconLoadPriority priority,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
        if (string.IsNullOrWhiteSpace(path)) return ValueTask.FromResult<ImageSource?>(null);
        if (pixelSize <= 0) throw new ArgumentOutOfRangeException(nameof(pixelSize));
        if (!Enum.IsDefined(priority)) throw new ArgumentOutOfRangeException(nameof(priority));
        cancellationToken.ThrowIfCancellationRequested();

        var key = IconCacheKey.Create(path, pixelSize, GetVersionStamp(path), DefaultThemeVariant);
        if (TryGetCached(key, out var cached)) return ValueTask.FromResult(cached);

        var candidate = new InflightEntry(key);
        var entry = _inflight.GetOrAdd(key, candidate);
        if (ReferenceEquals(entry, candidate))
        {
            Enqueue(new IconRequest(path, pixelSize, priority, entry));
        }
        else
        {
            candidate.Dispose();
        }

        return new ValueTask<ImageSource?>(WaitForEntryAsync(entry, cancellationToken));
    }

    public void Invalidate(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        string identity;
        try
        {
            identity = IconCacheKey.Create(path, 1, 0, DefaultThemeVariant).Identity;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return;
        }

        lock (_cacheGate)
        {
            foreach (var key in _cache.Keys.Where(key => key.Identity == identity).ToArray())
            {
                RemoveCached(key);
            }
        }

        foreach (var pair in _inflight.Where(pair => pair.Key.Identity == identity).ToArray())
        {
            if (_inflight.TryRemove(pair.Key, out var entry)) entry.Cancel();
        }
    }

    public void Clear()
    {
        lock (_cacheGate)
        {
            _cache.Clear();
            _lru.Clear();
        }

        foreach (var pair in _inflight.ToArray())
        {
            if (_inflight.TryRemove(pair.Key, out var entry)) entry.Cancel();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0) return;

        foreach (var queue in _queues) queue.Writer.TryComplete();
        foreach (var entry in _inflight.Values) entry.Cancel();
        _shutdown.Cancel();

        try
        {
            await Task.WhenAll(_workers).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }

        foreach (var pair in _inflight.ToArray())
        {
            if (_inflight.TryRemove(pair.Key, out var entry)) entry.Dispose();
        }

        _shutdown.Dispose();
        _queueSignal.Dispose();
    }

    private static Channel<IconRequest> CreateQueue() =>
        Channel.CreateUnbounded<IconRequest>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });

    private async Task<ImageSource?> WaitForEntryAsync(
        InflightEntry entry,
        CancellationToken cancellationToken)
    {
        entry.AddWaiter();
        try
        {
            return await entry.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (entry.ReleaseWaiter() == 0 && !entry.Task.IsCompleted)
            {
                _inflight.TryRemove(new KeyValuePair<IconCacheKey, InflightEntry>(entry.Key, entry));
                entry.Cancel();
            }
        }
    }

    private void Enqueue(IconRequest request)
    {
        var queue = _queues[(int)request.Priority];
        if (!queue.Writer.TryWrite(request))
        {
            _inflight.TryRemove(new KeyValuePair<IconCacheKey, InflightEntry>(request.Entry.Key, request.Entry));
            request.Entry.Fail(new ObjectDisposedException(nameof(ShellIconService)));
            return;
        }

        _queueSignal.Release();
    }

    private async Task ProcessQueueAsync()
    {
        while (true)
        {
            await _queueSignal.WaitAsync(_shutdown.Token).ConfigureAwait(false);
            if (!TryDequeue(out var request)) continue;
            await ProcessRequestAsync(request).ConfigureAwait(false);
        }
    }

    private bool TryDequeue(out IconRequest request)
    {
        foreach (var queue in _queues)
        {
            if (queue.Reader.TryRead(out var queued))
            {
                request = queued;
                return true;
            }
        }

        request = default!;
        return false;
    }

    private async Task ProcessRequestAsync(IconRequest request)
    {
        var entry = request.Entry;
        try
        {
            entry.CancellationToken.ThrowIfCancellationRequested();
            var image = await _extractor
                .ExtractAsync(request.Path, request.PixelSize, entry.CancellationToken)
                .ConfigureAwait(false);
            entry.CancellationToken.ThrowIfCancellationRequested();

            if (image is not null && image.CanFreeze && !image.IsFrozen) image.Freeze();
            if (_inflight.TryGetValue(entry.Key, out var current) && ReferenceEquals(current, entry))
            {
                AddCached(entry.Key, image);
                entry.Complete(image);
            }
        }
        catch (OperationCanceledException) when (entry.CancellationToken.IsCancellationRequested)
        {
            entry.CancelCompletion();
        }
        catch (Exception exception)
        {
            entry.Fail(exception);
        }
        finally
        {
            _inflight.TryRemove(new KeyValuePair<IconCacheKey, InflightEntry>(entry.Key, entry));
            entry.Dispose();
        }
    }

    private bool TryGetCached(IconCacheKey key, out ImageSource? image)
    {
        lock (_cacheGate)
        {
            if (!_cache.TryGetValue(key, out var node))
            {
                image = null;
                return false;
            }

            _lru.Remove(node);
            _lru.AddFirst(node);
            image = node.Value.Image;
            return true;
        }
    }

    private void AddCached(IconCacheKey key, ImageSource? image)
    {
        lock (_cacheGate)
        {
            if (_cache.TryGetValue(key, out var existing))
            {
                existing.Value = new CacheEntry(key, image);
                _lru.Remove(existing);
                _lru.AddFirst(existing);
                return;
            }

            var node = _lru.AddFirst(new CacheEntry(key, image));
            _cache[key] = node;
            while (_cache.Count > _capacity && _lru.Last is { } oldest)
            {
                _lru.RemoveLast();
                _cache.Remove(oldest.Value.Key);
            }
        }
    }

    private void RemoveCached(IconCacheKey key)
    {
        if (!_cache.Remove(key, out var node)) return;
        _lru.Remove(node);
    }

    private static long GetVersionStamp(string path)
    {
        try
        {
            if (File.Exists(path)) return File.GetLastWriteTimeUtc(path).Ticks;
            if (Directory.Exists(path)) return Directory.GetLastWriteTimeUtc(path).Ticks;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        return 0;
    }

    private sealed class InflightEntry : IDisposable
    {
        private readonly CancellationTokenSource _cancellation = new();
        private readonly TaskCompletionSource<ImageSource?> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationToken _cancellationToken;
        private int _waiters;

        public InflightEntry(IconCacheKey key)
        {
            Key = key;
            _cancellationToken = _cancellation.Token;
        }

        public IconCacheKey Key { get; }
        public Task<ImageSource?> Task => _completion.Task;
        public CancellationToken CancellationToken => _cancellationToken;

        public void AddWaiter() => Interlocked.Increment(ref _waiters);
        public int ReleaseWaiter() => Interlocked.Decrement(ref _waiters);
        public void Complete(ImageSource? image) => _completion.TrySetResult(image);
        public void Fail(Exception exception) => _completion.TrySetException(exception);
        public void CancelCompletion() => _completion.TrySetCanceled(_cancellationToken);

        public void Cancel()
        {
            try
            {
                if (!_cancellation.IsCancellationRequested) _cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            CancelCompletion();
        }

        public void Dispose() => _cancellation.Dispose();
    }

    private sealed record CacheEntry(IconCacheKey Key, ImageSource? Image);
    private sealed record IconRequest(
        string Path,
        int PixelSize,
        IconLoadPriority Priority,
        InflightEntry Entry);
}
