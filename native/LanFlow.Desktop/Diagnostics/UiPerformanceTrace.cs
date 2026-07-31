using System.Collections.Concurrent;
using System.Diagnostics;

namespace LanFlow.Desktop.Diagnostics;

public sealed class UiPerformanceTrace
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly TraceSource _source = new("LanFlow.UI");
    private readonly ConcurrentDictionary<string, long> _starts = new();

    public void GroupSwitchStarted(string groupId) => _starts[groupId] = _clock.ElapsedTicks;

    public void SelectionAcknowledged(string groupId) => Write(groupId, "selection-ack", 0);

    public void ContentStable(string groupId, int realizedContainers) =>
        Write(groupId, "content-stable", realizedContainers);

    private void Write(string groupId, string marker, int realized)
    {
        if (!_starts.TryGetValue(groupId, out var start)) return;

        var elapsedMs = (_clock.ElapsedTicks - start) * 1000d / Stopwatch.Frequency;
        _source.TraceEvent(
            TraceEventType.Information,
            0,
            $"group={groupId};marker={marker};elapsedMs={elapsedMs:F2};realized={realized}");
        _source.Flush();
    }
}
