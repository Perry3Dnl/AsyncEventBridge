namespace AsyncEventBridge.Tests;

public sealed class EventStreamTests
{
    [Fact]
    public async Task PublishesValuesInOrderAndUnsubscribesOnDispose()
    {
        var source = new TestEventSource<TestEventArgs>();
        var stream = EventStream.Create<TestEventArgs>(
            handler => source.Changed += handler,
            handler => source.Changed -= handler);
        var enumerator = stream.GetAsyncEnumerator();

        try
        {
            var firstMove = enumerator.MoveNextAsync().AsTask();
            source.Raise(new TestEventArgs(1));

            Assert.True(await firstMove);
            Assert.Equal(1, enumerator.Current.Value);

            var secondMove = enumerator.MoveNextAsync().AsTask();
            source.Raise(new TestEventArgs(2));

            Assert.True(await secondMove);
            Assert.Equal(2, enumerator.Current.Value);
            Assert.Equal(1, source.HandlerCount);
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        Assert.Equal(0, source.HandlerCount);
        Assert.Equal(1, source.AddCount);
        Assert.Equal(1, source.RemoveCount);
    }

    [Fact]
    public async Task PredicateFiltersValuesUntilMatch()
    {
        var source = new TestEventSource<TestEventArgs>();
        var stream = EventStream.Create<TestEventArgs>(
            handler => source.Changed += handler,
            handler => source.Changed -= handler,
            eventArgs => eventArgs.Value >= 10);
        var enumerator = stream.GetAsyncEnumerator();

        try
        {
            var move = enumerator.MoveNextAsync().AsTask();

            source.Raise(new TestEventArgs(5));
            Assert.False(move.IsCompleted);

            source.Raise(new TestEventArgs(10));

            Assert.True(await move);
            Assert.Equal(10, enumerator.Current.Value);
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }

    [Fact]
    public async Task PredicateExceptionFaultsStreamAndUnsubscribes()
    {
        var source = new TestEventSource<TestEventArgs>();
        var expected = new InvalidOperationException("predicate failed");
        var stream = EventStream.Create<TestEventArgs>(
            handler => source.Changed += handler,
            handler => source.Changed -= handler,
            _ => throw expected);
        var enumerator = stream.GetAsyncEnumerator();

        var move = enumerator.MoveNextAsync().AsTask();
        source.Raise(new TestEventArgs(1));

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(async () => await move);

        Assert.Same(expected, actual);
        Assert.Equal(0, source.HandlerCount);
        await enumerator.DisposeAsync();
    }

    [Fact]
    public async Task CancellationStopsEnumerationAndUnsubscribes()
    {
        var source = new TestEventSource<TestEventArgs>();
        using var cancellation = new CancellationTokenSource();
        var stream = EventStream.Create<TestEventArgs>(
            handler => source.Changed += handler,
            handler => source.Changed -= handler,
            cancellationToken: cancellation.Token);
        var enumerator = stream.GetAsyncEnumerator();

        var move = enumerator.MoveNextAsync().AsTask();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await move);

        Assert.Equal(0, source.HandlerCount);
        await enumerator.DisposeAsync();
    }

    [Fact]
    public async Task ReentrantEventDuringSubscriptionIsBuffered()
    {
        var source = new ReentrantSubscriptionSource();
        var stream = EventStream.Create<TestEventArgs>(
            handler => source.Changed += handler,
            handler => source.Changed -= handler);
        var enumerator = stream.GetAsyncEnumerator();

        try
        {
            Assert.True(await enumerator.MoveNextAsync());
            Assert.Equal(42, enumerator.Current.Value);
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        Assert.Equal(0, source.HandlerCount);
    }

    [Fact]
    public async Task NonGenericEventHandlerCanBeConsumedAsStream()
    {
        var source = new NonGenericEventSource();
        var expected = new EventArgs();
        var stream = EventStream.Create(
            handler => source.Tick += handler,
            handler => source.Tick -= handler);
        var enumerator = stream.GetAsyncEnumerator();

        try
        {
            var move = enumerator.MoveNextAsync().AsTask();
            source.Raise(expected);

            Assert.True(await move);
            Assert.Same(expected, enumerator.Current);
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        Assert.Equal(0, source.HandlerCount);
    }
}
