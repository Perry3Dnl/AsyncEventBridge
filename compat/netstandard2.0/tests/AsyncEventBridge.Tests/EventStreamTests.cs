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

    [Fact]
    public async Task UnboundedIgnoresCapacityAndPreservesValues()
    {
        var source = new TestEventSource<TestEventArgs>();
        var options = new EventStreamOptions
        {
            Capacity = -1,
            FullMode = EventStreamFullMode.Unbounded,
        };
        var stream = EventStream.Create<TestEventArgs>(
            handler => source.Changed += handler,
            handler => source.Changed -= handler,
            options: options);
        var enumerator = stream.GetAsyncEnumerator();

        try
        {
            var initialMove = enumerator.MoveNextAsync().AsTask();
            source.Raise(new TestEventArgs(0));
            Assert.True(await initialMove);

            source.Raise(new TestEventArgs(1));
            source.Raise(new TestEventArgs(2));
            source.Raise(new TestEventArgs(3));

            Assert.True(await enumerator.MoveNextAsync());
            Assert.Equal(1, enumerator.Current.Value);
            Assert.True(await enumerator.MoveNextAsync());
            Assert.Equal(2, enumerator.Current.Value);
            Assert.True(await enumerator.MoveNextAsync());
            Assert.Equal(3, enumerator.Current.Value);
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }

    [Fact]
    public async Task DropOldestKeepsNewestBufferedValues()
    {
        var source = new TestEventSource<TestEventArgs>();
        var options = new EventStreamOptions
        {
            Capacity = 2,
            FullMode = EventStreamFullMode.DropOldest,
        };
        var stream = EventStream.Create<TestEventArgs>(
            handler => source.Changed += handler,
            handler => source.Changed -= handler,
            options: options);
        var enumerator = stream.GetAsyncEnumerator();

        try
        {
            var initialMove = enumerator.MoveNextAsync().AsTask();
            source.Raise(new TestEventArgs(0));
            Assert.True(await initialMove);

            source.Raise(new TestEventArgs(1));
            source.Raise(new TestEventArgs(2));
            source.Raise(new TestEventArgs(3));

            Assert.True(await enumerator.MoveNextAsync());
            Assert.Equal(2, enumerator.Current.Value);
            Assert.True(await enumerator.MoveNextAsync());
            Assert.Equal(3, enumerator.Current.Value);
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }

    [Fact]
    public async Task DropWriteKeepsExistingBufferedValues()
    {
        var source = new TestEventSource<TestEventArgs>();
        var options = new EventStreamOptions
        {
            Capacity = 2,
            FullMode = EventStreamFullMode.DropWrite,
        };
        var stream = EventStream.Create<TestEventArgs>(
            handler => source.Changed += handler,
            handler => source.Changed -= handler,
            options: options);
        var enumerator = stream.GetAsyncEnumerator();

        try
        {
            var initialMove = enumerator.MoveNextAsync().AsTask();
            source.Raise(new TestEventArgs(0));
            Assert.True(await initialMove);

            source.Raise(new TestEventArgs(1));
            source.Raise(new TestEventArgs(2));
            source.Raise(new TestEventArgs(3));

            Assert.True(await enumerator.MoveNextAsync());
            Assert.Equal(1, enumerator.Current.Value);
            Assert.True(await enumerator.MoveNextAsync());
            Assert.Equal(2, enumerator.Current.Value);
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }

    [Fact]
    public async Task DisposeAsyncSurfacesUnsubscribeFailure()
    {
        EventHandler<TestEventArgs>? handler = null;
        var cleanupFailure = new InvalidOperationException("unsubscribe failed");
        var stream = EventStream.Create<TestEventArgs>(
            subscribedHandler => handler = subscribedHandler,
            _ => throw cleanupFailure);
        var enumerator = stream.GetAsyncEnumerator();

        var move = enumerator.MoveNextAsync().AsTask();
        handler!(null, new TestEventArgs(1));
        Assert.True(await move);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await enumerator.DisposeAsync());

        Assert.Same(cleanupFailure, actual);
    }

    [Fact]
    public async Task PredicateFailureAndUnsubscribeFailureAreAggregatedInOrder()
    {
        EventHandler<TestEventArgs>? handler = null;
        var primaryFailure = new InvalidOperationException("predicate failed");
        var cleanupFailure = new ApplicationException("unsubscribe failed");
        var stream = EventStream.Create<TestEventArgs>(
            subscribedHandler => handler = subscribedHandler,
            _ => throw cleanupFailure,
            _ => throw primaryFailure);
        var enumerator = stream.GetAsyncEnumerator();

        var move = enumerator.MoveNextAsync().AsTask();
        handler!(null, new TestEventArgs(1));

        var actual = await Assert.ThrowsAsync<AggregateException>(async () => await move);

        Assert.Equal(2, actual.InnerExceptions.Count);
        Assert.Same(primaryFailure, actual.InnerExceptions[0]);
        Assert.Same(cleanupFailure, actual.InnerExceptions[1]);
    }

    [Fact]
    public async Task CancellationAndUnsubscribeFailureAreAggregatedInOrder()
    {
        using var cancellation = new CancellationTokenSource();
        var cleanupFailure = new InvalidOperationException("unsubscribe failed");
        var stream = EventStream.Create<TestEventArgs>(
            _ => { },
            _ => throw cleanupFailure,
            cancellationToken: cancellation.Token);
        var enumerator = stream.GetAsyncEnumerator();

        var move = enumerator.MoveNextAsync().AsTask();
        cancellation.Cancel();

        var actual = await Assert.ThrowsAsync<AggregateException>(async () => await move);

        Assert.Equal(2, actual.InnerExceptions.Count);
        Assert.IsAssignableFrom<OperationCanceledException>(actual.InnerExceptions[0]);
        Assert.Same(cleanupFailure, actual.InnerExceptions[1]);
    }


    [Fact]
    public void RejectsNonPositiveCapacityForBoundedModes()
    {
        var source = new TestEventSource<TestEventArgs>();
        var options = new EventStreamOptions
        {
            Capacity = 0,
            FullMode = EventStreamFullMode.DropOldest,
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => EventStream.Create<TestEventArgs>(
            handler => source.Changed += handler,
            handler => source.Changed -= handler,
            options: options));
    }

    [Fact]
    public void RejectsUnknownFullMode()
    {
        var source = new TestEventSource<TestEventArgs>();
        var options = new EventStreamOptions { FullMode = (EventStreamFullMode)999 };

        Assert.Throws<ArgumentOutOfRangeException>(() => EventStream.Create<TestEventArgs>(
            handler => source.Changed += handler,
            handler => source.Changed -= handler,
            options: options));
    }
}
