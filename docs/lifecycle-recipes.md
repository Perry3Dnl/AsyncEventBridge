# Lifecycle recipes

Event-driven systems often have a lifetime separate from the value event itself: connected/disconnected, ready/not-ready, started/stopped, signed-in/signed-out, or device-present/device-missing.

AsyncEventBridge provides a small set of operators specifically for those subscription-lifetime problems.

## Start only after an event

Use `StartAfter` when the value source should not even be enumerated before activation:

```csharp
await foreach (Reading reading in sensor.ReadingChangedStream()
    .StartAfter(
        token => sensor.ConnectedAsync(token),
        cancellationToken))
{
    Process(reading);
}
```

For an event-backed stream, the value event is not subscribed until the activation wait succeeds.

## Stop when an event occurs

Use `TakeUntil` when a stream should remain active until another event wins:

```csharp
await foreach (Reading reading in sensor.ReadingChangedStream()
    .TakeUntil(
        token => sensor.DisconnectedAsync(token),
        cancellationToken))
{
    Process(reading);
}
```

If a source value and a successful stop are both complete at the observed boundary, the stop wins and that value is not published.

## One start/stop window

The two operators compose:

```csharp
await foreach (Reading reading in sensor.ReadingChangedStream()
    .StartAfter(token => sensor.ConnectedAsync(token))
    .TakeUntil(token => sensor.DisconnectedAsync(token)))
{
    Process(reading);
}
```

The outer lifetime owns cleanup. If stop happens before start, the pending start wait is cancelled and the value source is never subscribed.

## Reconnect repeatedly

Use `RepeatBetween` for APIs that can repeatedly activate and deactivate:

```csharp
await foreach (Reading reading in sensor.ReadingChangedStream()
    .RepeatBetween(
        token => sensor.ConnectedAsync(token),
        token => sensor.DisconnectedAsync(token),
        cancellationToken))
{
    Process(reading);
}
```

Each successful activation creates a fresh source enumeration. A successful stop closes the active cycle, cleans it up, and rearms activation.

## Prefer current state when the API exposes it

If the source exposes both current state and a state-change event, prefer `RepeatWhile` over waiting only for a future Connected/Disconnected event.

Boolean state:

```csharp
await foreach (Reading reading in sensor.ReadingChangedStream()
    .RepeatWhile(
        () => sensor.IsConnected,
        token => sensor.ConnectionChangedAsync(token),
        cancellationToken))
{
    Process(reading);
}
```

Richer state:

```csharp
await foreach (Reading reading in sensor.ReadingChangedStream()
    .RepeatWhile(
        () => sensor.State,
        state => state == SensorState.Connected,
        token => sensor.StateChangedAsync(token),
        cancellationToken))
{
    Process(reading);
}
```

This handles already-active state correctly and avoids the classic check-then-subscribe race.

While inactive, the value source is not enumerated. Reactivation creates a fresh source enumeration.

## Observe session boundaries explicitly

Use a lifecycle variant when the application needs to know when a cycle starts or ends:

```csharp
await foreach (var item in sensor.ReadingChangedStream()
    .RepeatWhileWithLifecycle(
        () => sensor.IsConnected,
        token => sensor.ConnectionChangedAsync(token),
        cancellationToken))
{
    switch (item.Kind)
    {
        case EventStreamLifecycleEventKind.Activated:
            BeginSession(item.Cycle);
            break;

        case EventStreamLifecycleEventKind.Value:
            Process(item.Cycle, item.Value);
            break;

        case EventStreamLifecycleEventKind.Deactivated:
        case EventStreamLifecycleEventKind.SourceCompleted:
            EndSession(item.Cycle);
            break;
    }
}
```

The stable lifecycle kinds are:

```text
Unspecified     = 0
Activated       = 1
Value           = 2
Deactivated     = 3
SourceCompleted = 4
```

Cycle numbers are one-based and are allocated only after successful activation.

## Wait for state without consuming a stream

Use `EventCondition.WaitUntilAsync` when the goal is simply to wait until state becomes true:

```csharp
await EventCondition.WaitUntilAsync(
    () => client.IsConnected,
    token => client.ConnectionChangedAsync(token),
    cancellationToken);
```

The change wait is armed before state is read, so a transition during setup cannot be missed.

## Cancellation and faults

External cancellation terminates the composed lifetime.

A faulted lifecycle wait or source remains a fault. AsyncEventBridge does not turn unrelated failures into normal stop markers.

Cleanup is deterministic where the supplied source and wait factories honor cancellation and eventually complete.

For the exact ordering rules, see [Cancellation, lifecycle, and cleanup contract](1.0-cancellation-lifecycle-cleanup.md).
