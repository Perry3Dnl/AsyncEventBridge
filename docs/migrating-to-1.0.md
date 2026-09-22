# Migrating to AsyncEventBridge 1.0

AsyncEventBridge 1.0 freezes the long-term public contract after the pre-1.0 releases.

Most 0.4/0.5 code continues to use the same event-to-async and async-to-event vocabulary. The changes below are the intentional source or behavioral cleanups that should be addressed before moving to 1.0.

## Package version

During stabilization the package version is:

```xml
<PackageReference Include="AsyncEventBridge" Version="1.0.0-preview.1" />
```

For the final stable release, replace the preview version with `1.0.0`.

The unified NuGet package contains both the modern `net10.0` runtime and the `netstandard2.0` compatibility runtime and selects the matching generator automatically.

## EventStreamFullMode.Grow -> Unbounded

Pre-1.0 code:

```csharp
var options = new EventStreamOptions
{
    FullMode = EventStreamFullMode.Grow,
};
```

1.0:

```csharp
var options = new EventStreamOptions
{
    FullMode = EventStreamFullMode.Unbounded,
};
```

The semantics remain lossless/unbounded. `Unbounded` retains numeric value `0`.

`Capacity` is ignored in this mode. It applies only to bounded drop modes.

## EventStreamFullMode.DropNewest -> DropWrite

A pre-1.0 name described the bounded policy as `DropNewest`, but the actual behavior discarded the incoming write when the buffer was full.

1.0 names that behavior explicitly:

```csharp
FullMode = EventStreamFullMode.DropWrite;
```

The value retains numeric value `2`.

This aligns the name with `System.Threading.Channels.BoundedChannelFullMode.DropWrite`. It also avoids conflicting with the distinct Channels `DropNewest` behavior, which removes the newest value already in the buffer.

If you consume AsyncEventBridge metrics, update the bounded-mode tag from:

```text
drop_newest
```

to:

```text
drop_write
```

## Boolean EventCondition.WaitUntilAsync now returns Task

Pre-1.0:

```csharp
bool ready = await EventCondition.WaitUntilAsync(
    () => client.IsReady,
    token => client.ReadyChangedAsync(token),
    cancellationToken);
```

1.0:

```csharp
await EventCondition.WaitUntilAsync(
    () => client.IsReady,
    token => client.ReadyChangedAsync(token),
    cancellationToken);
```

Successful completion already means the boolean condition is true, so returning `true` carried no additional information and implied that `false` might be a normal successful result.

When the matching state value is needed, use the generic overload:

```csharp
ConnectionState state = await EventCondition.WaitUntilAsync(
    () => client.State,
    state => state == ConnectionState.Connected,
    token => client.StateChangedAsync(token),
    cancellationToken);
```

## EventStreamLifecycleEventKind numeric values changed

1.0 reserves zero for the default/uninitialized state:

```text
Unspecified     = 0
Activated       = 1
Value           = 2
Deactivated     = 3
SourceCompleted = 4
```

Code should compare enum members by name rather than persisting or interpreting the old numeric values.

If lifecycle marker values were serialized or persisted during pre-1.0 development, migrate that data explicitly.

## Class and interface generation

1.0 supports both classes and interfaces:

```csharp
[GenerateAsyncEvents]
public interface ISensor
{
    event EventHandler<Reading>? ReadingChanged;
}
```

Third-party or framework interfaces can be targeted at assembly level:

```csharp
[assembly: GenerateAsyncEventsFor(typeof(INotifyPropertyChanged))]
```

For interfaces, generation covers events declared directly on the targeted interface. Target a base interface independently when its declared events also need generated APIs.

This is additive, but applications that previously used low-level plumbing around interface-only APIs can now remove that plumbing.

Struct targets remain unsupported.

## Sender-aware occurrence predicates

Generated sender-aware APIs now include predicate overloads over the full occurrence:

```csharp
EventOccurrence<Sensor, Reading> occurrence =
    await sensor.ReadingChangedOccurrenceAsync(
        item => ReferenceEquals(item.Sender, sensor) && item.Payload.IsValid,
        cancellationToken);
```

This is additive. Existing occurrence calls continue to compile.

## Event-stream buffering default

The 1.0 default remains:

```text
FullMode = Unbounded
Capacity = 100
```

The capacity does not bound an unbounded stream. It becomes the default hard limit only when `DropOldest` or `DropWrite` is selected.

If an application requires bounded memory, opt into a bounded mode explicitly.

## Bridge lifecycle expectations

The 1.0 bridge contract is intentionally explicit:

- attach event handlers before `Connect()`;
- bridges do not replay values or terminal outcomes;
- a bridge can be connected only once;
- `Dispose()` suppresses future publication without waiting for async cleanup;
- `EventStreamBridge<T>.DisposeAsync()` is the completion boundary for bridge-owned enumeration cleanup and in-flight publication.

Code that relied on attaching handlers after `Connect()` should move those subscriptions before the call.

## Cleanup failures

1.0 preserves the primary operation outcome before cleanup failures.

For example, cancellation plus an unsubscribe failure is observed as a faulted `AggregateException` containing:

1. the `OperationCanceledException`;
2. the cleanup failure.

Likewise, timeout plus cleanup failure preserves `TimeoutException` first.

Applications that intentionally provide throwing unsubscribe/dispose callbacks should account for the aggregate contract.

## Generator diagnostics

The stable generator diagnostic vocabulary is:

```text
AEB001  unsupported event delegate/payload shape
AEB002  invalid generation target
AEB003  duplicate or redundant generation request
```

Unsupported explicitly requested generation is no longer something callers should expect to fail silently.

## What did not change

The central 1.0 developer model remains:

```csharp
sensor.ValueChanged += OnValueChanged;

var value = await sensor.ValueChangedAsync(cancellationToken);

await foreach (var item in sensor.ValueChangedStream(cancellationToken))
{
    Process(item);
}
```

and in the opposite direction:

```csharp
using EventBridge<Result> bridge = task.ToEventBridge();

bridge.Completed += OnCompleted;
bridge.Faulted += OnFaulted;
bridge.Cancelled += OnCancelled;
bridge.Connect();
```

1.0 is intended to stabilize this model rather than replace it.
