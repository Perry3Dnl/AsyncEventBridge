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

    [Fact]
    public async Task SourceFaultRemainsPrimaryWhenStopCleanupAlsoFails()
    {
        var values = new EventSource();
        var stop = new EventSource(throwOnRemove: true);

        var stream = EventStream.Create<TestArgs>(
                handler => values.Changed += handler,
                handler => values.Changed -= handler,
                predicate: _ => throw new InvalidOperationException("source failed"))
            .TakeUntil(token => EventAwaiter.WaitAsync<TestArgs>(
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
        var values = new EventSource();
        var stop = new EventSource();
        using var cancellation = new CancellationTokenSource();

        var stream = EventStream.Create<TestArgs>(
                handler => values.Changed += handler,
                handler => values.Changed -= handler)
            .TakeUntil(
                token => EventAwaiter.WaitAsync<TestArgs>(
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
        var source = new SingleBufferedValueAsyncEnumerable<TestArgs>(new TestArgs(42));
        var stop = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        stop.SetResult(true);

        var stream = source.TakeUntil(_ => stop.Task);

        await using var enumerator = stream.GetAsyncEnumerator();

        Assert.False(await enumerator.MoveNextAsync());
        Assert.Equal(1, source.DisposeCount);
    }

    private sealed class EventSource
    {
        private readonly bool _throwOnRemove;
        private EventHandler<TestArgs>? _changed;

        internal EventSource(bool throwOnRemove = false)
        {
            _throwOnRemove = throwOnRemove;
        }

        internal event EventHandler<TestArgs>? Changed
        {
            add => _changed += value;
            remove
            {
                _changed -= value;

                if (_throwOnRemove)
                {
                    throw new InvalidOperationException("unsubscribe failed");
                }
            }
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

    private sealed class SingleBufferedValueAsyncEnumerable<T> : IAsyncEnumerable<T>
    {
        private readonly T _value;
        private int _disposeCount;

        internal SingleBufferedValueAsyncEnumerable(T value)
        {
            _value = value;
        }

        internal int DisposeCount => Volatile.Read(ref _disposeCount);

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            new Enumerator(this, _value);

        private sealed class Enumerator : IAsyncEnumerator<T>
        {
            private readonly SingleBufferedValueAsyncEnumerable<T> _owner;
            private readonly T _value;
            private bool _moved;

            internal Enumerator(SingleBufferedValueAsyncEnumerable<T> owner, T value)
            {
                _owner = owner;
                _value = value;
            }

            public T Current { get; private set; } = default!;

            public ValueTask<bool> MoveNextAsync()
            {
                if (_moved)
                {
                    return new ValueTask<bool>(false);
                }

                _moved = true;
                Current = _value;
                return new ValueTask<bool>(true);
            }

            public ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref _owner._disposeCount);
                return default;
            }
        }
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
