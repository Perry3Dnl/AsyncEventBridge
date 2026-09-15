namespace AsyncEventBridge.Tests;

public sealed class ModernEventPayloadTests
{
    [Fact]
    public async Task WaitAsyncSupportsValueTypePayload()
    {
        var source = new IntEventSource();
        var wait = EventAwaiter.WaitAsync<int>(
            handler => source.Changed += handler,
            handler => source.Changed -= handler,
            value => value >= 10);

        source.Raise(9);
        Assert.False(wait.IsCompleted);

        source.Raise(10);

        Assert.Equal(10, await wait);
        Assert.Equal(0, source.HandlerCount);
    }

    [Fact]
    public async Task StreamSupportsRecordStructPayload()
    {
        var source = new ReadingEventSource();
        await using var enumerator = EventStream.Create<Reading>(
            handler => source.Changed += handler,
            handler => source.Changed -= handler)
            .GetAsyncEnumerator();

        var moveNext = enumerator.MoveNextAsync().AsTask();
        source.Raise(new Reading(42, "sensor-a"));

        Assert.True(await moveNext);
        Assert.Equal(new Reading(42, "sensor-a"), enumerator.Current);

        await enumerator.DisposeAsync();
        Assert.Equal(0, source.HandlerCount);
    }

    private sealed class IntEventSource
    {
        private EventHandler<int>? _changed;

        internal event EventHandler<int>? Changed
        {
            add => _changed += value;
            remove => _changed -= value;
        }

        internal int HandlerCount => _changed?.GetInvocationList().Length ?? 0;

        internal void Raise(int value) => _changed?.Invoke(this, value);
    }

    private sealed class ReadingEventSource
    {
        private EventHandler<Reading>? _changed;

        internal event EventHandler<Reading>? Changed
        {
            add => _changed += value;
            remove => _changed -= value;
        }

        internal int HandlerCount => _changed?.GetInvocationList().Length ?? 0;

        internal void Raise(Reading value) => _changed?.Invoke(this, value);
    }

    private readonly record struct Reading(int Value, string SensorId);
}
