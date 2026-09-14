namespace AsyncEventBridge.Tests;

public sealed class PublicValidationTests
{
    [Fact]
    public void EventAwaiterRejectsNullSubscriptionCallbacks()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = EventAwaiter.WaitAsync<TestEventArgs>(
                null!,
                _ => { });
        });

        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = EventAwaiter.WaitAsync<TestEventArgs>(
                _ => { },
                null!);
        });
    }

    [Fact]
    public void EventAwaiterRejectsInvalidTimeout()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _ = EventAwaiter.WaitAsync<TestEventArgs>(
                _ => { },
                _ => { },
                timeout: TimeSpan.FromMilliseconds(-2));
        });
    }

    [Fact]
    public void EventAwaiterAcceptsInfiniteTimeout()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var wait = EventAwaiter.WaitAsync<TestEventArgs>(
            _ => { },
            _ => { },
            cancellationToken: cancellation.Token,
            timeout: Timeout.InfiniteTimeSpan);

        Assert.True(wait.IsCanceled);
    }

    [Fact]
    public void EventStreamRejectsNullSubscriptionCallbacks()
    {
        Assert.Throws<ArgumentNullException>(() => EventStream.Create<TestEventArgs>(
            null!,
            _ => { }));

        Assert.Throws<ArgumentNullException>(() => EventStream.Create<TestEventArgs>(
            _ => { },
            null!));
    }

    [Fact]
    public async Task EventStreamSubscriptionFailureAfterPartialSubscribeCleansUp()
    {
        var source = new TestEventSource<TestEventArgs>();
        var expected = new InvalidOperationException("subscribe failed");
        var stream = EventStream.Create<TestEventArgs>(
            handler =>
            {
                source.Changed += handler;
                throw expected;
            },
            handler => source.Changed -= handler);

        var enumerator = stream.GetAsyncEnumerator();

        try
        {
            var actual = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await enumerator.MoveNextAsync().AsTask());

            Assert.Same(expected, actual);
            Assert.Equal(0, source.HandlerCount);
            Assert.Equal(1, source.RemoveCount);
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }

    [Fact]
    public void TaskBridgeDisposeBeforeCompletionSuppressesPublication()
    {
        var completion = new TaskCompletionSource<int>();
        EventBridge<int> bridge = completion.Task.ToEventBridge();
        var published = 0;

        bridge.Completed += (_, _) => published++;
        bridge.Faulted += (_, _) => published++;
        bridge.Cancelled += (_, _) => published++;
        bridge.Connect();
        bridge.Dispose();

        completion.SetResult(42);

        Assert.Equal(0, published);
    }
}
