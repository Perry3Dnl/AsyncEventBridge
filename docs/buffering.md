# Buffering and slow consumers

A synchronous .NET event can fire faster than an async consumer can process values. Because the event callback cannot asynchronously wait for the consumer, an event-to-stream adapter must either buffer, drop, or block.

AsyncEventBridge makes that choice explicit.

## Default: unbounded and lossless

The default is:

```csharp
new EventStreamOptions
{
    Capacity = 100,
    FullMode = EventStreamFullMode.Unbounded,
};
```

`Unbounded` preserves accepted event values and has no fixed buffer limit.

The `Capacity = 100` default does **not** limit an unbounded stream. Capacity is ignored in `Unbounded` mode.

Use this default when losing an event would be semantically incorrect and the producer is not expected to outpace the consumer indefinitely.

## Bounded: keep the newest state

Use `DropOldest` when recent values matter more than old buffered history:

```csharp
var options = new EventStreamOptions
{
    Capacity = 100,
    FullMode = EventStreamFullMode.DropOldest,
};
```

When the buffer is full, the oldest buffered value is discarded to make room for the incoming value.

Typical examples include rapidly changing state, UI telemetry, or sensor readings where the newest state is more useful than complete history.

## Bounded: preserve queued work

Use `DropWrite` when already-buffered values should be preserved and new arrivals may be discarded:

```csharp
var options = new EventStreamOptions
{
    Capacity = 100,
    FullMode = EventStreamFullMode.DropWrite,
};
```

When the buffer is full, the incoming value is discarded.

This name intentionally matches `System.Threading.Channels.BoundedChannelFullMode.DropWrite`.

## Observe drops

On modern .NET, bounded loss can be observed:

```csharp
var options = new EventStreamOptions
{
    Capacity = 100,
    FullMode = EventStreamFullMode.DropWrite,
    DropObserver = droppedCount =>
        Console.WriteLine($"Dropped: {droppedCount}"),
};

await foreach (Reading reading in
    sensor.ReadingChangedStream(options, cancellationToken))
{
    Process(reading);
}

Console.WriteLine(options.DroppedCount);
```

`DroppedCount` is thread-safe and accumulates across streams/enumerations that reuse the same options instance.

Runtime metrics are also available on modern .NET; see [Metrics](metrics.md).

## Which mode should I choose?

Choose `Unbounded` when every event matters and producer/consumer imbalance is naturally bounded.

Choose `DropOldest` when the stream represents changing state and fresher values matter more than history.

Choose `DropWrite` when values already accepted into the buffer should keep priority over new arrivals.

If losing values is unacceptable **and** a producer can continuously outrun the consumer, a synchronous event is not a backpressure-capable protocol. Consider changing the producer contract to an asynchronous API instead.

## Why there is no blocking mode

AsyncEventBridge deliberately does not block the synchronous event callback waiting for buffer space.

Blocking can:

- change the timing contract of the source event;
- create reentrancy surprises;
- deadlock when the consumer needs the producer thread or synchronization context;
- make an observational adapter control a producer that was never designed for async backpressure.

The package therefore chooses explicit buffering or dropping rather than hidden producer blocking.
