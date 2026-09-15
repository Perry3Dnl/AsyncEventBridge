# Changelog

All notable changes to the native modern-.NET line of AsyncEventBridge are documented here.

## Unreleased

### Native .NET runtime

- Move the runtime, tests, stress tests, sample, and package consumers to `net10.0`.
- Remove the `Microsoft.Bcl.AsyncInterfaces` compatibility dependency from the modern runtime.
- Keep the Roslyn source generator on `netstandard2.0` so it remains broadly compatible with compiler hosts.
- Remove the .NET Standard compatibility consumer from the modern solution; compatibility remains the responsibility of `base/netstandard2.0`.
- Remove the `EventArgs` generic constraint from modern one-shot event waits and event streams so ordinary value, record, struct, DTO, and reference payloads can cross the event-to-async boundary.

### Modern event waits

- Add an optional `TimeProvider` to `EventAwaiter.WaitAsync(...)` so timeout behavior can use virtual/test time.
- Replace the runtime-specific timeout scheduler abstraction with `TimeProvider.CreateTimer(...)`.
- Use `CancellationToken.UnsafeRegister(...)` for the internal cancellation callback to avoid unnecessary execution-context capture on the hot wait path.
- Preserve timeout, cancellation, event, subscription, cleanup, and race semantics through the existing test suite plus virtual-time tests.
- Make completion-state storage valid for both reference and value-type event payloads.
- Surface optional `TimeProvider` parameters from generated timeout overloads and forward them to the runtime, so generated APIs can participate in deterministic/virtual-time tests directly.

### Modern event streams

- Replace the compatibility queue / signal / cancellation implementation with `System.Threading.Channels`.
- Preserve `Grow`, `DropOldest`, and `DropNewest` public buffering semantics; `DropNewest` maps to the channel `DropWrite` behavior so the incoming event is discarded at capacity.
- Keep subscription, predicate-fault, cancellation, ordering, reentrancy, and cleanup behavior covered by runtime and stress tests.
- Support non-`EventArgs` payloads in the generic stream runtime.
- Add thread-safe `EventStreamOptions.DroppedCount` observability for bounded streams, backed by the channel's actual dropped-item callback rather than inferred write outcomes.
- Add `EventStreamOptions.DropObserver` for immediate drop telemetry; observers receive the updated lifetime drop count, run on the producer thread, and cannot fault the stream if they throw.
- Define drop counters as lifetime aggregates for the `EventStreamOptions` instance, so reusing one options object can intentionally aggregate telemetry across multiple stream enumerations.

### Async -> events

- Add `ValueTask` and `ValueTask<T>` `ToEventBridge()` overloads for modern async APIs that do not naturally return `Task`.
- Define explicit ownership semantics for `ValueTask`: once handed to a bridge, the original value task must not be consumed independently.
- Verify `ValueTask<T>` behavior through runtime tests and a consumer restored from the generated NuGet package.
- Consume `ValueTask` and `ValueTask<T>` directly inside event bridges instead of converting through `.AsTask()`, preserving completion, fault, and cancellation classification while avoiding the intermediate task wrapper.
- Classify `OperationCanceledException` from an `IAsyncEnumerable<T>` as `Cancelled` only when the bridge lifetime token is actually cancelled; an unrelated source-thrown cancellation exception now publishes `Faulted` instead of being misreported as bridge cancellation.

### Performance and verification

- Add a BenchmarkDotNet project for one-shot wait and buffered event-stream benchmarks.
- Add bounded `DropNewest` benchmark baselines for counter-only and counter-plus-observer telemetry paths.
- Add direct-`ValueTask` versus `.AsTask()` bridge benchmarks; a focused .NET 10 run reduced allocation for a completed generic bridge from 296 B to 224 B per operation (72 B), while timing remained close enough on the hosted runner that no general latency claim is made.
- Build the benchmark project in normal CI without executing benchmarks on every push.
- Require the generated NuGet package to contain native `net10.0` assets.
- Restore, compile, and execute isolated `net10.0` consumers from the generated `.nupkg` in CI.
- Exercise generated `EventHandler<int>` and `EventHandler<TSender, int>` APIs through the packaged runtime smoke test, including their generated `TimeProvider` timeout overloads.
- Exercise bounded generated streams from the packaged runtime consumer and verify both drop counts and observer callbacks without changing retained event ordering.
- Pin the modern SDK baseline with `global.json` while allowing compatible .NET 10 feature-band roll-forward.
- Produce and validate NuGet symbol packages and repository/source metadata in CI.
- Restore, build, and test the full modern solution on Linux, Windows, and macOS.
- Strengthen public API lock tests to cover method return types, generic arity, parameter order/types/optionality, event handler types, and property types/accessors instead of protecting names alone.

### Source generator

- Retain `[assembly: GenerateAsyncEventsFor(typeof(...))]`, custom event-handler-shaped delegate support, and `AEB001` diagnostics from the stable base.
- Generate async waits and streams for modern `EventHandler<TPayload>` events where `TPayload` does not need to derive from `EventArgs`.
- Generate adapters for .NET 10 `EventHandler<TSender, TPayload>` events while keeping AsyncEventBridge's async contract payload-centric: the second event parameter becomes the task/stream result.
- Allow custom two-parameter `void` delegates whose second parameter is a normal non-ref-like payload type.
- Reject ref-like payloads such as `Span<T>` with `AEB001`, because they cannot safely escape an event callback into `Task<T>` or `IAsyncEnumerable<T>`.
- Reject generic payload type parameters that use `allows ref struct`, since they may legally become ref-like and therefore cannot safely cross the async lifetime boundary.
- Preserve `allows ref struct` anti-constraints on generic source-type parameters when the generated facade itself does not expose that parameter as an async payload.
- Move the generator's Roslyn dependency to 4.12 so ref-like anti-constraint metadata can be inspected without unnecessarily adopting newer compiler APIs.
- Add `TimeProvider` to generated timeout overloads without changing the simple no-timeout call shape.
- Continue packaging the source generator with the runtime package.

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
