# Changelog

## 0.4.0 - In progress

- Rename the portable event-stream lossless mode from `Grow` to `Unbounded` and define `Capacity` as bounded-mode-only.
- Align portable wait, stream, and stream-bridge cleanup exception ordering with the shared 0.4 runtime contract.
- Add `EventBridgeOptions` and configurable subscriber exception reporting to the portable Task / async-stream event bridges.
- Preserve subscriber isolation by default with `TraceAndContinue`, with explicit report and ignore policies.
- Keep the Unity portable core byte-for-byte aligned with the .NET Standard compatibility runtime.

## 0.3.0 - In progress

- Move Unity development onto the shared `main` release line.
- Align the UPM package version with the repository-wide `0.3.0` version.
- Make Unity release-readiness part of the same gate as modern .NET and .NET Standard compatibility.
- Preserve Unity-specific APIs and host requirements without requiring feature-for-feature parity with modern .NET.


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
