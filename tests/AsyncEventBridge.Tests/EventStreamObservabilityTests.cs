namespace AsyncEventBridge.Tests;

public sealed class EventStreamObservabilityTests
{
    [Fact]
    public async Task DropOldestTracksDroppedValuesAndObserverCounts()
    {
        var source = new TestEventSource<TestEventArgs>();
        var observedCounts = new List<long>();
        var options = new EventStreamOptions
        {
            Capacity = 2,
            FullMode = EventStreamFullMode.DropOldest,
            DropObserver = observedCounts.Add,
        };
        var stream = EventStream.Create<TestEventArgs>(
            handler => source.Changed += handler,
            handler => source.Changed -= handler,
            options: options);
        await using var enumerator = stream.GetAsyncEnumerator();

        var firstMove = enumerator.MoveNextAsync().AsTask();
        source.Raise(new TestEventArgs(0));
        Assert.True(await firstMove);

        source.Raise(new TestEventArgs(1));
        source.Raise(new TestEventArgs(2));
        source.Raise(new TestEventArgs(3));
        source.Raise(new TestEventArgs(4));

        Assert.Equal(2, options.DroppedCount);
        Assert.Equal(new long[] { 1, 2 }, observedCounts);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(3, enumerator.Current.Value);
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(4, enumerator.Current.Value);
    }

    [Fact]
    public async Task DropWriteTracksDroppedValuesWithoutChangingBufferedValues()
    {
        var source = new TestEventSource<TestEventArgs>();
        var observedCounts = new List<long>();
        var options = new EventStreamOptions
        {
            Capacity = 2,
            FullMode = EventStreamFullMode.DropWrite,
            DropObserver = observedCounts.Add,
        };
        var stream = EventStream.Create<TestEventArgs>(
            handler => source.Changed += handler,
            handler => source.Changed -= handler,
            options: options);
        await using var enumerator = stream.GetAsyncEnumerator();

        var firstMove = enumerator.MoveNextAsync().AsTask();
        source.Raise(new TestEventArgs(0));
        Assert.True(await firstMove);

        source.Raise(new TestEventArgs(1));
        source.Raise(new TestEventArgs(2));
        source.Raise(new TestEventArgs(3));
        source.Raise(new TestEventArgs(4));

        Assert.Equal(2, options.DroppedCount);
        Assert.Equal(new long[] { 1, 2 }, observedCounts);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(1, enumerator.Current.Value);
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(2, enumerator.Current.Value);
    }

    [Fact]
    public async Task OptionsAreSnapshottedAtStreamCreationWhileDropCountRemainsLive()
    {
        var source = new TestEventSource<TestEventArgs>();
        var originalObserverCounts = new List<long>();
        var replacementObserverCalls = 0;
        var options = new EventStreamOptions
        {
            Capacity = 1,
            FullMode = EventStreamFullMode.DropOldest,
            DropObserver = originalObserverCounts.Add,
        };
        var stream = EventStream.Create<TestEventArgs>(
            handler => source.Changed += handler,
            handler => source.Changed -= handler,
            options: options);

        options.Capacity = 100;
        options.FullMode = EventStreamFullMode.Unbounded;
        options.DropObserver = _ => replacementObserverCalls++;

        await using var enumerator = stream.GetAsyncEnumerator();

        var firstMove = enumerator.MoveNextAsync().AsTask();
        source.Raise(new TestEventArgs(0));
        Assert.True(await firstMove);
        Assert.Equal(0, enumerator.Current.Value);

        source.Raise(new TestEventArgs(1));
        source.Raise(new TestEventArgs(2));

        Assert.Equal(1, options.DroppedCount);
        Assert.Equal(new long[] { 1 }, originalObserverCounts);
        Assert.Equal(0, replacementObserverCalls);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(2, enumerator.Current.Value);
    }

    [Fact]
    public async Task DropObserverExceptionDoesNotFaultStream()
    {
        var source = new TestEventSource<TestEventArgs>();
        var options = new EventStreamOptions
        {
            Capacity = 1,
            FullMode = EventStreamFullMode.DropOldest,
            DropObserver = _ => throw new InvalidOperationException("telemetry failed"),
        };
        var stream = EventStream.Create<TestEventArgs>(
            handler => source.Changed += handler,
            handler => source.Changed -= handler,
            options: options);
        await using var enumerator = stream.GetAsyncEnumerator();

        var firstMove = enumerator.MoveNextAsync().AsTask();
        source.Raise(new TestEventArgs(0));
        Assert.True(await firstMove);

        source.Raise(new TestEventArgs(1));
        source.Raise(new TestEventArgs(2));

        Assert.Equal(1, options.DroppedCount);
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(2, enumerator.Current.Value);
    }

    [Fact]
    public async Task UnboundedModeDoesNotReportDrops()
    {
        var source = new TestEventSource<TestEventArgs>();
        var observerCalls = 0;
        var options = new EventStreamOptions
        {
            Capacity = 1,
            FullMode = EventStreamFullMode.Unbounded,
            DropObserver = _ => observerCalls++,
        };
        var stream = EventStream.Create<TestEventArgs>(
            handler => source.Changed += handler,
            handler => source.Changed -= handler,
            options: options);
        await using var enumerator = stream.GetAsyncEnumerator();

        var firstMove = enumerator.MoveNextAsync().AsTask();
        source.Raise(new TestEventArgs(0));
        Assert.True(await firstMove);

        source.Raise(new TestEventArgs(1));
        source.Raise(new TestEventArgs(2));
        source.Raise(new TestEventArgs(3));

        Assert.Equal(0, options.DroppedCount);
        Assert.Equal(0, observerCalls);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(1, enumerator.Current.Value);
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(2, enumerator.Current.Value);
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(3, enumerator.Current.Value);
    }
}
