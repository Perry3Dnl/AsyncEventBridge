<p align="center">
  <img src="assets/AsyncEventBridge.png" alt="AsyncEventBridge icon" width="128" height="128">
</p>

<h1 align="center">AsyncEventBridge</h1>

<p align="center"><strong>Bridge classic .NET events and modern async code in both directions.</strong></p>

`main` is the single development and release line for AsyncEventBridge starting with **0.3.0**. The modern .NET 10 implementation remains at the repository root, the .NET Standard 2.0 compatibility implementation lives under `compat/netstandard2.0`, and the Unity UPM package lives under `Packages/com.perry3d.async-event-bridge`.

The `0.5.0` development line builds on the hardened 0.4 contracts and expands AsyncEventBridge into a focused interoperability layer for event-driven async workflows. Waiting, streaming, state conditions, lifecycle composition, adaptation, and async-to-event bridging remain one package, without trying to become a general Rx or async-LINQ replacement.

## What it bridges

```text
Event                -> Task
Event                -> IAsyncEnumerable<T>
Task                 -> EventBridge
Task<T>              -> EventBridge<T>
ValueTask            -> EventBridge
ValueTask<T>         -> EventBridge<T>
IAsyncEnumerable<T>  -> EventStreamBridge<T>
Event stream + wait  -> lifecycle-safe async workflow
```

The modern line also includes sender-aware event occurrences, heterogeneous/N-way wait composition, bounded-stream telemetry, `System.Diagnostics.Metrics`, `TimeProvider`, and Native AOT/trimming verification. The 0.5 stream-workflow primitives, `EventStreamComposition.StartAfter`, `TakeUntil`, `RepeatBetween`, and `RepeatBetweenWithLifecycle`, are also available on the .NET Standard 2.0 and Unity portable runtimes.

## Package

Package ID:

```text
AsyncEventBridge
```

For a project consuming the `0.5.0` package:

```xml
<PackageReference Include="AsyncEventBridge" Version="0.5.0" />
```

The source generator ships in the same NuGet package; there is no separate analyzer package to install.

## Before and after

AsyncEventBridge is designed so the **event API still looks like an event API** and the **async API still looks like normal async .NET**. The package owns the subscription, cancellation, race, cleanup, and lifecycle plumbing between them.

The "without AsyncEventBridge" examples below are illustrative raw-.NET implementations of the same intent. Production code normally needs to handle additional failure and race cases as well.

### Wait for one event

Without AsyncEventBridge, turning an event into something awaitable usually means manually creating a completion source, managing the handler lifetime, wiring cancellation, and making sure cleanup happens on every path:

```csharp
static async Task<int> WaitForNextValueAsync(
    Sensor sensor,
    CancellationToken cancellationToken)
{
    var completion =
        new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously);

    EventHandler<int>? handler = null;
    CancellationTokenRegistration registration = default;

    handler = (_, value) =>
    {
        if (completion.TrySetResult(value))
        {
            sensor.ValueChanged -= handler;
            registration.Dispose();
        }
    };

    sensor.ValueChanged += handler;

    registration = cancellationToken.Register(() =>
    {
        if (completion.TrySetCanceled(cancellationToken))
        {
            sensor.ValueChanged -= handler;
        }
    });

    try
    {
        return await completion.Task;
    }
    finally
    {
        sensor.ValueChanged -= handler;
        registration.Dispose();
    }
}
```

With AsyncEventBridge, the event keeps its normal name and gains the async shape a .NET developer would expect:

```csharp
int value = await sensor.ValueChangedAsync(cancellationToken);
```

Existing event-first code is unchanged:

```csharp
sensor.ValueChanged += OnValueChanged;
```

Both styles can exist against the same source type.

### Consume repeated events

Without the package, an async stream over an event normally needs a queue/channel, subscription management, cancellation coordination, completion semantics, and disposal logic.

With AsyncEventBridge:

```csharp
await foreach (int value in
    sensor.ValueChangedStream(cancellationToken))
{
    Process(value);
}
```

The calling code looks like any other `IAsyncEnumerable<T>`; the fact that values originate from a synchronous .NET event is an implementation detail.

### Stay active while state is active

A reconnecting event API often exposes both current state and a change event. Hand-written code has to deal with:

```text
check state
subscribe
state changes between those operations
unsubscribe/re-subscribe the value event
disconnect
reconnect
cancellation
cleanup
```

With the state-driven lifecycle API:

```csharp
await foreach (Reading reading in sensor.ReadingChangedStream()
    .RepeatWhile(
        () => sensor.IsConnected,
        token => sensor.ConnectionChangedAsync(token),
        cancellationToken))
{
    Process(reading);
}
```

For enum or richer state:

```csharp
await foreach (Reading reading in sensor.ReadingChangedStream()
    .RepeatWhile(
        () => sensor.State,
        state => state == SensorState.Connected,
        token => sensor.StateChangedAsync(token),
        cancellationToken))
{
    Process(reading);
}
```

Already-active state starts immediately. Inactive state keeps the value source unsubscribed. Disconnect/reconnect cycles are handled behind the stream API.

### Safely await current-or-future state

The classic unsafe pattern is:

```csharp
if (!client.IsConnected)
{
    // The state can change here, before the event is subscribed.
    await WaitForConnectedEventSomehowAsync();
}
```

With AsyncEventBridge:

```csharp
await EventCondition.WaitUntilAsync(
    () => client.IsConnected,
    token => client.ConnectionChangedAsync(token),
    cancellationToken);
```

The change wait is armed before the state snapshot is checked, so already-satisfied state and transitions during setup are both handled safely.

### Keep an event-first API over async work

The bridge works in the other direction too. Event-oriented consumers do not need to be rewritten just because the implementation becomes asynchronous.

Without a bridge, a class commonly grows custom continuation code and custom result/error events around every task.

With AsyncEventBridge:

```csharp
using EventBridge<SensorConfiguration> bridge =
    LoadSensorConfigurationAsync().ToEventBridge();

bridge.Completed += (_, e) => Apply(e.Value);
bridge.Faulted += (_, e) => Log(e.Exception);
bridge.Cancelled += (_, _) => HandleCancellation();

bridge.Connect();
```

For an async stream:

```csharp
await using EventStreamBridge<Reading> bridge =
    ReadingsAsync().ToEventBridge();

bridge.Value += (_, e) => Process(e.Value);
bridge.Completed += (_, _) => OnCompleted();
bridge.Faulted += (_, e) => Log(e.Exception);

bridge.Connect(cancellationToken);
```

To an event-first developer, these are ordinary .NET events. To an async-first developer, the source remains an ordinary `Task`, `ValueTask`, or `IAsyncEnumerable<T>`.

### One source, both programming styles

A generated event source can serve old and new code at the same time:

```csharp
// Existing event-oriented code.
sensor.ValueChanged += OnValueChanged;

// Async code waiting for one occurrence.
int next = await sensor.ValueChangedAsync(cancellationToken);

// Async code consuming repeated occurrences.
await foreach (int value in sensor.ValueChangedStream(cancellationToken))
{
    Process(value);
}
```

That is the core goal of the package: **developers should be able to use the programming model they already know, while AsyncEventBridge handles the translation layer behind it.**

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

The modern runtime uses `System.Threading.Channels` internally. The default is explicitly **unbounded and lossless**: accepted values are preserved, but sustained producer/consumer imbalance can grow memory usage without a fixed upper bound.

`EventStreamOptions` exposes these buffering modes. In 0.4, the previous `Grow` enum member was renamed to `Unbounded` before the 1.0 API freeze:

```text
Unbounded   preserve accepted values with no fixed buffer limit
DropOldest  discard the oldest buffered value at Capacity
DropNewest  keep the existing buffer and discard the incoming value at Capacity
```

`Capacity` applies only to the two bounded drop modes. It is ignored by `Unbounded`. The property still defaults to `100` so switching to a bounded mode has a useful default; it does not impose a 100-item limit on the default stream.

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

## Compose event streams with event waits

0.5 adds a small lifecycle vocabulary for event-backed streams.

`StartAfter` delays source enumeration until an activation wait succeeds. For generated event streams, that means the underlying event is not even subscribed until activation:

```csharp
await foreach (Reading reading in sensor.ReadingChangedStream()
    .StartAfter(
        token => sensor.ConnectedAsync(token),
        cancellationToken))
{
    Process(reading);
}
```

`TakeUntil` keeps a stream active until a termination wait succeeds:

```csharp
await foreach (Reading reading in sensor.ReadingChangedStream()
    .TakeUntil(
        token => sensor.DisconnectedAsync(token),
        cancellationToken))
{
    Process(reading);
}
```

The two primitives compose into a complete event-driven lifetime:

```csharp
await foreach (Reading reading in sensor.ReadingChangedStream()
    .StartAfter(token => sensor.ConnectedAsync(token))
    .TakeUntil(token => sensor.DisconnectedAsync(token)))
{
    Process(reading);
}
```

Because `TakeUntil` owns the outer lifetime, a disconnect that happens before connection cancels and cleans the pending start wait without ever subscribing the value stream.

For systems that reconnect repeatedly, `RepeatBetween` turns those one-shot windows into one continuous async stream:

```csharp
await foreach (Reading reading in sensor.ReadingChangedStream()
    .RepeatBetween(
        token => sensor.ConnectedAsync(token),
        token => sensor.DisconnectedAsync(token),
        cancellationToken))
{
    Process(reading);
}
```

Every successful activation starts a fresh source enumeration. A successful stop ends that active cycle, performs deterministic cleanup, and rearms activation. A stop that arrives while inactive closes that inactive cycle and rearms without subscribing the source. Source completion also ends only the current cycle; source or lifecycle faults terminate the repeating workflow.

When callers need to observe the lifecycle itself instead of only its values, `RepeatBetweenWithLifecycle` emits strongly typed lifecycle events:

```csharp
await foreach (var item in sensor.ReadingChangedStream()
    .RepeatBetweenWithLifecycle(
        token => sensor.ConnectedAsync(token),
        token => sensor.DisconnectedAsync(token),
        cancellationToken))
{
    switch (item.Kind)
    {
        case EventStreamLifecycleEventKind.Activated:
            BeginSession(item.Cycle);
            break;
        case EventStreamLifecycleEventKind.Value:
            Process(item.Cycle, item.Value);
            break;
        case EventStreamLifecycleEventKind.Deactivated:
        case EventStreamLifecycleEventKind.SourceCompleted:
            EndSession(item.Cycle, item.Kind);
            break;
    }
}
```

Cycle numbers are one-based and advance only after successful activation. `Deactivated` means the stop wait ended the active cycle; `SourceCompleted` means the source ended naturally. Redundant stop notifications while inactive do not emit fake transitions or consume cycle numbers.

Start and stop waits participate in the same deterministic cancellation/cleanup rules as the rest of AsyncEventBridge. Faulted or independently cancelled lifecycle waits propagate their outcomes; cleanup failures remain observable after the primary outcome.

## Wait for event-driven state safely

Event-only waits are not enough when an API also exposes current state. If a client is already connected, waiting only for its next `Connected` event can hang. Checking the property first and subscribing second creates the opposite problem: the state can change between those operations.

`EventCondition.WaitUntilAsync` arms the state-change wait first and then reads the current state:

```csharp
ConnectionState state = await EventCondition.WaitUntilAsync(
    () => client.State,
    state => state == ConnectionState.Connected,
    token => client.StateChangedAsync(token),
    cancellationToken);

// Boolean state has a shorter overload.
await EventCondition.WaitUntilAsync(
    () => client.IsConnected,
    token => client.ConnectionChangedAsync(token),
    cancellationToken);
```

If the condition is already true, the temporary change wait is cancelled, observed, and cleaned up before returning. If a transition happens while the wait is being armed, it is either visible in the subsequent state snapshot or captured by the armed event wait. Spurious change notifications simply cause another subscribe-before-check attempt.

For state-driven lifecycles, callers normally do not need to compose those waits manually. `RepeatWhile` combines current-state checks, change events, activation, deactivation, and reactivation:

```csharp
await foreach (var reading in sensor.ReadingChangedStream()
    .RepeatWhile(
        () => sensor.State,
        state => state == SensorState.Connected,
        token => sensor.StateChangedAsync(token),
        cancellationToken))
{
    Process(reading);
}
```

For ordinary boolean properties the call is shorter:

```csharp
await foreach (var reading in sensor.ReadingChangedStream()
    .RepeatWhile(
        () => sensor.IsConnected,
        token => sensor.ConnectionChangedAsync(token),
        cancellationToken))
{
    Process(reading);
}
```

`RepeatWhileWithLifecycle` exposes the same state-driven stream with `Activated`, `Value`, `Deactivated`, and `SourceCompleted` markers when the application needs explicit session boundaries.

The design goal is that neither programming model feels foreign: existing events remain normal events, generated async methods follow normal async naming, event streams read like ordinary `await foreach`, and async work can still be surfaced back through normal .NET events with `ToEventBridge()`. The coordination machinery stays behind those familiar call shapes.

This is deliberately narrower than adding a general stream-operator library: 0.5 workflow APIs are intended for event-specific coordination and lifetime problems.

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

## Bridge lifecycle boundaries

Bridge event publication uses subscriber snapshots. Adding or removing a subscriber while one event is already being dispatched affects future publications, not the invocation list already captured for the current event.

`EventBridge.Dispose()` and `EventStreamBridge.Dispose()` suppress future publication but do not interrupt a handler snapshot already in flight. `EventStreamBridge.DisposeAsync()` additionally waits for bridge-owned enumeration cleanup and in-flight publication; after it completes, no further bridge handler can run.

Bridges do not replay values or terminal events to late subscribers. See [`docs/0.4-bridge-lifecycle.md`](docs/0.4-bridge-lifecycle.md) for the full lifecycle and threading contract.

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

Subscriber exceptions are isolated: one throwing event subscriber does not stop remaining subscribers or bridge processing. The default policy writes failures through `Trace.TraceError`.

For explicit control, pass `EventBridgeOptions` to `ToEventBridge(...)`. `TraceAndContinue` is the default, `ReportAndContinue` forwards failures to a configured callback, and `IgnoreAndContinue` suppresses bridge-level reporting. All policies continue dispatching remaining subscribers; there is deliberately no background-task `Propagate` mode.

See [`docs/0.4-subscriber-exceptions.md`](docs/0.4-subscriber-exceptions.md) for the policy and threading contract.

## Runtime metrics

The runtime emits BCL-native metrics from the `AsyncEventBridge` meter. No OpenTelemetry package is required by the library itself.

```text
asynceventbridge.event_wait.outcomes
  asynceventbridge.wait.outcome = success | cancelled | timeout | faulted

asynceventbridge.event_stream.dropped
  asynceventbridge.stream.full_mode = drop_oldest | drop_newest
```

Tags are intentionally bounded and low-cardinality. Applications can collect these instruments with `MeterListener`, `dotnet-counters`, OpenTelemetry, or another `System.Diagnostics.Metrics` consumer.

See [`docs/metrics.md`](docs/metrics.md). For the buffering tradeoff and default-policy rationale, see [`docs/0.4-stream-buffering.md`](docs/0.4-stream-buffering.md).

## Native AOT and trimming

The runtime declares AOT compatibility and CI verifies the packed NuGet package by publishing a separate `linux-x64` Native AOT consumer.

The gate fails if the publish produces `ILxxxx` trimming/AOT warnings, then executes the resulting native binary. The native smoke path covers generated event APIs, sender-aware occurrences, event composition, `ValueTask<T>` bridging, and async-stream bridging.

## Performance

BenchmarkDotNet baselines live under `benchmarks/` and compile in normal CI.

Measured optimization work is documented in [`docs/performance-baselines.md`](docs/performance-baselines.md). In particular, the ordinary successful low-level one-shot wait has been reduced from an earlier 568 B baseline to 440 B per operation on the measured completed-wait path.

Benchmarks are intentionally not executed on every CI run so normal verification remains deterministic.

## Verification

CI runs the release gate on pushes to `main` and `release/**`, and on pull requests targeting `main`:

- restore/build/test the full .NET 10 solution, including runtime, generator, lifecycle, race, stress, metrics, and API-lock coverage;
- restore/build/test the .NET Standard 2.0 compatibility solution independently;
- run the modern and compatibility sensor samples;
- create and inspect the modern, compatibility, and unified NuGet package layouts;
- compile and execute clean modern and compatibility consumers from the packed artifacts;
- publish and execute the packaged Native AOT consumer with no `ILxxxx` warnings;
- independently restore/build/test modern and compatibility code on Windows and macOS;
- validate the Unity manifest/version, portable-core parity, asset metadata, Unity generator build, and runtime compilation against Unity API stubs.

The automated Unity job is a repository/compile gate, not a substitute for a real Unity Editor. Unity Test Framework execution, IL2CPP acceptance, and sample validation in a supported Unity Editor remain manual release checks.

See [`docs/release-readiness.md`](docs/release-readiness.md) for the complete release contract and [`docs/public-api.md`](docs/public-api.md) for the public surface.

## Release model

Starting with `0.3.0`, all supported editions are developed from `main`:

- modern .NET 10 runtime and NuGet work at the repository root;
- .NET Standard 2.0 compatibility work under `compat/netstandard2.0`;
- Unity UPM work under `Packages/com.perry3d.async-event-bridge` plus the Unity-specific generator/test projects.

The historical `dotnet-latest`, `base/netstandard2.0`, and `unity` branches are migration/reference lines rather than independent release trains. A release is ready only when every supported edition reaches the same release-readiness gate. Feature parity is not required when a feature depends on runtime-specific capabilities.

## Build from source

```text
dotnet restore AsyncEventBridge.sln
dotnet build AsyncEventBridge.sln -c Release
dotnet test AsyncEventBridge.sln -c Release --no-build
dotnet pack src/AsyncEventBridge/AsyncEventBridge.csproj -c Release
```

## License

AsyncEventBridge is licensed under the **Mozilla Public License 2.0 (MPL-2.0)**. See [`LICENSE`](LICENSE).
