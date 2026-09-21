namespace AsyncEventBridge.Tests;

public sealed class SimpleEventWaitOptimizationTests
{
    [Fact]
    public async Task EventDuringSubscriptionCompletesAndUnsubscribes()
    {
        var source = new ReentrantSource();

        var wait = EventAwaiter.WaitAsync<int>(
            handler => source.Changed += handler,
            handler => source.Changed -= handler);

        Assert.Equal(42, await wait);
        Assert.Equal(0, source.HandlerCount);
        Assert.Equal(1, source.RemoveCount);
    }

    [Fact]
    public async Task SubscriptionAndCleanupFailuresAreBothPreserved()
    {
        var wait = EventAwaiter.WaitAsync<int>(
            _ => throw new InvalidOperationException("subscribe failed"),
            _ => throw new ApplicationException("unsubscribe failed"));

        var exception = await Assert.ThrowsAsync<AggregateException>(() => wait);

        Assert.Collection(
            exception.InnerExceptions,
            innerException => Assert.Equal("subscribe failed", innerException.Message),
            innerException => Assert.Equal("unsubscribe failed", innerException.Message));
    }

    [Fact]
    public async Task ConcurrentEventsProduceOneResultAndOneCleanup()
    {
        var source = new ConcurrentSource();
        using var barrier = new Barrier(3);
        var wait = EventAwaiter.WaitAsync<int>(
            handler => source.Changed += handler,
            handler => source.Changed -= handler);

        var first = Task.Run(() =>
        {
            barrier.SignalAndWait();
            source.Raise(1);
        });
        var second = Task.Run(() =>
        {
            barrier.SignalAndWait();
            source.Raise(2);
        });

        barrier.SignalAndWait();
        await Task.WhenAll(first, second);

        Assert.Contains(await wait, new[] { 1, 2 });
        Assert.Equal(0, source.HandlerCount);
        Assert.Equal(1, source.RemoveCount);
    }

    private sealed class ReentrantSource
    {
        private EventHandler<int>? _changed;

        internal event EventHandler<int>? Changed
        {
            add
            {
                _changed += value;
                value?.Invoke(this, 42);
            }
            remove
            {
                RemoveCount++;
                _changed -= value;
            }
        }

        internal int HandlerCount => _changed?.GetInvocationList().Length ?? 0;
        internal int RemoveCount { get; private set; }
    }

    private sealed class ConcurrentSource
    {
        private EventHandler<int>? _changed;
        private int _removeCount;

        internal event EventHandler<int>? Changed
        {
            add => _changed += value;
            remove
            {
                Interlocked.Increment(ref _removeCount);
                _changed -= value;
            }
        }

        internal int HandlerCount => _changed?.GetInvocationList().Length ?? 0;
        internal int RemoveCount => Volatile.Read(ref _removeCount);

        internal void Raise(int value) => _changed?.Invoke(this, value);
    }
}
