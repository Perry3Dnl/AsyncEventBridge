using System.Diagnostics.Metrics;
using BenchmarkDotNet.Attributes;

namespace AsyncEventBridge.Benchmarks;

[MemoryDiagnoser]
public class MetricsBenchmarks
{
    private readonly MetricBenchmarkSource _source = new();
    private MeterListener? _listener;

    [Params(false, true)]
    public bool ListenerEnabled { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        if (!ListenerEnabled)
        {
            return;
        }

        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == "AsyncEventBridge")
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };
        _listener.SetMeasurementEventCallback<long>(static (_, _, _, _) => { });
        _listener.Start();
    }

    [GlobalCleanup]
    public void Cleanup() => _listener?.Dispose();

    [Benchmark]
    public async Task<int> WaitCompletion()
    {
        var wait = EventAwaiter.WaitAsync<int>(
            handler => _source.Changed += handler,
            handler => _source.Changed -= handler);

        _source.Raise(42);
        return await wait;
    }

    [Benchmark]
    public async Task<long> BoundedDropWriteBurst()
    {
        var options = new EventStreamOptions
        {
            Capacity = 8,
            FullMode = EventStreamFullMode.DropWrite,
        };
        var stream = EventStream.Create<int>(
            handler => _source.Changed += handler,
            handler => _source.Changed -= handler,
            options: options);

        await using var enumerator = stream.GetAsyncEnumerator();

        var firstMove = enumerator.MoveNextAsync().AsTask();
        _source.Raise(0);
        await firstMove;

        long checksum = enumerator.Current;

        for (var i = 1; i <= 32; i++)
        {
            _source.Raise(i);
        }

        for (var i = 0; i < options.Capacity; i++)
        {
            if (!await enumerator.MoveNextAsync())
            {
                throw new InvalidOperationException("Bounded stream ended before the retained buffer was drained.");
            }

            checksum += enumerator.Current;
        }

        return checksum + options.DroppedCount;
    }

    private sealed class MetricBenchmarkSource
    {
        internal event EventHandler<int>? Changed;

        internal void Raise(int value) => Changed?.Invoke(this, value);
    }
}
