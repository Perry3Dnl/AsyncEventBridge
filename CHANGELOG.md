# Changelog

All notable changes across the supported AsyncEventBridge release tracks are documented here.

## Unreleased

### 1.0 stabilization

- Start the long-term 1.0 stabilization line with a feature-freeze rule: new public surface must close a concrete interop, safety, compatibility, or broad-adoption gap.
- Change the boolean `EventCondition.WaitUntilAsync(...)` convenience overload from `Task<bool>` to `Task`; successful completion already means the condition became true, while the generic state overload continues to return the matching state snapshot.
- Reserve `EventStreamLifecycleEventKind.Unspecified = 0` and move real lifecycle markers to non-zero values so `default(EventStreamLifecycleEvent<T>)` cannot masquerade as an activation.
- Rename `EventStreamFullMode.DropNewest` to `DropWrite` before the 1.0 freeze. The behavior already matches `System.Threading.Channels.BoundedChannelFullMode.DropWrite`: the incoming value is discarded at capacity. This keeps the name aligned with the BCL and leaves room for the distinct Channels `DropNewest` behavior to be added later without ambiguity.
- Rename the bounded-drop metric tag from `drop_newest` to `drop_write` to match the public buffering contract.
- Make unified-package generator selection forward-compatible so TFMs compatible with `net10.0` use the modern generator rather than requiring an exact `net10.0` target.
- Strengthen public-API locks, including constructor coverage on modern .NET and exact method/event/property signatures on the .NET Standard compatibility line.

### 0.5 async interoperability

- Start the 0.5 development line from the frozen 0.4 release candidate.
- Add `EventStreamComposition.TakeUntil(...)` for lifecycle-safe coordination between an async event stream and a cancellable asynchronous stop wait.
- Add `EventStreamComposition.StartAfter(...)` to defer source enumeration/subscription until a cancellable activation wait completes successfully.
- Make `StartAfter(...).TakeUntil(...)` a composable inactive/active/stopped lifecycle: a stop that wins before activation cancels the pending start and never subscribes the source.
- Add `EventStreamComposition.RepeatBetween(...)` for reconnecting/re-entering lifecycles that continuously rearm activation after each deactivation or natural source completion.
- Create a fresh source enumeration for every repeated active cycle; treat successful stop-before-start as an inactive-cycle boundary rather than terminal completion.
- Keep repeated lifecycle faults and external cancellation terminal while preserving deterministic per-cycle cleanup.
- Add `EventStreamComposition.RepeatBetweenWithLifecycle(...)` plus `EventStreamLifecycleEvent<T>` / `EventStreamLifecycleEventKind` so reconnecting workflows can observe activation, values, deactivation, natural source completion, and one-based cycle identity.
- Distinguish `Deactivated` from `SourceCompleted`; keep stop-before-start silent and keep faults/cancellation as terminal outcomes rather than marker values.
- Add `EventCondition.WaitUntilAsync(...)` for APIs that expose both current state and state-change events, using subscribe-before-check ordering to avoid missed transitions and to handle already-satisfied state safely.
- Re-arm state-change waits after spurious notifications and deterministically cancel/observe the temporary wait when the predicate is already satisfied or state evaluation fails.
- Add boolean `EventCondition.WaitUntilAsync(...)` convenience overloads for `IsConnected`/`IsReady`-style state.
- Add `EventStreamComposition.RepeatWhile(...)` and `RepeatWhileWithLifecycle(...)` to combine current state, state-change events, repeated activation/deactivation, and fresh per-cycle source subscriptions behind one natural call shape.
- Keep inactive state genuinely unsubscribed through the state-driven composition path while preserving the existing public `TakeUntil` source/disposal contract.
- Arm state-driven lifecycle source/stop coordination before exposing `Activated`, preventing event loss while lifecycle markers are handled.
- Create the stop wait per enumeration and share a coordination token with the source enumerator.
- Cancel, observe, and dispose the losing side before the composed sequence reports completion.
- Preserve the 0.4 primary-outcome-first cleanup policy when the source, stop wait, cancellation callbacks, or enumerator disposal fail.
- Define stop completion as the winner when both a source move and the stop wait are already complete at the observed move boundary.
- Add runtime coverage for normal stop completion, source completion, stop faults, external cancellation, deterministic boundary behavior, and combined source/cleanup failures.
- Port the same `StartAfter` and `TakeUntil` contracts to the .NET Standard 2.0 and Unity portable core and lock the compatibility public surface.
- Exercise `StartAfter` through packaged modern and compatibility runtime consumers in addition to direct runtime tests.
- Document the 0.5 interoperability direction and explicitly keep general async-LINQ/Rx functionality out of scope.

### 0.4 generator architecture

- Consolidate inherited-event discovery, accessibility, event-shape classification, type rendering, and generic constraints into shared generator infrastructure.
- Introduce a normalized `EventGenerationModel` consumed by all modern generator emission paths.
- Split generation into dedicated wait, stream, and occurrence emitters instead of maintaining independent `StringBuilder` pipelines in each generator entry point.
- Remove the private `EventGenerationInfo`, `EventInfo`, `EventClassification`, and `EventKind` families that duplicated the normalized event model.
- Add cross-generator behavioral parity coverage for standard, custom, unsupported, nested-generic, nullable, keyword-identifier, hidden-inherited-event, and annotated-base scenarios.
- Preserve the generated public API while reducing the three generator entry-point files from roughly 87 KB combined to roughly 23.5 KB combined.
- Move the shared repository and Unity UPM development version to `0.4.0`.
- Document the 0.4 generator pipeline in `docs/0.4-generator-architecture.md`.
- Define consistent cleanup-exception ordering across waits, streams, occurrence APIs, and stream bridges: preserve the primary outcome first and aggregate cleanup failures after it.
- Ensure event-stream unsubscribe failures cannot replace an existing predicate, cancellation, subscription, or channel failure.
- Explicitly drive stream-bridge enumerators so source failures and enumerator-disposal failures can both be reported.
- Make `EventStreamBridge.DisposeAsync()` surface asynchronous enumerator cleanup failures while synchronous disposal remains non-blocking.
- Port the cleanup contract to the .NET Standard 2.0 and Unity portable runtime sources and add compatibility regression coverage.
- Document the cleanup contract in `docs/0.4-cleanup-semantics.md`.
- Rename the lossless stream mode from `Grow` to `EventStreamFullMode.Unbounded` while retaining numeric value `0`; this is a deliberate pre-1.0 source-breaking cleanup that avoids a permanent duplicate enum alias.
- Define `Capacity` as a bounded-mode setting only: it is ignored by `Unbounded` and validated only for `DropOldest` / `DropNewest`.
- Normalize unbounded buffering semantics across modern .NET, .NET Standard 2.0, and Unity instead of using `Capacity` as an allocation hint only on compatibility runtimes.
- Keep the unbounded default to avoid silent event loss; document the operational memory-growth tradeoff and explicit bounded alternatives in `docs/0.4-stream-buffering.md`.
- Add `EventBridgeOptions` and `EventBridgeSubscriberExceptionPolicy` so async-to-event bridges can explicitly trace, report, or ignore subscriber failures while always continuing remaining subscribers.
- Preserve `TraceAndContinue` as the default subscriber behavior; add `ReportAndContinue` with a required observer callback and `IgnoreAndContinue` for deliberate silent isolation.
- Snapshot subscriber policy options when a bridge is created, and isolate/report observer failures without destabilizing bridge processing.
- Deliberately omit a subscriber-exception propagation mode because bridge publication is async-driven and lacks a reliable synchronous caller; document the contract in `docs/0.4-subscriber-exceptions.md`.
- Lock bridge publication to subscriber-snapshot semantics: add/remove/dispose during an in-flight publication affects future publication but does not rewrite the captured invocation list.
- Clarify `Dispose()` as a non-waiting suppression boundary and `EventStreamBridge.DisposeAsync()` as the completion boundary after which no bridge handlers remain in flight.
- Add deterministic lifecycle coverage for terminal publication mutation, value-publication mutation, disposal from handlers, late subscribers, and in-flight terminal disposal.
- Lock exactly-once stream terminal behavior when source completion and cancellation occur on opposite sides of the terminal-publication boundary.
- Remove thread-pool scheduling dependence from the two timing-sensitive disposal tests that had intermittently failed on Windows convergence runners.
- Run the blocked terminal-snapshot subscriber test on a dedicated long-running worker and avoid exact runtime `Task` implementation assertions, keeping lifecycle tests deterministic across .NET 8/10 and Linux/macOS/Windows runners.
- Document the bridge lifecycle contract in `docs/0.4-bridge-lifecycle.md`.

### 0.3 convergence

- Move the modern .NET, .NET Standard 2.0 compatibility, and Unity product sources onto one release line.
- Set the shared repository and Unity UPM version to `0.3.0`.
- Keep runtime-specific feature sets where platform capabilities differ; 0.3 targets release-readiness parity rather than artificial feature parity.
- Treat modern .NET, compatibility, Unity, source generators, package verification, documentation, and CI as one release gate.
- Preserve the historical split branches as migration/reference points instead of independent version lines.


### Native .NET runtime

- Move the runtime, tests, stress tests, sample, and package consumers to `net10.0`.
- Remove the `Microsoft.Bcl.AsyncInterfaces` compatibility dependency from the modern runtime.
- Keep the Roslyn source generator on `netstandard2.0` so it remains broadly compatible with compiler hosts.
- Remove the .NET Standard compatibility consumer from the modern solution; compatibility remains the responsibility of `base/netstandard2.0`.
- Remove the `EventArgs` generic constraint from modern one-shot event waits and event streams so ordinary value, record, struct, DTO, and reference payloads can cross the event-to-async boundary.
- Emit built-in `System.Diagnostics.Metrics` counters from the `AsyncEventBridge` meter for terminal wait outcomes and bounded-stream drops without taking a logging or OpenTelemetry dependency.
- Keep built-in metric tags bounded and low-cardinality: wait outcome (`success`, `cancelled`, `timeout`, `faulted`) and stream full mode (`drop_oldest`, `drop_newest`).

### Modern event waits

- Add `EventOccurrence<TSender, TPayload>` plus low-level and generated sender-aware wait/stream facades for code where sender identity is part of the event semantics. Existing payload-only generated APIs remain unchanged.
- Add heterogeneous two-wait `EventComposition.WaitAnyAsync` with deterministic loser cancellation/observation so racing event waits do not leak losing subscriptions.
- Add heterogeneous two-wait `EventComposition.WaitAllAsync`, returning `EventWaitAllResult<TFirst, TSecond>` while cancelling and observing the still-pending sibling if either wait faults or is cancelled.
- Add indexed N-way homogeneous `WaitAnyAsync` returning `EventWaitAnyResult<T>` with a zero-based winner index and value; all losing waits are cancelled and observed before the method returns.
- Add indexed N-way homogeneous `WaitAllAsync` returning results in input order, with fail-fast cancellation/observation of remaining waits after the first observed fault or cancellation.
- Make composition startup transactional: if a later wait factory throws or returns `null`, already-started waits are cancelled and observed before the startup failure is rethrown.
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
- Add a low-level-versus-generated event-wait matrix covering successful completion, cancellation, and timeout with allocation measurements.
- Split the common no-predicate/non-cancellable/no-finite-timeout event wait onto a lean internal state and allocate predicate synchronization only when required. The measured successful low-level wait fell from 536 B to 440 B per operation and the generated wait from 560 B to 464 B; cancellation and timeout paths each dropped 24 B. Hosted-runner timing was not used for a cross-run latency claim.
- Add bounded `DropNewest` benchmark baselines for counter-only and counter-plus-observer telemetry paths.
- Add direct-`ValueTask` versus `.AsTask()` bridge benchmarks; a focused .NET 10 run reduced allocation for a completed generic bridge from 296 B to 224 B per operation (72 B), while timing remained close enough on the hosted runner that no general latency claim is made.
- Add `MeterListener` regression tests for exported wait-outcome and bounded-drop measurements.
- Benchmark built-in metrics with collection disabled and enabled. Wait completion remained 440 B in both cases; the bounded-drop burst remained 2,288 B in both cases, with about 0.8% timing overhead for active collection in that focused run.
- Build the benchmark project in normal CI without executing benchmarks on every push.
- Require the generated NuGet package to contain native `net10.0` assets.
- Restore, compile, and execute isolated `net10.0` consumers from the generated `.nupkg` in CI.
- Exercise generated `EventHandler<int>` and `EventHandler<TSender, int>` APIs through the packaged runtime smoke test, including their generated `TimeProvider` timeout overloads.
- Exercise sender-aware occurrence waits/streams and the heterogeneous/indexed event-composition APIs through both the packaged runtime consumer and the Native AOT package consumer, including post-composition handler-count checks for leaked subscriptions.
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
