# AsyncEventBridge for Unity

Unity-focused distribution of AsyncEventBridge. It follows the same version number as the .NET/NuGet package, is packaged for Unity Package Manager, and adds Unity-native `Awaitable`, lifecycle, main-thread, `UnityEvent`, and Inspector integration.

## Baseline

- Unity 2023.1 or newer.
- Uses Unity `Awaitable` for Unity-facing one-shot operations.
- Keeps the portable AsyncEventBridge runtime source aligned with the matching NuGet version.
- Designed for Mono and IL2CPP; runtime code does not require reflection or runtime code generation.
- Bundles a Unity-compatible Roslyn source-generator build so generated APIs work when the UPM package is installed by itself.

## CLR events: generated Awaitable APIs

Annotate a type you own:

```csharp
using AsyncEventBridge;

[GenerateAsyncEvents]
public sealed class Sensor
{
    public event EventHandler<ReadingEventArgs>? Reading;
}
```

The Unity generator emits `Awaitable<T>` facades in `AsyncEventBridge.Unity`. Owner overloads automatically link caller cancellation with `MonoBehaviour.destroyCancellationToken` and `Application.exitCancellationToken`:

```csharp
using AsyncEventBridge.Unity;
using UnityEngine;

public sealed class SensorView : MonoBehaviour
{
    public Sensor Sensor = null!;

    private async Awaitable Start()
    {
        var reading = await Sensor.ReadingAsync(this, args => args.Value > 10);
        Debug.Log(reading.Value);
    }
}
```

For third-party or framework types that you cannot annotate, use the assembly-level target:

```csharp
[assembly: GenerateAsyncEventsFor(typeof(ThirdPartySensor))]
```

The generator also supports event-handler-shaped custom delegates and reports `AEB001` when an explicitly targeted event cannot be adapted safely.

## Inspector / UnityEvent integration

Unity already lets a `UnityEvent` be awaited directly. AsyncEventBridge builds on that instead of replacing it: `WaitAsync` adds timeout, predicate, caller cancellation, owner-destruction cancellation, and application-exit cancellation while preserving Inspector-configured persistent listeners.

```csharp
using AsyncEventBridge.Unity;
using UnityEngine;
using UnityEngine.Events;

public sealed class ConfirmationPanel : MonoBehaviour
{
    [SerializeField] private UnityEvent<int> confirmed = new();

    private async Awaitable Start()
    {
        var value = await confirmed.WaitAsync(
            this,
            value => value >= 10,
            timeout: TimeSpan.FromSeconds(30));

        Debug.Log($"Confirmed: {value}");
    }
}
```

`UnityEvent`, `UnityEvent<T0>`, `UnityEvent<T0,T1>`, `UnityEvent<T0,T1,T2>`, and `UnityEvent<T0,T1,T2,T3>` are supported. Multi-argument waits return value tuples.

### Buffered UnityEvent streams

`AsAsyncEnumerable(...)` turns repeated Inspector/runtime UnityEvents into buffered async streams without the event-loss window caused by repeatedly awaiting one event at a time. Listener subscription and cleanup are marshalled through Unity's main-thread synchronization context.

```csharp
await foreach (var score in scoreChanged.AsAsyncEnumerable(this, score => score >= 0))
{
    Debug.Log(score);
}
```

The normal `EventStreamOptions` buffer modes (`Grow`, `DropOldest`, and `DropNewest`) apply.

## Async work -> Inspector events

`UnityAsyncBridge` publishes `Task`, `Task<T>`, and `IAsyncEnumerable<T>` outcomes through `UnityEvent` instances on Unity's main thread. This is useful when designers should wire the reaction in the Inspector while the producer stays asynchronous C# code.

```csharp
[SerializeField] private UnityEvent<int> loaded = new();
[SerializeField] private UnityEvent<string> failed = new();
[SerializeField] private UnityEvent cancelled = new();

private async Awaitable Start()
{
    await UnityAsyncBridge.PublishAsync(
        LoadScoreAsync(),
        this,
        loaded,
        failed,
        cancelled);
}
```

The owner overload suppresses publication after the owning `MonoBehaviour` is destroyed or the application exits. Task cancellation can be published through the cancellation UnityEvent; faults are published as strings so they are straightforward to bind in the Inspector.

## Unity package tests

The package contains both `Tests/Runtime` and `Tests/Editor` Unity Test Framework assemblies. Coverage includes:

- predicate and timeout behavior for UnityEvent waits;
- owner-destruction cancellation;
- main-thread completion/publication;
- buffered UnityEvent streaming;
- preserving Inspector persistent listeners;
- Task-to-UnityEvent publication.

For a Git/registry dependency, add `com.perry3d.async-event-bridge` to the consuming project's `testables` list when you want Unity Test Runner to expose the package tests. Embedded packages are testable automatically.

## Package layout

- `Runtime/Core`: matching portable AsyncEventBridge runtime sources.
- `Runtime/Unity`: Unity-specific Awaitable, UnityEvent, stream, and publication integration.
- `Analyzers`: prebuilt Unity-compatible source generator, imported with the `RoslynAnalyzer` label and disabled as a normal plugin.
- `Tests/Runtime` and `Tests/Editor`: Unity Test Framework coverage.
- `Documentation~`: package documentation ignored by Unity asset import.
