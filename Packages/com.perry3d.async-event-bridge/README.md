# AsyncEventBridge for Unity

Unity-focused distribution of AsyncEventBridge. It follows the same version number as the .NET/NuGet package but is packaged for Unity Package Manager and adds Unity-specific runtime behavior.

## Baseline

- Unity 2023.1 or newer.
- Uses Unity `Awaitable` for Unity-facing one-shot waits.
- Keeps the core AsyncEventBridge runtime source aligned with the matching NuGet version.
- Designed for Mono and IL2CPP; runtime code does not require reflection or runtime code generation.

## Current Unity API

`AsyncEventBridge.Unity.UnityEventAwaiter` waits for normal C# `EventHandler` / `EventHandler<TEventArgs>` events using `Awaitable<T>` instead of `Task<T>`.

The overloads that take a `MonoBehaviour` automatically link cancellation to both `MonoBehaviour.destroyCancellationToken` and `Application.exitCancellationToken`, in addition to any caller token.

```csharp
using AsyncEventBridge.Unity;
using UnityEngine;

public sealed class PlayerView : MonoBehaviour
{
    private async Awaitable Start()
    {
        var args = await UnityEventAwaiter.WaitAsync(
            this,
            handler => player.Ready += handler,
            handler => player.Ready -= handler);

        // Resumes on the Unity main thread.
    }
}
```

Unity's own `UnityEvent` is already awaitable in Unity 2023.1+, so this package does not wrap it merely to duplicate built-in behavior.

## Planned before 0.2.0 is release-ready

- Unity-targeted source-generator build and generated `Awaitable` facades.
- Unity-specific async-to-event bridge that guarantees main-thread publication.
- Unity Test Framework coverage, including lifecycle cancellation and IL2CPP-oriented smoke coverage.
- CI verification that the vendored core runtime files are byte-for-byte identical to the matching core sources.
