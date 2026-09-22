namespace AsyncEventBridge.Tests;

public sealed class EventConditionTests
{
    [Fact]
    public async Task PreCancelledTokenDoesNotArmChangeWait()
    {
        var source = new BoolStateSource(false);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var wait = EventCondition.WaitUntilAsync(
            () => source.State,
            state => state,
            token => EventAwaiter.WaitAsync<TestArgs>(
                handler => source.Changed += handler,
                handler => source.Changed -= handler,
                cancellationToken: token),
            cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
        Assert.Equal(0, source.AddCount);
        Assert.Equal(0, source.HandlerCount);
    }

    [Fact]
    public async Task AlreadySatisfiedStateArmsAndCleansChangeWaitBeforeReturning()
    {
        var source = new BoolStateSource(true);

        await EventCondition.WaitUntilAsync(
            () => source.State,
            state => state,
            token => EventAwaiter.WaitAsync<TestArgs>(
                handler => source.Changed += handler,
                handler => source.Changed -= handler,
                cancellationToken: token));

        Assert.True(source.State);
        Assert.Equal(1, source.AddCount);
        Assert.Equal(0, source.HandlerCount);
    }

    [Fact]
    public async Task TransitionDuringSubscriptionIsNotMissed()
    {
        var source = new BoolStateSource(false);

        await EventCondition.WaitUntilAsync(
            () => source.State,
            state => state,
            token => EventAwaiter.WaitAsync<TestArgs>(
                handler => source.SubscribeAndBecomeTrue(handler),
                handler => source.Changed -= handler,
                cancellationToken: token));

        Assert.True(source.State);
        Assert.Equal(1, source.AddCount);
        Assert.Equal(0, source.HandlerCount);
    }

    [Fact]
    public async Task SpuriousChangeRearmsUntilStateMatches()
    {
        var source = new BoolStateSource(false);

        var wait = EventCondition.WaitUntilAsync(
            () => source.State,
            state => state,
            token => EventAwaiter.WaitAsync<TestArgs>(
                handler => source.Changed += handler,
                handler => source.Changed -= handler,
                cancellationToken: token));

        await WaitUntilAsync(() => source.AddCount >= 1);
        source.Raise();

        await WaitUntilAsync(() => source.AddCount >= 2);
        Assert.False(wait.IsCompleted);

        source.SetState(true);
        source.Raise();

        await wait;
        Assert.True(source.State);
        Assert.Equal(0, source.HandlerCount);
    }

    [Fact]
    public async Task ChangeWaitFaultRemainsTheSinglePrimaryFailure()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => EventCondition.WaitUntilAsync(
                () => false,
                state => state,
                _ => Task.FromException(new InvalidOperationException("change failed"))));

        Assert.Equal("change failed", exception.Message);
    }

    [Fact]
    public async Task StateReadFailureCancelsAndObservesArmedChangeWait()
    {
        var source = new BoolStateSource(false);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => EventCondition.WaitUntilAsync<bool>(
                () => throw new InvalidOperationException("state read failed"),
                state => state,
                token => EventAwaiter.WaitAsync<TestArgs>(
                    handler => source.Changed += handler,
                    handler => source.Changed -= handler,
                    cancellationToken: token)));

        Assert.Equal("state read failed", exception.Message);
        Assert.Equal(1, source.AddCount);
        Assert.Equal(0, source.HandlerCount);
    }

    [Fact]
    public async Task ExternalCancellationCleansPendingChangeWait()
    {
        var source = new BoolStateSource(false);
        using var cancellation = new CancellationTokenSource();

        var wait = EventCondition.WaitUntilAsync(
            () => source.State,
            state => state,
            token => EventAwaiter.WaitAsync<TestArgs>(
                handler => source.Changed += handler,
                handler => source.Changed -= handler,
                cancellationToken: token),
            cancellation.Token);

        await WaitUntilAsync(() => source.HandlerCount == 1);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
        Assert.Equal(0, source.HandlerCount);
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

        Assert.True(condition(), "The expected event-condition state was not reached.");
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

        internal void SubscribeAndBecomeTrue(EventHandler<TestArgs> handler)
        {
            Changed += handler;
            SetState(true);
            Raise();
        }

        internal void Raise()
        {
            EventHandler<TestArgs>? handlers;

            lock (_gate)
            {
                handlers = _changed;
            }

            handlers?.Invoke(this, new TestArgs());
        }
    }

    private sealed class TestArgs : EventArgs
    {
    }
}
