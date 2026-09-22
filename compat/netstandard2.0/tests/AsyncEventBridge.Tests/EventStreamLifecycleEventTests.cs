namespace AsyncEventBridge.Tests;

public sealed class EventStreamLifecycleEventTests
{
    [Fact]
    public async Task RepeatBetweenWithLifecycleEmitsActivationValueAndDeactivation()
    {
        var values = new EventSource();
        var start = new EventSource();
        var stop = new EventSource();
        using var cancellation = new CancellationTokenSource();

        var stream = EventStream.Create<TestArgs>(
                handler => values.Changed += handler,
                handler => values.Changed -= handler)
            .RepeatBetweenWithLifecycle(
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

        var activated = enumerator.MoveNextAsync().AsTask();
        await WaitUntilAsync(() => start.HandlerCount == 1 && stop.HandlerCount == 1);
        start.Raise(1);

        Assert.True(await activated);
        Assert.Equal(EventStreamLifecycleEventKind.Activated, enumerator.Current.Kind);
        Assert.Equal(1, enumerator.Current.Cycle);

        var value = enumerator.MoveNextAsync().AsTask();
        await WaitUntilAsync(() => values.HandlerCount == 1);
        values.Raise(42);

        Assert.True(await value);
        Assert.Equal(EventStreamLifecycleEventKind.Value, enumerator.Current.Kind);
        Assert.Equal(42, enumerator.Current.Value);

        var deactivated = enumerator.MoveNextAsync().AsTask();
        stop.Raise(1);

        Assert.True(await deactivated);
        Assert.Equal(EventStreamLifecycleEventKind.Deactivated, enumerator.Current.Kind);
        Assert.Equal(1, enumerator.Current.Cycle);

        cancellation.Cancel();
    }

    [Fact]
    public async Task StopBeforeStartDoesNotConsumeCycleNumber()
    {
        var values = new EventSource();
        var start = new EventSource();
        var stop = new EventSource();
        using var cancellation = new CancellationTokenSource();

        var stream = EventStream.Create<TestArgs>(
                handler => values.Changed += handler,
                handler => values.Changed -= handler)
            .RepeatBetweenWithLifecycle(
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
        Assert.False(move.IsCompleted);
        Assert.Equal(0, values.AddCount);

        start.Raise(2);

        Assert.True(await move);
        Assert.Equal(EventStreamLifecycleEventKind.Activated, enumerator.Current.Kind);
        Assert.Equal(1, enumerator.Current.Cycle);

        cancellation.Cancel();
    }

    [Fact]
    public async Task SourceCompletionRemainsTheLifecycleReasonWhenStopFinishesOnlyDuringCleanup()
    {
        var source = new EmptyAsyncEnumerable<TestArgs>();
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
    public void LifecycleEventValueIsOnlyAvailableForValueKind()
    {
        var source = new SingleValueSource();
        var stream = source.RepeatBetweenWithLifecycle(
            _ => Task.CompletedTask,
            token => Task.Delay(Timeout.InfiniteTimeSpan, token));

        Assert.NotNull(stream);
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
        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            new Enumerator();

        private sealed class Enumerator : IAsyncEnumerator<T>
        {
            public T Current => default!;

            public ValueTask<bool> MoveNextAsync() => new ValueTask<bool>(false);

            public ValueTask DisposeAsync() => default;
        }
    }

    private sealed class SingleValueSource : IAsyncEnumerable<TestArgs>
    {
        public IAsyncEnumerator<TestArgs> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            new Enumerator();

        private sealed class Enumerator : IAsyncEnumerator<TestArgs>
        {
            public TestArgs Current => new TestArgs(1);
            public ValueTask<bool> MoveNextAsync() => new ValueTask<bool>(false);
            public ValueTask DisposeAsync() => default;
        }
    }
}
