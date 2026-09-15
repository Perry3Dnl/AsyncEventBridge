# Performance baselines

This document records benchmark measurements used to guide optimization work on `dotnet-latest`.

The benchmarks live in `benchmarks/AsyncEventBridge.Benchmarks` and use BenchmarkDotNet with `MemoryDiagnoser`. Timing values from hosted CI runners are useful primarily for comparisons made within the same run. Allocation values are treated as the more stable cross-run signal.

## Event wait matrix

The characterization and optimization runs used .NET 10.0.12 with BenchmarkDotNet 0.15.8 on Ubuntu 24.04 GitHub-hosted runners. Each used one launch, three warmup iterations, and five measurement iterations.

### Initial characterization

| Path | Mean | Allocated |
| --- | ---: | ---: |
| Low-level completion | 120.4 ns | 536 B |
| Generated completion | 134.2 ns | 560 B |
| Low-level cancellation | 2.715 us | 1,072 B |
| Generated cancellation | 2.695 us | 1,096 B |
| Low-level timeout | 3.143 us | 1,304 B |
| Generated timeout | 3.203 us | 1,328 B |

### Optimized waiter

The common no-predicate, non-cancellable, no-finite-timeout path now uses a smaller internal wait state instead of carrying predicate, cancellation, timeout, timer, and registration state that it cannot use. The controlled waiter also allocates its predicate synchronization gate only when a predicate exists.

| Path | Mean on optimization runner | Allocated | Allocation change |
| --- | ---: | ---: | ---: |
| Low-level completion | 140.9 ns | 440 B | **-96 B (-17.9%)** |
| Generated completion | 147.9 ns | 464 B | **-96 B (-17.1%)** |
| Low-level cancellation | 2.516 us | 1,048 B | **-24 B** |
| Generated cancellation | 2.530 us | 1,072 B | **-24 B** |
| Low-level timeout | 2.974 us | 1,280 B | **-24 B** |
| Generated timeout | 3.032 us | 1,304 B | **-24 B** |

The two runs landed on different hosted-runner CPU models, so the timing columns must not be compared across runs as evidence of a latency improvement or regression. The allocation deltas are the accepted signal for this optimization.

### Interpretation

- The successful event-completion path is the normal hot path. Its low-level allocation is now 440 B per completed wait, down from 568 B before the terminal-state work and 536 B immediately before the lean-state optimization.
- The lean simple-wait state removes 96 B per successful wait in the current benchmark while preserving subscription cleanup, synchronous/reentrant event delivery, concurrent winner behavior, and failure aggregation.
- Cancellation and timeout remain on the full controlled state. Making the predicate gate lazy removes 24 B from these no-predicate paths without weakening their cancellation/timeout race machinery.
- The generated facade still adds 24 B in each measured path. That comes from generated source capture/subscription plumbing. Removing it cleanly would require a more invasive source-aware runtime contract, so 24 B is currently accepted rather than expanding the public API solely for a small allocation reduction.
- Cancellation and timeout are intentionally exception-based terminal paths and therefore allocate substantially more than successful completion. They should not be optimized at the expense of the normal completion path unless application measurements show they are unusually frequent.
- Cancellation measurements include creation and disposal of the consumer-owned `CancellationTokenSource` used to trigger cancellation after subscription.
- Timeout measurements use a reusable manual `TimeProvider` so the benchmark exercises timeout registration and completion without sleeping and without allocating a new provider for each operation.

## Runtime metrics overhead

The production-metrics pass uses the BCL `System.Diagnostics.Metrics` implementation. A focused .NET 10.0.12 run compared the same operations with no collector and with an active `MeterListener`.

| Path | Listener | Mean | Allocated |
| --- | --- | ---: | ---: |
| Wait completion | Disabled | 99.72 ns | 440 B |
| Wait completion | Enabled | 99.20 ns | 440 B |
| Bounded `DropNewest` burst | Disabled | 5.192 us | 2,288 B |
| Bounded `DropNewest` burst | Enabled | 5.234 us | 2,288 B |

The listener-enabled wait result is within benchmark noise of the disabled case, while the actively collected bounded-drop burst is roughly 0.8% slower in this run. Neither path gained any managed allocation when collection was enabled. This is the acceptance signal for keeping the built-in metrics enabled by default rather than adding a public instrumentation toggle.

## Optimization rule

Do not accept a more complicated implementation solely because it is theoretically faster. Performance changes should preserve lifecycle, cleanup, cancellation, timeout, reentrancy, and race semantics and should demonstrate a repeatable improvement in the relevant BenchmarkDotNet baseline.
