# Modern .NET line

`dotnet-latest` is the native modern-.NET edition of AsyncEventBridge.

## Design rules

- Target the current stable .NET runtime directly instead of multi-targeting the compatibility baseline.
- Keep the conceptual bridge model compatible with `base/netstandard2.0`, but allow implementation and API improvements that rely on modern BCL/runtime features.
- Prefer built-in concurrency primitives over compatibility shims when they provide clearer semantics or lower maintenance cost.
- Treat allocation rate, contention, cancellation/timeout behavior, and async-stream throughput as first-class quality metrics.
- Shared bug fixes should normally land in `base/netstandard2.0` first when they apply there; modern-only optimizations stay on this branch.

## Initial modernization roadmap

1. Native `net10.0` runtime/package and `net10.0` test/sample consumers.
2. Replace the compatibility event-stream buffer/signaling implementation with `System.Threading.Channels`.
3. Move event timeout scheduling to `TimeProvider` and add deterministic virtual-time tests.
4. Establish BenchmarkDotNet baselines for one-shot waits, stream throughput, bridge publication, cancellation, and contention.
5. Review modern API opportunities (`ValueTask`, modern cancellation/time APIs, async-disposal paths, source-generator output) only where benchmarks or ergonomics justify the divergence.
