# Getting started

This guide takes the shortest path from installation to a working async facade over an ordinary .NET event.

## 1. Install the package

For the current 1.0 release candidate:

```bash
dotnet add package AsyncEventBridge --version 1.0.0-rc.1
```

or:

```xml
<PackageReference Include="AsyncEventBridge" Version="1.0.0-rc.1" />
```

The runtime and source generator ship in the same NuGet package. You do not need a separate analyzer package.

## 2. Mark a type for generation

For a type you own, add `[GenerateAsyncEvents]`:

```csharp
using AsyncEventBridge;

[GenerateAsyncEvents]
public sealed class Sensor
{
    public event EventHandler<ReadingEventArgs>? ReadingChanged;

    public void Raise(int value) =>
        ReadingChanged?.Invoke(this, new ReadingEventArgs(value));
}

public sealed class ReadingEventArgs(int value) : EventArgs
{
    public int Value { get; } = value;
}
```

The original event is unchanged. Existing event subscribers keep working normally.

## 3. Await one event

The generator adds a method named after the event with an `Async` suffix:

```csharp
using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));

Task<ReadingEventArgs> nextReading =
    sensor.ReadingChangedAsync(cancellation.Token);

sensor.Raise(42);

ReadingEventArgs reading = await nextReading;
Console.WriteLine(reading.Value);
```

AsyncEventBridge owns the temporary subscription and removes it when the wait completes, is cancelled, times out, or fails.

## 4. Filter events

Generated waits can ignore values until one matches:

```csharp
ReadingEventArgs highReading =
    await sensor.ReadingChangedAsync(
        reading => reading.Value >= 100,
        cancellationToken);
```

The event remains subscribed until a matching value wins or the operation terminates.

## 5. Consume repeated events

For repeated values, use the generated stream:

```csharp
await foreach (ReadingEventArgs reading in
    sensor.ReadingChangedStream(cancellationToken))
{
    Console.WriteLine(reading.Value);
}
```

This is an ordinary `IAsyncEnumerable<T>`. Enumeration owns the event subscription; stopping or disposing the enumeration removes it.

The default buffer is unbounded and lossless. If producers can outrun consumers for a sustained period, read [Buffering and slow consumers](buffering.md).

## 6. Keep event-first code unchanged

Async and event consumers can use the same source at the same time:

```csharp
sensor.ReadingChanged += OnReadingChanged;

ReadingEventArgs next =
    await sensor.ReadingChangedAsync(cancellationToken);
```

That is the central design goal: event-first code stays event-first, async code gets normal async shapes, and the bridge plumbing stays inside the package.

## Next steps

- [Events to async](events-to-async.md)
- [Async to events](async-to-events.md)
- [Third-party and legacy event sources](third-party-events.md)
- [Lifecycle recipes](lifecycle-recipes.md)
- [Troubleshooting and FAQ](troubleshooting.md)

For exact target-framework differences, see the [support matrix](support-matrix.md).
