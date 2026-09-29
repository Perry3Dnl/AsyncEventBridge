# SensorMonitoring sample

This sample exercises the main AsyncEventBridge adoption paths on the native .NET 10 line.

Run it from the repository root:

```text
dotnet run --project samples/SensorMonitoring/SensorMonitoring.csproj -c Release
```

The sample demonstrates:

- generated one-shot event waits;
- generated event streams through `IAsyncEnumerable<T>`;
- state-driven reconnecting lifecycles through `RepeatWhileWithLifecycle`;
- `Task<T>` exposed back to event-oriented consumers;
- `IAsyncEnumerable<T>` exposed back through `EventStreamBridge<T>`.

The lifecycle section deliberately raises a value while the sensor is disconnected to demonstrate that the value source is not observed outside an active session. It waits for the emitted `Activated` lifecycle marker before publishing values, so the example does not rely on scheduler timing to know when the event stream is subscribed.

The repository-level README and focused docs cover additional modern features:

- sender-aware `EventOccurrence<TSender, TPayload>` APIs;
- `EventComposition.WaitAnyAsync` / `WaitAllAsync`;
- bounded stream drop telemetry;
- `TimeProvider`-controlled timeouts;
- `ValueTask` bridges;
- `System.Diagnostics.Metrics` instrumentation;
- Native AOT/trimming verification.

The sample is built and executed in CI as part of the 1.0 release gate.
