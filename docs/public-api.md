# Public API — v0.4.0

AsyncEventBridge `0.4.0` keeps the native .NET 10 runtime on the unified release line and hardens behavioral contracts before 1.0.

The normal Event -> async entry points are generated APIs such as `<EventName>Async(...)`, `<EventName>Stream(...)`, and sender-aware `<EventName>OccurrenceAsync(...)` / `<EventName>OccurrenceStream(...)`. The normal async -> events entry point is `ToEventBridge()`.

## Runtime baseline

The runtime targets **.NET 10 (`net10.0`)** directly. The source generator remains `netstandard2.0` so Roslyn compiler-host compatibility is not tied to the runtime target.

The runtime is verified under trimming and Native AOT through a packaged `linux-x64` consumer in CI.

## Selecting event source types

For a source type you own:

```csharp
[GenerateAsyncEvents]
public sealed class Sensor
{
    public event EventHandler<int>? ValueChanged;
}
```

For a public type you cannot annotate:

```csharp
[assembly: GenerateAsyncEventsFor(typeof(ThirdParty.LegacySensor))]
```

Assembly-level generation does not modify the target type. It generates extensions in the consuming compilation and only exposes events accessible there.

## Supported event delegates

The generator supports:

```text
System.EventHandler
System.EventHandler<TPayload>
System.EventHandler<TSender, TPayload>
custom void delegates with exactly two non-ref parameters
```

The payload must be safe to carry across an async lifetime. Concrete ref-like payloads and generic payload parameters that allow ref structs are rejected.

Generator diagnostics currently include:

```text
AEB001  unsupported event delegate/payload shape
AEB002  invalid GenerateAsyncEventsFor target
AEB003  duplicate or redundant generation request
```

## Event -> Task

Low-level manual integration uses `EventAwaiter.WaitAsync(...)` and supports filtering, cancellation, timeout, and an optional `TimeProvider`.

Generated payload-centric APIs keep sender plumbing out of the result:

```csharp
int value = await sensor.ValueChangedAsync(cancellationToken);
```

Strongly typed sender delegates still return the second event argument from the ordinary generated API.

## Sender-aware event occurrences

When sender identity matters, use the occurrence facade:

```csharp
EventOccurrence<Sensor, int> occurrence =
    await sensor.ValueChangedOccurrenceAsync(cancellationToken);

Sensor sender = occurrence.Sender;
int value = occurrence.Payload;
```

Low-level integration uses `EventOccurrenceAwaiter.WaitAsync<TSender, TPayload>(...)`.

## Event -> IAsyncEnumerable<T>

Generated event streams expose repeated payloads:

```csharp
await foreach (int value in sensor.ValueChangedStream(cancellationToken))
{
    Process(value);
}
```

Sender-aware streams are available through `<EventName>OccurrenceStream(...)` and the low-level `EventOccurrenceStream.Create(...)` API.

`EventStreamOptions` defaults to:

```text
Capacity = 100
FullMode = Unbounded
DroppedCount = 0
DropObserver = null
```

The fixed enum values are:

```text
Unbounded = 0
Grow = Unbounded
DropOldest = 1
DropNewest = 2
```

`Unbounded` is the canonical lossless mode. `Grow` remains a source-compatible alias for the same numeric value. In this mode, `Capacity` is ignored completely; the default value of `100` is only the default hard limit used if a caller selects `DropOldest` or `DropNewest`.

The unbounded default deliberately avoids silent event loss, but sustained producer throughput above consumer throughput can grow memory usage without a fixed upper bound. Applications that require a memory bound must opt into one of the two explicit drop policies. Bounded drop telemetry comes from the underlying channel's real dropped-item callback.

## Event composition

`EventComposition` provides lifecycle-safe composition for cancellable event waits.

Two heterogeneous waits:

```csharp
EventWaitAnyResult<int, string> any = await EventComposition.WaitAnyAsync(
    token => source.NumberAsync(token),
    token => source.TextAsync(token),
    cancellationToken);

EventWaitAllResult<int, string> all = await EventComposition.WaitAllAsync(
    token => source.NumberAsync(token),
    token => source.TextAsync(token),
    cancellationToken);
```

N homogeneous waits:

```csharp
EventWaitAnyResult<int> winner = await EventComposition.WaitAnyAsync(
    waits,
    cancellationToken);

IReadOnlyList<int> values = await EventComposition.WaitAllAsync(
    waits,
    cancellationToken);
```

Composition owns coordination cancellation. Losing or pending waits are cancelled and observed so hidden event subscriptions are not left behind. Startup failures and cleanup failures remain observable.

## Task / ValueTask -> events

```csharp
EventBridge ToEventBridge(this Task task);
EventBridge<T> ToEventBridge<T>(this Task<T> task);
EventBridge ToEventBridge(this ValueTask task);
EventBridge<T> ToEventBridge<T>(this ValueTask<T> task);
```

`EventBridge` publishes `Completed`, `Faulted`, and `Cancelled`. Generic bridges carry results through `AsyncValueEventArgs<T>`.

A bridge can be connected only once. A `ValueTask` handed to a bridge is owned by the bridge for observation and should not also be consumed independently unless its producer explicitly permits multiple consumption.

## IAsyncEnumerable<T> -> events

```csharp
EventStreamBridge<T> ToEventBridge<T>(this IAsyncEnumerable<T> source);
```

The stream bridge publishes `Value`, `Completed`, `Faulted`, and `Cancelled` in enumeration order.

`Dispose()` suppresses new publication without waiting for already-running dispatch. `DisposeAsync()` additionally waits for bridge-owned enumeration cleanup and in-flight dispatch. Owner disposal does not publish `Cancelled`.

A source-thrown `OperationCanceledException` is classified as bridge cancellation only when the bridge lifetime token was actually cancelled; otherwise it is surfaced as `Faulted`.

## Subscriber exception policy

Async -> events publication isolates subscribers. If one bridge event handler throws, AsyncEventBridge writes the failure through `System.Diagnostics.Trace.TraceError` and continues dispatching remaining subscribers. Subscriber exceptions are not propagated through the bridge.

## Metrics

The runtime exposes the `AsyncEventBridge` meter with stable low-cardinality counters:

```text
asynceventbridge.event_wait.outcomes
asynceventbridge.event_stream.dropped
```

See `docs/metrics.md` for instrument names, tags, and semantics.

## Generated API rules

The generator preserves normal C# accessibility, inheritance, generic constraints, member hiding, source-method precedence, and collision-safe extension naming. It supports directly annotated classes and public third-party targets selected at assembly level.

Generated async payloads must be safe to escape the synchronous event callback. Ref-like values are therefore deliberately not supported as async results.

## Intended public runtime types

The public surface is protected by API-lock tests and includes:

```text
GenerateAsyncEventsAttribute
GenerateAsyncEventsForAttribute
EventAwaiter
EventStream
EventStreamOptions
EventStreamFullMode
EventOccurrence<TSender, TPayload>
EventOccurrenceAwaiter
EventOccurrenceStream
EventComposition
EventWaitAnyResult<TFirst, TSecond>
EventWaitAnyResult<T>
EventWaitAllResult<TFirst, TSecond>
AsyncEventBridgeExtensions
EventBridge
EventBridge<T>
EventStreamBridge<T>
AsyncValueEventArgs<T>
AsyncFaultedEventArgs
```

API-lock tests protect exported types, exact method signatures and optional parameters, event/property shapes, configuration defaults, and enum numeric values.
