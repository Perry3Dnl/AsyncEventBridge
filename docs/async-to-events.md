# Async to events

Use this guide when the implementation is asynchronous but existing consumers expect ordinary .NET events.

AsyncEventBridge supports one-shot tasks and repeated async streams without requiring event-first callers to adopt `Task` or `IAsyncEnumerable<T>`.

## Task to events

A `Task` can be exposed through an `EventBridge`:

```csharp
using EventBridge bridge =
    SaveAsync().ToEventBridge();

bridge.Completed += (_, _) => OnSaved();
bridge.Faulted += (_, e) => Log(e.Exception);
bridge.Cancelled += (_, _) => OnCancelled();

bridge.Connect();
```

For a result-bearing task:

```csharp
using EventBridge<Customer> bridge =
    LoadCustomerAsync().ToEventBridge();

bridge.Completed += (_, e) => Show(e.Value);
bridge.Faulted += (_, e) => Log(e.Exception);
bridge.Cancelled += (_, _) => OnCancelled();

bridge.Connect();
```

On modern .NET, `ValueTask` and `ValueTask<T>` have equivalent bridge paths.

## Why Connect() is explicit

Subscribe handlers first, then call `Connect()`:

```csharp
using EventBridge<Result> bridge = operation.ToEventBridge();

bridge.Completed += OnCompleted;
bridge.Faulted += OnFaulted;
bridge.Cancelled += OnCancelled;

bridge.Connect();
```

This order is intentional. An already-completed task can publish immediately. Explicit connection gives event consumers a deterministic opportunity to attach handlers before observation begins.

## Async stream to events

Use `EventStreamBridge<T>` for `IAsyncEnumerable<T>`:

```csharp
await using EventStreamBridge<Reading> bridge =
    ReadingsAsync().ToEventBridge();

bridge.Value += (_, e) => Process(e.Value);
bridge.Completed += (_, _) => OnCompleted();
bridge.Faulted += (_, e) => Log(e.Exception);
bridge.Cancelled += (_, _) => OnCancelled();

bridge.Connect(cancellationToken);
```

The bridge owns enumeration after `Connect()`.

## Disposal

For one-shot task bridges, `Dispose()` suppresses future publication that has not already begun and releases bridge-owned subscriber references.

For `EventStreamBridge<T>`:

- `Dispose()` requests shutdown and returns without waiting for asynchronous source cleanup.
- `DisposeAsync()` waits for the enumeration loop, source enumerator disposal, and publication already in flight.

Use `await using` when deterministic async-stream cleanup matters.

## Cancellation versus disposal

Disposing a bridge does not synthesize a `Cancelled` event.

`Cancelled` describes cancellation observed from the connected asynchronous operation. Owner disposal is a separate lifetime action and suppresses future publication.

For async streams, a source-thrown `OperationCanceledException` is classified as bridge cancellation only when the bridge lifetime token was actually cancelled. An unrelated `OperationCanceledException` is treated as a fault.

## Subscriber exceptions

Subscriber failures are isolated so one event handler does not stop the remaining subscribers or rewrite the underlying async operation's terminal state.

`EventBridgeOptions` controls reporting behavior. The default policy traces and continues. Applications can choose reporting or ignore-and-continue behavior while preserving the same bridge lifetime semantics.

## Typical use cases

Async-to-event bridges are useful when:

- a legacy public API must remain event-based while its implementation becomes async;
- UI or plugin consumers already depend on events;
- a gradual migration needs both event-first and async-first consumers;
- an async stream needs to feed a component that only understands event callbacks.

If all consumers are already async-aware, keep the original `Task` or `IAsyncEnumerable<T>` instead of adding an unnecessary bridge.

## Exact lifecycle contract

See [Cancellation, lifecycle, and cleanup contract](1.0-cancellation-lifecycle-cleanup.md) for publication snapshots, disposal boundaries, terminal outcomes, and cleanup ordering.
