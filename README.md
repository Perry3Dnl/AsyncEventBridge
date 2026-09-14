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
var user = await LoadUserAsync();
```

Do not introduce an event bridge when there is no event/async boundary to bridge.

A useful design rule for this project is:

> Use AsyncEventBridge where event-driven and async code meet. Do not use it to rebuild async code on top of async code.

AsyncEventBridge also does not aim to be an Rx replacement, event bus or messaging framework.

## Example: migrating an event consumer

The important part of a migration example is that the application stays the same. Only the way the event is consumed changes.

Suppose the existing system already contains this type:

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

When needed, a caller can wait for a specific event value:

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

## Design goals

The public API is designed from the consumer's point of view first. A developer migrating an existing application should be able to see clearly:

- what code already exists;
- what code AsyncEventBridge replaces;
- what code the application still needs to own;
- how the same boundary can remain in production if that is the right architecture.

Internally, the project is correctness-first. Subscription lifecycle, cancellation, timeouts, races, reentrancy and cleanup are treated as core behavior rather than edge cases.

Generated APIs are intended to remain a thin facade over central runtime components so lifecycle and concurrency behavior stay consistent.

## Current vertical slice

The current implementation focuses on converting `EventHandler<TEventArgs>` and `EventHandler` events into awaitable operations.

The generated API supports the simple form:

```csharp
var e = await sensor.ValueChangedAsync();
```

and progressively adds optional filtering, timeout and cancellation behavior.

The runtime owns subscription, unsubscription, predicates, cancellation, timeout handling, single-winner completion and cleanup.

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

1. Finalize and harden the Event -> Async public API and runtime.
2. Add `Task -> Events` and `Task<T> -> Events` interoperability.
3. Add `IAsyncEnumerable<T>` bridging in both directions.
4. Turn the migration examples into a complete beginner-friendly guide for GitHub and the NuGet package documentation.
