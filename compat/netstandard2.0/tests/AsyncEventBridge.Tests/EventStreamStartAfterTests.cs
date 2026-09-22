namespace AsyncEventBridge.Tests;

public sealed class EventStreamStartAfterTests
{
    [Fact]
    public async Task StartAfterDoesNotSubscribeSourceUntilStartCompletes()
    {
        var values = new EventSource();
        var start = new EventSource();

        var stream = EventStream.Create<TestArgs>(
                handler => values.Changed += handler,
                handler => values.Changed -= handler)
            .StartAfter(token => EventAwaiter.WaitAsync<TestArgs>(
                handler => start.Changed += handler,
                handler => start.Changed -= handler,
                cancellationToken: token));

        await using var enumerator = stream.GetAsyncEnumerator();
        var move = enumerator.MoveNextAsync().AsTask();

        Assert.Equal(1, start.HandlerCount);
        Assert.Equal(0, values.HandlerCount);
        Assert.False(values.Subscribed.IsCompleted);

        start.Raise(1);
        await values.Subscribed.WaitAsync(TimeSpan.FromSeconds(5));
        values.Raise(42);

        Assert.True(await move);
        Assert.Equal(42, enumerator.Current.Value);
    }

    [Fact]
    public async Task StartFaultNeverStartsSource()
    {
        var values = new EventSource();

        var stream = EventStream.Create<TestArgs>(
                handler => values.Changed += handler,
                handler => values.Changed -= handler)
            .StartAfter(_ => Task.FromException(new InvalidOperationException("start failed")));

        await using var enumerator = stream.GetAsyncEnumerator();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => enumerator.MoveNextAsync().AsTask());

        Assert.Equal("start failed", exception.Message);
        Assert.Equal(0, values.HandlerCount);
        Assert.False(values.Subscribed.IsCompleted);
    }

    [Fact]
    public async Task ExternalCancellationBeforeStartCleansWaitWithoutStartingSource()
    {
        var values = new EventSource();
        var start = new EventSource();
        using var cancellation = new CancellationTokenSource();

        var stream = EventStream.Create<TestArgs>(
                handler => values.Changed += handler,
                handler => values.Changed -= handler)
            .StartAfter(
                token => EventAwaiter.WaitAsync<TestArgs>(
                    handler => start.Changed += handler,
                    handler => start.Changed -= handler,
                    cancellationToken: token),
                cancellation.Token);

        await using var enumerator = stream.GetAsyncEnumerator();
        var move = enumerator.MoveNextAsync().AsTask();

        Assert.Equal(1, start.HandlerCount);
        Assert.Equal(0, values.HandlerCount);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => move);
        Assert.Equal(0, start.HandlerCount);
        Assert.Equal(0, values.HandlerCount);
    }

    [Fact]
    public async Task StartAfterAndTakeUntilStopBeforeStartWithoutSourceSubscription()
    {
        var values = new EventSource();
        var start = new EventSource();
        var stop = new EventSource();

        var stream = EventStream.Create<TestArgs>(
                handler => values.Changed += handler,
                handler => values.Changed -= handler)
            .StartAfter(token => EventAwaiter.WaitAsync<TestArgs>(
                handler => start.Changed += handler,
                handler => start.Changed -= handler,
                cancellationToken: token))
            .TakeUntil(token => EventAwaiter.WaitAsync<TestArgs>(
                handler => stop.Changed += handler,
                handler => stop.Changed -= handler,
                cancellationToken: token));

        await using var enumerator = stream.GetAsyncEnumerator();
        var move = enumerator.MoveNextAsync().AsTask();

        Assert.Equal(1, start.HandlerCount);
        Assert.Equal(1, stop.HandlerCount);
        Assert.Equal(0, values.HandlerCount);

        stop.Raise(1);

        Assert.False(await move);
        Assert.Equal(0, start.HandlerCount);
        Assert.Equal(0, stop.HandlerCount);
        Assert.Equal(0, values.HandlerCount);
    }

    [Fact]
    public async Task StartAfterDisposesSourceWhenItCompletes()
    {
        var source = new EmptyAsyncEnumerable<TestArgs>();
        var stream = source.StartAfter(_ => Task.CompletedTask);

        await using var enumerator = stream.GetAsyncEnumerator();

        Assert.False(await enumerator.MoveNextAsync());
        Assert.Equal(1, source.DisposeCount);
    }

    private sealed class EventSource
    {
        private readonly TaskCompletionSource<bool> _subscribed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private EventHandler<TestArgs>? _changed;

        internal event EventHandler<TestArgs>? Changed
        {
            add
            {
                _changed += value;
                _subscribed.TrySetResult(true);
            }
            remove => _changed -= value;
        }

        internal int HandlerCount => _changed?.GetInvocationList().Length ?? 0;
        internal Task Subscribed => _subscribed.Task;
        internal void Raise(int value) => _changed?.Invoke(this, new TestArgs(value));
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

    private sealed class TestArgs : EventArgs
    {
        internal TestArgs(int value) { Value = value; }
        internal int Value { get; }
    }
}
