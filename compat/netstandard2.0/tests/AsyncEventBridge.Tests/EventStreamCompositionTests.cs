namespace AsyncEventBridge.Tests;

public sealed class EventStreamCompositionTests
{
    [Fact]
    public async Task TakeUntilStopsStreamAndCleansBothEventSubscriptions()
    {
        var values = new EventSource();
        var stop = new EventSource();

        var stream = EventStream.Create<TestArgs>(
                handler => values.Changed += handler,
                handler => values.Changed -= handler)
            .TakeUntil(token => EventAwaiter.WaitAsync<TestArgs>(
                handler => stop.Changed += handler,
                handler => stop.Changed -= handler,
                cancellationToken: token));

        await using var enumerator = stream.GetAsyncEnumerator();

        var firstMove = enumerator.MoveNextAsync().AsTask();

        Assert.Equal(1, values.HandlerCount);
        Assert.Equal(1, stop.HandlerCount);

        values.Raise(10);

        Assert.True(await firstMove);
        Assert.Equal(10, enumerator.Current.Value);

        var secondMove = enumerator.MoveNextAsync().AsTask();
        stop.Raise(1);

        Assert.False(await secondMove);
        Assert.Equal(0, values.HandlerCount);
        Assert.Equal(0, stop.HandlerCount);
    }

    [Fact]
    public async Task SourceCompletionCancelsAndObservesPendingStopWait()
    {
        var source = new EmptyAsyncEnumerable<TestArgs>();
        var stop = new EventSource();

        var stream = source.TakeUntil(token => EventAwaiter.WaitAsync<TestArgs>(
            handler => stop.Changed += handler,
            handler => stop.Changed -= handler,
            cancellationToken: token));

        await using var enumerator = stream.GetAsyncEnumerator();

        Assert.False(await enumerator.MoveNextAsync());
        Assert.Equal(1, source.DisposeCount);
        Assert.Equal(0, stop.HandlerCount);
    }

    [Fact]
    public async Task StopFaultPropagatesWithoutStartingSourceConsumption()
    {
        var values = new EventSource();

        var stream = EventStream.Create<TestArgs>(
                handler => values.Changed += handler,
                handler => values.Changed -= handler)
            .TakeUntil(_ => Task.FromException(new InvalidOperationException("stop failed")));

        await using var enumerator = stream.GetAsyncEnumerator();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => enumerator.MoveNextAsync().AsTask());

        Assert.Equal("stop failed", exception.Message);
        Assert.Equal(0, values.HandlerCount);
    }

    private sealed class EventSource
    {
        private EventHandler<TestArgs>? _changed;

        internal event EventHandler<TestArgs>? Changed
        {
            add => _changed += value;
            remove => _changed -= value;
        }

        internal int HandlerCount => _changed?.GetInvocationList().Length ?? 0;

        internal void Raise(int value) => _changed?.Invoke(this, new TestArgs(value));
    }

    private sealed class TestArgs : EventArgs
    {
        internal TestArgs(int value)
        {
            Value = value;
        }

        internal int Value { get; }
    }

    private sealed class EmptyAsyncEnumerable<T> : IAsyncEnumerable<T>
    {
        private int _disposeCount;

        internal int DisposeCount => Volatile.Read(ref _disposeCount);

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            new Enumerator(this);

        private sealed class Enumerator : IAsyncEnumerator<T>
        {
            private readonly EmptyAsyncEnumerable<T> _owner;

            internal Enumerator(EmptyAsyncEnumerable<T> owner)
            {
                _owner = owner;
            }

            public T Current => default!;

            public ValueTask<bool> MoveNextAsync() => new ValueTask<bool>(false);

            public ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref _owner._disposeCount);
                return default;
            }
        }
    }
}
