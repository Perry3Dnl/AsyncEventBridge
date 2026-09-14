# Public API draft

This document defines the intended consumer-facing shape before every runtime detail is finalized.

The goal is to keep the common path obvious, discoverable, and small while leaving concurrency and lifecycle complexity behind the facade.

All examples use the same fictional sensor monitoring system. Later examples extend that environment instead of introducing unrelated domains.

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

Proposed generated signatures:

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

Proposed generated signatures:

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

Planned facade:

```csharp
await foreach (var value in sensor.ValueChangedStream(cancellationToken))
{
    Console.WriteLine(value.Value);
}
```

For generic events, a predicate overload is planned as well.

## Async -> events

The same monitoring system may also contain newer async APIs. These examples bridge those APIs back to older event-driven consumers.

### `Task`

```csharp
using var source = SaveSensorConfigurationAsync().ToEventSource();

source.Completed += OnConfigurationSaved;
source.Faulted += OnConfigurationSaveFailed;
source.Cancelled += OnConfigurationSaveCancelled;

source.Connect();
```

### `Task<T>`

```csharp
using var source = LoadSensorConfigurationAsync().ToEventSource();

source.Completed += OnConfigurationLoaded;
source.Faulted += OnConfigurationLoadFailed;
source.Cancelled += OnConfigurationLoadCancelled;

source.Connect();
```

`Completed` receives `AsyncValueEventArgs<T>` and exposes the result through `Value`.

`Task` and `Task<T>` event sources are now implemented on the API-design branch. They observe the existing task and publish its terminal outcome once the bridge is connected.

### `IAsyncEnumerable<T>`

The async-stream event source is still a public API draft. The intended shape is:

```csharp
await using var source = ReadSensorValuesAsync().ToEventSource();

source.Value += OnSensorValue;
source.Completed += OnSensorStreamCompleted;
source.Faulted += OnSensorStreamFailed;
source.Cancelled += OnSensorStreamCancelled;

source.Connect(cancellationToken);
```

The cancellation token belongs to stream consumption, so it is passed to `Connect(...)` rather than `ToEventSource(...)`.

`Value` is the event raised for every value produced by the `IAsyncEnumerable<T>`. The event uses `AsyncValueEventArgs<T>`, so the produced value is available as `e.Value`.

The name is intentionally simple and domain-neutral: the bridge exposes values without implying that they were received, generated, or produced by any specific kind of source.

## Why `Connect()` exists

`Connect()` is the explicit boundary between configuring the event-facing side and allowing the bridge to carry values or terminal outcomes across it.

This lets a consumer attach all event handlers first and then connect both programming models in one clear step:

```csharp
source.Completed += OnCompleted;
source.Faulted += OnFaulted;
source.Cancelled += OnCancelled;

source.Connect();
```

The verb describes the bridge itself rather than claiming to start the underlying async operation.

## Documentation rule

Before/after examples use the same classes, variables, and scenario. Only the code being replaced by AsyncEventBridge changes.

The sensor monitoring environment remains the common example throughout the README, migration guide, API examples, and future NuGet documentation.

## Initial public types

```text
GenerateAsyncEventsAttribute
EventAwaiter
AsyncEventSourceExtensions
TaskEventSource
TaskEventSource<T>
AsyncEnumerableEventSource<T>
AsyncValueEventArgs<T>
AsyncFaultedEventArgs
```

No Rx-style operators, event bus concepts, or messaging abstractions are part of the public surface.
