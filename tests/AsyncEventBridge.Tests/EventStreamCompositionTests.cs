namespace AsyncEventBridge.Tests;

public sealed class EventStreamCompositionTests
{
    [Fact]
    public async Task TakeUntilStopsStreamAndCleansBothEventSubscriptions()
    {
        var values = new IntSource();
        var stop = new IntSource();

        var stream = EventStream.Create<int>(
                handler => values.Changed += handler,
                handler => values.Changed -= handler)
            .TakeUntil(token => EventAwaiter.WaitAsync<int>(
                handler => stop.Changed += handler,
                handler => stop.Changed -= handler,
                cancellationToken: token));

        await using var enumerator = stream.GetAsyncEnumerator();

        var firstMove = enumerator.MoveNextAsync().AsTask();

        Assert.Equal(1, values.HandlerCount);
        Assert.Equal(1, stop.HandlerCount);

        values.Raise(10);

        Assert.True(await firstMove);
        Assert.Equal(10, enumerator.Current);

        var secondMove = enumerator.MoveNextAsync().AsTask();
        stop.Raise(1);

        Assert.False(await secondMove);
        Assert.Equal(0, values.HandlerCount);
        Assert.Equal(0, stop.HandlerCount);
        Assert.False(await enumerator.MoveNextAsync());
    }

    [Fact]
    public async Task SourceCompletionCancelsAndObservesPendingStopWait()
    {
        var source = new EmptyAsyncEnumerable<int>();
        var stop = new IntSource();

        var stream = source.TakeUntil(token => EventAwaiter.WaitAsync<int>(
            handler => stop.Changed += handler,
            handler => stop.Changed -= handler,
            cancellationToken: token));

        await using var enumerator = stream.GetAsyncEnumerator();

        Assert.False(await enumerator.MoveNextAsync());
        Assert.Equal(1, source.DisposeCount);
        Assert.Equal(0, stop.HandlerCount);
    }

    [Fact]
    public async Task StopFaultWinsWithoutStartingSourceConsumption()
    {
        var values = new IntSource();

        var stream = EventStream.Create<int>(
                handler => values.Changed += handler,
                handler => values.Changed -= handler)
            .TakeUntil(_ => Task.FromException(new InvalidOperationException("stop failed")));

        await using var enumerator = stream.GetAsyncEnumerator();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => enumerator.MoveNextAsync().AsTask());

        Assert.Equal("stop failed", exception.Message);
        Assert.Equal(0, values.HandlerCount);
    }

    [Fact]
    public async Task SourceFaultRemainsPrimaryWhenStopCleanupAlsoFails()
    {
        var values = new IntSource();
        var stop = new IntSource(throwOnRemove: true);

        var stream = EventStream.Create<int>(
                handler => values.Changed += handler,
                handler => values.Changed -= handler,
                predicate: _ => throw new InvalidOperationException("source failed"))
            .TakeUntil(token => EventAwaiter.WaitAsync<int>(
                handler => stop.Changed += handler,
                handler => stop.Changed -= handler,
                cancellationToken: token));

        await using var enumerator = stream.GetAsyncEnumerator();

        var move = enumerator.MoveNextAsync().AsTask();
        values.Raise(1);

        var exception = await Assert.ThrowsAsync<AggregateException>(() => move);
        var flattened = exception.Flatten().InnerExceptions;

        Assert.Equal("source failed", flattened[0].Message);
        Assert.Contains(flattened, error => error.Message == "unsubscribe failed");
        Assert.Equal(0, values.HandlerCount);
        Assert.Equal(0, stop.HandlerCount);
    }

    [Fact]
    public async Task ExternalCancellationStopsBothParticipants()
    {
        var values = new IntSource();
        var stop = new IntSource();
        using var cancellation = new CancellationTokenSource();

        var stream = EventStream.Create<int>(
                handler => values.Changed += handler,
                handler => values.Changed -= handler)
            .TakeUntil(
                token => EventAwaiter.WaitAsync<int>(
                    handler => stop.Changed += handler,
                    handler => stop.Changed -= handler,
                    cancellationToken: token),
                cancellation.Token);

        await using var enumerator = stream.GetAsyncEnumerator();
        var move = enumerator.MoveNextAsync().AsTask();

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => move);
        Assert.Equal(0, values.HandlerCount);
        Assert.Equal(0, stop.HandlerCount);
    }

    [Fact]
    public async Task StopCompletionWinsWhenSourceValueIsAlreadyAvailableAtBoundary()
    {
        var source = new SingleBufferedValueAsyncEnumerable<int>(42);
        var stop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        stop.SetResult();

        var stream = source.TakeUntil(_ => stop.Task);

        await using var enumerator = stream.GetAsyncEnumerator();

        Assert.False(await enumerator.MoveNextAsync());
        Assert.Equal(1, source.DisposeCount);
    }

    private sealed class IntSource(bool throwOnRemove = false)
    {
        private EventHandler<int>? _changed;

        internal event EventHandler<int>? Changed
        {
            add => _changed += value;
            remove
            {
                _changed -= value;
                if (throwOnRemove)
                {
                    throw new InvalidOperationException("unsubscribe failed");
                }
            }
        }

        internal int HandlerCount => _changed?.GetInvocationList().Length ?? 0;

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

    private sealed class SingleBufferedValueAsyncEnumerable<T>(T value) : IAsyncEnumerable<T>
    {
        private int _disposeCount;

        internal int DisposeCount => Volatile.Read(ref _disposeCount);

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            new Enumerator(this, value);

        private sealed class Enumerator(
            SingleBufferedValueAsyncEnumerable<T> owner,
            T value) : IAsyncEnumerator<T>
        {
            private bool _moved;

            public T Current { get; private set; } = default!;

            public ValueTask<bool> MoveNextAsync()
            {
                if (_moved)
                {
                    return new ValueTask<bool>(false);
                }

                _moved = true;
                Current = value;
                return new ValueTask<bool>(true);
            }

            public ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref owner._disposeCount);
                return default;
            }
        }
    }
}
