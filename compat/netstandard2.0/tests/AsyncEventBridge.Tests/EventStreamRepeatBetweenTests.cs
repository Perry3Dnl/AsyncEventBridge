namespace AsyncEventBridge.Tests;

public sealed class EventStreamRepeatBetweenTests
{
    [Fact]
    public async Task RepeatBetweenStreamsAcrossMultipleLifecycleCycles()
    {
        var values = new EventSource();
        var start = new EventSource();
        var stop = new EventSource();
        using var cancellation = new CancellationTokenSource();

        var stream = EventStream.Create<TestArgs>(
                handler => values.Changed += handler,
                handler => values.Changed -= handler)
            .RepeatBetween(
                token => EventAwaiter.WaitAsync<TestArgs>(
                    handler => start.Changed += handler,
                    handler => start.Changed -= handler,
                    cancellationToken: token),
                token => EventAwaiter.WaitAsync<TestArgs>(
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
        Assert.Equal(10, enumerator.Current.Value);

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
        Assert.Equal(20, enumerator.Current.Value);

        cancellation.Cancel();
    }

    [Fact]
    public async Task StopBeforeStartRearmsWithoutSubscribingSource()
    {
        var values = new EventSource();
        var start = new EventSource();
        var stop = new EventSource();
        using var cancellation = new CancellationTokenSource();

        var stream = EventStream.Create<TestArgs>(
                handler => values.Changed += handler,
                handler => values.Changed -= handler)
            .RepeatBetween(
                token => EventAwaiter.WaitAsync<TestArgs>(
                    handler => start.Changed += handler,
                    handler => start.Changed -= handler,
                    cancellationToken: token),
                token => EventAwaiter.WaitAsync<TestArgs>(
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

        start.Raise(2);
        await WaitUntilAsync(() => values.AddCount >= 1);
        values.Raise(42);

        Assert.True(await move);
        Assert.Equal(42, enumerator.Current.Value);

        cancellation.Cancel();
    }

    [Fact]
    public async Task LifecycleFaultTerminatesRepeatingWorkflow()
    {
        var source = new SingleValueSource();

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

    private sealed class EventSource
    {
        private readonly object _gate = new();
        private EventHandler<TestArgs>? _changed;
        private int _addCount;

        internal event EventHandler<TestArgs>? Changed
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
            EventHandler<TestArgs>? handlers;

            lock (_gate)
            {
                handlers = _changed;
            }

            handlers?.Invoke(this, new TestArgs(value));
        }
    }

    private sealed class TestArgs : EventArgs
    {
        internal TestArgs(int value)
        {
            Value = value;
        }

        internal int Value { get; }
    }

    private sealed class SingleValueSource : IAsyncEnumerable<TestArgs>
    {
        private int _enumerationCount;

        internal int EnumerationCount => Volatile.Read(ref _enumerationCount);

        public IAsyncEnumerator<TestArgs> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _enumerationCount);
            return new Enumerator();
        }

        private sealed class Enumerator : IAsyncEnumerator<TestArgs>
        {
            public TestArgs Current => new TestArgs(1);
            public ValueTask<bool> MoveNextAsync() => new ValueTask<bool>(true);
            public ValueTask DisposeAsync() => default;
        }
    }
}
