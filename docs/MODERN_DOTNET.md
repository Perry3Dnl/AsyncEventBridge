# Modern .NET line

`dotnet-latest` is the native modern-.NET edition of AsyncEventBridge.

## Design rules

- Target the current stable .NET runtime directly instead of multi-targeting the compatibility baseline.
- Keep the conceptual bridge model compatible with `base/netstandard2.0`, but allow implementation and API improvements that rely on modern BCL/runtime features.
- Prefer built-in concurrency primitives over compatibility shims when they provide clearer semantics or lower maintenance cost.
- Treat allocation rate, contention, cancellation/timeout behavior, async-stream throughput, and backpressure observability as first-class quality metrics.
- Add modern-only features when they solve a concrete integration, correctness, performance, testability, or operability problem; do not add APIs merely because the runtime makes them possible.
- Shared bug fixes should normally land in `base/netstandard2.0` first when they apply there; modern-only optimizations stay on this branch.

## Core foundation gate

The basic modern edition is considered complete when all of these remain true:

- [x] Runtime targets `net10.0` directly; the Roslyn analyzer stays `netstandard2.0` for compiler-host compatibility.
- [x] The repository has an explicit .NET 10 SDK baseline through `global.json`, deterministic builds, nullable analysis, and warnings-as-errors.
- [x] Event -> `Task` and Event -> `IAsyncEnumerable<T>` work for standard events, modern payloads, strongly typed senders, and supported custom delegate shapes.
- [x] `Task`, `Task<T>`, `ValueTask`, `ValueTask<T>`, and `IAsyncEnumerable<T>` can bridge back to events.
- [x] Cancellation, timeout, cleanup, races, reentrancy, and `TimeProvider` behavior are covered by tests.
- [x] Event streams use `System.Threading.Channels` with explicit `Grow`, `DropOldest`, and `DropNewest` semantics.
- [x] Source generation supports owned and third-party types and reports unsupported event shapes with `AEB001`.
- [x] Public API lock tests protect the intended runtime surface.
- [x] Runtime, generator, lifecycle, race, stress, generated-code, and packaged-consumer tests are part of CI.
- [x] The NuGet package contains the `net10.0` runtime, XML documentation, analyzer, README, icon, license metadata, repository metadata, and a symbol package.
- [x] Linux performs the full package pipeline; Windows and macOS independently restore, build, and test the solution.
- [x] BenchmarkDotNet baselines are kept in the solution and compile in CI before performance work is accepted.

This gate is deliberately narrower than the complete product roadmap. Optional integrations and aggressive optimizations are not required for the modern edition to have a sound core.

## Completed modern capabilities above the baseline

1. `System.Threading.Channels` event-stream buffering.
2. `TimeProvider` timeout scheduling plus deterministic virtual-time tests and generated timeout overloads.
3. Direct `ValueTask` / `ValueTask<T>` event bridges.
4. Modern event payload support, including non-`EventArgs` payloads and `EventHandler<TSender, TPayload>`.
5. Bounded-stream drop observability through thread-safe dropped-event counters and optional observers backed by the channel's real drop callback.
6. BenchmarkDotNet baselines for one-shot waits, stream throughput, and bounded-drop telemetry.

## Work after the foundation

Remaining work is now separate from “basic completeness”:

1. Benchmark-driven optimization of allocations, contention, cancellation, and stream throughput.
2. Additional production integrations only where they materially improve real applications, such as metrics or hosting integration.
3. Additional ergonomic APIs only where they remove recurring consumer boilerplate without duplicating BCL abstractions.
4. Broader examples, documentation, release notes, versioning, and final release preparation.
5. Experimental techniques such as pooling, `IValueTaskSource<T>`, reusable waiters, or generated fast paths only when measurements justify their complexity.

The foundation should remain boring and dependable; experimentation belongs above it, not inside it.
