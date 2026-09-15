# Changelog

## 0.2.0 - Unreleased

- Add the first Unity Package Manager distribution.
- Target Unity 2023.1.0f1+ so Unity-facing APIs can use `Awaitable` directly.
- Add `unityRelease` metadata and CI validation for the Asset Store-required manifest fields.
- Preserve `.meta` files for the importable Interactive Dialogue sample so sample asset identities remain stable.
- Add Unity-native waiting for `EventHandler` and `EventHandler<TEventArgs>` events.
- Add `MonoBehaviour` lifecycle-aware cancellation that links destroy, application-exit, and caller cancellation tokens.
- Bundle the Unity-compatible source generator as a `RoslynAnalyzer` asset so the UPM package is self-contained.
- Add generated `Awaitable` facades for annotated and assembly-targeted CLR events, including supported custom delegates and `AEB001` diagnostics.
- Add rich `UnityEvent` / Inspector integration for arities zero through four with timeout, predicate, lifecycle cancellation, and buffered async-stream adapters.
- Add main-thread `Task`, `Task<T>`, and `IAsyncEnumerable<T>` publication to UnityEvents for Inspector-driven reactions.
- Add Runtime and Editor Unity Test Framework suites covering lifecycle, main-thread, buffering, and persistent-listener behavior.
- Add an importable Interactive Dialogue + Live Code sample scene that highlights the active `WaitAsync` line while a real `UnityEvent` drives the conversation.
- Keep the Unity package version synchronized with the NuGet package version.
