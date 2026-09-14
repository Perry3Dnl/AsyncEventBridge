# Public API draft

This document defines the intended consumer-facing shape while the first public release is being hardened.

The goal is to keep the common path obvious, discoverable, and small while leaving concurrency and lifecycle complexity behind the facade.

All examples use the same fictional sensor monitoring system. Later examples extend that environment instead of introducing unrelated domains.

## Compatibility baseline

The complete runtime targets **.NET Standard 2.0**. This is the minimum full runtime contract, not a reduced compatibility build.

Future framework targets are additive. They should only be introduced when they provide a concrete compatibility or performance benefit, and normal feature work must not require raising the baseline.

The public API and semantics described below are therefore designed against .NET Standard 2.0 first. Async-stream interfaces required by that target are supplied through `Microsoft.Bcl.AsyncInterfaces`.

The NuGet package is a single package: the runtime is placed under `lib/netstandard2.0` and the source generator under `analyzers/dotnet/cs`. CI compiles a .NET Standard 2.0 / C# 8 project directly against the source projects and also compiles a separate consumer from the built `.nupkg`.

## Events -> async

Assume the existing system exposes a sensor with events such as `Connected`, `Disconnected`, `ValueChanged`, and `AlarmRaised`.

### `EventHandler`

Generated facade:

```csharp
await sensor.ConnectedAsync();

await sensor.ConnectedAsync(cancellationToken);

await sensor.ConnectedAsync(
    TimeSpan.FromSeconds(5),
    cancellationToken);
```

Generated signatures:

```csharp
Task ConnectedAsync(
    CancellationToken cancellationToken = default);

Task ConnectedAsync(
    TimeSpan timeout,
    CancellationToken cancellationToken = default);
```

### `EventHandler<TEventArgs>`

Generated facade:

```csharp
var value = await sensor.ValueChangedAsync();

var value = await sensor.ValueChangedAsync(
    e => e.Value >= 100);

var value = await sensor.ValueChangedAsync(
    e => e.Value >= 100,
    cancellationToken);

var value = await sensor.ValueChangedAsync(
    e => e.Value >= 100,
    TimeSpan.FromSeconds(5),
    cancellationToken);
```

Generated signatures:

```csharp
Task<SensorEventArgs> ValueChangedAsync(
    CancellationToken cancellationToken = default);

Task<SensorEventArgs> ValueChangedAsync(
    Predicate<SensorEventArgs> predicate,
    CancellationToken cancellationToken = default);

Task<SensorEventArgs> ValueChangedAsync(
    TimeSpan timeout,
    CancellationToken cancellationToken = default);

Task<SensorEventArgs> ValueChangedAsync(
    Predicate<SensorEventArgs> predicate,
    TimeSpan timeout,
    CancellationToken cancellationToken = default);
```

The low-level `EventAwaiter` remains available for advanced/manual bridging. The generated methods are the normal entry point.

### Event streams

Generated facade for consuming repeated event occurrences:

```csharp
await foreach (var value in sensor.ValueChangedStream(cancellationToken))
{
    Console.WriteLine(value.Value);
}
```

Generic events also support filtering:

```csharp
await foreach (var value in sensor.ValueChangedStream(
    e => e.Value >= 100,
    cancellationToken))
{
    Console.WriteLine(value.Value);
}
```

Buffering can be configured explicitly when a bounded stream is required:

```csharp
await foreach (var value in sensor.ValueChangedStream(
    new EventStreamOptions
    {
        Capacity = 100,
        FullMode = EventStreamFullMode.DropOldest
    },
    cancellationToken))
{
    Console.WriteLine(value.Value);
}
```

Generated signatures:

```csharp
IAsyncEnumerable<SensorEventArgs> ValueChangedStream(
    CancellationToken cancellationToken = default);

IAsyncEnumerable<SensorEventArgs> ValueChangedStream(
    EventStreamOptions options,
    CancellationToken cancellationToken = default);

IAsyncEnumerable<SensorEventArgs> ValueChangedStream(
    Predicate<SensorEventArgs> predicate,
    CancellationToken cancellationToken = default);

IAsyncEnumerable<SensorEventArgs> ValueChangedStream(
    Predicate<SensorEventArgs> predicate,
    EventStreamOptions options,
    CancellationToken cancellationToken = default);
```

A non-generic `EventHandler` is exposed as `IAsyncEnumerable<EventArgs>`.

The runtime subscribes when enumeration begins and unsubscribes when the enumeration is cancelled, disposed, or leaves the `await foreach`. Events are yielded in the order they reach the bridge. Because a normal .NET event cannot be asynchronously backpressured, values must either be buffered or explicitly dropped when the async consumer is behind.

`EventStreamOptions` makes that behavior explicit:

- `Grow` is the default. It preserves every event value. `Capacity` is the initial buffer capacity, and the buffer can grow beyond it. This is lossless, but sustained producer throughput above consumer throughput can grow memory usage without a fixed upper bound.
- `DropOldest` treats `Capacity` as a hard limit and removes the oldest buffered value when a new value arrives at capacity.
- `DropNewest` treats `Capacity` as a hard limit and drops the newly arriving value when the buffer is already at capacity.

The default capacity is `100`. The enum values are fixed as `Grow = 0`, `DropOldest = 1`, and `DropNewest = 2` so the public contract cannot accidentally change through enum reordering.

`Capacity` and `FullMode` use ordinary setters so the options object remains friendly to the .NET Standard 2.0 compatibility baseline and older C# hosts. `EventStream.Create` snapshots the values before enumeration starts, so later changes to that options instance do not mutate an active stream configuration.

The bridge never blocks the synchronous event producer. A `Wait`/blocking full mode is intentionally not part of the API.

The low-level `EventStream` runtime remains available for advanced/manual bridging, while generated `...Stream()` methods are the normal entry point.

### Generated method accessibility and type support

The generator follows the accessibility of the source API instead of widening it:

```text
public source type + public event     -> public generated methods
internal source type                  -> internal generated methods
internal / protected internal event   -> internal generated methods when accessible
protected / private event             -> no top-level extension methods
```

Generic source classes are supported, including their generic constraints. Accessible nested source classes are supported as well; generated extension methods carry the containing and nested generic parameters and constraints required to address the source type correctly.

Public inherited events are included when the annotated base type does not already generate the bridge API. If an annotated base class already generates an inherited event API, a derived annotated type reuses that extension rather than generating a duplicate. Normal C# member hiding is respected, so a derived member with the same name prevents an inaccessible or hidden base event from being bridged accidentally.

Generated extension classes live in the `AsyncEventBridge` namespace and encode the source namespace, nesting, and generic arity in their generated class name. The class name is normally invisible to consumers because extension syntax is the normal entry point. It also provides an explicit fallback when the source type already declares an instance method with the same name:

```csharp
await sensor.ValueChangedAsync(); // source instance method wins if one exists

await AsyncEventBridge.Demo_DOT_SensorAsyncEventExtensions
    .ValueChangedAsync(sensor);   // explicit generated bridge method
```

## Async -> events

The same monitoring system may also contain newer async APIs. These examples bridge those APIs back to older event-driven consumers.

### Naming decision

The public vocabulary for the async -> events direction is:

```text
ToEventBridge()
EventBridge
EventBridge<T>
EventStreamBridge<T>
Connect()
Connect(cancellationToken)
```

`ToEventBridge()` creates the event-facing bridge object. Consumers attach their handlers first, then call `Connect()` to connect the bridge to the async source and allow publication.

This naming is intentional. `Connect()` describes the bridge operation and does not imply that it starts the underlying `Task`.

### `Task`

```csharp
using EventBridge bridge =
    SaveSensorConfigurationAsync().ToEventBridge();

bridge.Completed += OnConfigurationSaved;
bridge.Faulted += OnConfigurationSaveFailed;
bridge.Cancelled += OnConfigurationSaveCancelled;

bridge.Connect();
```

### `Task<T>`

```csharp
using EventBridge<SensorConfiguration> bridge =
    LoadSensorConfigurationAsync().ToEventBridge();

bridge.Completed += OnConfigurationLoaded;
bridge.Faulted += OnConfigurationLoadFailed;
bridge.Cancelled += OnConfigurationLoadCancelled;

bridge.Connect();
```

`Completed` receives `AsyncValueEventArgs<T>` and exposes the result through `Value`.

The bridge observes the existing task and publishes its terminal outcome once connected. It does not claim ownership of how or when the task itself was started.

Calling `Dispose()` before terminal publication suppresses that future outcome and clears the handlers. If a terminal publication already began before disposal won the race, that in-flight event dispatch is allowed to finish after `Dispose()` returns.

### `IAsyncEnumerable<T>`

Async streams use a stream-specific bridge:

```csharp
await using EventStreamBridge<SensorValue> bridge =
    ReadSensorValuesAsync().ToEventBridge();

bridge.Value += OnSensorValue;
bridge.Completed += OnSensorStreamCompleted;
bridge.Faulted += OnSensorStreamFailed;
bridge.Cancelled += OnSensorStreamCancelled;

bridge.Connect(cancellationToken);
```

`EventStreamBridge<T>` consumes the async sequence and publishes each item through `Value` in enumeration order. The stream then publishes exactly one terminal outcome: `Completed`, `Faulted`, or `Cancelled`.

The stream uses `EventStreamBridge<T>` instead of forcing stream-specific `Value` behavior onto the task-oriented `EventBridge<T>` type.

The cancellation token belongs to stream consumption, so it is passed to `Connect(...)` rather than `ToEventBridge(...)`.

`Value` uses `AsyncValueEventArgs<T>`, so the produced value is available as `e.Value`. Subscriber exceptions are isolated so one throwing handler does not stop other handlers or the stream bridge itself.

`Dispose()` requests cancellation and suppresses future publication. `DisposeAsync()` does the same and also waits for asynchronous enumerator cleanup. Owner disposal does not publish `Cancelled`; `Cancelled` represents cancellation of the connected stream operation.

The bridge does not buffer or replay values. A handler attached after `Connect()` may miss values that were already published, which matches normal .NET event behavior.

### Stream bridge lifecycle and races

The stream bridge has an explicit race contract so disposal and terminal outcomes remain predictable under concurrency:

- `Dispose()` is non-blocking with respect to event dispatch. It suppresses new publication, but an event publication that already started is allowed to finish after `Dispose()` returns.
- `DisposeAsync()` suppresses new publication and waits for an in-flight event publication plus asynchronous enumerator cleanup. After it completes, the bridge will not invoke another event handler.
- Owner disposal does not publish `Cancelled`.
- `Completed`, `Faulted`, and `Cancelled` are mutually exclusive terminal outcomes. At most one is published.
- If cancellation races with natural completion or a fault, no outcome gets artificial priority. Whichever outcome reaches terminal publication first wins.
- A `Value` publication that already started may finish during disposal. A value that has not yet entered publication is suppressed once disposal wins the race.

These guarantees are covered with controlled synchronization tests rather than timing-based `Task.Delay` assertions.

## Why `Connect()` exists

`Connect()` is the explicit boundary between configuring the event-facing side and allowing the bridge to carry values or terminal outcomes across it.

This lets a consumer attach all event handlers first and then connect both programming models in one clear step:

```csharp
bridge.Completed += OnCompleted;
bridge.Faulted += OnFaulted;
bridge.Cancelled += OnCancelled;

bridge.Connect();
```

The verb describes the bridge itself rather than claiming to start the underlying async operation.

## Packaging contract

The source generator and runtime ship together in one `AsyncEventBridge` package. CI verifies all of the following from the built package rather than only from project references:

- the runtime assembly and XML documentation are present;
- the generator is present in `analyzers/dotnet/cs`;
- the README and icon are present;
- a .NET Standard 2.0 consumer can restore and compile generated methods from the package;
- a separate runtime consumer can execute Event -> Task, Task<T> -> Events, and IAsyncEnumerable<T> -> Events from the package;
- the `.nupkg` is retained as a CI artifact for manual host testing.

## Documentation rule

Before/after examples use the same classes, variables, and scenario. Only the code being replaced by AsyncEventBridge changes.

The sensor monitoring environment remains the common example throughout the README, migration guide, API examples, NuGet documentation, and `samples/SensorMonitoring`.

## Intended public types

```text
GenerateAsyncEventsAttribute
EventAwaiter
EventStream
EventStreamOptions
EventStreamFullMode
AsyncEventBridgeExtensions
EventBridge
EventBridge<T>
EventStreamBridge<T>
AsyncValueEventArgs<T>
AsyncFaultedEventArgs
```

The runtime test suite locks this exported type set and the intentional public methods, events, and properties so accidental API expansion is caught during CI. Stream defaults and enum numeric values are locked as well.

No Rx-style operators, event bus concepts, or messaging abstractions are part of the public surface.
