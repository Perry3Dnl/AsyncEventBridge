# Migration guide: event-driven to async

This guide is for teams with an existing event-driven .NET API that want to move application code toward `Task`, `await`, cancellation, and `IAsyncEnumerable<T>` without rewriting the event source in one large change.

AsyncEventBridge is intended to be a **migration layer**, not a permanent architectural requirement. Existing event consumers can keep working while async consumers are introduced. If the producer later becomes natively async, the bridge can be removed again.

## The migration shape

A typical migration looks like this:

```text
today
event source
    -> event handlers
    -> event handlers
    -> event handlers

transition
event source
    -> existing event handlers
    -> AsyncEventBridge -> await
    -> AsyncEventBridge -> await foreach

later, if desired
native async source
    -> await
    -> await foreach
```

You do not have to convert the producer and every consumer at the same time.

## Example starting point

Assume an existing component looks like this:

```csharp
public sealed class Sensor
{
    public event EventHandler<ReadingEventArgs>? ReadingChanged;

    public bool IsConnected { get; private set; }

    public event EventHandler? ConnectionChanged;

    public void RaiseReading(int value) =>
        ReadingChanged?.Invoke(this, new ReadingEventArgs(value));
}

public sealed class ReadingEventArgs(int value) : EventArgs
{
    public int Value { get; } = value;
}
```

Existing consumers may already contain code such as:

```csharp
sensor.ReadingChanged += OnReadingChanged;

private void OnReadingChanged(object? sender, ReadingEventArgs e)
{
    Process(e);
}
```

Do **not** delete that code just to begin the migration.

## Step 1: add the async facade without changing the event API

For a type you own, annotate it:

```csharp
using AsyncEventBridge;

[GenerateAsyncEvents]
public sealed class Sensor
{
    public event EventHandler<ReadingEventArgs>? ReadingChanged;

    public bool IsConnected { get; private set; }

    public event EventHandler? ConnectionChanged;
}
```

The existing events remain ordinary .NET events. AsyncEventBridge only adds generated extension methods alongside them.

That means old and new consumers can coexist:

```csharp
// Existing event-first consumer.
sensor.ReadingChanged += OnReadingChanged;

// New async consumer.
ReadingEventArgs reading =
    await sensor.ReadingChangedAsync(cancellationToken);
```

This is the safest first migration step because it does not require a flag day.

## Step 2: convert one-shot event subscriptions to await

Event code often subscribes only because it wants the **next** matching occurrence.

Before:

```csharp
void WaitForHighReading(Sensor sensor)
{
    EventHandler<ReadingEventArgs>? handler = null;

    handler = (_, reading) =>
    {
        if (reading.Value < 100)
        {
            return;
        }

        sensor.ReadingChanged -= handler;
        ContinueAfterHighReading(reading);
    };

    sensor.ReadingChanged += handler;
}
```

During migration:

```csharp
ReadingEventArgs reading =
    await sensor.ReadingChangedAsync(
        reading => reading.Value >= 100,
        cancellationToken);

ContinueAfterHighReading(reading);
```

The call site now follows normal structured async control flow. Subscription lifetime, unsubscription, cancellation races, predicate failures, and cleanup are owned by the bridge.

### Timeout

If the old code had a timer next to the event subscription, replace the pair with a timeout overload:

```csharp
ReadingEventArgs reading =
    await sensor.ReadingChangedAsync(
        TimeSpan.FromSeconds(10),
        cancellationToken,
        TimeProvider.System);
```

A timeout is reported as `TimeoutException`; caller cancellation remains cancellation.

## Step 3: convert repeated handlers to async streams

A long-lived event subscription is usually closer to a stream than a one-shot task.

Before:

```csharp
sensor.ReadingChanged += (_, reading) =>
{
    Process(reading);
};
```

During migration:

```csharp
await foreach (ReadingEventArgs reading in
    sensor.ReadingChangedStream(cancellationToken))
{
    Process(reading);
}
```

This gives the subscription a structured lifetime: enumeration starts the subscription and ending or disposing the enumeration removes it.

If the producer can outrun the consumer for a sustained period, choose a buffering policy deliberately. The default is unbounded and lossless. See [Buffering and slow consumers](buffering.md).

## Step 4: migrate state-driven event code carefully

Stateful event APIs usually expose both:

- a current property, such as `IsConnected`; and
- an event, such as `ConnectionChanged`.

Do not translate this mechanically:

```csharp
if (!sensor.IsConnected)
{
    await sensor.ConnectionChangedAsync(cancellationToken);
}
```

That can still be logically wrong: the next change event may represent a disconnect, or the desired state may change during setup.

Use `EventCondition` when you want to wait until a state is true:

```csharp
await EventCondition.WaitUntilAsync(
    () => sensor.IsConnected,
    token => sensor.ConnectionChangedAsync(token),
    cancellationToken);
```

Use `RepeatWhile` when a stream should only be subscribed while the state is active:

```csharp
await foreach (ReadingEventArgs reading in
    sensor.ReadingChangedStream()
        .RepeatWhile(
            () => sensor.IsConnected,
            token => sensor.ConnectionChangedAsync(token),
            cancellationToken))
{
    Process(reading);
}
```

This also handles already-active state and reconnect cycles.

## Step 5: decide where AsyncEventBridge is allowed to appear

For application code, using generated methods directly is usually fine.

For a library, large application, or codebase where you want an easy future exit, put the bridge behind your own async-facing boundary.

For example:

```csharp
public interface ISensorAsync
{
    Task<ReadingEventArgs> WaitForReadingAsync(
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<ReadingEventArgs> Readings(
        CancellationToken cancellationToken = default);
}
```

Implement it with AsyncEventBridge during the migration:

```csharp
public sealed class SensorAsyncAdapter(Sensor sensor) : ISensorAsync
{
    public Task<ReadingEventArgs> WaitForReadingAsync(
        CancellationToken cancellationToken = default) =>
        sensor.ReadingChangedAsync(cancellationToken);

    public IAsyncEnumerable<ReadingEventArgs> Readings(
        CancellationToken cancellationToken = default) =>
        sensor.ReadingChangedStream(cancellationToken);
}
```

Now the rest of the application depends on `ISensorAsync`, not on AsyncEventBridge.

That creates an explicit seam:

```text
legacy Sensor events
        |
AsyncEventBridge adapter
        |
ISensorAsync
        |
application async code
```

Later you can replace only the adapter implementation.

## Step 6: move the producer to a native async contract when it makes sense

If you own the event source and eventually want a fully async architecture, move async semantics into the producer itself.

For a one-shot operation, the end state may simply be:

```csharp
public Task<ReadingEventArgs> ReadNextAsync(
    CancellationToken cancellationToken = default)
{
    // Native async implementation owned by Sensor.
}
```

For a naturally repeated source, expose `IAsyncEnumerable<T>` directly:

```csharp
public IAsyncEnumerable<ReadingEventArgs> ReadingsAsync(
    CancellationToken cancellationToken = default)
{
    // Native async stream implementation owned by Sensor.
}
```

During a compatibility period the producer can expose both forms:

```csharp
public event EventHandler<ReadingEventArgs>? ReadingChanged;

public IAsyncEnumerable<ReadingEventArgs> ReadingsAsync(
    CancellationToken cancellationToken = default)
{
    // Native stream.
}
```

Old consumers keep the event. New consumers use the native stream. Once no event consumers remain, the event can be deprecated and eventually removed according to your own compatibility policy.

## Removing AsyncEventBridge later

There are two different exit scenarios.

### Exit A: the producer is now natively async

This is the cleanest exit.

Change the adapter:

```csharp
public sealed class SensorAsyncAdapter(Sensor sensor) : ISensorAsync
{
    public Task<ReadingEventArgs> WaitForReadingAsync(
        CancellationToken cancellationToken = default) =>
        sensor.ReadNextAsync(cancellationToken);

    public IAsyncEnumerable<ReadingEventArgs> Readings(
        CancellationToken cancellationToken = default) =>
        sensor.ReadingsAsync(cancellationToken);
}
```

The application does not change because it already depends on your own interface.

Then:

1. remove `[GenerateAsyncEvents]` and any `[GenerateAsyncEventsFor]` requests that are no longer used;
2. replace remaining generated calls such as `ReadingChangedAsync` and `ReadingChangedStream`;
3. remove direct uses of `EventAwaiter`, `EventStream`, `EventCondition`, or lifecycle composition if those responsibilities have moved into your native API;
4. remove the `AsyncEventBridge` package reference;
5. clean and rebuild from an empty `bin/obj` state;
6. run cancellation, timeout, disposal, reconnect, and shutdown tests again.

### Exit B: the producer is still event-driven

If the underlying API is still event-only, removing AsyncEventBridge does **not** remove the event/async interoperability problem.

You have three choices:

- keep AsyncEventBridge;
- replace it with another adapter;
- own the subscription, cancellation, buffering, and cleanup code yourself.

For example, a minimal one-shot adapter can be written with `TaskCompletionSource<T>`, but production code must correctly handle event/cancellation races and deterministic unsubscription:

```csharp
public static Task<ReadingEventArgs> WaitForReadingAsync(
    Sensor sensor,
    CancellationToken cancellationToken)
{
    var completion =
        new TaskCompletionSource<ReadingEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);

    EventHandler<ReadingEventArgs>? handler = null;
    CancellationTokenRegistration registration = default;

    handler = (_, reading) =>
    {
        if (!completion.TrySetResult(reading))
        {
            return;
        }

        sensor.ReadingChanged -= handler;
        registration.Dispose();
    };

    sensor.ReadingChanged += handler;

    registration = cancellationToken.Register(() =>
    {
        if (completion.TrySetCanceled(cancellationToken))
        {
            sensor.ReadingChanged -= handler;
        }
    });

    return completion.Task;
}
```

That example illustrates why the bridge exists; it is not a complete replacement for all the race and cleanup behavior AsyncEventBridge provides.

The important point is that the dependency is removable. What cannot be removed for free is the **semantic work** required to turn a synchronous event source into a correct async lifetime.

## Migration order that scales well

For a real codebase, use this order:

1. add AsyncEventBridge without changing existing event consumers;
2. migrate one-shot consumers to generated async waits;
3. migrate long-lived consumers to generated streams;
4. replace state/event races with `EventCondition` or `RepeatWhile`;
5. introduce your own async-facing interfaces at architectural boundaries;
6. move producers to native async APIs only where that provides real value;
7. retire old event consumers;
8. remove the bridge where both sides are now natively async.

Do not convert events merely because async exists. Events remain a good model for synchronous notifications with multiple observers.

## Completion checklist

Before declaring an event-to-async migration complete, verify:

- every async wait has a defined cancellation lifetime;
- timeout and cancellation are not conflated;
- long-lived subscriptions have a clear disposal/enumeration boundary;
- stateful APIs do not use unsafe check-then-subscribe logic;
- bounded stream loss, if enabled, is intentional and observable;
- legacy event consumers still work until intentionally removed;
- application-level async interfaces do not expose AsyncEventBridge-specific types if future removal matters;
- removing the package would require changing the adapter boundary, not the entire application.

## Related guides

- [Events to async API guide](events-to-async.md)
- [Lifecycle recipes](lifecycle-recipes.md)
- [Buffering and slow consumers](buffering.md)
- [Third-party and legacy event sources](third-party-events.md)
- [Cancellation, lifecycle, and cleanup contract](1.0-cancellation-lifecycle-cleanup.md)
