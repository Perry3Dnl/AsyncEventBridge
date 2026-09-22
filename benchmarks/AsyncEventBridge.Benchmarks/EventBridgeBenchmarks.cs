using BenchmarkDotNet.Attributes;

namespace AsyncEventBridge.Benchmarks;

[MemoryDiagnoser]
public class EventBridgeBenchmarks
{
    private readonly BenchmarkEventSource _source = new();
    private readonly BenchmarkEventArgs _singleArgs = new(42);
    private int _bridgeResult;

    [Benchmark]
    public async Task<int> WaitAndRaise()
    {
        var wait = EventAwaiter.WaitAsync<BenchmarkEventArgs>(
            handler => _source.Changed += handler,
            handler => _source.Changed -= handler);

        _source.Raise(_singleArgs);
        return (await wait).Value;
    }

    [Benchmark]
    public int CompletedValueTaskBridge() =>
        RunCompletedBridge(new ValueTask<int>(42).ToEventBridge());

    [Benchmark]
    public int CompletedValueTaskViaAsTaskBaseline() =>
        RunCompletedBridge(new ValueTask<int>(42).AsTask().ToEventBridge());

    [Params(32, 256)]
    public int BurstSize { get; set; }

    [Benchmark]
    public async Task<int> BufferedStreamBurst()
    {
        var stream = EventStream.Create<BenchmarkEventArgs>(
            handler => _source.Changed += handler,
            handler => _source.Changed -= handler,
            options: new EventStreamOptions
            {
                Capacity = BurstSize,
                FullMode = EventStreamFullMode.Unbounded,
            });

        await using var enumerator = stream.GetAsyncEnumerator();

        var firstMove = enumerator.MoveNextAsync().AsTask();
        _source.Raise(new BenchmarkEventArgs(0));
        await firstMove;

        var checksum = enumerator.Current.Value;

        for (var i = 1; i < BurstSize; i++)
        {
            _source.Raise(new BenchmarkEventArgs(i));
        }

        for (var i = 1; i < BurstSize; i++)
        {
            if (!await enumerator.MoveNextAsync())
            {
                throw new InvalidOperationException("Stream ended before the benchmark burst was drained.");
            }

            checksum += enumerator.Current.Value;
        }

        return checksum;
    }

    [Benchmark]
    public Task<long> BoundedDropWriteCountOnly() => RunDropWriteBurst(observeDrops: false);

    [Benchmark]
    public Task<long> BoundedDropWriteWithObserver() => RunDropWriteBurst(observeDrops: true);

    private int RunCompletedBridge(EventBridge<int> bridge)
    {
        _bridgeResult = 0;

        using (bridge)
        {
            bridge.Completed += CaptureBridgeResult;
            bridge.Connect();
        }

        return _bridgeResult;
    }

    private void CaptureBridgeResult(object? sender, AsyncValueEventArgs<int> eventArgs) =>
        _bridgeResult = eventArgs.Value;

    private async Task<long> RunDropWriteBurst(bool observeDrops)
    {
        long observedCount = 0;
        var options = new EventStreamOptions
        {
            Capacity = 8,
            FullMode = EventStreamFullMode.DropWrite,
            DropObserver = observeDrops ? count => observedCount = count : null,
        };
        var stream = EventStream.Create<BenchmarkEventArgs>(
            handler => _source.Changed += handler,
            handler => _source.Changed -= handler,
            options: options);

        await using var enumerator = stream.GetAsyncEnumerator();

        var firstMove = enumerator.MoveNextAsync().AsTask();
        _source.Raise(new BenchmarkEventArgs(0));
        await firstMove;

        long checksum = enumerator.Current.Value;

        for (var i = 1; i <= BurstSize; i++)
        {
            _source.Raise(new BenchmarkEventArgs(i));
        }

        for (var i = 0; i < options.Capacity; i++)
        {
            if (!await enumerator.MoveNextAsync())
            {
                throw new InvalidOperationException("Bounded stream ended before the retained buffer was drained.");
            }

            checksum += enumerator.Current.Value;
        }

        return checksum + options.DroppedCount + observedCount;
    }

    private sealed class BenchmarkEventSource
    {
        internal event EventHandler<BenchmarkEventArgs>? Changed;

        internal void Raise(BenchmarkEventArgs eventArgs) => Changed?.Invoke(this, eventArgs);
    }

    public sealed class BenchmarkEventArgs(int value) : EventArgs
    {
        public int Value { get; } = value;
    }
}
