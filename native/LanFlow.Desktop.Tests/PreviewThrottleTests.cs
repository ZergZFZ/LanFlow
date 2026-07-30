using LanFlow.Desktop.Presentation;
using Xunit;

namespace LanFlow.Desktop.Tests;

public sealed class PreviewThrottleTests
{
    [Fact]
    public void Push_CoalescesBurstAndAppliesLatestValueAtTrailingEdge()
    {
        var scheduler = new ManualTimerScheduler();
        var applied = new List<double>();
        using var throttle = new PreviewThrottle<double>(
            TimeSpan.FromMilliseconds(33),
            scheduler,
            applied.Add);

        throttle.Push(0.80);
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(5));
        throttle.Push(0.70);
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(5));
        throttle.Push(0.60);

        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(22));
        Assert.Empty(applied);
        Assert.Equal(1, scheduler.ScheduledCount);

        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(1));

        Assert.Equal([0.60], applied);
        Assert.Equal(1, scheduler.ScheduledCount);
    }

    [Fact]
    public void Flush_AppliesPendingValueImmediatelyAndCancelsScheduledCallback()
    {
        var scheduler = new ManualTimerScheduler();
        var applied = new List<double>();
        using var throttle = new PreviewThrottle<double>(
            TimeSpan.FromMilliseconds(33),
            scheduler,
            applied.Add);

        throttle.Push(0.80);
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(10));
        throttle.Push(0.60);
        throttle.Flush();

        Assert.Equal([0.60], applied);

        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(100));
        Assert.Equal([0.60], applied);
    }

    [Fact]
    public void Dispose_CancelsPendingValueAndIgnoresFuturePushes()
    {
        var scheduler = new ManualTimerScheduler();
        var applied = new List<double>();
        var throttle = new PreviewThrottle<double>(
            TimeSpan.FromMilliseconds(33),
            scheduler,
            applied.Add);

        throttle.Push(0.80);
        throttle.Dispose();
        throttle.Push(0.60);
        throttle.Flush();
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(100));

        Assert.Empty(applied);
    }

    private sealed class ManualTimerScheduler : ITimerScheduler
    {
        private readonly List<ScheduledAction> _scheduled = [];
        private TimeSpan _elapsed;

        public int ScheduledCount { get; private set; }

        public IDisposable Schedule(TimeSpan delay, Action action)
        {
            ScheduledCount++;
            var scheduled = new ScheduledAction(_elapsed + delay, action);
            _scheduled.Add(scheduled);
            return scheduled;
        }

        public void AdvanceBy(TimeSpan duration)
        {
            _elapsed += duration;
            while (true)
            {
                var next = _scheduled
                    .Where(item => !item.IsDisposed && item.DueAt <= _elapsed)
                    .OrderBy(item => item.DueAt)
                    .FirstOrDefault();
                if (next is null)
                {
                    return;
                }

                next.Dispose();
                next.Action();
            }
        }

        private sealed class ScheduledAction(TimeSpan dueAt, Action action) : IDisposable
        {
            public TimeSpan DueAt { get; } = dueAt;
            public Action Action { get; } = action;
            public bool IsDisposed { get; private set; }

            public void Dispose() => IsDisposed = true;
        }
    }
}
