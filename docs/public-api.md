# Public API — 1.0 stabilization baseline

This document describes the public API being stabilized for AsyncEventBridge `1.0.0`. The repository may continue to use a pre-1.0 development version until the release gate is complete.

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
DropOldest = 1
DropWrite = 2
```

`Unbounded` is the canonical lossless mode and retains numeric value `0`. In 0.4 it replaces the earlier `Grow` name before the 1.0 API freeze. In this mode, `Capacity` is ignored completely; the default value of `100` is only the default hard limit used if a caller selects `DropOldest` or `DropWrite`.

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

## Event-driven state conditions

`EventCondition.WaitUntilAsync<TState>(...)` coordinates a current-state snapshot with a cancellable event-driven change wait:

```csharp
ConnectionState state = await EventCondition.WaitUntilAsync(
    () => client.State,
    state => state == ConnectionState.Connected,
    token => client.StateChangedAsync(token),
    cancellationToken);
```

The method arms `waitForChange` before calling `getState` on every attempt. This prevents a state transition from being lost between checking state and subscribing for changes.

If the predicate is already satisfied, the temporary change wait is cancelled and observed before the method returns the matching state snapshot. If a change notification arrives while the predicate is still false, the method rearms and rechecks. Spurious notifications are therefore supported without polling.

State access, predicate, change-wait, cancellation, and cleanup failures remain observable. Change waits should honor the supplied cancellation token so deterministic cleanup can complete.

A boolean convenience overload removes the identity predicate:

```csharp
await EventCondition.WaitUntilAsync(
    () => client.IsConnected,
    token => client.ConnectionChangedAsync(token),
    cancellationToken);
```

## State-driven stream lifecycles

`RepeatWhile(...)` is the normal high-level API when a stream should be active while current state satisfies a predicate:

```csharp
await foreach (var value in sensor.ValueChangedStream()
    .RepeatWhile(
        () => sensor.State,
        state => state == SensorState.Connected,
        token => sensor.StateChangedAsync(token),
        cancellationToken))
{
    Process(value);
}
```

Boolean state uses the shorter overload:

```csharp
source.RepeatWhile(
    () => sensor.IsConnected,
    token => sensor.ConnectionChangedAsync(token),
    cancellationToken);
```

The source is not enumerated while inactive. Already-active state starts immediately. Deactivation cleans the current source enumeration and rearms the state condition; reactivation creates a fresh source enumeration.

`RepeatWhileWithLifecycle(...)` returns `EventStreamLifecycleEvent<T>` markers for the same state-driven workflow. The active source/stop window is armed before `Activated` is emitted.

## Event-stream workflow composition

`EventStreamComposition.StartAfter(...)` delays source enumeration until a cancellable activation wait completes successfully:

```csharp
await foreach (var value in sensor.ValueChangedStream()
    .StartAfter(
        token => sensor.ConnectedAsync(token),
        cancellationToken))
{
    Process(value);
}
```

The source enumerator is not created before activation, so an event-backed source does not subscribe its underlying event before the start wait succeeds. A faulted or cancelled start wait propagates and the source never starts.

`EventStreamComposition.TakeUntil(...)` coordinates a source async stream with another cancellable stop wait:

```csharp
await foreach (var value in sensor.ValueChangedStream()
    .TakeUntil(
        token => sensor.DisconnectedAsync(token),
        cancellationToken))
{
    Process(value);
}
```

The stop wait is created per enumeration. The source and stop wait share a coordination token so whichever side finishes first can deterministically cancel and observe the other side. A successful stop wait ends the sequence; a faulted or independently cancelled stop wait propagates its outcome.

If a source move and the stop wait are both complete when the move boundary is observed, the stop wait wins and that value is not published. Source/stop cleanup follows the same primary-outcome-first aggregation policy as the rest of the runtime.

The operators compose directly: `source.StartAfter(startWait).TakeUntil(stopWait)` models an inactive/active/stopped lifecycle. Because `TakeUntil` is outermost, a stop that occurs before activation cancels and observes the pending start wait without subscribing the source.

For repeated activation/deactivation cycles, use:

```csharp
await foreach (var value in sensor.ValueChangedStream()
    .RepeatBetween(
        token => sensor.ConnectedAsync(token),
        token => sensor.DisconnectedAsync(token),
        cancellationToken))
{
    Process(value);
}
```

`RepeatBetween` creates a fresh source enumeration for each active cycle. Successful stop or natural source completion ends only that cycle and rearms activation. A successful stop observed while inactive rearms without starting the source. Source faults, lifecycle-wait faults, cleanup failures, and external cancellation terminate the repeating workflow.

These APIs are intentionally event-workflow-specific. 0.5 does not introduce a parallel general-purpose async LINQ or Rx operator set.

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

Async -> events publication always isolates subscriber exceptions and continues dispatching remaining subscribers.

`EventBridgeOptions.SubscriberExceptionPolicy` supports:

```text
TraceAndContinue = 0
ReportAndContinue = 1
IgnoreAndContinue = 2
```

`TraceAndContinue` is the default and preserves the earlier `Trace.TraceError` behavior. `ReportAndContinue` requires `SubscriberExceptionObserver`; `IgnoreAndContinue` performs no bridge-level reporting.

Options are snapshotted when `ToEventBridge(..., options)` creates the bridge. Observer failures are themselves isolated and traced.

A propagation policy is intentionally not exposed because bridge event publication is driven by async observation and generally has no synchronous application caller to receive the exception. See `docs/0.4-subscriber-exceptions.md`.

## Bridge lifecycle

Portable bridge events use subscriber snapshots. A handler added or removed while one publication is already in flight changes future publications only; it does not rewrite the invocation list captured for the current event.

Bridges do not replay values or terminal outcomes. Handlers should be attached before `Connect()`; already-completed tasks or synchronously advancing async sources may publish before `Connect()` returns.

`EventBridge.Dispose()` suppresses terminal publication that has not started, but it does not interrupt a terminal subscriber snapshot already in flight.

`EventStreamBridge.Dispose()` suppresses future values/terminal publication and requests source cancellation without waiting for a handler already in flight. `DisposeAsync()` additionally waits for bridge-owned enumeration cleanup and in-flight publication. Its completion can therefore depend on the source honoring cancellation or eventually returning from async enumeration/cleanup.

See `docs/0.4-bridge-lifecycle.md`.

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
EventCondition
EventStream
EventStreamComposition
EventStreamLifecycleEvent<T>
EventStreamLifecycleEventKind
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
EventBridgeOptions
EventBridgeSubscriberExceptionPolicy
EventStreamBridge<T>
AsyncValueEventArgs<T>
AsyncFaultedEventArgs
```

API-lock tests protect exported types, exact method signatures and optional parameters, event/property shapes, configuration defaults, and enum numeric values.
