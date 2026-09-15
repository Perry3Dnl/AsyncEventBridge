# AsyncEventBridge Unity integration

The Unity package keeps the cross-platform AsyncEventBridge runtime behavior available while adding Unity-native async surfaces.

## Threading contract

`UnityEventAwaiter` must be started from the Unity main thread. Event completion may originate on another thread, but cleanup and `Awaitable` completion are marshalled back through the captured Unity synchronization context. This makes it safe for continuations to use Unity APIs.

## Lifecycle cancellation

Prefer the overloads that accept a `MonoBehaviour` for scene- or component-scoped work. They cache the behaviour's `destroyCancellationToken` before awaiting and link it with `Application.exitCancellationToken` plus any caller token.

## Core/runtime relationship

The files under `Runtime/Core` mirror the matching core runtime sources used by the NuGet package. CI is responsible for preventing drift between those copies. Generic fixes belong in the core first; Unity-only behavior belongs under `Runtime/Unity`.
