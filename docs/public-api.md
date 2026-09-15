# Public API — v0.1.0

AsyncEventBridge `0.1.0` establishes the .NET Standard 2.0 baseline and the first public bridge contract.

The generated event APIs are the normal Event -> async entry points. `ToEventBridge()` is the normal async -> events entry point.

## Compatibility baseline

The complete runtime targets **.NET Standard 2.0**.

Async-stream interfaces on this target are provided through `Microsoft.Bcl.AsyncInterfaces`. Generated source remains compatible with C# 8 syntax.

## Selecting event source types

For a source type you own, annotate the type:

```csharp
[GenerateAsyncEvents]
public sealed class Sensor
{
    public event EventHandler? Connected;
    public event EventHandler<SensorEventArgs>? ValueChanged;
}
```

For a type you cannot annotate, request generation at assembly level:

```csharp
[assembly: GenerateAsyncEventsFor(typeof(ThirdParty.LegacySensor))]
```

`GenerateAsyncEventsForAttribute` is repeatable, so a consuming assembly can target multiple external event sources.

The assembly-level form does not modify the target type. It generates extension methods in the consuming compilation and only uses events that are accessible there.

## Supported event delegates

The generator supports:

```text
System.EventHandler
System.EventHandler<TEventArgs>
custom void delegates with two non-ref parameters where the second parameter derives from EventArgs
```

The custom-delegate rule covers common delegates such as `PropertyChangedEventHandler`, `NotifyCollectionChangedEventHandler`, `ElapsedEventHandler`, and similarly shaped framework or legacy delegates.

A custom delegate is adapted internally to the central `EventAwaiter` / `EventStream` runtime behavior. The generated facade remains the same regardless of the source delegate type.

If an annotated or explicitly targeted event uses an unsupported delegate shape, the generator reports:

```text
AEB001: Unsupported event delegate
```

Unsupported delegates are therefore visible in build output rather than being silently skipped.

## Event -> Task

For a non-generic `EventHandler` event:

```csharp
Task ConnectedAsync(CancellationToken cancellationToken = default);
Task ConnectedAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
```

For an event whose second delegate parameter is `SensorEventArgs`:

```csharp
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

Typed events expose:

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

`EventBridge<T>` publishes the same terminal events, with the result carried by `AsyncValueEventArgs<T>`.

Consumers attach handlers and then call:

```csharp
bridge.Connect();
```

`Connect()` does not start the underlying task. It connects the already-created async source to event publication. A bridge can only be connected once.

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

## Subscriber exception policy

Async -> events publication isolates subscribers. If a bridge event handler throws, the bridge catches the exception, writes it through `System.Diagnostics.Trace.TraceError`, and continues with the remaining subscribers.

The exception is not propagated through the bridge. This differs from ordinary synchronous event invocation and is part of the v0.1.0 bridge contract.

## Generated API rules

The generator follows these rules:

- source accessibility is never widened;
- external targets expose only events accessible to the consuming compilation;
- public inherited class events are supported;
- protected and private events are not surfaced as top-level generated extensions;
- generic source classes are supported;
- accessible nested source classes are supported;
- generic constraints are preserved;
- normal C# member hiding is respected;
- source instance methods keep normal C# precedence;
- generated extension classes use collision-safe names and remain explicitly callable when a source method conflicts;
- duplicate assembly-level requests for the same target do not create duplicate generated APIs.

## Public runtime types

The intended exported runtime type set is:

```text
GenerateAsyncEventsAttribute
GenerateAsyncEventsForAttribute
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
