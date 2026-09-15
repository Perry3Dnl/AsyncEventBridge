# Modern .NET line

`dotnet-latest` is the native modern-.NET edition of AsyncEventBridge.

## Design rules

- Target the current stable .NET runtime directly instead of multi-targeting the compatibility baseline.
- Keep the conceptual bridge model compatible with `base/netstandard2.0`, but allow implementation and API improvements that rely on modern BCL/runtime features.
- Prefer built-in concurrency primitives over compatibility shims when they provide clearer semantics or lower maintenance cost.
- Treat allocation rate, contention, cancellation/timeout behavior, async-stream throughput, and backpressure observability as first-class quality metrics.
- Add modern-only features when they solve a concrete integration, correctness, performance, testability, or operability problem; do not add APIs merely because the runtime makes them possible.
- Shared bug fixes should normally land in `base/netstandard2.0` first when they apply there; modern-only optimizations stay on this branch.

## Modernization roadmap

Completed foundations:

1. Native `net10.0` runtime/package and `net10.0` test/sample consumers.
2. `System.Threading.Channels` event-stream buffering.
3. `TimeProvider` timeout scheduling plus deterministic virtual-time tests and generated timeout overloads.
4. BenchmarkDotNet baselines for one-shot waits and stream throughput.
5. Direct `ValueTask` / `ValueTask<T>` event bridges.
6. Modern event payload support, including non-`EventArgs` payloads and `EventHandler<TSender, TPayload>`.
7. Bounded-stream drop observability through thread-safe dropped-event counters and optional observers backed by the channel's real drop callback.

Next candidates should continue to earn their complexity. Useful areas include richer `System.Diagnostics.Metrics` integration, Channel-facing interoperability, and benchmark-driven allocation/locking improvements. Pooling, `IValueTaskSource<T>`, reusable waiters, or specialized generated fast paths should only be adopted when measurements show a material gain.
