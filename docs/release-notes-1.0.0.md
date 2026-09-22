# AsyncEventBridge 1.0.0 release notes

> Draft for the 1.0 release candidate. Finalize after the complete release gate, including real Unity Editor/IL2CPP acceptance, is green.

AsyncEventBridge 1.0 establishes the long-term public contract for bridging ordinary .NET events and modern async code without forcing either side to adopt a different programming model.

Event-oriented code can remain event-oriented:

```csharp
sensor.ValueChanged += OnValueChanged;
```

while async-oriented code can use the same source naturally:

```csharp
var value = await sensor.ValueChangedAsync(cancellationToken);

await foreach (var item in sensor.ValueChangedStream(cancellationToken))
{
    Process(item);
}
```

Async sources can also be exposed back to event consumers through `EventBridge` and `EventStreamBridge<T>`.

## Supported targets

The unified NuGet package supports:

- modern .NET through `net10.0`;
- compatibility consumers through `netstandard2.0`;
- source generation selected automatically for the consuming target framework.

The Unity distribution supports Unity `2023.1.0f1+` through the UPM package:

```text
com.perry3d.async-event-bridge
```

The Unity release includes Unity-native `Awaitable`, `UnityEvent`, lifecycle cancellation, generated CLR-event facades, and async-to-Inspector publication.

## Event to async

1.0 includes:

- `EventAwaiter.WaitAsync` for one-shot event waits;
- generated `<EventName>Async(...)` methods;
- `EventStream.Create` and generated `<EventName>Stream(...)` methods;
- filtering, cancellation, timeout, and deterministic cleanup;
- modern sender-aware `EventOccurrence<TSender, TPayload>` waits and streams;
- `EventCondition.WaitUntilAsync` for current-state + state-change APIs;
- `EventComposition.WaitAnyAsync` / `WaitAllAsync`;
- lifecycle-safe `StartAfter`, `TakeUntil`, `RepeatBetween`, and `RepeatWhile` stream coordination.

## Async to events

The reverse bridge supports:

- `Task` → `EventBridge`;
- `Task<T>` → `EventBridge<T>`;
- `ValueTask` / `ValueTask<T>` on modern .NET;
- `IAsyncEnumerable<T>` → `EventStreamBridge<T>`.

Bridges expose ordinary `Completed`, `Faulted`, `Cancelled`, and stream `Value` events.

`Connect()` remains explicit so subscribers can be attached before already-completed work publishes.

## Source generation

The 1.0 generator supports:

- classes and interfaces owned by the consumer;
- assembly-level adaptation of public third-party classes and interfaces;
- `EventHandler`, typed event-handler shapes, and compatible custom two-parameter `void` delegates;
- modern non-ref-like payloads including value types;
- stable diagnostics for unsupported or redundant generation requests.

Representative third-party interface adaptation:

```csharp
[assembly: GenerateAsyncEventsFor(typeof(INotifyPropertyChanged))]

PropertyChangedEventArgs change =
    await model.PropertyChangedAsync(cancellationToken);
```

Struct generation remains intentionally unsupported because copied value-type subscription lifetime would be surprising and unsafe for the generated facade.

## Event-stream buffering

The default stream mode is:

```text
Unbounded
```

It preserves accepted event values without a fixed buffer limit.

Applications that require bounded memory can explicitly choose:

- `DropOldest`;
- `DropWrite`.

Modern .NET also exposes thread-safe dropped-item telemetry through `DroppedCount` and `DropObserver`.

There is intentionally no producer-blocking mode because blocking a synchronous event callback can alter source behavior or deadlock application code.

## Cancellation, timeout, and cleanup

1.0 freezes a primary-outcome-first cleanup contract.

If cleanup fails after another terminal outcome, the original outcome stays first:

```text
fault + cleanup failure        -> AggregateException(fault, cleanup)
cancellation + cleanup failure -> AggregateException(cancellation, cleanup)
timeout + cleanup failure      -> AggregateException(timeout, cleanup)
```

Timeout remains distinct from cancellation.

Composition APIs cancel and observe losing/pending waits before returning so event subscriptions are not left behind.

See `docs/1.0-cancellation-lifecycle-cleanup.md` for the complete behavioral contract.

## Bridge lifecycle

Bridge publication uses subscriber snapshots.

Handlers added or removed during a publication affect later publications, not the captured publication already in flight.

`EventStreamBridge<T>.Dispose()` requests shutdown without waiting.

`DisposeAsync()` is the asynchronous completion boundary for bridge-owned enumeration cleanup and in-flight publication.

Bridges do not replay terminal outcomes or values.

## Reliability and packaging

The 1.0 release gate includes:

- public API locks;
- nullable-reference contract locks;
- generator overload/diagnostic parity tests;
- event/cancellation/timeout race coverage;
- concurrent event producer stress;
- bounded-buffer drop contention coverage;
- disposal/publication race coverage;
- lifecycle reconnect/stop-before-start tests;
- modern, compatibility, and multi-target packed consumers;
- Native AOT/trimming validation;
- Windows, Linux, and macOS convergence;
- NuGet symbol/source metadata verification;
- Unity package metadata and compile validation;
- real Unity Editor tests and IL2CPP acceptance before final publication.

## Intentional pre-1.0 cleanup

Projects upgrading from earlier development versions should review:

```text
docs/migrating-to-1.0.md
```

Important pre-1.0 cleanups include:

- `EventStreamFullMode.Grow` → `Unbounded`;
- `DropNewest` → `DropWrite`;
- boolean `EventCondition.WaitUntilAsync` now returns `Task`;
- lifecycle marker numeric values reserve zero for `Unspecified`;
- bounded-drop metric tag `drop_newest` → `drop_write`.

## Deliberate non-goals

AsyncEventBridge 1.0 is not:

- an Rx replacement;
- a general async-LINQ library;
- a scheduler framework;
- a retry/backoff framework;
- a persistence/replay system;
- a DI or logging integration package.

Those boundaries are intentional. The package owns the difficult event/async interoperability mechanics while leaving application-level policy to the application.

## License

AsyncEventBridge 1.0 is distributed under the MIT License.

The project used MPL-2.0 during pre-1.0 development. The license was deliberately changed before the stable 1.0 contract to reduce adoption and redistribution friction.

## Stability policy

After 1.0:

- patch releases fix defects;
- minor releases are additive;
- existing cancellation, disposal, buffering, generator, and cleanup contracts remain stable;
- breaking changes require a future major version.

The default response to a proposed feature is to first ask whether the existing primitives already compose the scenario safely.
