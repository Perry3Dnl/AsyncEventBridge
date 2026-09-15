# SensorMonitoring sample

This sample exercises the primary AsyncEventBridge Event -> async flow on the native .NET 10 line.

Run it from the repository root:

```text
dotnet run --project samples/SensorMonitoring/SensorMonitoring.csproj -c Release
```

The sample demonstrates generated event waits and streams in ordinary application code. It is intentionally small; the repository-level README and focused docs cover the more advanced modern features:

- sender-aware `EventOccurrence<TSender, TPayload>` APIs;
- `EventComposition.WaitAnyAsync` / `WaitAllAsync`;
- bounded stream drop telemetry;
- `TimeProvider`-controlled timeouts;
- `ValueTask` and async-stream bridges;
- `System.Diagnostics.Metrics` instrumentation;
- Native AOT/trimming verification.

The sample is built and executed in CI as part of the release gate on `main` and `dotnet-latest`.
