# Modern .NET line

`main` is the primary native modern-.NET edition of AsyncEventBridge. `dotnet-latest` remains the modern development/integration line when changes need to stage before `main`.

## Design rules

- Target the current stable .NET runtime directly instead of multi-targeting the compatibility baseline.
- Keep the conceptual bridge model compatible with `base/netstandard2.0`, but allow implementation and API improvements that rely on modern BCL/runtime features.
- Prefer built-in concurrency primitives over compatibility shims when they provide clearer semantics or lower maintenance cost.
- Treat allocation rate, contention, cancellation/timeout behavior, async-stream throughput, and backpressure observability as first-class quality metrics.
- Add modern-only features when they solve a concrete integration, correctness, performance, testability, or operability problem; do not add APIs merely because the runtime makes them possible.
- Shared bug fixes should normally land in `base/netstandard2.0` first when they apply there; modern-only optimizations stay on the modern line.

## Core foundation gate

The basic modern edition is considered complete when all of these remain true:

- [x] Runtime targets `net10.0` directly; the Roslyn analyzer stays `netstandard2.0` for compiler-host compatibility.
- [x] The repository has an explicit .NET 10 SDK baseline through `global.json`, deterministic builds, nullable analysis, and warnings-as-errors.
- [x] Event -> `Task` and Event -> `IAsyncEnumerable<T>` work for standard events, modern payloads, strongly typed senders, and supported custom delegate shapes.
- [x] `Task`, `Task<T>`, `ValueTask`, `ValueTask<T>`, and `IAsyncEnumerable<T>` can bridge back to events.
- [x] Cancellation, timeout, cleanup, races, reentrancy, and `TimeProvider` behavior are covered by tests.
- [x] Event streams use `System.Threading.Channels` with explicit `Grow`, `DropOldest`, and `DropNewest` semantics.
- [x] Source generation supports owned and third-party types and reports unsupported event shapes and invalid/redundant generation requests with `AEB001`-`AEB003` diagnostics.
- [x] Public API lock tests protect the intended runtime surface.
- [x] Runtime, generator, lifecycle, race, stress, generated-code, and packaged-consumer tests are part of CI.
- [x] The NuGet package contains the `net10.0` runtime, XML documentation, analyzer, README, icon, license metadata, repository metadata, and a symbol package.
- [x] Linux performs the full package pipeline; Windows and macOS independently restore, build, and test the solution.
- [x] BenchmarkDotNet baselines are kept in the solution and compile in CI before performance work is accepted.
- [x] The packed package is published and executed as a Native AOT smoke consumer with trimming/AOT warnings treated as failures.

## Completed modern capabilities above the baseline

1. `System.Threading.Channels` event-stream buffering.
2. `TimeProvider` timeout scheduling plus deterministic virtual-time tests and generated timeout overloads.
3. Direct `ValueTask` / `ValueTask<T>` event bridges.
4. Modern event payload support, including non-`EventArgs` payloads and `EventHandler<TSender, TPayload>`.
5. Bounded-stream drop observability through thread-safe counters and optional observers backed by the channel's real drop callback.
6. BenchmarkDotNet baselines plus benchmark-driven allocation reductions on one-shot waits and `ValueTask` bridges.
7. Native AOT and trimming verification against the packed NuGet package, including execution of the native smoke-test binary.
8. Generator diagnostics: `AEB001` for unsupported event delegates, `AEB002` for invalid `GenerateAsyncEventsFor` targets, and `AEB003` for duplicate or redundant generation requests.
9. Sender-aware occurrence waits/streams through `EventOccurrence<TSender, TPayload>`.
10. Lifecycle-safe event-wait composition: heterogeneous two-way any/all and indexed N-way homogeneous any/all with deterministic sibling cancellation and cleanup observation.
11. BCL-native `System.Diagnostics.Metrics` instrumentation for terminal wait outcomes and bounded-stream drops with low-cardinality tags.
12. Release-grade package metadata, symbols, packaged-consumer verification, and Linux/Windows/macOS CI.

## Post-0.2 work

Work after the `0.2.0` hardening pass is deliberately separated from basic product readiness:

1. Benchmark-driven optimization of allocations, contention, composition overhead, and stream throughput where measurements show material wins.
2. Additional production integrations only where they materially improve real applications rather than duplicating BCL or OpenTelemetry abstractions.
3. Additional ergonomic APIs only where they remove recurring consumer boilerplate; composition helpers must preserve deterministic subscription cleanup.
4. Broader end-to-end samples for generated adapters, sender-aware occurrences, bounded streams, metrics, and composition.
5. Experimental techniques such as pooling, `IValueTaskSource<T>`, reusable waiters, or generated fast paths only when measurements justify their complexity.

The foundation should remain boring and dependable; experimentation belongs above it, not inside it.
