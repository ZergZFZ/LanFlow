using LanFlow.Desktop.Models;
using LanFlow.Desktop.Presentation;
using Xunit;

namespace LanFlow.Desktop.Tests;

public sealed class GroupSwitchCoordinatorTests
{
    [Fact]
    public void Hover_OnlyLatestTargetFiresAfterTwoHundredMilliseconds()
    {
        var clock = new ManualTimerScheduler();
        using var coordinator = new GroupSwitchCoordinator(clock, TimeSpan.FromMilliseconds(200));
        var fired = new List<GroupSwitchRequestedEventArgs>();
        coordinator.SwitchRequested += (_, e) => fired.Add(e);

        coordinator.BeginHover(Group("A"));
        clock.AdvanceBy(TimeSpan.FromMilliseconds(100));
        coordinator.BeginHover(Group("B"));
        clock.AdvanceBy(TimeSpan.FromMilliseconds(199));

        Assert.Empty(fired);

        clock.AdvanceBy(TimeSpan.FromMilliseconds(1));

        var request = Assert.Single(fired);
        Assert.Equal("B", request.Group.Id);
        Assert.Equal(GroupSwitchReason.Hover, request.Reason);
    }

    [Fact]
    public void Hover_CancelMatchingTargetPreventsSwitch()
    {
        var clock = new ManualTimerScheduler();
        using var coordinator = new GroupSwitchCoordinator(clock, TimeSpan.FromMilliseconds(200));
        var fired = new List<GroupSwitchRequestedEventArgs>();
        var group = Group("A");
        coordinator.SwitchRequested += (_, e) => fired.Add(e);

        coordinator.BeginHover(group);
        coordinator.CancelHover(group);
        clock.AdvanceBy(TimeSpan.FromMilliseconds(200));

        Assert.Empty(fired);
    }

    [Fact]
    public void SelectedGroup_IsSkippedForAllRequestKinds()
    {
        var clock = new ManualTimerScheduler();
        using var coordinator = new GroupSwitchCoordinator(clock, TimeSpan.FromMilliseconds(200))
        {
            SelectedGroupId = "A",
        };
        var fired = new List<GroupSwitchRequestedEventArgs>();
        var selected = Group("A");
        coordinator.SwitchRequested += (_, e) => fired.Add(e);

        coordinator.RequestClick(selected);
        coordinator.BeginHover(selected);
        coordinator.BeginDragHover(selected);
        clock.AdvanceBy(TimeSpan.FromMilliseconds(500));

        Assert.Empty(fired);
    }

    [Fact]
    public void HoverCancellation_DoesNotCancelIndependentDragHover()
    {
        var clock = new ManualTimerScheduler();
        using var coordinator = new GroupSwitchCoordinator(clock, TimeSpan.FromMilliseconds(200));
        var fired = new List<GroupSwitchRequestedEventArgs>();
        var hover = Group("hover");
        var drag = Group("drag");
        coordinator.SwitchRequested += (_, e) => fired.Add(e);

        coordinator.BeginDragHover(drag);
        coordinator.BeginHover(hover);
        coordinator.CancelHover(hover);
        clock.AdvanceBy(TimeSpan.FromMilliseconds(200));

        var request = Assert.Single(fired);
        Assert.Equal("drag", request.Group.Id);
        Assert.Equal(GroupSwitchReason.DragHover, request.Reason);
    }

    [Fact]
    public void Click_FiresImmediatelyAndInvalidatesPendingHover()
    {
        var clock = new ManualTimerScheduler();
        using var coordinator = new GroupSwitchCoordinator(clock, TimeSpan.FromMilliseconds(200));
        var fired = new List<GroupSwitchRequestedEventArgs>();
        coordinator.SwitchRequested += (_, e) => fired.Add(e);

        coordinator.BeginHover(Group("hover"));
        clock.AdvanceBy(TimeSpan.FromMilliseconds(100));
        coordinator.RequestClick(Group("click"));

        var click = Assert.Single(fired);
        Assert.Equal("click", click.Group.Id);
        Assert.Equal(GroupSwitchReason.Click, click.Reason);

        clock.AdvanceBy(TimeSpan.FromMilliseconds(500));
        Assert.Single(fired);
    }

    [Fact]
    public void NewRequests_UseMonotonicallyIncreasingGenerations()
    {
        var clock = new ManualTimerScheduler();
        using var coordinator = new GroupSwitchCoordinator(clock, TimeSpan.FromMilliseconds(200));
        var generations = new List<long>();
        coordinator.SwitchRequested += (_, e) => generations.Add(e.Generation);

        coordinator.RequestClick(Group("A"));
        coordinator.BeginHover(Group("B"));
        clock.AdvanceBy(TimeSpan.FromMilliseconds(200));
        coordinator.BeginDragHover(Group("C"));
        clock.AdvanceBy(TimeSpan.FromMilliseconds(200));

        Assert.Equal(3, generations.Count);
        Assert.True(generations[0] < generations[1]);
        Assert.True(generations[1] < generations[2]);
    }

    [Fact]
    public void EndDrag_CancelsOnlyDragHover()
    {
        var clock = new ManualTimerScheduler();
        using var coordinator = new GroupSwitchCoordinator(clock, TimeSpan.FromMilliseconds(200));
        var fired = new List<GroupSwitchRequestedEventArgs>();
        coordinator.SwitchRequested += (_, e) => fired.Add(e);

        coordinator.BeginHover(Group("hover"));
        coordinator.BeginDragHover(Group("drag"));
        coordinator.EndDrag();
        clock.AdvanceBy(TimeSpan.FromMilliseconds(200));

        var request = Assert.Single(fired);
        Assert.Equal("hover", request.Group.Id);
        Assert.Equal(GroupSwitchReason.Hover, request.Reason);
    }

    private static Group Group(string id) => new() { Id = id, Name = id };

    private sealed class ManualTimerScheduler : ITimerScheduler
    {
        private readonly List<ScheduledAction> _actions = [];
        private TimeSpan _now;
        private long _order;

        public IDisposable Schedule(TimeSpan delay, Action action)
        {
            var scheduled = new ScheduledAction(_now + delay, _order++, action);
            _actions.Add(scheduled);
            return scheduled;
        }

        public void AdvanceBy(TimeSpan amount)
        {
            var target = _now + amount;
            while (true)
            {
                var next = _actions
                    .Where(action => !action.IsDisposed && action.Due <= target)
                    .OrderBy(action => action.Due)
                    .ThenBy(action => action.Order)
                    .FirstOrDefault();
                if (next is null)
                {
                    break;
                }

                _actions.Remove(next);
                _now = next.Due;
                next.Invoke();
            }

            _now = target;
        }

        private sealed class ScheduledAction(
            TimeSpan due,
            long order,
            Action action) : IDisposable
        {
            public TimeSpan Due { get; } = due;
            public long Order { get; } = order;
            public bool IsDisposed { get; private set; }

            public void Dispose() => IsDisposed = true;

            public void Invoke()
            {
                if (!IsDisposed)
                {
                    IsDisposed = true;
                    action();
                }
            }
        }
    }
}
