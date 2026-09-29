# Troubleshooting and FAQ

This page covers the problems most likely to appear when first integrating AsyncEventBridge.

## The generated Async method or Stream method does not exist

Check these first:

1. The project references the `AsyncEventBridge` NuGet package.
2. The source type is annotated with `[GenerateAsyncEvents]`, or the assembly contains `[GenerateAsyncEventsFor(typeof(...))]`.
3. The event is accessible from the consuming compilation.
4. The event delegate shape is supported.
5. The build has no `AEB001`, `AEB002`, or `AEB003` diagnostics explaining the request.

The generator ships in the same package; no second analyzer package should be installed.

## AEB001: unsupported event delegate

The generator found the event but cannot safely carry its shape across an async lifetime.

Typical causes:

- the delegate does not return `void`;
- it does not have exactly two parameters;
- a parameter uses `ref`, `out`, or `in`;
- the sender or payload is ref-like;
- on the .NET Standard 2.0 compatibility line, the payload is not `EventArgs`-based.

See [Generator diagnostics](diagnostics.md).

## AEB002: invalid generation target

`GenerateAsyncEventsFor` supports classes and interfaces.

Structs are intentionally rejected because extension-based subscription against a copied value type would have unsafe and surprising lifetime semantics.

## AEB003: duplicate generation request

The same target is being requested more than once.

For a type you own, prefer:

```csharp
[GenerateAsyncEvents]
```

For a public type you cannot annotate, prefer:

```csharp
[assembly: GenerateAsyncEventsFor(typeof(...))]
```

Do not use both for the same type.

## My wait never completes

Verify that:

- the event is actually raised after the wait has been created;
- a predicate is not filtering every event;
- the cancellation token has not already been cancelled;
- the source is the same instance on which the generated extension method is called.

For stateful APIs, do not wait only for a future event if the desired state may already be true. Use `EventCondition.WaitUntilAsync` or `RepeatWhile`.

## My stream keeps growing in memory

The default stream mode is intentionally `Unbounded` so accepted events are not silently lost.

If producers can outrun consumers for sustained periods, configure `DropOldest` or `DropWrite` with an explicit capacity.

See [Buffering and slow consumers](buffering.md).

## Why is Capacity 100 if the default is unbounded?

`Capacity` is ignored in `Unbounded` mode.

Its default value of 100 exists so changing only `FullMode` to a bounded policy already has a usable hard limit.

## Why is there no blocking backpressure mode?

Blocking a synchronous event callback can alter the source API's semantics or deadlock the application. AsyncEventBridge therefore does not turn a synchronous event into a producer-blocking protocol.

If the producer must wait for consumer capacity, the producer itself should expose an asynchronous contract.

## Why does ToEventBridge() require Connect()?

It gives consumers time to subscribe before observation starts.

An already-completed task can publish immediately, so this is the safe pattern:

```csharp
using EventBridge<Result> bridge = operation.ToEventBridge();

bridge.Completed += OnCompleted;
bridge.Faulted += OnFaulted;
bridge.Cancelled += OnCancelled;

bridge.Connect();
```

## Why did Dispose() not wait for an async stream to finish cleaning up?

`EventStreamBridge<T>.Dispose()` requests shutdown but is intentionally synchronous.

Use:

```csharp
await bridge.DisposeAsync();
```

or `await using` when you need the asynchronous cleanup boundary to complete before continuing.

## Why did disposal not raise Cancelled?

Owner disposal and source cancellation are different concepts.

Disposal suppresses future publication. `Cancelled` represents cancellation observed from the connected async operation; AsyncEventBridge does not synthesize it merely because the bridge owner disposed the bridge.

## Why can cleanup produce AggregateException?

AsyncEventBridge preserves the primary outcome first.

For example, if an operation faults and unsubscription also fails, both failures remain observable, with the original fault first. The same rule applies to cancellation or timeout followed by cleanup failure.

See [Cancellation, lifecycle, and cleanup contract](1.0-cancellation-lifecycle-cleanup.md).

## A lifecycle stream missed the value I raised at disconnect

Lifecycle boundaries deliberately define ordering.

For `TakeUntil` and the state-driven lifecycle windows, if a successful stop and a source value are both complete at the observed boundary, the stop wins and that value is not published.

If a test or sample needs deterministic sequencing, observe the emitted `Value` or lifecycle marker before triggering the next state transition.

## Can I use a third-party type without modifying it?

Yes. Use:

```csharp
[assembly: GenerateAsyncEventsFor(typeof(ThirdPartyType))]
```

See [Third-party and legacy event sources](third-party-events.md).

## Does AsyncEventBridge replace Rx or async LINQ?

No. It is intentionally focused on event/async interoperability, subscription lifetime, cancellation, buffering, cleanup, and source-generated adapter plumbing.

General stream transformation, scheduling, retry policy, persistence, and replay are outside the 1.0 product boundary.

## Where do I look next?

- [Getting started](getting-started.md)
- [Events to async](events-to-async.md)
- [Async to events](async-to-events.md)
- [Lifecycle recipes](lifecycle-recipes.md)
- [Support matrix](support-matrix.md)
