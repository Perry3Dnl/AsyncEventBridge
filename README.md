<p align="center">
  <img src="assets/AsyncEventBridge.png" alt="AsyncEventBridge icon" width="128" height="128">
</p>

<h1 align="center">AsyncEventBridge</h1>

<p align="center"><strong>Bridge classic .NET events and modern async code in both directions.</strong></p>

## v0.1.0 — first release

AsyncEventBridge `0.1.0` is the first release of the project. The complete runtime targets **.NET Standard 2.0**.

That means the baseline is not a reduced compatibility build: the full public runtime API is available on the .NET Standard 2.0 target. The source generator is included in the same package.

AsyncEventBridge is useful when one side of an application uses traditional .NET events and the other side uses `Task`, `Task<T>`, `IAsyncEnumerable<T>`, or `async`/`await`.

> Use AsyncEventBridge where event-driven and async code meet. If both sides are already async, use normal async code directly.

## Install

Package ID:

```text
AsyncEventBridge
```

.NET CLI:

```text
dotnet add package AsyncEventBridge --version 0.1.0
```

PackageReference:

```xml
<PackageReference Include="AsyncEventBridge" Version="0.1.0" />
```

The package contains both the runtime and the source generator. No second generator package is required.

In code, import the package namespace:

```csharp
using AsyncEventBridge;
```

## Quick start

AsyncEventBridge supports both directions:

```text
Event                -> Task
Event                -> IAsyncEnumerable<T>
Task                 -> EventBridge
Task<T>              -> EventBridge<T>
IAsyncEnumerable<T>  -> EventStreamBridge<T>
```

### 1. Await an existing event

Start with a normal event-driven type and add `[GenerateAsyncEvents]`:

```csharp
using AsyncEventBridge;
using System;

[GenerateAsyncEvents]
public sealed class Sensor
{
    public event EventHandler<SensorEventArgs>? ValueChanged;

    public void Update(int value)
    {
        ValueChanged?.Invoke(this, new SensorEventArgs(value));
    }
}

public sealed class SensorEventArgs : EventArgs
{
    public SensorEventArgs(int value)
    {
        Value = value;
    }

    public int Value { get; }
}
```

The generator adds a `ValueChangedAsync(...)` extension method:

```csharp
SensorEventArgs value = await sensor.ValueChangedAsync();
Console.WriteLine(value.Value);
```

The original `ValueChanged` event remains unchanged. AsyncEventBridge only creates an async-facing facade around it.

### 2. Filter, cancel, or time out an event wait

Wait for a matching event:

```csharp
SensorEventArgs value = await sensor.ValueChangedAsync(
    e => e.Value >= 100);
```

Use cancellation:

```csharp
SensorEventArgs value = await sensor.ValueChangedAsync(
    cancellationToken);
```

Use a timeout:

```csharp
SensorEventArgs value = await sensor.ValueChangedAsync(
    TimeSpan.FromSeconds(5),
    cancellationToken);
```

Combine filtering and timeout when needed:

```csharp
SensorEventArgs value = await sensor.ValueChangedAsync(
    e => e.Value >= 100,
    TimeSpan.FromSeconds(5),
    cancellationToken);
```

### 3. Consume repeated event occurrences as an async stream

The same event can be consumed repeatedly through `IAsyncEnumerable<T>`:

```csharp
await foreach (SensorEventArgs value in sensor.ValueChangedStream(cancellationToken))
{
    Console.WriteLine(value.Value);
}
```

Filtering is supported here as well:

```csharp
await foreach (SensorEventArgs value in sensor.ValueChangedStream(
    e => e.Value >= 100,
    cancellationToken))
{
    Console.WriteLine(value.Value);
}
```

The bridge subscribes when enumeration starts and unsubscribes when enumeration ends, is disposed, or is cancelled.

## Event stream buffering

A synchronous .NET event cannot asynchronously wait for a slow consumer, so repeated events must either be buffered or dropped according to an explicit policy.

The default is:

```text
Capacity = 100
FullMode = Grow
```

`Grow` preserves every accepted event value. `Capacity` is its initial capacity, not a hard limit. If producers continuously outrun consumers, memory usage can grow without a fixed upper bound.

For bounded buffering:

```csharp
await foreach (SensorEventArgs value in sensor.ValueChangedStream(
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

The available modes are:

```text
Grow        preserve all values; buffer may grow beyond Capacity
DropOldest  keep the newest buffered values within Capacity
DropNewest  preserve the existing buffer and drop the incoming value at Capacity
```

AsyncEventBridge intentionally does not provide a blocking `Wait` buffer mode. Blocking a synchronous event producer changes event semantics and can introduce deadlocks.

## Bridge Task to events

Async code can also be exposed to an event-driven consumer.

For `Task`:

```csharp
using EventBridge bridge = SaveSensorConfigurationAsync().ToEventBridge();

bridge.Completed += (_, _) => Console.WriteLine("Saved");
bridge.Faulted += (_, e) => Console.Error.WriteLine(e.Exception);
bridge.Cancelled += (_, _) => Console.WriteLine("Cancelled");

bridge.Connect();
```

For `Task<T>`:

```csharp
using EventBridge<SensorConfiguration> bridge =
    LoadSensorConfigurationAsync().ToEventBridge();

bridge.Completed += (_, e) => Console.WriteLine(e.Value);
bridge.Faulted += (_, e) => Console.Error.WriteLine(e.Exception);
bridge.Cancelled += (_, _) => Console.WriteLine("Cancelled");

bridge.Connect();
```

`ToEventBridge()` creates the event-facing bridge. Attach handlers first, then call `Connect()`.

`Connect()` does not start the underlying task. A `Task` may already be running or may already have completed. `Connect()` starts publication through the bridge.

A bridge can only be connected once.

## Bridge IAsyncEnumerable<T> to events

For an async stream:

```csharp
await using EventStreamBridge<SensorValue> bridge =
    ReadSensorValuesAsync().ToEventBridge();

bridge.Value += (_, e) => Console.WriteLine(e.Value);
bridge.Completed += (_, _) => Console.WriteLine("Completed");
bridge.Faulted += (_, e) => Console.Error.WriteLine(e.Exception);
bridge.Cancelled += (_, _) => Console.WriteLine("Cancelled");

bridge.Connect(cancellationToken);
```

Values are published in enumeration order. The stream then publishes one terminal event: `Completed`, `Faulted`, or `Cancelled`.

The bridge does not buffer or replay values in this direction. A handler attached after publication has started can miss earlier values, which matches normal .NET event behavior.

## Lifecycle rules

The public lifecycle is intentionally explicit:

```text
Task / Task<T> bridge
    ToEventBridge()
    attach handlers
    Connect()
    Dispose()

IAsyncEnumerable<T> bridge
    ToEventBridge()
    attach handlers
    Connect(cancellationToken)
    Dispose() or DisposeAsync()
```

For `EventStreamBridge<T>`, `Dispose()` suppresses new publication without waiting for a handler invocation that has already started. `DisposeAsync()` additionally waits for bridge-owned async enumeration cleanup and any publication already in progress. After `DisposeAsync()` completes, the bridge will not invoke more handlers.

Owner disposal does not publish `Cancelled`. `Cancelled` represents cancellation of the connected async operation.

Terminal completion, cancellation, and fault races publish at most one terminal outcome. No terminal outcome is given artificial priority.

## Generated API rules

Generated methods are designed to stay predictable:

- public source APIs produce public generated APIs;
- internal source APIs remain internal;
- protected and private events are not exposed as top-level generated extensions;
- public inherited events are supported;
- generic and accessible nested source types are supported;
- generic constraints are preserved;
- normal C# instance-method precedence is respected.

If a source type already contains an instance method named `ValueChangedAsync()`, that instance method wins during normal method resolution. The generated AsyncEventBridge method remains explicitly callable through its generated extension class.

## Low-level APIs

Most consumers should use generated `<EventName>Async(...)`, `<EventName>Stream(...)`, and `ToEventBridge()` methods.

For manual integration, the runtime also exposes:

```text
EventAwaiter
EventStream
EventStreamOptions
EventStreamFullMode
EventBridge
EventBridge<T>
EventStreamBridge<T>
AsyncValueEventArgs<T>
AsyncFaultedEventArgs
```

## .NET Standard 2.0 baseline

Version `0.1.0` is built around **.NET Standard 2.0** as the minimum complete runtime contract.

Async-stream interfaces required by this target are supplied through `Microsoft.Bcl.AsyncInterfaces`. The generated code is kept compatible with C# 8 syntax so older hosts are not forced to use newer language syntax just because the generator is present.

CI verifies this baseline using a dedicated .NET Standard 2.0 / C# 8 consumer. It also packs the NuGet package, restores separate consumers from the resulting `.nupkg`, builds generated APIs from that package, and executes a packaged runtime smoke test.

Newer target frameworks may be added in future releases when they provide a concrete compatibility or performance benefit. They should remain additive and should not replace the .NET Standard 2.0 baseline without a deliberate compatibility decision.

## Sample

A complete sensor-monitoring example is available in:

```text
samples/SensorMonitoring
```

Run it with:

```text
dotnet run --project samples/SensorMonitoring/SensorMonitoring.csproj
```

The sample is built and executed in CI.

## Build from source

```text
dotnet restore AsyncEventBridge.sln
dotnet build AsyncEventBridge.sln -c Release
dotnet test AsyncEventBridge.sln -c Release --no-build
dotnet pack src/AsyncEventBridge/AsyncEventBridge.csproj -c Release
```

## Scope of v0.1.0

The first release focuses on one job: reliable interoperability between ordinary .NET events and async code.

It is not an event bus, messaging system, reactive framework, or replacement for normal `async`/`await`.

The supported bridge matrix for `0.1.0` is:

```text
Event                -> Task
Event                -> IAsyncEnumerable<T>
Task                 -> EventBridge
Task<T>              -> EventBridge<T>
IAsyncEnumerable<T>  -> EventStreamBridge<T>
```

Future versions can add targets, host validation, and other compatibility work without changing that core boundary model.
