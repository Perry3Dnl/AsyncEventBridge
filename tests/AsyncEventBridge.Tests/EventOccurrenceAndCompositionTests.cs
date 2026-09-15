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
