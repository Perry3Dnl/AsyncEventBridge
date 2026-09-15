# Performance baselines

This document records benchmark measurements used to guide optimization work on `dotnet-latest`.

The benchmarks live in `benchmarks/AsyncEventBridge.Benchmarks` and use BenchmarkDotNet with `MemoryDiagnoser`. Timing values from hosted CI runners are useful primarily for comparisons made within the same run. Allocation values are treated as the more stable cross-run signal.

## Event wait matrix

Measured on .NET 10.0.12 with BenchmarkDotNet 0.15.8 on an Ubuntu 24.04 GitHub-hosted runner. The characterization run used one launch, three warmup iterations, and five measurement iterations.

| Path | Mean | Allocated |
| --- | ---: | ---: |
| Low-level completion | 120.4 ns | 536 B |
| Generated completion | 134.2 ns | 560 B |
| Low-level cancellation | 2.715 us | 1,072 B |
| Generated cancellation | 2.695 us | 1,096 B |
| Low-level timeout | 3.143 us | 1,304 B |
| Generated timeout | 3.203 us | 1,328 B |

### Interpretation

- The generated facade adds 24 B in each measured path. That overhead is small but consistent enough to keep visible for later generator/runtime optimization work.
- The successful event-completion path remains the most important optimization target because it is the normal hot path. Its low-level allocation is currently 536 B per completed wait.
- The earlier single-winner terminal-state rewrite reduced the successful low-level wait from the previous 568 B baseline to 536 B, a 32 B reduction per operation.
- Cancellation and timeout are intentionally exception-based terminal paths and therefore allocate substantially more than successful completion. They should not be optimized at the expense of the normal completion path unless application measurements show they are unusually frequent.
- Cancellation measurements include creation and disposal of the consumer-owned `CancellationTokenSource` used to trigger cancellation after subscription.
- Timeout measurements use a reusable manual `TimeProvider` so the benchmark exercises timeout registration and completion without sleeping and without allocating a new provider for each operation.

## Optimization rule

Do not accept a more complicated implementation solely because it is theoretically faster. Performance changes should preserve lifecycle, cleanup, cancellation, timeout, reentrancy, and race semantics and should demonstrate a repeatable improvement in the relevant BenchmarkDotNet baseline.
