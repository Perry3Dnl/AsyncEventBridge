<p align="center">
  <img src="assets/AsyncEventBridge.png" alt="AsyncEventBridge icon" width="128" height="128">
</p>

<h1 align="center">AsyncEventBridge</h1>

<p align="center"><strong>Bridge classic .NET events and modern async code in both directions.</strong></p>

`main` is the native modern-.NET edition of AsyncEventBridge and targets **.NET 10 (`net10.0`)**. The package contains both the runtime and source generator and is designed for applications that want event-to-async and async-to-event interoperability without taking a dependency on Rx or a logging/telemetry framework.

> Need the broad compatibility line? Use `base/netstandard2.0`. Need the Unity package? Use `unity`.

## What it bridges

```text
Event                -> Task
Event                -> IAsyncEnumerable<T>
Task                 -> EventBridge
Task<T>              -> EventBridge<T>
ValueTask            -> EventBridge
ValueTask<T>         -> EventBridge<T>
IAsyncEnumerable<T>  -> EventStreamBridge<T>
```

The modern line also includes sender-aware event occurrences, lifecycle-safe event composition, bounded-stream telemetry, `System.Diagnostics.Metrics`, `TimeProvider`, and Native AOT/trimming verification.

## Package

Package ID:

```text
AsyncEventBridge
```

For a project consuming the `0.2.0` package:

```xml
<PackageReference Include="AsyncEventBridge" Version="0.2.0" />
```

The source generator ships in the same NuGet package; there is no separate analyzer package to install.

## Await a .NET event

For a type you own, annotate it:

```csharp
using AsyncEventBridge;

[GenerateAsyncEvents]
public sealed class Sensor
{
    public event EventHandler<int>? ValueChanged;

    public void Raise(int value) => ValueChanged?.Invoke(this, value);
}
```

The generator creates the async facade:

```csharp
int value = await sensor.ValueChangedAsync(cancellationToken);
```

Filtering and timeout overloads are generated as well. Timeout APIs expose `TimeProvider` for deterministic testing:

```csharp
int value = await sensor.ValueChangedAsync(
    TimeSpan.FromSeconds(30),
    cancellationToken,
    timeProvider);
```

For low-level/manual integration, use `EventAwaiter`:

```csharp
int value = await EventAwaiter.WaitAsync<int>(
    handler => sensor.ValueChanged += handler,
    handler => sensor.ValueChanged -= handler,
    predicate: value => value >= 100,
    cancellationToken,
    timeout: TimeSpan.FromSeconds(5),
    timeProvider: TimeProvider.System);
```

## Modern event shapes

The modern package is not limited to `EventArgs` payloads. Value types, records, DTOs, and other normal non-ref-like payloads are supported.

.NET 10 strongly typed sender delegates work too:

```csharp
[GenerateAsyncEvents]
public sealed class Sensor
{
    public event EventHandler<Sensor, Reading>? ReadingChanged;
}

Reading reading = await sensor.ReadingChangedAsync();
```

The ordinary generated API remains payload-centric: it returns the second event parameter.

Custom two-parameter `void` delegates are supported when their sender and payload shapes are compatible with an async lifetime. This covers common framework patterns such as `PropertyChangedEventHandler`, `NotifyCollectionChangedEventHandler`, `ElapsedEventHandler`, and similar legacy delegates.

Ref-like async payloads such as `Span<T>` are deliberately rejected because they cannot safely escape the synchronous event callback.

## Sender-aware occurrences

When sender identity is part of the event semantics, use the opt-in occurrence facade:

```csharp
EventOccurrence<Sensor, Reading> occurrence =
    await sensor.ReadingChangedOccurrenceAsync(cancellationToken);

Process(occurrence.Sender, occurrence.Payload);
```

Repeated sender-aware events are available as streams:

```csharp
await foreach (EventOccurrence<Sensor, Reading> item in
    sensor.ReadingChangedOccurrenceStream(cancellationToken))
{
    Process(item.Sender, item.Payload);
}
```

The low-level equivalents are `EventOccurrenceAwaiter` and `EventOccurrenceStream`.

## Third-party event sources

For a public type you cannot annotate:

```csharp
[assembly: GenerateAsyncEventsFor(typeof(System.Timers.Timer))]
```

Generation happens in the consuming compilation without modifying the target type.

Generator diagnostics make unsupported requests visible instead of silently omitting APIs:

```text
AEB001  unsupported event delegate/payload shape
AEB002  invalid GenerateAsyncEventsFor target
AEB003  duplicate or redundant generation request
```

## Event streams

Repeated events can be consumed through `IAsyncEnumerable<T>`:

```csharp
await foreach (int value in sensor.ValueChangedStream(cancellationToken))
{
    Console.WriteLine(value);
}
```

The modern runtime uses `System.Threading.Channels` internally. `EventStreamOptions` exposes three buffering modes:

```text
Grow        keep accepted values in an unbounded channel
DropOldest  discard the oldest buffered value at capacity
DropNewest  keep the existing buffer and discard the incoming value
```

Example bounded stream:

```csharp
var options = new EventStreamOptions
{
    Capacity = 100,
    FullMode = EventStreamFullMode.DropNewest,
    DropObserver = droppedCount =>
        Console.WriteLine($"Dropped events: {droppedCount}"),
};

await foreach (Reading reading in sensor.ReadingChangedStream(options, cancellationToken))
{
    Process(reading);
}

Console.WriteLine($"Total dropped: {options.DroppedCount}");
```

`DroppedCount` is thread-safe and aggregates across uses of the same options instance. The drop callback is backed by the channel's actual dropped-item notification rather than inferred from timing or write outcomes.

There is intentionally no producer-blocking mode: blocking a synchronous event callback can change event semantics or introduce deadlocks.

## Compose event waits

`EventComposition` coordinates event waits while ensuring pending/losing waits are cancelled and observed, so ignored tasks do not leave hidden event subscriptions behind.

### Wait for either of two different events

```csharp
EventWaitAnyResult<ConnectedEventArgs, ErrorEventArgs> result =
    await EventComposition.WaitAnyAsync(
        token => client.ConnectedAsync(token),
        token => client.ErrorAsync(token),
        cancellationToken);

if (result.IsFirst)
{
    HandleConnected(result.First);
}
else
{
    HandleError(result.Second);
}
```

### Wait for both different events

```csharp
EventWaitAllResult<ReadyEventArgs, AuthenticatedEventArgs> result =
    await EventComposition.WaitAllAsync(
        token => client.ReadyAsync(token),
        token => client.AuthenticatedAsync(token),
        cancellationToken);

Use(result.First, result.Second);
```

### N-way homogeneous composition

```csharp
EventWaitAnyResult<int> winner =
    await EventComposition.WaitAnyAsync(waits, cancellationToken);

Console.WriteLine($"Wait {winner.Index} produced {winner.Value}");

IReadOnlyList<int> all =
    await EventComposition.WaitAllAsync(waits, cancellationToken);
```

N-way `WaitAllAsync` preserves input order and fails fast by cancelling and observing pending siblings when one member faults or startup fails.

See [`docs/event-composition.md`](docs/event-composition.md) for detailed lifecycle semantics.

## Async work back to events

Tasks and value tasks can be exposed through ordinary .NET events:

```csharp
using EventBridge<SensorConfiguration> bridge =
    LoadSensorConfigurationAsync().ToEventBridge();

bridge.Completed += (_, e) => Console.WriteLine(e.Value);
bridge.Faulted += (_, e) => Console.Error.WriteLine(e.Exception);
bridge.Cancelled += (_, _) => Console.WriteLine("Cancelled");
bridge.Connect();
```

`ValueTask` and `ValueTask<T>` are supported directly. Once a value task is handed to a bridge, the bridge owns observing it; do not independently consume the same value task unless its producer explicitly supports that.

Async streams can be exposed through events too:

```csharp
await using EventStreamBridge<SensorValue> bridge =
    ReadSensorValuesAsync().ToEventBridge();

bridge.Value += (_, e) => Console.WriteLine(e.Value);
bridge.Connect(cancellationToken);
```

Subscriber exceptions are isolated: one throwing event subscriber does not stop remaining subscribers or bridge processing. Failures are written through `Trace.TraceError`.

## Runtime metrics

The runtime emits BCL-native metrics from the `AsyncEventBridge` meter. No OpenTelemetry package is required by the library itself.

```text
asynceventbridge.event_wait.outcomes
  asynceventbridge.wait.outcome = success | cancelled | timeout | faulted

asynceventbridge.event_stream.dropped
  asynceventbridge.stream.full_mode = drop_oldest | drop_newest
```

Tags are intentionally bounded and low-cardinality. Applications can collect these instruments with `MeterListener`, `dotnet-counters`, OpenTelemetry, or another `System.Diagnostics.Metrics` consumer.

See [`docs/metrics.md`](docs/metrics.md).

## Native AOT and trimming

The runtime declares AOT compatibility and CI verifies the packed NuGet package by publishing a separate `linux-x64` Native AOT consumer.

The gate fails if the publish produces `ILxxxx` trimming/AOT warnings, then executes the resulting native binary. The native smoke path covers generated event APIs, sender-aware occurrences, event composition, `ValueTask<T>` bridging, and async-stream bridging.

## Performance

BenchmarkDotNet baselines live under `benchmarks/` and compile in normal CI.

Measured optimization work is documented in [`docs/performance-baselines.md`](docs/performance-baselines.md). In particular, the ordinary successful low-level one-shot wait has been reduced from an earlier 568 B baseline to 440 B per operation on the measured completed-wait path.

Benchmarks are intentionally not executed on every CI run so normal verification remains deterministic.

## Verification

Every push/PR to `main` or `dotnet-latest` runs the release gate:

- restore/build the full .NET 10 solution;
- run runtime, generator, lifecycle, race, stress, metrics, and API-lock tests;
- run the sensor sample;
- create and inspect `.nupkg` and `.snupkg` artifacts;
- compile a clean consumer against the packed package;
- execute a separate packaged runtime consumer;
- publish and execute the packaged Native AOT consumer with no `ILxxxx` warnings;
- independently restore/build/test on Windows and macOS.

See [`docs/release-readiness.md`](docs/release-readiness.md) for the complete release contract and [`docs/public-api.md`](docs/public-api.md) for the public surface.

## Branch model

```text
base/netstandard2.0
├── unity
└── main / dotnet-latest
```

- `main`: primary native .NET 10 product line.
- `dotnet-latest`: modern development/integration line when work is staged before `main`.
- `base/netstandard2.0`: broad compatibility baseline.
- `unity`: Unity-specific package and host integration.

Shared fixes should normally land in the compatibility base first when they genuinely apply to all editions. Modern-only APIs, AOT work, metrics, composition, and modern performance changes belong on the modern line.

## Build from source

```text
dotnet restore AsyncEventBridge.sln
dotnet build AsyncEventBridge.sln -c Release
dotnet test AsyncEventBridge.sln -c Release --no-build
dotnet pack src/AsyncEventBridge/AsyncEventBridge.csproj -c Release
```

## License

AsyncEventBridge is licensed under the **Mozilla Public License 2.0 (MPL-2.0)**. See [`LICENSE`](LICENSE).
