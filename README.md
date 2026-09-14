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

## Compatibility baseline

The complete runtime targets **.NET Standard 2.0**. That is the compatibility floor for the public API, not a reduced feature variant.

Newer framework targets may be added later when they provide a concrete benefit, but normal feature development should not raise the minimum runtime requirement. The intended public behavior remains the same across future targets.

Async-stream interfaces needed by the .NET Standard 2.0 runtime are supplied through `Microsoft.Bcl.AsyncInterfaces`. The runtime avoids newer convenience APIs when equivalent behavior can be implemented without changing the consumer-facing contract.

A dedicated .NET Standard 2.0 / C# 8 compatibility project is built in CI so the baseline is verified by an actual consumer compile rather than only by the runtime project's target framework declaration.

## Package shape

AsyncEventBridge is prepared as **one NuGet package**. Consumers should not need a separate generator package.

The package contains the .NET Standard 2.0 runtime under `lib/netstandard2.0`, XML API documentation, the source generator under `analyzers/dotnet/cs`, the README, and the package icon.

The repository currently uses the pre-release package version `0.1.0-preview.1`. A local package can be built with:

```text
dotnet pack src/AsyncEventBridge/AsyncEventBridge.csproj -c Release
```

CI additionally restores a separate .NET Standard 2.0 consumer from the generated `.nupkg`, compiles the generated APIs from that package, executes a second packaged runtime consumer, and retains the `.nupkg` as a workflow artifact. This catches packaging mistakes that ordinary project-reference tests cannot detect.

## One example environment throughout the documentation

The documentation deliberately uses one fictional **sensor monitoring system** from beginning to end.

You will keep seeing the same concepts, such as `Sensor`, `sensor`, `ValueChanged`, `Connected`, `Disconnected`, `AlarmRaised`, and sensor configuration. Later examples extend that same environment instead of switching to unrelated examples such as users, orders, chat messages, or completely different applications.

This is intentional. A reader should only need to learn one example system. Each following example can then focus on the one new AsyncEventBridge concept being introduced.

Before/after migration examples follow an additional rule: both sides use the same classes, objects and scenario. Only the code that AsyncEventBridge replaces should change.

A runnable version of this environment lives in `samples/SensorMonitoring` and is built and executed as part of CI.

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

Because a normal synchronous .NET event cannot wait asynchronously for a slow consumer, the stream must either buffer values or explicitly drop them. The default is lossless: `EventStreamFullMode.Grow` preserves every event value and starts with a buffer capacity of 100. If the producer keeps outrunning the consumer, that buffer is allowed to grow beyond its initial capacity and memory usage can therefore grow without a fixed upper bound.

When bounded memory is more important than preserving every value, configure the generated stream explicitly:

```csharp
await foreach (var value in sensor.ValueChangedStream(
    new EventStreamOptions
    {
        Capacity = 100,
        FullMode = EventStreamFullMode.DropOldest
    },
    cancellationToken))
{
    Console.WriteLine(value.Value);
}
```

`DropOldest` removes the oldest buffered value when the hard capacity is reached. `DropNewest` keeps the existing buffer and drops the newly arriving value. The bridge intentionally does not provide a blocking `Wait` mode, because blocking a synchronous event producer can change event semantics and introduce deadlock risk.

The bridge subscribes when enumeration begins and unsubscribes when the `await foreach` ends, is disposed, or is cancelled.

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

`EventStreamBridge<T>` publishes values in enumeration order and then one terminal event: `Completed`, `Faulted`, or `Cancelled`. `Dispose()` suppresses new publication without waiting for an event dispatch that is already in progress. `DisposeAsync()` waits for an in-flight dispatch and asynchronous enumerator cleanup; after it completes, the bridge will not invoke another event handler. Owner disposal itself does not publish `Cancelled`.

For task bridges, `Dispose()` suppresses an outcome that has not started publication yet. A terminal event publication that already began may finish after `Dispose()` returns, matching the stream bridge's non-blocking synchronous disposal rule.

If cancellation races with successful completion or a fault, the stream bridge gives no outcome artificial priority. At most one terminal event is published, and the outcome that reaches terminal publication first wins.

## Generated API behavior

Generated extensions live in the `AsyncEventBridge` namespace. The normal call remains the short extension syntax:

```csharp
await sensor.ValueChangedAsync();
```

If `Sensor` already declares an instance method with the same signature, normal C# resolution lets that instance method win. The generated bridge method remains explicitly callable through its generated extension class.

The generator also supports generic and accessible nested source types and preserves their generic constraints on generated methods. Public inherited events are included when an annotated base type does not already provide the bridge. If an annotated base type already generates that event API, the derived type does not generate a duplicate.

Generated methods never make an existing event more visible. Public source types and public event signatures can produce public extensions; internal source members produce internal extensions; protected and private events are not exposed through a top-level extension API.

## Design goals

The public API is designed from the consumer's point of view first. A developer migrating an existing application should be able to see clearly:

- what code already exists;
- what code AsyncEventBridge replaces;
- what code the application still needs to own;
- how the same boundary can remain in production if that is the right architecture.

Internally, the project is correctness-first. Subscription lifecycle, cancellation, timeouts, races, reentrancy and cleanup are treated as core behavior rather than edge cases.

Generated APIs are intended to remain a thin facade over central runtime components so lifecycle and concurrency behavior stay consistent.

## Current implementation

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

Filtering, timeout, cancellation, and explicit stream-buffer policy are added progressively without changing the simple entry points.

`Task`, `Task<T>`, and `IAsyncEnumerable<T>` can also be bridged back to event-driven consumers through `ToEventBridge()`. Tasks expose terminal events through `EventBridge` / `EventBridge<T>`, while async streams use `EventStreamBridge<T>` with `Value` plus terminal events.

## Correctness targets

The test suites cover the event-to-async runtime with:

- event success and predicate filtering;
- predicate exceptions;
- cancellation and timeout cleanup;
- invalid arguments and timeout validation;
- subscription failure after a partial subscribe and cleanup;
- event during subscription (reentrancy);
- event vs cancellation;
- event vs timeout;
- cancellation vs timeout;
- independent concurrent waits;
- 100+ parallel waits;
- repeated race stress;
- zero handlers remaining after completion;
- repeated event ordering through async streams;
- concurrent event-stream producers in lossless `Grow` mode;
- async-stream filtering, cancellation, disposal, and reentrant subscription cleanup;
- lossless growing buffers and bounded `DropOldest` / `DropNewest` behavior.

The async-to-events tests also cover task outcome publication, async-stream value ordering, completion, faults, cancellation, disposal, single-connect behavior, subscriber exception isolation, handler removal, no-replay behavior, in-flight disposal behavior, completion/cancellation races, and suppression of task publication after owner disposal.

Generator tests cover method collisions, inherited events, accessibility, generic and nested source types, generic constraints, hidden members, generated class-name collisions, and C# 8-compatible generated syntax. A public API lock test also verifies that the runtime does not accidentally expose additional public types or methods and locks stream option defaults plus enum numeric values.

The compatibility consumer compiles every generated one-shot and stream overload against .NET Standard 2.0 / C# 8. Package smoke consumers separately verify that the same APIs compile and execute when consuming the built NuGet package rather than project references.

Race tests use controlled synchronization rather than timing-based `Task.Delay` assertions.

## Project layout

```text
src/
  AsyncEventBridge/
  AsyncEventBridge.Generators/
samples/
  SensorMonitoring/
tests/
  AsyncEventBridge.Tests/
  AsyncEventBridge.Generators.Tests/
  AsyncEventBridge.StressTests/
  AsyncEventBridge.Compatibility/
  AsyncEventBridge.PackageSmoke/
  AsyncEventBridge.PackageRuntimeSmoke/
docs/
  public-api-draft.md
  release-readiness.md
```

## Release boundary

The codebase can verify runtime behavior, generator behavior, package contents, package consumption, packaged runtime execution, the compatibility floor, and the sample automatically.

A few release decisions deliberately remain outside implementation because they belong to the package owner: licensing or commercial terms, the final stable version number, optional package signing, NuGet.org publishing credentials, and which external hosts are advertised as explicitly certified.

Host-specific certification is separate from .NET Standard compatibility. For example, a Unity version should only be advertised as certified after the actual packaged artifact has been tested in that Unity version.

## Roadmap

1. Keep the .NET Standard 2.0 API and behavior frozen behind tests.
2. Finalize the release-owner choices: licensing/commercial terms, first stable version, signing if wanted, and NuGet publishing.
3. Publish and verify the first package from a clean external consumer.
4. Add newer runtime targets only where they provide a concrete compatibility or performance benefit.
5. Certify additional host environments such as Unity without changing the baseline contract.
