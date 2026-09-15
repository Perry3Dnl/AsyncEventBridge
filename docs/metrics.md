# Runtime metrics

The native modern-.NET runtime emits production metrics through `System.Diagnostics.Metrics`. No logging, OpenTelemetry, or exporter package is required by AsyncEventBridge itself.

## Meter

The meter name is stable:

```text
AsyncEventBridge
```

Applications can enable that meter with any `System.Diagnostics.Metrics` consumer. OpenTelemetry-based applications can add the meter name to their metrics pipeline and choose their own exporter.

## Instruments

| Instrument | Type | Unit | Tags |
| --- | --- | --- | --- |
| `asynceventbridge.event_wait.outcomes` | `Counter<long>` | `{wait}` | `asynceventbridge.wait.outcome` |
| `asynceventbridge.event_stream.dropped` | `Counter<long>` | `{event}` | `asynceventbridge.stream.full_mode` |

### Wait outcomes

`asynceventbridge.event_wait.outcomes` records one measurement for each terminal event-wait result after cleanup semantics have been applied.

The `asynceventbridge.wait.outcome` tag has four bounded values:

```text
success
cancelled
timeout
faulted
```

A successful event followed by an unsubscribe/cleanup failure is therefore reported as `faulted`, matching the task observed by the caller. Pre-cancelled waits and zero-duration timeouts are also reported even though they do not subscribe to the source event.

### Stream drops

`asynceventbridge.event_stream.dropped` is emitted from the same `System.Threading.Channels` dropped-item callback that backs `EventStreamOptions.DroppedCount`.

The `asynceventbridge.stream.full_mode` tag has two bounded values:

```text
drop_oldest
drop_newest
```

`Grow` streams do not emit drop measurements.

`EventStreamOptions.DroppedCount` remains useful for per-options-instance application logic. The metrics counter is process-wide telemetry intended for monitoring systems and is aggregated by the configured metrics consumer/exporter.

## Cardinality and privacy

AsyncEventBridge intentionally does not attach event names, source types, capacities, object identities, exception messages, or user data as metric tags. The built-in tags have fixed, low-cardinality value sets so production collectors can aggregate them safely.

## Performance

The runtime uses static BCL `Meter`/`Counter<long>` instances and one pre-created tag pair per outcome/mode. Measurement recording does not allocate tag collections on each operation. `benchmarks/AsyncEventBridge.Benchmarks/MetricsBenchmarks.cs` compares the wait and bounded-drop paths with metrics collection disabled and with an active `MeterListener`.
