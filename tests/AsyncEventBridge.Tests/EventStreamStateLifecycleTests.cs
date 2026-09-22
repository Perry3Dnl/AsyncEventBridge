namespace AsyncEventBridge.Tests;

public sealed class EventStreamStateLifecycleTests
{
    [Fact]
    public async Task RepeatWhileStartsImmediatelyWhenStateIsAlreadyActive()
    {
        var values = new IntSource();
        var state = new BoolStateSource(initialState: true);
        using var cancellation = new CancellationTokenSource();

        var stream = EventStream.Create<int>(
                handler => values.Changed += handler,
                handler => values.Changed -= handler)
            .RepeatWhile(
                () => state.State,
                current => current,
                token => EventAwaiter.WaitAsync<int>(
                    handler => state.Changed += handler,
                    handler => state.Changed -= handler,
                    cancellationToken: token),
                cancellation.Token);

        await using var enumerator = stream.GetAsyncEnumerator();
        var move = enumerator.MoveNextAsync().AsTask();

        await WaitUntilAsync(() => values.HandlerCount == 1);

        Assert.Equal(1, state.HandlerCount);

        values.Raise(42);

        Assert.True(await move);
        Assert.Equal(42, enumerator.Current);

        cancellation.Cancel();
    }

    [Fact]
    public async Task RepeatWhileStaysUnsubscribedWhileInactiveWithoutHotLooping()
    {
        var values = new IntSource();
        var state = new BoolStateSource(initialState: false);
        using var cancellation = new CancellationTokenSource();

        var stream = EventStream.Create<int>(
                handler => values.Changed += handler,
                handler => values.Changed -= handler)
            .RepeatWhile(
                () => state.State,
                current => current,
                token => EventAwaiter.WaitAsync<int>(
                    handler => state.Changed += handler,
                    handler => state.Changed -= handler,
                    cancellationToken: token),
                cancellation.Token);

        await using var enumerator = stream.GetAsyncEnumerator();
        var move = enumerator.MoveNextAsync().AsTask();

        await WaitUntilAsync(() => state.AddCount >= 1);

        Assert.Equal(0, values.AddCount);
        Assert.Equal(1, state.HandlerCount);

        await Task.Delay(25);

        Assert.Equal(1, state.AddCount);
        Assert.Equal(0, values.AddCount);
        Assert.False(move.IsCompleted);

        state.SetState(true);
        state.Raise();

        await WaitUntilAsync(() => values.HandlerCount == 1);
        values.Raise(7);

        Assert.True(await move);
        Assert.Equal(7, enumerator.Current);

        cancellation.Cancel();
    }

    [Fact]
    public async Task RepeatWhileDeactivatesAndReactivatesWithFreshSourceSubscription()
    {
        var values = new IntSource();
        var state = new BoolStateSource(initialState: true);
        using var cancellation = new CancellationTokenSource();

        var stream = EventStream.Create<int>(
                handler => values.Changed += handler,
                handler => values.Changed -= handler)
            .RepeatWhile(
                () => state.State,
                current => current,
                token => EventAwaiter.WaitAsync<int>(
                    handler => state.Changed += handler,
                    handler => state.Changed -= handler,
                    cancellationToken: token),
                cancellation.Token);

        await using var enumerator = stream.GetAsyncEnumerator();

        var firstMove = enumerator.MoveNextAsync().AsTask();
        await WaitUntilAsync(() => values.AddCount >= 1);
        values.Raise(1);

        Assert.True(await firstMove);
        Assert.Equal(1, enumerator.Current);

        var secondMove = enumerator.MoveNextAsync().AsTask();

        state.SetState(false);
        state.Raise();

        await WaitUntilAsync(() =>
            values.HandlerCount == 0 &&
            state.HandlerCount == 1);

        Assert.False(secondMove.IsCompleted);

        state.SetState(true);
        state.Raise();

        await WaitUntilAsync(() => values.AddCount >= 2);
        values.Raise(2);

        Assert.True(await secondMove);
        Assert.Equal(2, enumerator.Current);
        Assert.Equal(2, values.AddCount);

        cancellation.Cancel();
    }

    [Fact]
    public async Task RepeatWhileWithLifecycleArmsSourceBeforeActivatedMarker()
    {
        var values = new IntSource();
        var state = new BoolStateSource(initialState: true);
        using var cancellation = new CancellationTokenSource();

        var stream = EventStream.Create<int>(
                handler => values.Changed += handler,
                handler => values.Changed -= handler)
            .RepeatWhileWithLifecycle(
                () => state.State,
                current => current,
                token => EventAwaiter.WaitAsync<int>(
                    handler => state.Changed += handler,
                    handler => state.Changed -= handler,
                    cancellationToken: token),
                cancellation.Token);

        await using var enumerator = stream.GetAsyncEnumerator();

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(EventStreamLifecycleEventKind.Activated, enumerator.Current.Kind);
        Assert.Equal(1, enumerator.Current.Cycle);
        Assert.Equal(1, values.HandlerCount);

        var valueMove = enumerator.MoveNextAsync().AsTask();
        values.Raise(10);

        Assert.True(await valueMove);
        Assert.Equal(EventStreamLifecycleEventKind.Value, enumerator.Current.Kind);
        Assert.Equal(10, enumerator.Current.Value);
        Assert.Equal(1, enumerator.Current.Cycle);

        var deactivatedMove = enumerator.MoveNextAsync().AsTask();
        state.SetState(false);
        state.Raise();

        Assert.True(await deactivatedMove);
        Assert.Equal(EventStreamLifecycleEventKind.Deactivated, enumerator.Current.Kind);
        Assert.Equal(1, enumerator.Current.Cycle);
        Assert.Equal(0, values.HandlerCount);

        var nextActivation = enumerator.MoveNextAsync().AsTask();
        await WaitUntilAsync(() => state.HandlerCount == 1);

        state.SetState(true);
        state.Raise();

        Assert.True(await nextActivation);
        Assert.Equal(EventStreamLifecycleEventKind.Activated, enumerator.Current.Kind);
        Assert.Equal(2, enumerator.Current.Cycle);
        Assert.Equal(1, values.HandlerCount);

        cancellation.Cancel();
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

        Assert.True(condition(), "The expected state-driven lifecycle state was not reached.");
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

    private sealed class BoolStateSource
    {
        private readonly object _gate = new();
        private EventHandler<int>? _changed;
        private bool _state;
        private int _addCount;

        internal BoolStateSource(bool initialState)
        {
            _state = initialState;
        }

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

        internal bool State
        {
            get
            {
                lock (_gate)
                {
                    return _state;
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

        internal void SetState(bool value)
        {
            lock (_gate)
            {
                _state = value;
            }
        }

        internal void Raise()
        {
            EventHandler<int>? handlers;

            lock (_gate)
            {
                handlers = _changed;
            }

            handlers?.Invoke(this, 1);
        }
    }

    private sealed class CountingEnumerable<T> : IAsyncEnumerable<T>
    {
        private int _enumerationCount;

        internal int EnumerationCount => Volatile.Read(ref _enumerationCount);

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _enumerationCount);
            return new Enumerator();
        }

        private sealed class Enumerator : IAsyncEnumerator<T>
        {
            public T Current => default!;

            public ValueTask<bool> MoveNextAsync() => new ValueTask<bool>(false);

            public ValueTask DisposeAsync() => default;
        }
    }
}
