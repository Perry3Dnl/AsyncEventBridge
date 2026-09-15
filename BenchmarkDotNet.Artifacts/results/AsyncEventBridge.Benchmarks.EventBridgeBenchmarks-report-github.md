```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
Intel Xeon 6973P-C 4.15GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4
  Job-NTRUNJ : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

IterationCount=5  WarmupCount=3  

```
| Method                              | BurstSize | Mean     | Error    | StdDev  | Gen0   | Allocated |
|------------------------------------ |---------- |---------:|---------:|--------:|-------:|----------:|
| **CompletedValueTaskBridge**            | **32**        | **101.0 ns** |  **9.49 ns** | **2.47 ns** | **0.0026** |     **224 B** |
| CompletedValueTaskViaAsTaskBaseline | 32        | 112.9 ns |  8.28 ns | 2.15 ns | 0.0035 |     296 B |
| **CompletedValueTaskBridge**            | **256**       | **107.4 ns** |  **2.28 ns** | **0.59 ns** | **0.0026** |     **224 B** |
| CompletedValueTaskViaAsTaskBaseline | 256       | 105.2 ns | 12.95 ns | 3.36 ns | 0.0035 |     296 B |
