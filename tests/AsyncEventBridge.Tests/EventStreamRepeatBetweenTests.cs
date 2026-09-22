namespace AsyncEventBridge.Tests;

public sealed class EventStreamRepeatBetweenTests
{
    [Fact]
    public async Task RepeatBetweenStreamsAcrossMultipleLifecycleCycles()
    {
        var values = new IntSource();
        var start = new IntSource();
        var stop = new IntSource();
        using var cancellation = new CancellationTokenSource();

        var stream = EventStream.Create<int>(
                handler => values.Changed += handler,
                handler => values.Changed -= handler)
            .RepeatBetween(
                token => EventAwaiter.WaitAsync<int>(
                    handler => start.Changed += handler,
                    handler => start.Changed -= handler,
                    cancellationToken: token),
                token => EventAwaiter.WaitAsync<int>(
                    handler => stop.Changed += handler,
                    handler => stop.Changed -= handler,
                    cancellationToken: token),
                cancellation.Token);

        await using var enumerator = stream.GetAsyncEnumerator();

        var firstMove = enumerator.MoveNextAsync().AsTask();
        await WaitUntilAsync(() => start.AddCount >= 1 && stop.AddCount >= 1);

        Assert.Equal(0, values.HandlerCount);

        start.Raise(1);
        await WaitUntilAsync(() => values.AddCount >= 1);

        values.Raise(10);

        Assert.True(await firstMove);
        Assert.Equal(10, enumerator.Current);

        var secondMove = enumerator.MoveNextAsync().AsTask();
        stop.Raise(1);

        await WaitUntilAsync(() =>
            start.AddCount >= 2 &&
            stop.AddCount >= 2 &&
            values.HandlerCount == 0);

        start.Raise(2);
        await WaitUntilAsync(() => values.AddCount >= 2);

        values.Raise(20);

        Assert.True(await secondMove);
        Assert.Equal(20, enumerator.Current);

        var cancelledMove = enumerator.MoveNextAsync().AsTask();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledMove);
        await WaitUntilAsync(() =>
            values.HandlerCount == 0 &&
            start.HandlerCount == 0 &&
            stop.HandlerCount == 0);
    }

    [Fact]
    public async Task StopBeforeStartRearmsWithoutSubscribingSource()
    {
        var values = new IntSource();
        var start = new IntSource();
        var stop = new IntSource();
        using var cancellation = new CancellationTokenSource();

        var stream = EventStream.Create<int>(
                handler => values.Changed += handler,
                handler => values.Changed -= handler)
            .RepeatBetween(
                token => EventAwaiter.WaitAsync<int>(
                    handler => start.Changed += handler,
                    handler => start.Changed -= handler,
                    cancellationToken: token),
                token => EventAwaiter.WaitAsync<int>(
                    handler => stop.Changed += handler,
                    handler => stop.Changed -= handler,
                    cancellationToken: token),
                cancellation.Token);

        await using var enumerator = stream.GetAsyncEnumerator();
        var move = enumerator.MoveNextAsync().AsTask();

        await WaitUntilAsync(() => start.AddCount >= 1 && stop.AddCount >= 1);
        stop.Raise(1);

        await WaitUntilAsync(() => start.AddCount >= 2 && stop.AddCount >= 2);

        Assert.Equal(0, values.AddCount);
        Assert.Equal(0, values.HandlerCount);

        start.Raise(2);
        await WaitUntilAsync(() => values.AddCount >= 1);
        values.Raise(42);

        Assert.True(await move);
        Assert.Equal(42, enumerator.Current);

        cancellation.Cancel();
    }

    [Fact]
    public async Task NaturalSourceCompletionStartsFreshEnumerationOnNextCycle()
    {
        var source = new OneValuePerEnumeration();
        using var cancellation = new CancellationTokenSource();

        var stream = source.RepeatBetween(
            _ => Task.CompletedTask,
            token => Task.Delay(Timeout.InfiniteTimeSpan, token),
            cancellation.Token);

        await using var enumerator = stream.GetAsyncEnumerator();

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(1, enumerator.Current);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(2, enumerator.Current);

        Assert.Equal(2, source.EnumerationCount);

        cancellation.Cancel();
    }

    [Fact]
    public async Task LifecycleFaultTerminatesRepeatingWorkflow()
    {
        var source = new OneValuePerEnumeration();

        var stream = source.RepeatBetween(
            _ => Task.FromException(new InvalidOperationException("activation failed")),
            token => Task.Delay(Timeout.InfiniteTimeSpan, token));

        await using var enumerator = stream.GetAsyncEnumerator();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => enumerator.MoveNextAsync().AsTask());

        Assert.Equal("activation failed", exception.Message);
        Assert.Equal(0, source.EnumerationCount);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 5_000; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(1);
        }

        Assert.True(condition(), "The expected lifecycle state was not reached.");
    }

    private sealed class IntSource
    {
        private readonly object _gate = new();
        private EventHandler<int>? _changed;
        private int _addCount;

        internal event EventHandler<int>? Changed
        {
            add
            {
                lock (_gate)
                {
                    _changed += value;
                    _addCount++;
                }
            }
            remove
            {
                lock (_gate)
                {
                    _changed -= value;
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

        internal int AddCount
        {
            get
            {
                lock (_gate)
                {
                    return _addCount;
                }
            }
        }

        internal void Raise(int value)
        {
            EventHandler<int>? handlers;

            lock (_gate)
            {
                handlers = _changed;
            }

            handlers?.Invoke(this, value);
        }
    }

    private sealed class OneValuePerEnumeration : IAsyncEnumerable<int>
    {
        private int _enumerationCount;

        internal int EnumerationCount => Volatile.Read(ref _enumerationCount);

        public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            var value = Interlocked.Increment(ref _enumerationCount);
            return new Enumerator(value);
        }

        private sealed class Enumerator(int value) : IAsyncEnumerator<int>
        {
            private bool _yielded;

            public int Current { get; private set; }

            public ValueTask<bool> MoveNextAsync()
            {
                if (_yielded)
                {
                    return new ValueTask<bool>(false);
                }

                _yielded = true;
                Current = value;
                return new ValueTask<bool>(true);
            }

            public ValueTask DisposeAsync() => default;
        }
    }
}
