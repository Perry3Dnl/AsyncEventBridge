<p align="center">
  <img src="assets/AsyncEventBridge.png" alt="AsyncEventBridge icon" width="128" height="128">
</p>

<h1 align="center">AsyncEventBridge</h1>

<p align="center"><strong>Bridge event-driven and async .NET code in both directions.</strong></p>

AsyncEventBridge provides two-way interoperability between traditional .NET events and modern async code.

The project is correctness-first: subscription lifecycle, cancellation, timeouts, races, reentrancy and cleanup are treated as core behavior rather than edge cases.

## Current vertical slice

The first implementation focuses on `EventHandler<TEventArgs>` and `EventHandler` to async waits.

```csharp
[GenerateAsyncEvents]
public sealed class Sensor
{
    public event EventHandler<SensorEventArgs>? ValueChanged;
}

var result = await sensor.ValueChangedAsync(
    eventArgs => eventArgs.Value == 100,
    cancellationToken);
```

A timeout overload is generated as well:

```csharp
var result = await sensor.ValueChangedAsync(
    TimeSpan.FromSeconds(10),
    eventArgs => eventArgs.Value == 100,
    cancellationToken);
```

The generated methods are a facade over a central runtime engine. The runtime owns subscription, unsubscription, predicates, cancellation, timeout handling, single-winner completion and cleanup.

## Correctness targets

The test suites cover:

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
- zero handlers remaining after completion.

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

1. Harden the event-to-task runtime and generator facade.
2. Add `Task<T> -> EventSource<T>` and `Task -> EventSource`.
3. Add `IAsyncEnumerable<T>` bridging in both directions.

AsyncEventBridge intentionally does not aim to be an Rx replacement, event bus or messaging framework.
