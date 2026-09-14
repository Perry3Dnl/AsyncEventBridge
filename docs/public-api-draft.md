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

source.Start();
```

### `Task<T>`

```csharp
using var source = LoadSensorConfigurationAsync().ToEventSource();

source.Completed += OnConfigurationLoaded;
source.Faulted += OnConfigurationLoadFailed;
source.Cancelled += OnConfigurationLoadCancelled;

source.Start();
```

`Completed` receives `AsyncValueEventArgs<T>` and exposes the result through `Value`.

`Task` and `Task<T>` event sources are now implemented on the API-design branch. They observe the existing task; they do not start or cancel it.

### `IAsyncEnumerable<T>`

The async-stream event source is still a public API draft. The intended shape is:

```csharp
await using var source = ReadSensorValuesAsync().ToEventSource();

source.Next += OnSensorValue;
source.Completed += OnSensorStreamCompleted;
source.Faulted += OnSensorStreamFailed;
source.Cancelled += OnSensorStreamCancelled;

source.Start(cancellationToken);
```

The cancellation token belongs to stream consumption, so the current direction is to pass it to `Start(...)` rather than `ToEventSource(...)`.

The name `Next` is not considered final yet; a more event-oriented name will be reviewed before the stream runtime is implemented.

## Why `Start()` exists

`Start()` is deliberate. It allows legacy/event-driven consumers to attach every handler before the bridge starts publishing anything.

Without an explicit start boundary, an already-completed `Task` or a very fast `IAsyncEnumerable<T>` can finish while handlers are still being attached. Avoiding that would require hidden delays, buffering, or replay semantics. An explicit `Start()` is deterministic and keeps the event model understandable.

The underlying `Task` may already be running. `Start()` starts observation/publication by the bridge; it does not start the task itself.

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
