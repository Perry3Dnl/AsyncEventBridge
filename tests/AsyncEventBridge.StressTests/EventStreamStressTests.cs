using AsyncEventBridge;

namespace AsyncEventBridge.StressTests;

public sealed class EventStreamStressTests
{
    [Fact]
    public async Task UnboundedModePreservesConcurrentProducerValues()
    {
        const int producerCount = 20;
        const int valuesPerProducer = 100;
        const int expectedCount = producerCount * valuesPerProducer;

        EventHandler<StressEventArgs>? changed = null;
        var stream = EventStream.Create<StressEventArgs>(
            handler => changed += handler,
            handler => changed -= handler,
            options: new EventStreamOptions
            {
                Capacity = 1,
                FullMode = EventStreamFullMode.Unbounded,
            });

        var enumerator = stream.GetAsyncEnumerator();

        try
        {
            var firstMove = enumerator.MoveNextAsync().AsTask();
            Assert.NotNull(changed);

            changed!(null, new StressEventArgs(-1));
            Assert.True(await firstMove);
            Assert.Equal(-1, enumerator.Current.Value);

            var producers = Enumerable.Range(0, producerCount)
                .Select(producer => Task.Run(() =>
                {
                    for (var offset = 0; offset < valuesPerProducer; offset++)
                    {
                        var value = (producer * valuesPerProducer) + offset;
                        changed!(null, new StressEventArgs(value));
                    }
                }))
                .ToArray();

            await Task.WhenAll(producers);

            var observed = new HashSet<int>();

            for (var index = 0; index < expectedCount; index++)
            {
                Assert.True(await enumerator.MoveNextAsync());
                Assert.True(observed.Add(enumerator.Current.Value));
            }

            Assert.Equal(expectedCount, observed.Count);
            Assert.Equal(Enumerable.Range(0, expectedCount), observed.OrderBy(value => value));
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        Assert.Null(changed);
    }

    [Fact]
    public async Task DropWriteAccountingStaysExactUnderConcurrentProducers()
    {
        const int producerCount = 8;
        const int valuesPerProducer = 200;
        const int capacity = 32;
        const int producedCount = producerCount * valuesPerProducer;

        EventHandler<StressEventArgs>? changed = null;
        var observerCalls = 0L;
        var options = new EventStreamOptions
        {
            Capacity = capacity,
            FullMode = EventStreamFullMode.DropWrite,
            DropObserver = _ => Interlocked.Increment(ref observerCalls),
        };

        var stream = EventStream.Create<StressEventArgs>(
            handler => changed += handler,
            handler => changed -= handler,
            options: options);

        await using var enumerator = stream.GetAsyncEnumerator();

        var initialMove = enumerator.MoveNextAsync().AsTask();
        Assert.NotNull(changed);

        changed!(null, new StressEventArgs(-1));
        Assert.True(await initialMove);
        Assert.Equal(-1, enumerator.Current.Value);

        var producers = Enumerable.Range(0, producerCount)
            .Select(producer => Task.Run(() =>
            {
                for (var offset = 0; offset < valuesPerProducer; offset++)
                {
                    changed!(null, new StressEventArgs((producer * valuesPerProducer) + offset));
                }
            }))
            .ToArray();

        await Task.WhenAll(producers);

        Assert.Equal(producedCount - capacity, options.DroppedCount);
        Assert.Equal(options.DroppedCount, Volatile.Read(ref observerCalls));

        var buffered = new HashSet<int>();

        for (var index = 0; index < capacity; index++)
        {
            Assert.True(await enumerator.MoveNextAsync());
            Assert.True(buffered.Add(enumerator.Current.Value));
        }

        Assert.Equal(capacity, buffered.Count);
    }

    private sealed class StressEventArgs : EventArgs
    {
        internal StressEventArgs(int value)
        {
            Value = value;
        }

        internal int Value { get; }
    }
}
