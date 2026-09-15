<p align="center">
  <img src="assets/AsyncEventBridge.png" alt="AsyncEventBridge icon" width="128" height="128">
</p>

<h1 align="center">AsyncEventBridge — Modern .NET</h1>

<p align="center"><strong>Bridge classic .NET events and modern async code in both directions.</strong></p>

This branch is the native modern-.NET edition of AsyncEventBridge. It targets **.NET 10 (`net10.0`)** directly and is free to use current BCL/runtime features instead of carrying the constraints required by the `.NET Standard 2.0` compatibility line.

> Need the broad compatibility build? Use `base/netstandard2.0`. Need the Unity package? Use `unity`.

## What it bridges

```text
Event                -> Task
Event                -> IAsyncEnumerable<T>
Task                 -> EventBridge
Task<T>              -> EventBridge<T>
ValueTask            -> EventBridge
ValueTask<T>         -> EventBridge<T>
IAsyncEnumerable<T>  -> EventStreamBridge<T>
```

The NuGet package contains both the runtime and the source generator.

## Native modern runtime

The modern line currently uses:

- `net10.0` runtime assets;
- `System.Threading.Channels` for event-stream buffering;
- `TimeProvider` for injectable/testable timeout scheduling;
- `CancellationToken.UnsafeRegister` on the internal one-shot wait cancellation path;
- native `IAsyncEnumerable<T>` / `IAsyncDisposable` support without `Microsoft.Bcl.AsyncInterfaces`;
- modern event payloads that do not need to derive from `EventArgs`;
- BenchmarkDotNet performance baselines maintained under `benchmarks/`.

The source generator itself remains a `netstandard2.0` analyzer because compiler-host compatibility is a different concern from the runtime target.

## Await a .NET event

For low-level/manual adapters:

```csharp
SensorEventArgs value = await EventAwaiter.WaitAsync<SensorEventArgs>(
    handler => sensor.ValueChanged += handler,
    handler => sensor.ValueChanged -= handler,
    eventArgs => eventArgs.Value >= 100,
    cancellationToken,
    TimeSpan.FromSeconds(5));
```

Modern code can inject a `TimeProvider` when timeout behavior needs deterministic or virtual time:

```csharp
SensorEventArgs value = await EventAwaiter.WaitAsync<SensorEventArgs>(
    handler => sensor.ValueChanged += handler,
    handler => sensor.ValueChanged -= handler,
    predicate: null,
    cancellationToken,
    timeout: TimeSpan.FromSeconds(30),
    timeProvider: timeProvider);
```

Normal callers can omit `timeProvider`; `TimeProvider.System` is used automatically.

The modern runtime is not restricted to `EventArgs` payloads. Value types, records, structs, DTOs, and other ordinary payload types can be awaited directly:

```csharp
Task<int> nextValue = EventAwaiter.WaitAsync<int>(
    handler => sensor.ValueChanged += handler,
    handler => sensor.ValueChanged -= handler);
```

## Generated async APIs

For a type you own, add `[GenerateAsyncEvents]`:

```csharp
using AsyncEventBridge;

[GenerateAsyncEvents]
public sealed class Sensor
{
    public event EventHandler<SensorEventArgs>? ValueChanged;
}
```

The generator creates the async facade:

```csharp
SensorEventArgs value = await sensor.ValueChangedAsync();
```

Filtering, timeout, cancellation, and async-stream facades remain available. Generated timeout overloads also expose the modern runtime's `TimeProvider`:

```csharp
SensorEventArgs value = await sensor.ValueChangedAsync(
    TimeSpan.FromSeconds(30),
    cancellationToken,
    timeProvider);
```

This allows deterministic timeout testing without dropping down to the low-level `EventAwaiter` API.

### Modern event payloads

On the modern .NET line, generated APIs also support payloads that do not derive from `EventArgs`:

```csharp
[GenerateAsyncEvents]
public sealed class Counter
{
    public event EventHandler<int>? ValueChanged;
}

int value = await counter.ValueChangedAsync();

await foreach (int item in counter.ValueChangedStream(cancellationToken))
{
    Console.WriteLine(item);
}
```

.NET 10 strongly typed sender events are supported as well:

```csharp
[GenerateAsyncEvents]
public sealed class Sensor
{
    public event EventHandler<Sensor, Reading>? ReadingChanged;
}

Reading reading = await sensor.ReadingChangedAsync();
```

AsyncEventBridge remains payload-centric: the generated task/stream carries the second event parameter. A strongly typed sender is used for event subscription but is not added to the async result.

Custom two-parameter `void` delegates receive the same treatment when the second parameter is a normal non-ref-like type:

```csharp
public delegate void ProgressChangedHandler(Worker sender, int percent);
```

Ref-like payloads such as `Span<T>` cannot safely cross the lifetime boundary into `Task<T>` or `IAsyncEnumerable<T>`. Those event shapes are rejected by the generator with `AEB001` instead of producing unsafe or unusable APIs.

For a public type you do not own:

```csharp
[assembly: GenerateAsyncEventsFor(typeof(System.Timers.Timer))]
```

Common framework delegates such as `ElapsedEventHandler`, `PropertyChangedEventHandler`, and `NotifyCollectionChangedEventHandler` remain supported. Unsupported delegate shapes produce the `AEB001` diagnostic rather than disappearing silently.

## Event streams

Repeated events can be consumed as `IAsyncEnumerable<T>`:

```csharp
await foreach (SensorEventArgs value in sensor.ValueChangedStream(cancellationToken))
{
    Console.WriteLine(value.Value);
}
```

The modern runtime uses `System.Threading.Channels` internally while preserving AsyncEventBridge's buffering contract:

```text
Grow        preserve all accepted values; memory can grow without a fixed bound
DropOldest  discard the oldest buffered value at capacity
DropNewest  keep existing buffered values and discard the incoming value at capacity
```

Configure bounded behavior with `EventStreamOptions`:

```csharp
var options = new EventStreamOptions
{
    Capacity = 100,
    FullMode = EventStreamFullMode.DropOldest,
};
```

There is deliberately no blocking producer mode: blocking a synchronous event callback can change event semantics and introduce deadlocks.

## Async work back to events

`Task`, `Task<T>`, `ValueTask`, `ValueTask<T>`, and `IAsyncEnumerable<T>` can be exposed through event bridges:

```csharp
using EventBridge<SensorConfiguration> bridge =
    LoadSensorConfigurationAsync().ToEventBridge();

bridge.Completed += (_, e) => Console.WriteLine(e.Value);
bridge.Faulted += (_, e) => Console.Error.WriteLine(e.Exception);
bridge.Cancelled += (_, _) => Console.WriteLine("Cancelled");
bridge.Connect();
```

A `ValueTask<T>` can be bridged directly:

```csharp
using EventBridge<SensorConfiguration> bridge =
    LoadSensorConfigurationValueAsync().ToEventBridge();
```

The bridge takes ownership of observing the supplied `ValueTask`. Do not independently await the same value task after handing it to the bridge unless its producer explicitly supports multiple consumption.

For async streams:

```csharp
await using EventStreamBridge<SensorValue> bridge =
    ReadSensorValuesAsync().ToEventBridge();

bridge.Value += (_, e) => Console.WriteLine(e.Value);
bridge.Connect(cancellationToken);
```

Subscriber exceptions are isolated so one event handler does not stop remaining handlers or bridge processing; failures are written through `Trace.TraceError`.

## Benchmarks

The modern branch includes BenchmarkDotNet benchmarks for the performance-sensitive paths. The project is compiled in CI but benchmarks are run explicitly so normal CI remains deterministic.

```text
dotnet run -c Release --project benchmarks/AsyncEventBridge.Benchmarks -- --filter *
```

Current benchmark coverage starts with:

- one-shot event wait + completion;
- buffered event-stream bursts.

As modern optimizations are introduced, they should be justified with these measurements rather than by assumption.

## Verification

CI on `dotnet-latest`:

- restores and builds the full .NET 10 solution;
- builds the benchmark project;
- runs runtime, generator, race, lifecycle, and stress tests;
- runs the sensor sample;
- produces the NuGet package;
- verifies `lib/net10.0` runtime assets and analyzer contents;
- restores a clean consumer from the generated `.nupkg` and compiles generated APIs;
- executes a separate packaged runtime smoke consumer, including modern event-payload, generated `TimeProvider`, and `ValueTask<T>` paths.

## Branch model

```text
base/netstandard2.0
├── unity
└── dotnet-latest
```

Shared bug fixes should normally land in the base first when they apply to all editions. Modern-only APIs, performance work, and implementation changes belong here.

See [`docs/MODERN_DOTNET.md`](docs/MODERN_DOTNET.md) for the modernization rules and roadmap.

## Build from source

```text
dotnet restore AsyncEventBridge.sln
dotnet build AsyncEventBridge.sln -c Release
dotnet test AsyncEventBridge.sln -c Release --no-build
dotnet pack src/AsyncEventBridge/AsyncEventBridge.csproj -c Release
```

## License

AsyncEventBridge is licensed under the **Mozilla Public License 2.0 (MPL-2.0)**. See [`LICENSE`](LICENSE) for the full license text.
