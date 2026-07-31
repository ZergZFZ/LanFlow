using System;

namespace LanFlow.Desktop.Presentation;

public sealed class PreviewThrottle<T> : IDisposable
{
    private readonly object _gate = new();
    private readonly TimeSpan _interval;
    private readonly ITimerScheduler _scheduler;
    private readonly Action<T> _apply;
    private IDisposable? _timer;
    private T? _pending;
    private bool _hasPending;
    private bool _isDisposed;

    public PreviewThrottle(TimeSpan interval, ITimerScheduler scheduler, Action<T> apply)
    {
        if (interval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval));
        }

        _interval = interval;
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
    }

    public void Push(T value)
    {
        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            _pending = value;
            _hasPending = true;
            _timer ??= _scheduler.Schedule(_interval, ApplyPending);
        }
    }

    public void Flush()
    {
        T? value;
        lock (_gate)
        {
            if (_isDisposed || !_hasPending)
            {
                return;
            }

            _timer?.Dispose();
            _timer = null;
            value = _pending;
            _pending = default;
            _hasPending = false;
        }

        _apply(value!);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _timer?.Dispose();
            _timer = null;
            _pending = default;
            _hasPending = false;
        }
    }

    private void ApplyPending()
    {
        T? value;
        lock (_gate)
        {
            if (_isDisposed || !_hasPending)
            {
                return;
            }

            _timer = null;
            value = _pending;
            _pending = default;
            _hasPending = false;
        }

        _apply(value!);
    }
}
