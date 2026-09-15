using System.Diagnostics.Metrics;

namespace AsyncEventBridge;

internal static class AsyncEventBridgeMetrics
{
    internal const string MeterName = "AsyncEventBridge";
    internal const string WaitOutcomesInstrumentName = "asynceventbridge.event_wait.outcomes";
    internal const string StreamDropsInstrumentName = "asynceventbridge.event_stream.dropped";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> WaitOutcomes = Meter.CreateCounter<long>(
        WaitOutcomesInstrumentName,
        unit: "{wait}",
        description: "Completed event waits grouped by terminal outcome.");

    private static readonly Counter<long> StreamDrops = Meter.CreateCounter<long>(
        StreamDropsInstrumentName,
        unit: "{event}",
        description: "Event-stream items dropped because a bounded buffer was full.");

    private static readonly KeyValuePair<string, object?> WaitSuccessTag =
        new("asynceventbridge.wait.outcome", "success");

    private static readonly KeyValuePair<string, object?> WaitCancelledTag =
        new("asynceventbridge.wait.outcome", "cancelled");

    private static readonly KeyValuePair<string, object?> WaitTimeoutTag =
        new("asynceventbridge.wait.outcome", "timeout");

    private static readonly KeyValuePair<string, object?> WaitFaultedTag =
        new("asynceventbridge.wait.outcome", "faulted");

    private static readonly KeyValuePair<string, object?> DropOldestTag =
        new("asynceventbridge.stream.full_mode", "drop_oldest");

    private static readonly KeyValuePair<string, object?> DropNewestTag =
        new("asynceventbridge.stream.full_mode", "drop_newest");

    internal static void RecordWaitSuccess() =>
        WaitOutcomes.Add(1, WaitSuccessTag);

    internal static void RecordWaitCancelled() =>
        WaitOutcomes.Add(1, WaitCancelledTag);

    internal static void RecordWaitTimeout() =>
        WaitOutcomes.Add(1, WaitTimeoutTag);

    internal static void RecordWaitFaulted() =>
        WaitOutcomes.Add(1, WaitFaultedTag);

    internal static void RecordStreamDrop(EventStreamFullMode fullMode)
    {
        switch (fullMode)
        {
            case EventStreamFullMode.DropOldest:
                StreamDrops.Add(1, DropOldestTag);
                break;
            case EventStreamFullMode.DropNewest:
                StreamDrops.Add(1, DropNewestTag);
                break;
        }
    }
}
