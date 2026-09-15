using BenchmarkDotNet.Attributes;

namespace AsyncEventBridge.Benchmarks;

[MemoryDiagnoser]
public class EventBridgeBenchmarks
{
    private readonly BenchmarkEventSource _source = new();
    private readonly BenchmarkEventArgs _singleArgs = new(42);

    [Benchmark]
    public async Task<int> WaitAndRaise()
    {
        var wait = EventAwaiter.WaitAsync<BenchmarkEventArgs>(
            handler => _source.Changed += handler,
            handler => _source.Changed -= handler);

        _source.Raise(_singleArgs);
        return (await wait).Value;
    }

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
                FullMode = EventStreamFullMode.Grow,
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
