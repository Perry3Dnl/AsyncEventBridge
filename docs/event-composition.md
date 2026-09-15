# Sender-aware events and composition

The native modern-.NET edition can preserve sender identity when an event's sender is part of its semantics, without changing the existing payload-only APIs.

Generated sender-aware APIs use `EventOccurrence<TSender, TPayload>`:

```csharp
EventOccurrence<Sensor, Reading> occurrence =
    await sensor.ReadingChangedOccurrenceAsync(cancellationToken);

Sensor sender = occurrence.Sender;
Reading reading = occurrence.Payload;
```

Repeated occurrences are available through the generated async-stream facade:

```csharp
await foreach (EventOccurrence<Sensor, Reading> occurrence
    in sensor.ReadingChangedOccurrenceStream(cancellationToken))
{
    Process(occurrence.Sender, occurrence.Payload);
}
```

The occurrence generator supports ordinary two-parameter `void` event delegates when both parameters can safely cross an async lifetime. Ref-like senders or payloads are excluded from occurrence generation. Existing `<EventName>Async` and `<EventName>Stream` methods remain payload-centric and unchanged.

## Racing event waits

`EventComposition.WaitAnyAsync` composes two heterogeneous cancellable waits:

```csharp
EventWaitAnyResult<ConnectedEventArgs, ErrorEventArgs> result =
    await EventComposition.WaitAnyAsync(
        token => client.ConnectedAsync(token),
        token => client.ErrorAsync(token),
        cancellationToken);
```

The winning value is available through `First` or `Second`, guarded by `IsFirst` and `IsSecond`.

Unlike a plain `Task.WhenAny` over event waits, `WaitAnyAsync` cancels and observes the losing wait before it returns. This gives the losing event subscription a deterministic cleanup path. Wait factories are therefore required to honor the cancellation token supplied by `WaitAnyAsync`.

If the winner faults, that fault remains observable. If loser cancellation triggers an unsubscribe or cleanup failure, that failure is also surfaced; it is not silently discarded. Cleanup cancellation itself is treated as expected loser termination rather than as a second failure.

Both generated occurrence APIs and the composition runtime are covered by normal runtime/generator tests. Packaged-consumer and Native AOT smoke tests verify that the generated sender-aware APIs work from the produced NuGet package rather than only through project references.
