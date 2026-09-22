namespace AsyncEventBridge.Tests;

public sealed class EventStreamStateLifecycleTests
{
    [Fact]
    public async Task RepeatWhileWaitsWithoutStartingSourceUntilStateBecomesActive()
    {
        var values = new EventSource();
        var state = new BoolStateSource(false);
        using var cancellation = new CancellationTokenSource();

        var stream = EventStream.Create<TestArgs>(
                handler => values.Changed += handler,
                handler => values.Changed -= handler)
            .RepeatWhile(
                () => state.State,
                current => current,
                token => EventAwaiter.WaitAsync<TestArgs>(
                    handler => state.Changed += handler,
                    handler => state.Changed -= handler,
                    cancellationToken: token),
                cancellation.Token);

        await using var enumerator = stream.GetAsyncEnumerator();
        var move = enumerator.MoveNextAsync().AsTask();

        await WaitUntilAsync(() => state.AddCount >= 1);

        Assert.Equal(0, values.AddCount);
        Assert.Equal(1, state.HandlerCount);

        state.SetState(true);
        state.Raise();

        await WaitUntilAsync(() => values.HandlerCount == 1);
        values.Raise(9);

        Assert.True(await move);
        Assert.Equal(9, enumerator.Current.Value);

        cancellation.Cancel();
    }

    [Fact]
    public async Task RepeatWhileWithLifecycleReactivatesWithNextCycle()
    {
        var values = new EventSource();
        var state = new BoolStateSource(true);
        using var cancellation = new CancellationTokenSource();

        var stream = EventStream.Create<TestArgs>(
                handler => values.Changed += handler,
                handler => values.Changed -= handler)
            .RepeatWhileWithLifecycle(
                () => state.State,
                current => current,
                token => EventAwaiter.WaitAsync<TestArgs>(
                    handler => state.Changed += handler,
                    handler => state.Changed -= handler,
                    cancellationToken: token),
                cancellation.Token);

        await using var enumerator = stream.GetAsyncEnumerator();

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(EventStreamLifecycleEventKind.Activated, enumerator.Current.Kind);
        Assert.Equal(1, enumerator.Current.Cycle);
        Assert.Equal(1, values.HandlerCount);

        var deactivated = enumerator.MoveNextAsync().AsTask();
        state.SetState(false);
        state.Raise();

        Assert.True(await deactivated);
        Assert.Equal(EventStreamLifecycleEventKind.Deactivated, enumerator.Current.Kind);
        Assert.Equal(1, enumerator.Current.Cycle);

        var activatedAgain = enumerator.MoveNextAsync().AsTask();
        await WaitUntilAsync(() => state.HandlerCount == 1);
        state.SetState(true);
        state.Raise();

        Assert.True(await activatedAgain);
        Assert.Equal(EventStreamLifecycleEventKind.Activated, enumerator.Current.Kind);
        Assert.Equal(2, enumerator.Current.Cycle);

        cancellation.Cancel();
    }

    [Fact]
    public async Task TakeUntilAlreadySatisfiedStopDoesNotStartSource()
    {
        var source = new CountingEnumerable<TestArgs>();

        var stream = source.TakeUntil(_ => Task.CompletedTask);

        await using var enumerator = stream.GetAsyncEnumerator();

        Assert.False(await enumerator.MoveNextAsync());
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

        Assert.True(condition(), "The expected state-driven lifecycle state was not reached.");
    }

    private sealed class EventSource
    {
        private readonly object _gate = new object();
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

    private sealed class BoolStateSource
    {
        private readonly object _gate = new object();
        private EventHandler<TestArgs>? _changed;
        private bool _state;
        private int _addCount;

        internal BoolStateSource(bool initialState)
        {
            _state = initialState;
        }

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
            EventHandler<TestArgs>? handlers;

            lock (_gate)
            {
                handlers = _changed;
            }

            handlers?.Invoke(this, new TestArgs(1));
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
