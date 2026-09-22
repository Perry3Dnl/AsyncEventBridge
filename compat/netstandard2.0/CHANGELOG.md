# Changelog

All notable changes to the .NET Standard 2.0 baseline of AsyncEventBridge are documented here.

## Unreleased

### 0.5 async interoperability

- Add `EventStreamComposition.TakeUntil(...)` to the .NET Standard 2.0 runtime.
- Add `EventStreamComposition.StartAfter(...)` so portable event streams can defer subscription until activation succeeds.
- Support composing `StartAfter(...).TakeUntil(...)` into an inactive/active/stopped lifecycle where stop-before-start never subscribes the source.
- Add portable `RepeatBetween(...)` support for reconnecting lifecycles with fresh per-cycle source enumeration and deterministic cleanup.
- Add portable `RepeatBetweenWithLifecycle(...)`, `EventStreamLifecycleEvent<T>`, and lifecycle event kinds for observing activation/value/deactivation/source-completion boundaries with cycle identity.
- Add portable `EventCondition.WaitUntilAsync(...)` with subscribe-before-check semantics for already-active state and missed-transition-safe state changes.
- Add boolean state-condition convenience plus portable `RepeatWhile(...)` / `RepeatWhileWithLifecycle(...)` state-driven lifecycle composition.
- Avoid creating the source enumerator when a stop condition is already satisfied, keeping inactive event-backed sources genuinely unsubscribed.
- Coordinate source enumeration and the stop wait through a shared cancellation lifetime.
- Cancel, observe, and dispose the losing side before reporting completion.
- Preserve primary source/stop failures ahead of cleanup failures.
- Keep the implementation C# 9-compatible so Unity can share the same portable source.

### 0.4 behavioral hardening

- Rename `EventStreamFullMode.Grow` to `Unbounded` before the 1.0 API freeze while retaining numeric value `0`.
- Treat `Capacity` as a bounded-mode-only setting and ignore it for unbounded streams.
- Preserve primary wait/stream outcomes when cleanup also fails and aggregate cleanup failures after the primary outcome.
- Add `EventBridgeOptions` with trace, report, and ignore subscriber-exception policies that always continue remaining subscribers.
- Snapshot bridge options at creation so live bridge behavior cannot be changed by later mutation of a shared options object.

### Runtime baseline

- Keep .NET Standard 2.0 as the complete portable runtime contract.
- Keep `Microsoft.Bcl.AsyncInterfaces` on the baseline target for async-stream compatibility.
- Centralize package versioning in the shared build configuration.
- Keep the runtime source compatible with the C# language level required by downstream compatibility targets.

### Source generator

- Add `[assembly: GenerateAsyncEventsFor(typeof(...))]` for generating async event facades around public types that cannot be annotated directly, including third-party and framework types.
- Add generated support for custom event-handler-shaped delegates that return `void`, have two non-ref parameters, and use an `EventArgs`-derived second parameter.
- Cover common delegates such as `PropertyChangedEventHandler`, `NotifyCollectionChangedEventHandler`, and `ElapsedEventHandler` through the custom delegate adapter path.
- Add `AEB001` warnings for annotated or explicitly targeted events whose delegate shape cannot be generated safely instead of silently skipping them.
- Keep generated adapter code compatible with the .NET Standard 2.0 baseline.
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
