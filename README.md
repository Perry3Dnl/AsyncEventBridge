<p align="center">
  <img src="assets/AsyncEventBridge.png" alt="AsyncEventBridge icon" width="128" height="128">
</p>

<h1 align="center">AsyncEventBridge</h1>

<p align="center"><strong>Bridge event-driven and async .NET code in both directions.</strong></p>

AsyncEventBridge connects traditional .NET event-driven code with modern `async`/`await`, `Task`, and async-stream based code.

It is designed for systems where both programming models need to live together: during a migration, at an integration boundary, or permanently when an event-based component and an async component are both valid parts of the application.

## What AsyncEventBridge is for

Imagine an existing system that exposes events while new application code is being written with `async`/`await`.

You should not have to rewrite the existing component just to consume one of its events asynchronously. AsyncEventBridge lets the two models meet at that boundary.

The same applies in the other direction: if new code returns a `Task<T>` or an `IAsyncEnumerable<T>`, an older event-driven part of the application can consume it through an event-shaped API.

This makes AsyncEventBridge useful in two ways:

- as a migration aid while an application moves from event-driven code toward async code;
- as a permanent interoperability layer when both models continue to exist by design.

A bridge does not have to be temporary. If an existing library naturally exposes events and your application naturally consumes async APIs, keeping AsyncEventBridge at that boundary is a valid production design.

## What AsyncEventBridge is not

AsyncEventBridge is **not a replacement for normal async programming**.

If both sides of your code already use `Task`, `Task<T>` or `IAsyncEnumerable<T>`, use normal `async`/`await` directly:

```csharp
var configuration = await LoadSensorConfigurationAsync();
```

Do not introduce an event bridge when there is no event/async boundary to bridge.

A useful design rule for this project is:

> Use AsyncEventBridge where event-driven and async code meet. Do not use it to rebuild async code on top of async code.

AsyncEventBridge also does not aim to be an Rx replacement, event bus or messaging framework.

## One example environment throughout the documentation

The documentation deliberately uses one fictional **sensor monitoring system** from beginning to end.

You will keep seeing the same concepts, such as `Sensor`, `sensor`, `ValueChanged`, `Connected`, `Disconnected`, `AlarmRaised`, and sensor configuration. Later examples extend that same environment instead of switching to unrelated examples such as users, orders, chat messages, or completely different applications.

This is intentional. A reader should only need to learn one example system. Each following example can then focus on the one new AsyncEventBridge concept being introduced.

Before/after migration examples follow an additional rule: both sides use the same classes, objects and scenario. Only the code that AsyncEventBridge replaces should change.

## Example: migrating an event consumer

Suppose the existing monitoring system already contains this type:

```csharp
[GenerateAsyncEvents]
public sealed class Sensor
{
    public event EventHandler<SensorEventArgs>? ValueChanged;
}

public sealed class SensorEventArgs : EventArgs
{
    public int Value { get; init; }
}

var sensor = new Sensor();
```

### Before

Traditional event-driven code subscribes a handler:

```csharp
sensor.ValueChanged += OnValueChanged;

void OnValueChanged(object? sender, SensorEventArgs e)
{
    Console.WriteLine(e.Value);
}
```

### After

Async code can wait for the same existing event:

```csharp
var e = await sensor.ValueChangedAsync();

Console.WriteLine(e.Value);
```

The `sensor` object is still the same object. `ValueChanged` is still the same event. The `Sensor` business logic does not need to become async just because its consumer does.

AsyncEventBridge takes care of the bridge mechanics such as subscribing, unsubscribing and completing the wait. Your application still owns the `Sensor`, still raises `ValueChanged`, and still decides what to do with the resulting `SensorEventArgs`.

## Optional filtering and cancellation

The simplest API should stay simple:

```csharp
var e = await sensor.ValueChangedAsync();
```

When needed, a caller can wait for a specific sensor value:

```csharp
var e = await sensor.ValueChangedAsync(
    e => e.Value >= 100);
```

Or allow the wait to be cancelled:

```csharp
var e = await sensor.ValueChangedAsync(
    cancellationToken);
```

Cancellation is optional. You do not need a `CancellationToken` for the basic case.

More advanced overloads can combine filtering, timeouts and cancellation without changing the simple entry point.

## Repeated events as an async stream

When the async side needs more than one occurrence of the same event, the generator also exposes a stream-shaped API:

```csharp
await foreach (var value in sensor.ValueChangedStream(cancellationToken))
{
    Console.WriteLine(value.Value);
}
```

The same event can be filtered before values reach the async consumer:

```csharp
await foreach (var value in sensor.ValueChangedStream(
    e => e.Value >= 100,
    cancellationToken))
{
    Console.WriteLine(value.Value);
}
```

The bridge subscribes when enumeration begins and unsubscribes when the `await foreach` ends, is disposed, or is cancelled. Because normal .NET events cannot wait asynchronously for a slow consumer, event values are buffered while the consumer catches up. The current runtime uses an unbounded buffer so values are not silently dropped; a producer that permanently outruns its consumer can therefore grow memory usage.

## Bridging async code back to events

The same sensor monitoring environment can contain newer async APIs while older consumers still expect events.

For example, a newer part of the system can load sensor configuration asynchronously:

```csharp
Task<SensorConfiguration> loadTask = LoadSensorConfigurationAsync();
```

An event-driven consumer creates an event bridge for that same task:

```csharp
using EventBridge<SensorConfiguration> bridge =
    loadTask.ToEventBridge();

bridge.Completed += OnConfigurationLoaded;
bridge.Faulted += OnConfigurationFailed;
bridge.Cancelled += OnConfigurationCancelled;

bridge.Connect();
```

`ToEventBridge()` creates the event-facing bridge. `Connect()` is the moment that configured bridge is connected to the async source and allowed to publish its outcome.

For an async stream, the same vocabulary is used with a stream-specific bridge:

```csharp
await using EventStreamBridge<SensorValue> bridge =
    ReadSensorValuesAsync().ToEventBridge();

bridge.Value += OnSensorValue;
bridge.Completed += OnSensorStreamCompleted;
bridge.Faulted += OnSensorStreamFailed;
bridge.Cancelled += OnSensorStreamCancelled;

bridge.Connect(cancellationToken);
```

`EventStreamBridge<T>` publishes values in enumeration order and then one terminal event: `Completed`, `Faulted`, or `Cancelled`. `DisposeAsync()` stops consumption, suppresses future publication, and waits for asynchronous enumerator cleanup. Disposal itself does not publish `Cancelled`.

## Design goals

The public API is designed from the consumer's point of view first. A developer migrating an existing application should be able to see clearly:

- what code already exists;
- what code AsyncEventBridge replaces;
- what code the application still needs to own;
- how the same boundary can remain in production if that is the right architecture.

Internally, the project is correctness-first. Subscription lifecycle, cancellation, timeouts, races, reentrancy and cleanup are treated as core behavior rather than edge cases.

Generated APIs are intended to remain a thin facade over central runtime components so lifecycle and concurrency behavior stay consistent.

## Current vertical slice

The current implementation supports converting `EventHandler<TEventArgs>` and `EventHandler` events into both one-shot awaitable operations and repeated async streams.

The generated API supports the simple one-shot form:

```csharp
var e = await sensor.ValueChangedAsync();
```

and the repeated stream form:

```csharp
await foreach (var e in sensor.ValueChangedStream(cancellationToken))
{
    Console.WriteLine(e.Value);
}
```

Filtering, timeout, and cancellation are added progressively without changing the simple entry points.

`Task`, `Task<T>`, and `IAsyncEnumerable<T>` can also be bridged back to event-driven consumers through `ToEventBridge()`. Tasks expose terminal events through `EventBridge` / `EventBridge<T>`, while async streams use `EventStreamBridge<T>` with `Value` plus terminal events.

## Correctness targets

The test suites cover the event-to-async runtime with:

- event success and predicate filtering;
- predicate exceptions;
- cancellation and timeout cleanup;
- event during subscription (reentrancy);
- event vs cancellation;
- event vs timeout;
- cancellation vs timeout;
- independent concurrent waits;
- 100+ parallel waits;
- repeated race stress;
- zero handlers remaining after completion;
- repeated event ordering through async streams;
- async-stream filtering, cancellation, disposal, and reentrant subscription cleanup.

The async-to-events tests also cover task outcome publication and async-stream value ordering, completion, faults, cancellation, disposal, single-connect behavior, and subscriber exception isolation.

Race tests use controlled synchronization rather than timing-based `Task.Delay` assertions.

## Project layout

```text
src/
  AsyncEventBridge/
  AsyncEventBridge.Generators/
tests/
  AsyncEventBridge.Tests/
  AsyncEventBridge.Generators.Tests/
  AsyncEventBridge.StressTests/
```

## Roadmap

1. Finalize and harden the Event -> Async public API and runtime.
2. Harden `Task -> Events` and `Task<T> -> Events` interoperability.
3. Harden async-stream bridging in both directions.
4. Turn the sensor monitoring examples into a complete beginner-friendly guide for GitHub and the NuGet package documentation.
