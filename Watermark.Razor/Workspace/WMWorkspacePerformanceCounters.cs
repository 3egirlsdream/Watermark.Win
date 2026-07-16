#nullable enable

using System.Diagnostics;

namespace Watermark.Razor.Workspace;

public sealed class WMWorkspacePerformanceCounters : IWMWorkspacePerformanceCounters
{
    private readonly object gate = new();
    private readonly Dictionary<WMWorkspaceMetricStage, int> calls = [];
    private readonly Dictionary<WMWorkspaceMetricStage, double> durations = [];

    public IDisposable Measure(WMWorkspaceMetricStage stage)
    {
        Increment(stage);
        return new Measurement(this, stage);
    }

    public void Increment(WMWorkspaceMetricStage stage)
    {
        lock (gate) calls[stage] = calls.GetValueOrDefault(stage) + 1;
    }

    public WMWorkspaceMetricSnapshot Snapshot()
    {
        lock (gate)
        {
            return new WMWorkspaceMetricSnapshot(
                new Dictionary<WMWorkspaceMetricStage, int>(calls),
                new Dictionary<WMWorkspaceMetricStage, double>(durations));
        }
    }

    public void Reset()
    {
        lock (gate)
        {
            calls.Clear();
            durations.Clear();
        }
    }

    private void AddDuration(WMWorkspaceMetricStage stage, double milliseconds)
    {
        lock (gate) durations[stage] = durations.GetValueOrDefault(stage) + milliseconds;
    }

    private sealed class Measurement(
        WMWorkspacePerformanceCounters owner,
        WMWorkspaceMetricStage stage) : IDisposable
    {
        private readonly long started = Stopwatch.GetTimestamp();
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            owner.AddDuration(stage, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }
}
