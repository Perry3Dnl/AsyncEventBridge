# Public API — v0.1.0

AsyncEventBridge `0.1.0` is the first release of the .NET Standard 2.0 baseline.

The public API is intentionally small. The generated event APIs are the normal Event -> async entry points, while `ToEventBridge()` is the normal async -> events entry point.

## Compatibility baseline

The complete runtime targets `.NET Standard 2.0`.

Async-stream interfaces on this target are provided through `Microsoft.Bcl.AsyncInterfaces`. Generated source is kept compatible with C# 8 syntax.

## Event -> Task

For an annotated source type:

```csharp
[GenerateAsyncEvents]
public sealed class Sensor
{
    public event EventHandler? Connected;
    public event EventHandler<SensorEventArgs>? ValueChanged;
}
```

The generator exposes:

```csharp
Task ConnectedAsync(CancellationToken cancellationToken = default);
Task ConnectedAsync(TimeSpan timeout, CancellationToken cancellationToken = default);

Task<SensorEventArgs> ValueChangedAsync(CancellationToken cancellationToken = default);
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

`EventAwaiter` is the low-level runtime API for manual integration.

## Event -> IAsyncEnumerable<T>

Generated event-stream APIs follow the same event name:

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

`EventStreamOptions` defaults to:

```text
Capacity = 100
FullMode = Grow
```

The fixed `EventStreamFullMode` values are:

```text
Grow = 0
DropOldest = 1
DropNewest = 2
```

`Grow` is lossless but can grow memory usage without a fixed upper bound when producers permanently outrun consumers. `DropOldest` and `DropNewest` use `Capacity` as a hard bound.

`EventStream` is the low-level runtime API for manual integration.

## Task -> events

```csharp
EventBridge ToEventBridge(this Task task);
EventBridge<T> ToEventBridge<T>(this Task<T> task);
```

`EventBridge` publishes:

```text
Completed
Faulted
Cancelled
```

`EventBridge<T>` publishes the same terminal events, with the completed result carried by `AsyncValueEventArgs<T>`.

Consumers attach handlers and then call:

```csharp
bridge.Connect();
```

`Connect()` does not start the underlying task. It connects the already-created async source to event publication.

A bridge can only be connected once.

## IAsyncEnumerable<T> -> events

```csharp
EventStreamBridge<T> ToEventBridge<T>(this IAsyncEnumerable<T> source);
```

The stream bridge publishes:

```text
Value
Completed
Faulted
Cancelled
```

Consumers attach handlers and then call:

```csharp
bridge.Connect(cancellationToken);
```

Values are published in enumeration order. There is no replay buffer in this direction.

`Dispose()` suppresses new publication without waiting for an event dispatch already in progress. `DisposeAsync()` also waits for bridge-owned async enumeration cleanup and in-flight dispatch. Owner disposal does not publish `Cancelled`.

## Generated API rules

The generator follows these rules:

- source accessibility is never widened;
- public inherited events are supported;
- protected and private events are not surfaced as top-level generated extensions;
- generic source types are supported;
- accessible nested source types are supported;
- generic constraints are preserved;
- normal C# member hiding is respected;
- source instance methods keep normal C# precedence;
- generated extension classes use collision-safe names and remain explicitly callable when a source method conflicts.

## Public runtime types

The intended exported runtime type set for `0.1.0` is:

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

The exported type set and public member names are locked by tests to catch accidental API expansion.
