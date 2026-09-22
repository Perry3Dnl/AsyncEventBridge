namespace AsyncEventBridge.Tests;

public sealed class EventStreamStartAfterTests
{
    [Fact]
    public async Task StartAfterDoesNotSubscribeSourceUntilStartCompletes()
    {
        var values = new IntSource();
        var start = new IntSource();

        var stream = EventStream.Create<int>(
                handler => values.Changed += handler,
                handler => values.Changed -= handler)
            .StartAfter(token => EventAwaiter.WaitAsync<int>(
                handler => start.Changed += handler,
                handler => start.Changed -= handler,
                cancellationToken: token));

        await using var enumerator = stream.GetAsyncEnumerator();
        var move = enumerator.MoveNextAsync().AsTask();

        Assert.Equal(1, start.HandlerCount);
        Assert.Equal(0, values.HandlerCount);
        Assert.False(values.Subscribed.IsCompleted);

        values.Raise(10);
        Assert.False(move.IsCompleted);

        start.Raise(1);
        await values.Subscribed.WaitAsync(TimeSpan.FromSeconds(5));
        values.Raise(42);

        Assert.True(await move);
        Assert.Equal(42, enumerator.Current);

        await enumerator.DisposeAsync();
        Assert.Equal(0, start.HandlerCount);
        Assert.Equal(0, values.HandlerCount);
    }

    [Fact]
    public async Task StartFaultNeverStartsSource()
    {
        var values = new IntSource();

        var stream = EventStream.Create<int>(
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
        var values = new IntSource();
        var start = new IntSource();
        using var cancellation = new CancellationTokenSource();

        var stream = EventStream.Create<int>(
                handler => values.Changed += handler,
                handler => values.Changed -= handler)
            .StartAfter(
                token => EventAwaiter.WaitAsync<int>(
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
        var values = new IntSource();
        var start = new IntSource();
        var stop = new IntSource();

        var stream = EventStream.Create<int>(
                handler => values.Changed += handler,
                handler => values.Changed -= handler)
            .StartAfter(token => EventAwaiter.WaitAsync<int>(
                handler => start.Changed += handler,
                handler => start.Changed -= handler,
                cancellationToken: token))
            .TakeUntil(token => EventAwaiter.WaitAsync<int>(
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
        var source = new EmptyAsyncEnumerable<int>();
        var stream = source.StartAfter(_ => Task.CompletedTask);

        await using var enumerator = stream.GetAsyncEnumerator();

        Assert.False(await enumerator.MoveNextAsync());
        Assert.Equal(1, source.DisposeCount);
    }

    private sealed class IntSource
    {
        private readonly TaskCompletionSource<bool> _subscribed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private EventHandler<int>? _changed;

        internal event EventHandler<int>? Changed
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
        internal void Raise(int value) => _changed?.Invoke(this, value);
    }

    private sealed class EmptyAsyncEnumerable<T> : IAsyncEnumerable<T>
    {
        private int _disposeCount;
        internal int DisposeCount => Volatile.Read(ref _disposeCount);

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            new Enumerator(this);

        private sealed class Enumerator(EmptyAsyncEnumerable<T> owner) : IAsyncEnumerator<T>
        {
            public T Current => default!;
            public ValueTask<bool> MoveNextAsync() => new(false);

            public ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref owner._disposeCount);
                return default;
            }
        }
    }
}
