# Changelog

All notable changes to AsyncEventBridge are documented here.

## Unreleased

### Runtime baseline

- Add a native .NET Standard 2.1 runtime asset while preserving .NET Standard 2.0 as the complete minimum runtime contract.
- Keep `Microsoft.Bcl.AsyncInterfaces` only on the .NET Standard 2.0 asset so modern runtimes and Unity do not need the compatibility dependency.
- Move package versioning to the shared build configuration so the NuGet and Unity packages follow the same version train.

### Unity

- Add the first Unity Package Manager distribution as `com.perry3d.async-event-bridge`, targeting Unity 2023.1+.
- Add Unity-native one-shot event waits that return `Awaitable<T>` and marshal cleanup/completion to the captured Unity main-thread synchronization context.
- Add `MonoBehaviour` lifecycle-aware waits that link caller cancellation with `destroyCancellationToken` and `Application.exitCancellationToken`.
- Bundle a Unity/Roslyn-3.8-compatible source-generator DLL in the UPM package as a `RoslynAnalyzer` asset.
- Add `UnityEvent` / Inspector waits for zero through four arguments with predicates, timeouts, lifecycle cancellation, and persistent-listener-safe runtime subscriptions.
- Add buffered `UnityEvent` async streams with main-thread listener subscription and cleanup.
- Add main-thread publication from `Task`, `Task<T>`, and `IAsyncEnumerable<T>` to UnityEvents for Inspector-driven reactions.
- Add Runtime and Editor Unity Test Framework suites for lifecycle cancellation, main-thread behavior, stream buffering, Task publication, and Inspector persistent-listener preservation.
- Keep the Unity package's vendored core runtime sources byte-for-byte aligned with the matching NuGet core through CI checks.

### Source generator

- Add `[assembly: GenerateAsyncEventsFor(typeof(...))]` for generating async event facades around public types that cannot be annotated directly, including third-party and framework types.
- Add generated support for custom event-handler-shaped delegates that return `void`, have two non-ref parameters, and use an `EventArgs`-derived second parameter.
- Cover common delegates such as `PropertyChangedEventHandler`, `NotifyCollectionChangedEventHandler`, and `ElapsedEventHandler` through the custom delegate adapter path.
- Add `AEB001` warnings for annotated or explicitly targeted events whose delegate shape cannot be generated safely instead of silently skipping them.
- Keep the new generated adapter code compatible with C# 8 and the existing .NET Standard 2.0 runtime baseline.
- Verify assembly-level generation and custom delegate adapters through generator tests, package-only compilation, and packaged runtime smoke tests.

## 0.1.0

First release.

### Runtime baseline

- Targets .NET Standard 2.0 as the complete minimum runtime contract.
- Uses `Microsoft.Bcl.AsyncInterfaces` for async-stream compatibility on the baseline target.
- Keeps the public runtime behavior consistent with the baseline rather than shipping a reduced compatibility build.

### Event -> async

- Generate one-shot `<EventName>Async(...)` methods for `EventHandler` and `EventHandler<TEventArgs>` events.
- Support filtering, cancellation, and timeout overloads.
- Generate repeated `<EventName>Stream(...)` methods returning `IAsyncEnumerable<T>`.
- Support `Grow`, `DropOldest`, and `DropNewest` event-stream buffering modes.
- Handle cancellation, reentrancy, cleanup, subscription failures, and terminal races.

### Async -> events

- Bridge `Task` to `EventBridge`.
- Bridge `Task<T>` to `EventBridge<T>`.
- Bridge `IAsyncEnumerable<T>` to `EventStreamBridge<T>`.
- Provide explicit `Connect()` lifecycle control.
- Publish `Completed`, `Faulted`, and `Cancelled` terminal events.
- Publish async-stream values through `Value` in enumeration order.
- Isolate subscriber exceptions so one handler does not stop other subscribers or bridge processing; exceptions are written through `Trace.TraceError` rather than propagated.

### Source generator

- Included in the same `AsyncEventBridge` NuGet package as the runtime.
- Supports public inherited events, generic source types, accessible nested types, and generic constraints.
- Preserves source accessibility and normal C# member-hiding behavior.
- Keeps generated source compatible with C# 8 syntax.
- Handles generated class-name collisions and source instance-method collisions.
- v0.1.0 generation is limited to annotatable classes and `EventHandler` / `EventHandler<TEventArgs>` events where `TEventArgs : EventArgs`.
- Custom event delegate generation and diagnostics for unsupported event delegate types are not part of v0.1.0.

### Packaging and license

- Ships the runtime and source generator in one NuGet package.
- Licensed under the Mozilla Public License 2.0 (`MPL-2.0`).
- Declares `MPL-2.0` in NuGet package metadata and verifies that expression in CI.

### Verification

- Public API lock tests protect the intended exported surface.
- Runtime, generator, race, lifecycle, and stress tests are included.
- A dedicated .NET Standard 2.0 / C# 8 compatibility consumer is built in CI.
- CI creates the NuGet package and validates its contents.
- Separate consumers restore from the generated `.nupkg`, compile generated APIs, and execute packaged runtime smoke tests.
- The `samples/SensorMonitoring` example is built and executed in CI.
