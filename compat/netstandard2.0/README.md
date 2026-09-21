<p align="center">
  <img src="assets/AsyncEventBridge.png" alt="AsyncEventBridge icon" width="128" height="128">
</p>

<h1 align="center">AsyncEventBridge</h1>

<p align="center"><strong>Bridge classic .NET events and modern async code in both directions.</strong></p>

AsyncEventBridge is for boundaries where one side of an application uses traditional .NET events and the other side uses `Task`, `Task<T>`, `IAsyncEnumerable<T>`, or `async`/`await`.

The compatibility runtime targets **.NET Standard 2.0**. Starting with 0.3.0, this implementation is developed on `main` under `compat/netstandard2.0` and participates in the same release gate as the modern and Unity editions. The source generator ships with the compatibility package.

> Use AsyncEventBridge where event-driven and async code meet. If both sides are already async, use normal async code directly.

## Install

Package ID:

```text
AsyncEventBridge
```

For the unified `0.3.0` release line:

```text
dotnet add package AsyncEventBridge --version 0.3.0
```

or:

```xml
<PackageReference Include="AsyncEventBridge" Version="0.3.0" />
```

Then import the namespace:

```csharp
using AsyncEventBridge;
```

The package contains both the runtime and the source generator. There is no separate generator package to install.

## What it bridges

```text
Event                -> Task
Event                -> IAsyncEnumerable<T>
Task                 -> EventBridge
Task<T>              -> EventBridge<T>
IAsyncEnumerable<T>  -> EventStreamBridge<T>
```

## Await an event from a type you own

Add `[GenerateAsyncEvents]` to the event source:

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

The generator adds an async facade without changing the original event:

```csharp
SensorEventArgs value = await sensor.ValueChangedAsync();
Console.WriteLine(value.Value);
```

Filtering, cancellation, and timeout are available when needed:

```csharp
SensorEventArgs value = await sensor.ValueChangedAsync(
    e => e.Value >= 100,
    TimeSpan.FromSeconds(5),
    cancellationToken);
```

The simple form stays simple:

```csharp
var value = await sensor.ValueChangedAsync();
```

## Generate for a type you do not own

You do not need source access to the event source. Request generation at assembly level:

```csharp
using AsyncEventBridge;

[assembly: GenerateAsyncEventsFor(typeof(System.Timers.Timer))]
```

`System.Timers.Timer` uses the custom `ElapsedEventHandler` delegate. AsyncEventBridge can adapt it directly:

```csharp
using var timer = new System.Timers.Timer(250)
{
    AutoReset = false
};

var elapsedTask = timer.ElapsedAsync();
timer.Start();

System.Timers.ElapsedEventArgs elapsed = await elapsedTask;
```

The same mechanism can be used for public event-based types from third-party packages:

```csharp
[assembly: GenerateAsyncEventsFor(typeof(ThirdParty.LegacySensor))]
```

The target type itself is not modified. The generator creates extension methods in the consuming compilation.

This is useful for legacy libraries, framework APIs, vendor SDKs, and other event sources that cannot be annotated with `[GenerateAsyncEvents]`.

## Custom event delegate support

Generated APIs are not limited to `EventHandler` and `EventHandler<TEventArgs>`.

A custom delegate is supported when it follows the normal event-handler shape:

- it returns `void`;
- it has exactly two non-`ref` parameters;
- the second parameter derives from `EventArgs`.

For example:

```csharp
public delegate void SensorChangedHandler(
    object? sender,
    SensorEventArgs e);

[GenerateAsyncEvents]
public sealed class Sensor
{
    public event SensorChangedHandler? Changed;
}
```

This produces the same style of facade:

```csharp
SensorEventArgs value = await sensor.ChangedAsync();

await foreach (SensorEventArgs item in sensor.ChangedStream(cancellationToken))
{
    Console.WriteLine(item.Value);
}
```

This covers common .NET patterns such as `PropertyChangedEventHandler`, `NotifyCollectionChangedEventHandler`, `ElapsedEventHandler`, and similar framework or legacy delegates that use an `EventArgs`-derived second parameter.

### Unsupported event diagnostics

If an annotated or explicitly targeted event uses a delegate shape that cannot be adapted safely, the generator reports a warning instead of silently ignoring it:

```text
AEB001: Unsupported event delegate
```

For example, a one-parameter delegate does not have enough information to map to the event bridge contract:

```csharp
public delegate void ValueHandler(int value);

[GenerateAsyncEvents]
public sealed class Sensor
{
    public event ValueHandler? Changed; // AEB001
}
```

The diagnostic includes the event and delegate type so a missing generated method is explainable from the build output.

## Consume repeated events as an async stream

Every supported generated event also gets an `<EventName>Stream(...)` facade:

```csharp
await foreach (SensorEventArgs value in sensor.ValueChangedStream(cancellationToken))
{
    Console.WriteLine(value.Value);
}
```

Filtering is supported:

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

A synchronous event cannot asynchronously wait for a slow consumer. Repeated events therefore need an explicit buffering policy.

The default is:

```text
Capacity = 100
FullMode = Unbounded
```

`Unbounded` preserves every accepted event value and has no fixed buffer limit. If producers continuously outrun consumers, memory usage can grow without a fixed upper bound.

`Capacity` is ignored by `Unbounded`. It is used only as the hard buffer limit for the two bounded drop modes. The default value of `100` exists so opting into a bounded mode has a useful capacity without another required setting.

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

The modes are (0.4 renames the previous `Grow` member to `Unbounded` before the 1.0 API freeze):

```text
Unbounded   preserve all accepted values; Capacity is ignored
DropOldest  keep the newest buffered values within Capacity
DropNewest  preserve the existing buffer and drop the incoming value at Capacity
```

There is deliberately no blocking `Wait` mode. Blocking a synchronous event producer changes event semantics and can introduce deadlocks.

## Bridge Task to events

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

Attach handlers before calling `Connect()`.

`Connect()` does not start the underlying `Task`. It connects the already-created async operation to event publication. A bridge can only be connected once.

## Bridge IAsyncEnumerable<T> to events

```csharp
await using EventStreamBridge<SensorValue> bridge =
    ReadSensorValuesAsync().ToEventBridge();

bridge.Value += (_, e) => Console.WriteLine(e.Value);
bridge.Completed += (_, _) => Console.WriteLine("Completed");
bridge.Faulted += (_, e) => Console.Error.WriteLine(e.Exception);
bridge.Cancelled += (_, _) => Console.WriteLine("Cancelled");

bridge.Connect(cancellationToken);
```

Values are published in enumeration order. The stream then publishes exactly one terminal event: `Completed`, `Faulted`, or `Cancelled`.

There is no replay buffer in this direction. A handler attached after publication has started can miss earlier values, matching ordinary .NET event behavior.

## Subscriber exception policy

The async-to-events bridges isolate subscribers from one another.

The default `EventBridgeSubscriberExceptionPolicy.TraceAndContinue` catches subscriber exceptions, writes them through `System.Diagnostics.Trace.TraceError`, and continues dispatching remaining subscribers.

Pass `EventBridgeOptions` to `ToEventBridge(...)` to select `ReportAndContinue` with a synchronous observer callback or `IgnoreAndContinue`. All policies preserve subscriber isolation. A propagation mode is deliberately not provided because bridge publication is driven by asynchronous observation and normally has no synchronous caller that can usefully receive a subscriber exception.

## Lifecycle rules

For task bridges:

```text
ToEventBridge()
attach handlers
Connect()
Dispose()
```

For async-stream bridges:

```text
ToEventBridge()
attach handlers
Connect(cancellationToken)
Dispose() or DisposeAsync()
```

`Dispose()` suppresses new publication without waiting for a handler invocation that already started.

For `EventStreamBridge<T>`, `DisposeAsync()` also waits for bridge-owned async enumeration cleanup and in-flight publication. After it completes, no further event handler will be invoked by that bridge.

Owner disposal does not publish `Cancelled`. `Cancelled` represents cancellation of the connected async operation.

Completion, cancellation, and fault races publish at most one terminal outcome. No terminal outcome is given artificial priority.

## Generated API rules

The generator keeps generated APIs aligned with the source API:

- source accessibility is never widened;
- public inherited events are supported;
- protected and private events are not surfaced as top-level extension methods;
- generic source types are supported;
- accessible nested source types are supported;
- generic constraints are preserved;
- normal C# member hiding is respected;
- source instance methods keep normal C# precedence;
- generated extension holder names are collision-safe.

If a source type already has an applicable instance method named `ValueChangedAsync()`, the instance method wins under normal C# method resolution. The generated bridge method remains callable explicitly through its generated extension class.

## Low-level APIs

Generated methods are the normal entry point, but the runtime APIs remain public for manual adapters and unusual integration boundaries:

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

`.NET Standard 2.0` is the minimum complete runtime contract.

Async-stream interfaces required by this target are supplied through `Microsoft.Bcl.AsyncInterfaces`. Generated source remains compatible with C# 8 syntax.

CI verifies the baseline with a dedicated .NET Standard 2.0 / C# 8 consumer. It also builds the NuGet package, restores isolated consumers from the generated `.nupkg`, compiles generated APIs from that package, and runs a packaged runtime smoke test. The package smoke test includes assembly-level generation against an unowned framework type and a custom event delegate.

Newer framework targets can be added when they provide a concrete compatibility or performance benefit, but they should remain additive to the .NET Standard 2.0 baseline.

## Sample

A runnable sensor-monitoring example is available in:

```text
samples/SensorMonitoring
```

Run it with:

```text
dotnet run --project samples/SensorMonitoring/SensorMonitoring.csproj
```

## Build from source

```text
dotnet restore AsyncEventBridge.sln
dotnet build AsyncEventBridge.sln -c Release
dotnet test AsyncEventBridge.sln -c Release --no-build
dotnet pack src/AsyncEventBridge/AsyncEventBridge.csproj -c Release
```

## License

AsyncEventBridge is licensed under the **Mozilla Public License 2.0 (MPL-2.0)**. See [`LICENSE`](LICENSE) for the full license text.
