# Changelog

## 0.2.0 - Unreleased

- Add the first Unity Package Manager distribution.
- Target Unity 2023.1+ so Unity-facing APIs can use `Awaitable` directly.
- Add Unity-native waiting for `EventHandler` and `EventHandler<TEventArgs>` events.
- Add `MonoBehaviour` lifecycle-aware cancellation that links destroy, application-exit, and caller cancellation tokens.
- Keep the Unity package version synchronized with the NuGet package version.
