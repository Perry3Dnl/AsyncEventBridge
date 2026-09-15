namespace AsyncEventBridge.Tests;

public sealed class EventOccurrenceAndCompositionTests
{
    [Fact]
    public async Task OccurrenceWaitPreservesSenderAndPayload()
    {
        var source = new StrongSource();
        var wait = EventOccurrenceAwaiter.WaitAsync<StrongSource, int>(
            handler => source.Changed += handler,
            handler => source.Changed -= handler);

        source.Raise(42);
        var occurrence = await wait;

        Assert.Same(source, occurrence.Sender);
        Assert.Equal(42, occurrence.Payload);
        Assert.Equal(0, source.HandlerCount);
    }

    [Fact]
    public async Task WaitAnyReturnsSecondWinnerAndCleansUpLoser()
    {
        var first = new IntSource();
        var second = new StringSource();

        var wait = EventComposition.WaitAnyAsync(
            token => EventAwaiter.WaitAsync<int>(
                handler => first.Changed += handler,
                handler => first.Changed -= handler,
                cancellationToken: token),
            token => EventAwaiter.WaitAsync<string>(
                handler => second.Changed += handler,
                handler => second.Changed -= handler,
                cancellationToken: token));

        second.Raise("ready");
        var result = await wait;

        Assert.True(result.IsSecond);
        Assert.False(result.IsFirst);
        Assert.Equal("ready", result.Second);
        Assert.Equal(0, first.HandlerCount);
        Assert.Equal(0, second.HandlerCount);
        Assert.Throws<InvalidOperationException>(() => _ = result.First);
    }

    [Fact]
    public async Task WaitAnySurfacesLosingCleanupFailure()
    {
        var first = new IntSource(throwOnRemove: true);
        var second = new StringSource();

        var wait = EventComposition.WaitAnyAsync(
            token => EventAwaiter.WaitAsync<int>(
                handler => first.Changed += handler,
                handler => first.Changed -= handler,
                cancellationToken: token),
            token => EventAwaiter.WaitAsync<string>(
                handler => second.Changed += handler,
                handler => second.Changed -= handler,
                cancellationToken: token));

        second.Raise("winner");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => wait);
        Assert.Equal("unsubscribe failed", exception.Message);
    }

    [Fact]
    public async Task IndexedWaitAnyReturnsWinnerIndexAndCleansUpAllLosers()
    {
        var first = new IntSource();
        var second = new IntSource();
        var third = new IntSource();
        Func<CancellationToken, Task<int>>[] waits =
        [
            token => WaitAsync(first, token),
            token => WaitAsync(second, token),
            token => WaitAsync(third, token),
        ];

        var wait = EventComposition.WaitAnyAsync(waits);
        second.Raise(22);
        var result = await wait;

        Assert.Equal(1, result.Index);
        Assert.Equal(22, result.Value);
        Assert.Equal(0, first.HandlerCount);
        Assert.Equal(0, second.HandlerCount);
        Assert.Equal(0, third.HandlerCount);
    }

    [Fact]
    public async Task IndexedWaitAnyStartupFailureCancelsAlreadyStartedWaits()
    {
        var source = new IntSource();
        Func<CancellationToken, Task<int>>[] waits =
        [
            token => WaitAsync(source, token),
            _ => throw new InvalidOperationException("start failed"),
        ];

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => EventComposition.WaitAnyAsync(waits));

        Assert.Equal("start failed", exception.Message);
        Assert.Equal(0, source.HandlerCount);
    }

    [Fact]
    public async Task WaitAllReturnsHeterogeneousResultsAndCleansUpBothWaits()
    {
        var first = new IntSource();
        var second = new StringSource();

        var wait = EventComposition.WaitAllAsync(
            token => WaitAsync(first, token),
            token => EventAwaiter.WaitAsync<string>(
                handler => second.Changed += handler,
                handler => second.Changed -= handler,
                cancellationToken: token));

        second.Raise("ready");
        first.Raise(17);
        var result = await wait;

        Assert.Equal(17, result.First);
        Assert.Equal("ready", result.Second);
        Assert.Equal(0, first.HandlerCount);
        Assert.Equal(0, second.HandlerCount);
    }

    [Fact]
    public async Task WaitAllFaultCancelsPendingSibling()
    {
        var pending = new IntSource();

        var wait = EventComposition.WaitAllAsync(
            _ => Task.FromException<int>(new InvalidOperationException("boom")),
            token => WaitAsync(pending, token));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => wait);

        Assert.Equal("boom", exception.Message);
        Assert.Equal(0, pending.HandlerCount);
    }

    [Fact]
    public async Task IndexedWaitAllPreservesInputOrdering()
    {
        var first = new IntSource();
        var second = new IntSource();
        var third = new IntSource();
        Func<CancellationToken, Task<int>>[] waits =
        [
            token => WaitAsync(first, token),
            token => WaitAsync(second, token),
            token => WaitAsync(third, token),
        ];

        var wait = EventComposition.WaitAllAsync(waits);
        third.Raise(30);
        first.Raise(10);
        second.Raise(20);
        var results = await wait;

        Assert.Equal([10, 20, 30], results);
        Assert.Equal(0, first.HandlerCount);
        Assert.Equal(0, second.HandlerCount);
        Assert.Equal(0, third.HandlerCount);
    }

    [Fact]
    public async Task IndexedWaitAllFaultCancelsEveryPendingSibling()
    {
        var first = new IntSource();
        var third = new IntSource();
        Func<CancellationToken, Task<int>>[] waits =
        [
            token => WaitAsync(first, token),
            _ => Task.FromException<int>(new InvalidOperationException("boom")),
            token => WaitAsync(third, token),
        ];

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => EventComposition.WaitAllAsync(waits));

        Assert.Equal("boom", exception.Message);
        Assert.Equal(0, first.HandlerCount);
        Assert.Equal(0, third.HandlerCount);
    }

    private static Task<int> WaitAsync(IntSource source, CancellationToken cancellationToken) =>
        EventAwaiter.WaitAsync<int>(
            handler => source.Changed += handler,
            handler => source.Changed -= handler,
            cancellationToken: cancellationToken);

    private sealed class StrongSource
    {
        private EventHandler<StrongSource, int>? _changed;

        public event EventHandler<StrongSource, int>? Changed
        {
            add => _changed += value;
            remove => _changed -= value;
        }

        public int HandlerCount => _changed?.GetInvocationList().Length ?? 0;

        public void Raise(int value) => _changed?.Invoke(this, value);
    }

    private sealed class IntSource(bool throwOnRemove = false)
    {
        private EventHandler<int>? _changed;

        public event EventHandler<int>? Changed
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

        public int HandlerCount => _changed?.GetInvocationList().Length ?? 0;

        public void Raise(int value) => _changed?.Invoke(this, value);
    }

    private sealed class StringSource
    {
        private EventHandler<string>? _changed;

        public event EventHandler<string>? Changed
        {
            add => _changed += value;
            remove => _changed -= value;
        }

        public int HandlerCount => _changed?.GetInvocationList().Length ?? 0;

        public void Raise(string value) => _changed?.Invoke(this, value);
    }
}
