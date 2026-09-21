namespace AsyncEventBridge.StressTests;

public sealed class EventAwaiterStressTests
{
    [Fact]
    public async Task EventCancellationRaceRemainsSingleWinnerAcrossManyIterations()
    {
        const int iterations = 250;

        for (var iteration = 0; iteration < iterations; iteration++)
        {
            var source = new StressEventSource();
            using var cancellation = new CancellationTokenSource();
            using var barrier = new Barrier(3);
            var wait = EventAwaiter.WaitAsync<StressEventArgs>(
                handler => source.Changed += handler,
                handler => source.Changed -= handler,
                cancellationToken: cancellation.Token);

            var raiseTask = Task.Run(() =>
            {
                barrier.SignalAndWait();
                source.Raise(new StressEventArgs(iteration));
            });

            var cancelTask = Task.Run(() =>
            {
                barrier.SignalAndWait();
                cancellation.Cancel();
            });

            barrier.SignalAndWait();
            await Task.WhenAll(raiseTask, cancelTask);

            try
            {
                Assert.Equal(iteration, (await wait).Value);
                Assert.True(wait.IsCompletedSuccessfully);
            }
            catch (OperationCanceledException)
            {
                Assert.True(wait.IsCanceled);
            }

            Assert.Equal(0, source.HandlerCount);
            Assert.Equal(1, source.RemoveCount);
        }
    }

    [Fact]
    public async Task HundredsOfParallelWaitsLeaveNoHandlersBehind()
    {
        const int count = 256;
        var source = new StressEventSource();
        var waits = Enumerable.Range(0, count)
            .Select(expected => EventAwaiter.WaitAsync<StressEventArgs>(
                handler => source.Changed += handler,
                handler => source.Changed -= handler,
                eventArgs => eventArgs.Value == expected))
            .ToArray();

        Parallel.For(0, count, value => source.Raise(new StressEventArgs(value)));

        var results = await Task.WhenAll(waits);

        Assert.Equal(count, results.Length);
        Assert.Equal(0, source.HandlerCount);
        Assert.Equal(count, source.RemoveCount);
    }

    private sealed class StressEventSource
    {
        private readonly object _gate = new();
        private EventHandler<StressEventArgs>? _changed;
        private int _removeCount;

        internal event EventHandler<StressEventArgs> Changed
        {
            add
            {
                lock (_gate)
                {
                    _changed += value;
                }
            }
            remove
            {
                lock (_gate)
                {
                    _changed -= value;
                    _removeCount++;
                }
            }
        }

        internal int HandlerCount
        {
            get
            {
                lock (_gate)
                {
                    return _changed?.GetInvocationList().Length ?? 0;
                }
            }
        }

        internal int RemoveCount => Volatile.Read(ref _removeCount);

        internal void Raise(StressEventArgs eventArgs)
        {
            EventHandler<StressEventArgs>? handlers;

            lock (_gate)
            {
                handlers = _changed;
            }

            handlers?.Invoke(this, eventArgs);
        }
    }

    private sealed class StressEventArgs(int value) : EventArgs
    {
        internal int Value { get; } = value;
    }
}
