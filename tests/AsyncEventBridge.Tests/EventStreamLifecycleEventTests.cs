namespace AsyncEventBridge.Tests;

public sealed class EventStreamLifecycleEventTests
{
    [Fact]
    public async Task RepeatBetweenWithLifecycleEmitsActivationValuesAndDeactivation()
    {
        var values = new IntSource();
        var start = new IntSource();
        var stop = new IntSource();
        using var cancellation = new CancellationTokenSource();

        var stream = EventStream.Create<int>(
                handler => values.Changed += handler,
                handler => values.Changed -= handler)
            .RepeatBetweenWithLifecycle(
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

        var activatedMove = enumerator.MoveNextAsync().AsTask();
        await WaitUntilAsync(() => start.HandlerCount == 1 && stop.HandlerCount == 1);

        start.Raise(1);

        Assert.True(await activatedMove);
        Assert.Equal(EventStreamLifecycleEventKind.Activated, enumerator.Current.Kind);
        Assert.Equal(1, enumerator.Current.Cycle);
        Assert.False(enumerator.Current.HasValue);
        Assert.Throws<InvalidOperationException>(() => _ = enumerator.Current.Value);

        var valueMove = enumerator.MoveNextAsync().AsTask();
        await WaitUntilAsync(() => values.HandlerCount == 1);
        values.Raise(42);

        Assert.True(await valueMove);
        Assert.Equal(EventStreamLifecycleEventKind.Value, enumerator.Current.Kind);
        Assert.Equal(1, enumerator.Current.Cycle);
        Assert.True(enumerator.Current.HasValue);
        Assert.Equal(42, enumerator.Current.Value);

        var deactivatedMove = enumerator.MoveNextAsync().AsTask();
        stop.Raise(1);

        Assert.True(await deactivatedMove);
        Assert.Equal(EventStreamLifecycleEventKind.Deactivated, enumerator.Current.Kind);
        Assert.Equal(1, enumerator.Current.Cycle);
        Assert.False(enumerator.Current.HasValue);

        var nextActivation = enumerator.MoveNextAsync().AsTask();
        await WaitUntilAsync(() => start.AddCount >= 2 && stop.AddCount >= 2);

        start.Raise(2);

        Assert.True(await nextActivation);
        Assert.Equal(EventStreamLifecycleEventKind.Activated, enumerator.Current.Kind);
        Assert.Equal(2, enumerator.Current.Cycle);

        cancellation.Cancel();
    }

    [Fact]
    public async Task NaturalSourceCompletionIsObservableAndNextActivationGetsNextCycleNumber()
    {
        var source = new OneValuePerEnumeration();
        using var cancellation = new CancellationTokenSource();

        var stream = source.RepeatBetweenWithLifecycle(
            _ => Task.CompletedTask,
            token => Task.Delay(Timeout.InfiniteTimeSpan, token),
            cancellation.Token);

        await using var enumerator = stream.GetAsyncEnumerator();

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(EventStreamLifecycleEventKind.Activated, enumerator.Current.Kind);
        Assert.Equal(1, enumerator.Current.Cycle);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(EventStreamLifecycleEventKind.Value, enumerator.Current.Kind);
        Assert.Equal(1, enumerator.Current.Cycle);
        Assert.Equal(1, enumerator.Current.Value);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(EventStreamLifecycleEventKind.SourceCompleted, enumerator.Current.Kind);
        Assert.Equal(1, enumerator.Current.Cycle);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(EventStreamLifecycleEventKind.Activated, enumerator.Current.Kind);
        Assert.Equal(2, enumerator.Current.Cycle);

        Assert.Equal(2, source.EnumerationCount);
        cancellation.Cancel();
    }

    [Fact]
    public async Task StopBeforeStartDoesNotEmitMarkerOrConsumeCycleNumber()
    {
        var values = new IntSource();
        var start = new IntSource();
        var stop = new IntSource();
        using var cancellation = new CancellationTokenSource();

        var stream = EventStream.Create<int>(
                handler => values.Changed += handler,
                handler => values.Changed -= handler)
            .RepeatBetweenWithLifecycle(
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

        Assert.False(move.IsCompleted);
        Assert.Equal(0, values.AddCount);

        start.Raise(2);

        Assert.True(await move);
        Assert.Equal(EventStreamLifecycleEventKind.Activated, enumerator.Current.Kind);
        Assert.Equal(1, enumerator.Current.Cycle);

        cancellation.Cancel();
    }

    [Fact]
    public async Task LifecycleFaultTerminatesInsteadOfBecomingMarker()
    {
        var source = new OneValuePerEnumeration();

        var stream = source.RepeatBetweenWithLifecycle(
            _ => Task.FromException(new InvalidOperationException("activation failed")),
            token => Task.Delay(Timeout.InfiniteTimeSpan, token));

        await using var enumerator = stream.GetAsyncEnumerator();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => enumerator.MoveNextAsync().AsTask());

        Assert.Equal("activation failed", exception.Message);
        Assert.Equal(0, source.EnumerationCount);
    }

    [Fact]
    public async Task SourceCompletionRemainsTheLifecycleReasonWhenStopFinishesOnlyDuringCleanup()
    {
        var source = new EmptyAsyncEnumerable<int>();
        var stop = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var stream = source.RepeatBetweenWithLifecycle(
            _ => Task.CompletedTask,
            _ => stop.Task);

        await using var enumerator = stream.GetAsyncEnumerator();

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(EventStreamLifecycleEventKind.Activated, enumerator.Current.Kind);

        var completionMove = enumerator.MoveNextAsync().AsTask();

        Assert.False(completionMove.IsCompleted);

        stop.SetResult(true);

        Assert.True(await completionMove);
        Assert.Equal(EventStreamLifecycleEventKind.SourceCompleted, enumerator.Current.Kind);
        Assert.Equal(1, enumerator.Current.Cycle);
    }

    [Fact]
    public void LifecycleEventKindNumericValuesStayStable()
    {
        Assert.Equal(0, (int)EventStreamLifecycleEventKind.Unspecified);
        Assert.Equal(1, (int)EventStreamLifecycleEventKind.Activated);
        Assert.Equal(2, (int)EventStreamLifecycleEventKind.Value);
        Assert.Equal(3, (int)EventStreamLifecycleEventKind.Deactivated);
        Assert.Equal(4, (int)EventStreamLifecycleEventKind.SourceCompleted);
    }

    [Fact]
    public void DefaultLifecycleEventIsUnspecified()
    {
        var lifecycleEvent = default(EventStreamLifecycleEvent<int>);

        Assert.Equal(EventStreamLifecycleEventKind.Unspecified, lifecycleEvent.Kind);
        Assert.Equal(0, lifecycleEvent.Cycle);
        Assert.False(lifecycleEvent.HasValue);
        Assert.Throws<InvalidOperationException>(() => _ = lifecycleEvent.Value);
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

    private sealed class EmptyAsyncEnumerable<T> : IAsyncEnumerable<T>
    {
        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            new Enumerator();

        private sealed class Enumerator : IAsyncEnumerator<T>
        {
            public T Current => default!;

            public ValueTask<bool> MoveNextAsync() => new ValueTask<bool>(false);

            public ValueTask DisposeAsync() => default;
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
