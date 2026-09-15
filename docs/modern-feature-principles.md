# Modern .NET feature principles

The `dotnet-latest` line is allowed to diverge from the `.NET Standard 2.0` compatibility baseline when modern .NET provides a concrete product benefit.

A modern-only API or implementation change should improve at least one of these dimensions:

- integration with APIs that are common in current .NET applications;
- correctness, lifecycle ownership, cancellation, or testability;
- throughput, allocation behavior, or contention;
- API clarity or removal of compatibility-era machinery;
- observability or diagnostics that are difficult to provide on the compatibility baseline.

New BCL or language features are not a goal by themselves. Prefer a smaller API surface until a use case is clear and testable.

## Current priorities

1. Native `ValueTask` / `ValueTask<T>` integration for async-to-event bridges.
2. `TimeProvider` support through both low-level and generated one-shot event APIs.
3. Channel-native integration only where subscription ownership and disposal can be made explicit.
4. Benchmark-driven optimization of one-shot waits and repeated event streams.
5. Modern diagnostics and observability where they expose otherwise hidden event drops, faults, or lifecycle state.

## Deferred until measured

- pooled `IValueTaskSource<T>` waiters;
- reusable await sources;
- custom lock-free queues that duplicate `System.Threading.Channels`;
- public knobs that simply mirror BCL implementation details.

These can be revisited when benchmarks or real integration scenarios show a concrete benefit.