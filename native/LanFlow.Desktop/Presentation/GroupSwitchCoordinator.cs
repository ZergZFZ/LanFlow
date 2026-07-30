using System;
using System.Windows.Threading;
using LanFlow.Desktop.Models;

namespace LanFlow.Desktop.Presentation;

public enum GroupSwitchReason
{
    Click,
    Hover,
    DragHover,
}

public sealed record GroupSwitchRequestedEventArgs(
    Group Group,
    GroupSwitchReason Reason,
    long Generation);

public interface ITimerScheduler
{
    IDisposable Schedule(TimeSpan delay, Action action);
}

public sealed class DispatcherTimerScheduler : ITimerScheduler
{
    private readonly Dispatcher _dispatcher;

    public DispatcherTimerScheduler(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public IDisposable Schedule(TimeSpan delay, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (delay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay));
        }

        return new ScheduledDispatcherAction(_dispatcher, delay, action);
    }

    private sealed class ScheduledDispatcherAction : IDisposable
    {
        private readonly DispatcherTimer _timer;
        private readonly Action _action;
        private bool _isDisposed;

        public ScheduledDispatcherAction(Dispatcher dispatcher, TimeSpan delay, Action action)
        {
            _action = action;
            _timer = new DispatcherTimer(DispatcherPriority.Input, dispatcher)
            {
                Interval = delay,
            };
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _timer.Stop();
            _timer.Tick -= Timer_Tick;
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (_isDisposed)
            {
                return;
            }

            Dispose();
            _action();
        }
    }
}

public sealed class GroupSwitchCoordinator : IDisposable
{
    private readonly object _gate = new();
    private readonly ITimerScheduler _timerScheduler;
    private readonly TimeSpan _intentDelay;
    private IDisposable? _hoverTimer;
    private IDisposable? _dragHoverTimer;
    private string? _hoverTargetId;
    private string? _dragHoverTargetId;
    private string? _selectedGroupId;
    private long _hoverGeneration;
    private long _dragHoverGeneration;
    private long _switchGeneration;
    private bool _isDisposed;

    public GroupSwitchCoordinator(ITimerScheduler timerScheduler, TimeSpan intentDelay)
    {
        _timerScheduler = timerScheduler ?? throw new ArgumentNullException(nameof(timerScheduler));
        if (intentDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(intentDelay));
        }

        _intentDelay = intentDelay;
    }

    public string? SelectedGroupId
    {
        get
        {
            lock (_gate)
            {
                return _selectedGroupId;
            }
        }
        set
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                _selectedGroupId = value;
                if (string.Equals(_hoverTargetId, value, StringComparison.Ordinal))
                {
                    CancelHoverCore();
                }

                if (string.Equals(_dragHoverTargetId, value, StringComparison.Ordinal))
                {
                    CancelDragHoverCore();
                }
            }
        }
    }

    public event EventHandler<GroupSwitchRequestedEventArgs>? SwitchRequested;

    public void RequestClick(Group group)
    {
        ArgumentNullException.ThrowIfNull(group);
        GroupSwitchRequestedEventArgs? request = null;
        lock (_gate)
        {
            ThrowIfDisposed();
            CancelHoverCore();
            if (!IsSelected(group.Id))
            {
                request = CreateRequest(group, GroupSwitchReason.Click);
            }
        }

        RaiseSwitchRequested(request);
    }

    public void BeginHover(Group group)
    {
        ArgumentNullException.ThrowIfNull(group);
        lock (_gate)
        {
            ThrowIfDisposed();
            CancelHoverCore();
            if (IsSelected(group.Id))
            {
                return;
            }

            var generation = ++_hoverGeneration;
            var targetId = group.Id;
            _hoverTargetId = targetId;
            _hoverTimer = _timerScheduler.Schedule(
                _intentDelay,
                () => CompleteHover(group, targetId, generation));
        }
    }

    public void CancelHover(Group group)
    {
        ArgumentNullException.ThrowIfNull(group);
        lock (_gate)
        {
            ThrowIfDisposed();
            if (string.Equals(_hoverTargetId, group.Id, StringComparison.Ordinal))
            {
                CancelHoverCore();
            }
        }
    }

    public void BeginDragHover(Group group)
    {
        ArgumentNullException.ThrowIfNull(group);
        lock (_gate)
        {
            ThrowIfDisposed();
            CancelDragHoverCore();
            if (IsSelected(group.Id))
            {
                return;
            }

            var generation = ++_dragHoverGeneration;
            var targetId = group.Id;
            _dragHoverTargetId = targetId;
            _dragHoverTimer = _timerScheduler.Schedule(
                _intentDelay,
                () => CompleteDragHover(group, targetId, generation));
        }
    }

    public void CancelDragHover(Group group)
    {
        ArgumentNullException.ThrowIfNull(group);
        lock (_gate)
        {
            ThrowIfDisposed();
            if (string.Equals(_dragHoverTargetId, group.Id, StringComparison.Ordinal))
            {
                CancelDragHoverCore();
            }
        }
    }

    public void EndDrag()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            CancelDragHoverCore();
        }
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
            CancelHoverCore();
            CancelDragHoverCore();
        }
    }

    private void CompleteHover(Group group, string targetId, long generation)
    {
        GroupSwitchRequestedEventArgs? request = null;
        lock (_gate)
        {
            if (_isDisposed || generation != _hoverGeneration ||
                !string.Equals(_hoverTargetId, targetId, StringComparison.Ordinal))
            {
                return;
            }

            _hoverTimer = null;
            _hoverTargetId = null;
            if (!IsSelected(targetId))
            {
                request = CreateRequest(group, GroupSwitchReason.Hover);
            }
        }

        RaiseSwitchRequested(request);
    }

    private void CompleteDragHover(Group group, string targetId, long generation)
    {
        GroupSwitchRequestedEventArgs? request = null;
        lock (_gate)
        {
            if (_isDisposed || generation != _dragHoverGeneration ||
                !string.Equals(_dragHoverTargetId, targetId, StringComparison.Ordinal))
            {
                return;
            }

            _dragHoverTimer = null;
            _dragHoverTargetId = null;
            if (!IsSelected(targetId))
            {
                request = CreateRequest(group, GroupSwitchReason.DragHover);
            }
        }

        RaiseSwitchRequested(request);
    }

    private GroupSwitchRequestedEventArgs CreateRequest(Group group, GroupSwitchReason reason) =>
        new(group, reason, ++_switchGeneration);

    private void RaiseSwitchRequested(GroupSwitchRequestedEventArgs? request)
    {
        if (request is not null)
        {
            SwitchRequested?.Invoke(this, request);
        }
    }

    private bool IsSelected(string groupId) =>
        string.Equals(_selectedGroupId, groupId, StringComparison.Ordinal);

    private void CancelHoverCore()
    {
        _hoverGeneration++;
        _hoverTargetId = null;
        _hoverTimer?.Dispose();
        _hoverTimer = null;
    }

    private void CancelDragHoverCore()
    {
        _dragHoverGeneration++;
        _dragHoverTargetId = null;
        _dragHoverTimer?.Dispose();
        _dragHoverTimer = null;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }
}

