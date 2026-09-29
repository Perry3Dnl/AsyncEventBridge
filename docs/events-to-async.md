# Events to async

Use this guide when the source API exposes .NET events and your consuming code wants `await`, cancellation, timeout, filtering, or `await foreach`.

For a step-by-step architectural migration, including how to remove AsyncEventBridge later, see [Migrate an event-driven codebase to async](migrating-event-driven-to-async.md).

## Generated APIs are the normal path

For a type you own:

```csharp
[GenerateAsyncEvents]
public sealed class Sensor
{
    public event EventHandler<ReadingEventArgs>? ReadingChanged;
}
```

AsyncEventBridge generates familiar extension methods such as:

```csharp
ReadingEventArgs reading =
    await sensor.ReadingChangedAsync(cancellationToken);

await foreach (ReadingEventArgs item in
    sensor.ReadingChangedStream(cancellationToken))
{
    Process(item);
}
```

The synchronous event is not replaced or wrapped. The generated API is an additional facade.

## Wait for the next event

Use `<EventName>Async(...)` for a one-shot wait:

```csharp
ReadingEventArgs reading =
    await sensor.ReadingChangedAsync(cancellationToken);
```

The temporary subscription is cleaned up after the event, cancellation, timeout, setup failure, or predicate failure.

## Wait for a matching event

Use a predicate when only some event values should complete the wait:

```csharp
ReadingEventArgs reading =
    await sensor.ReadingChangedAsync(
        value => value.Value >= 100,
        cancellationToken);
```

Non-matching events are ignored and the wait remains active.

## Timeout

Generated timeout overloads keep timeout distinct from cancellation:

```csharp
ReadingEventArgs reading =
    await sensor.ReadingChangedAsync(
        TimeSpan.FromSeconds(10),
        cancellationToken,
        TimeProvider.System);
```

A timeout produces `TimeoutException`. Caller cancellation produces cancellation.

Modern .NET exposes `TimeProvider` overloads so timeout behavior can be tested deterministically.

## Repeated events as an async stream

Use `<EventName>Stream(...)` when every event occurrence should be consumed:

```csharp
await foreach (ReadingEventArgs reading in
    sensor.ReadingChangedStream(cancellationToken))
{
    Process(reading);
}
```

The stream subscribes when enumeration starts and unsubscribes when enumeration ends.

For producer/consumer imbalance, see [Buffering and slow consumers](buffering.md).

## Preserve sender identity when it matters

The normal generated API returns the event payload. On modern .NET, sender-aware occurrence APIs are available when sender identity is part of the application semantics:

```csharp
EventOccurrence<Sensor, ReadingEventArgs> occurrence =
    await sensor.ReadingChangedOccurrenceAsync(cancellationToken);

Process(occurrence.Sender, occurrence.Payload);
```

The stream equivalent is:

```csharp
await foreach (EventOccurrence<Sensor, ReadingEventArgs> occurrence in
    sensor.ReadingChangedOccurrenceStream(cancellationToken))
{
    Process(occurrence.Sender, occurrence.Payload);
}
```

Occurrence predicates can inspect both sender and payload.

## Wait for current-or-future state

Do not write a check-then-subscribe sequence for APIs that expose both current state and a state-change event. The state can change between those operations.

Use `EventCondition`:

```csharp
await EventCondition.WaitUntilAsync(
    () => client.IsConnected,
    token => client.ConnectionChangedAsync(token),
    cancellationToken);
```

For richer state:

```csharp
ConnectionState state = await EventCondition.WaitUntilAsync(
    () => client.State,
    state => state == ConnectionState.Connected,
    token => client.StateChangedAsync(token),
    cancellationToken);
```

AsyncEventBridge arms the change wait before reading the state snapshot, closing the usual race.

## Wait for more than one event

Use `EventComposition` rather than a raw `Task.WhenAny` when losing event waits must be cancelled and cleaned up:

```csharp
EventWaitAnyResult<ConnectedEventArgs, ErrorEventArgs> result =
    await EventComposition.WaitAnyAsync(
        token => client.ConnectedAsync(token),
        token => client.ErrorAsync(token),
        cancellationToken);
```

`WaitAllAsync` is available for multiple required event waits. See [Event composition](event-composition.md) for the exact result types and cleanup behavior.

## Low-level integration

When generation is not appropriate, `EventAwaiter`, `EventStream`, `EventOccurrenceAwaiter`, and `EventOccurrenceStream` expose the same core behavior using explicit subscribe/unsubscribe delegates.

Prefer generated APIs when possible; they remove repetitive subscription plumbing and keep the call site easy to read.

## See also

- [Third-party and legacy event sources](third-party-events.md)
- [Lifecycle recipes](lifecycle-recipes.md)
- [Cancellation, lifecycle, and cleanup contract](1.0-cancellation-lifecycle-cleanup.md)
