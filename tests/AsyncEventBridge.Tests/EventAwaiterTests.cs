namespace AsyncEventBridge.Tests;

public sealed class EventAwaiterTests
{
    [Fact]
    public async Task EventCompletesAndUnsubscribes()
    {
        var source = new TestEventSource<TestEventArgs>();
        var wait = Wait(source);

        source.Raise(new TestEventArgs(7));

        var result = await wait;

        Assert.Equal(7, result.Value);
        Assert.Equal(1, source.AddCount);
        Assert.Equal(1, source.RemoveCount);
        Assert.Equal(0, source.HandlerCount);
    }

    [Fact]
    public async Task NonGenericEventHandlerCompletesAndUnsubscribes()
    {
        var source = new NonGenericEventSource();
        var expected = new EventArgs();
        var wait = EventAwaiter.WaitAsync(
            handler => source.Tick += handler,
            handler => source.Tick -= handler);

        source.Raise(expected);

        var result = await wait;

        Assert.Same(expected, result);
        Assert.Equal(0, source.HandlerCount);
    }

    [Fact]
    public async Task PredicateFiltersUntilMatch()
    {
        var source = new TestEventSource<TestEventArgs>();
        var wait = Wait(source, eventArgs => eventArgs.Value == 100);

        source.Raise(new TestEventArgs(99));
        Assert.False(wait.IsCompleted);
        Assert.Equal(1, source.HandlerCount);

        source.Raise(new TestEventArgs(100));
        var result = await wait;

        Assert.Equal(100, result.Value);
        Assert.Equal(0, source.HandlerCount);
    }

    [Fact]
    public async Task PredicateExceptionFaultsAndUnsubscribes()
    {
        var source = new TestEventSource<TestEventArgs>();
        var wait = Wait(source, _ => throw new InvalidOperationException("predicate failed"));

        source.Raise(new TestEventArgs(1));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => wait);
        Assert.Equal("predicate failed", exception.Message);
        Assert.Equal(0, source.HandlerCount);
    }

    [Fact]
    public async Task SubscriptionFailureAfterPartialSubscribeFaultsAndCleansUp()
    {
        var source = new TestEventSource<TestEventArgs>();
        var wait = EventAwaiter.WaitAsync<TestEventArgs>(
            handler =>
            {
                source.Changed += handler;
                throw new InvalidOperationException("subscribe failed");
            },
            handler => source.Changed -= handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => wait);

        Assert.Equal("subscribe failed", exception.Message);
        Assert.Equal(0, source.HandlerCount);
        Assert.Equal(1, source.RemoveCount);
    }

    [Fact]
    public async Task CancellationCancelsAndUnsubscribes()
    {
        var source = new TestEventSource<TestEventArgs>();
        using var cancellation = new CancellationTokenSource();
        var wait = Wait(source, cancellationToken: cancellation.Token);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
        Assert.True(wait.IsCanceled);
        Assert.Equal(0, source.HandlerCount);
    }

    [Fact]
    public async Task CancellationDuringSubscriptionCancelsAndCleansUp()
    {
        var source = new TestEventSource<TestEventArgs>();
        using var cancellation = new CancellationTokenSource();
        var wait = EventAwaiter.WaitAsync<TestEventArgs>(
            handler =>
            {
                source.Changed += handler;
                cancellation.Cancel();
            },
            handler => source.Changed -= handler,
            cancellationToken: cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);

        Assert.True(wait.IsCanceled);
        Assert.Equal(0, source.HandlerCount);
        Assert.Equal(1, source.RemoveCount);
    }

    [Fact]
    public async Task PreCancelledTokenDoesNotSubscribe()
    {
        var source = new TestEventSource<TestEventArgs>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var wait = Wait(source, cancellationToken: cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
        Assert.Equal(0, source.AddCount);
        Assert.Equal(0, source.RemoveCount);
    }

    [Fact]
    public async Task TimeoutFaultsAndUnsubscribesUsingInjectedTimeProvider()
    {
        var source = new TestEventSource<TestEventArgs>();
        var timeProvider = new ManualTimeProvider();
        var wait = EventAwaiter.WaitAsync<TestEventArgs>(
            handler => source.Changed += handler,
            handler => source.Changed -= handler,
            predicate: null,
            cancellationToken: default,
            timeout: TimeSpan.FromMinutes(1),
            timeProvider: timeProvider);

        timeProvider.Fire();

        await Assert.ThrowsAsync<TimeoutException>(() => wait);
        Assert.Equal(0, source.HandlerCount);
    }

    [Fact]
    public async Task TimeoutDuringTimerCreationFaultsAndCleansUp()
    {
        var source = new TestEventSource<TestEventArgs>();
        var wait = EventAwaiter.WaitAsync<TestEventArgs>(
            handler => source.Changed += handler,
            handler => source.Changed -= handler,
            predicate: null,
            cancellationToken: default,
            timeout: TimeSpan.FromMinutes(1),
            timeProvider: new ManualTimeProvider(fireOnCreate: true));

        await Assert.ThrowsAsync<TimeoutException>(() => wait);

        Assert.Equal(0, source.HandlerCount);
        Assert.Equal(1, source.RemoveCount);
    }

    [Fact]
    public async Task ZeroTimeoutDoesNotSubscribe()
    {
        var source = new TestEventSource<TestEventArgs>();
        var wait = EventAwaiter.WaitAsync<TestEventArgs>(
            handler => source.Changed += handler,
            handler => source.Changed -= handler,
            timeout: TimeSpan.Zero);

        await Assert.ThrowsAsync<TimeoutException>(() => wait);
        Assert.Equal(0, source.AddCount);
    }

    [Fact]
    public async Task EventDuringSubscriptionCompletesAndCleansUp()
    {
        var source = new ReentrantSubscriptionSource();

        var result = await EventAwaiter.WaitAsync<TestEventArgs>(
            handler => source.Changed += handler,
            handler => source.Changed -= handler,
            eventArgs => eventArgs.Value == 42);

        Assert.Equal(42, result.Value);
        Assert.Equal(0, source.HandlerCount);
    }

    [Fact]
    public async Task ConcurrentWaitsRemainIndependent()
    {
        var source = new TestEventSource<TestEventArgs>();
        var first = Wait(source, eventArgs => eventArgs.Value == 1);
        var second = Wait(source, eventArgs => eventArgs.Value == 2);

        Assert.Equal(2, source.HandlerCount);

        source.Raise(new TestEventArgs(1));
        Assert.Equal(1, (await first).Value);
        Assert.False(second.IsCompleted);
        Assert.Equal(1, source.HandlerCount);

        source.Raise(new TestEventArgs(2));
        Assert.Equal(2, (await second).Value);
        Assert.Equal(0, source.HandlerCount);
    }

    [Fact]
    public async Task MoreThanOneHundredParallelWaitsCompleteIndependently()
    {
        const int count = 128;
        var source = new TestEventSource<TestEventArgs>();
        var waits = Enumerable.Range(0, count)
            .Select(value => Wait(source, eventArgs => eventArgs.Value == value))
            .ToArray();

        Assert.Equal(count, source.HandlerCount);

        for (var value = count - 1; value >= 0; value--)
        {
            source.Raise(new TestEventArgs(value));
        }

        var results = await Task.WhenAll(waits);

        Assert.Equal(Enumerable.Range(0, count), results.Select(result => result.Value));
        Assert.Equal(0, source.HandlerCount);
        Assert.Equal(count, source.RemoveCount);
    }

    [Fact]
    public async Task EventAndCancellationRaceHasExactlyOneOutcome()
    {
        var source = new TestEventSource<TestEventArgs>();
        using var cancellation = new CancellationTokenSource();
        using var barrier = new Barrier(3);
        var wait = Wait(source, cancellationToken: cancellation.Token);

        var raiseTask = Task.Run(() =>
        {
            barrier.SignalAndWait();
            source.Raise(new TestEventArgs(5));
        });

        var cancelTask = Task.Run(() =>
        {
            barrier.SignalAndWait();
            cancellation.Cancel();
        });

        barrier.SignalAndWait();
        await Task.WhenAll(raiseTask, cancelTask);

        await AssertSingleEventOrCancellationOutcome(wait, 5);
        Assert.Equal(0, source.HandlerCount);
        Assert.Equal(1, source.RemoveCount);
    }

    [Fact]
    public async Task EventAndTimeoutRaceHasExactlyOneOutcome()
    {
        var source = new TestEventSource<TestEventArgs>();
        var timeProvider = new ManualTimeProvider();
        using var barrier = new Barrier(3);
        var wait = EventAwaiter.WaitAsync<TestEventArgs>(
            handler => source.Changed += handler,
            handler => source.Changed -= handler,
            predicate: null,
            cancellationToken: default,
            timeout: TimeSpan.FromHours(1),
            timeProvider: timeProvider);

        var raiseTask = Task.Run(() =>
        {
            barrier.SignalAndWait();
            source.Raise(new TestEventArgs(6));
        });

        var timeoutTask = Task.Run(() =>
        {
            barrier.SignalAndWait();
            timeProvider.Fire();
        });

        barrier.SignalAndWait();
        await Task.WhenAll(raiseTask, timeoutTask);

        try
        {
            Assert.Equal(6, (await wait).Value);
        }
        catch (TimeoutException)
        {
            Assert.True(wait.IsFaulted);
        }

        Assert.Equal(0, source.HandlerCount);
        Assert.Equal(1, source.RemoveCount);
    }

    [Fact]
    public async Task CancellationAndTimeoutRaceHasExactlyOneOutcome()
    {
        var source = new TestEventSource<TestEventArgs>();
        var timeProvider = new ManualTimeProvider();
        using var cancellation = new CancellationTokenSource();
        using var barrier = new Barrier(3);
        var wait = EventAwaiter.WaitAsync<TestEventArgs>(
            handler => source.Changed += handler,
            handler => source.Changed -= handler,
            predicate: null,
            cancellationToken: cancellation.Token,
            timeout: TimeSpan.FromHours(1),
            timeProvider: timeProvider);

        var cancelTask = Task.Run(() =>
        {
            barrier.SignalAndWait();
            cancellation.Cancel();
        });

        var timeoutTask = Task.Run(() =>
        {
            barrier.SignalAndWait();
            timeProvider.Fire();
        });

        barrier.SignalAndWait();
        await Task.WhenAll(cancelTask, timeoutTask);

        try
        {
            await wait;
            Assert.Fail("The wait must not complete successfully.");
        }
        catch (OperationCanceledException)
        {
            Assert.True(wait.IsCanceled);
        }
        catch (TimeoutException)
        {
            Assert.True(wait.IsFaulted);
        }

        Assert.Equal(0, source.HandlerCount);
        Assert.Equal(1, source.RemoveCount);
    }

    [Fact]
    public async Task TimeoutAndUnsubscribeFailureAreAggregatedInOrder()
    {
        var timeProvider = new ManualTimeProvider();
        var cleanupFailure = new InvalidOperationException("unsubscribe failed");
        var wait = EventAwaiter.WaitAsync<TestEventArgs>(
            _ => { },
            _ => throw cleanupFailure,
            timeout: TimeSpan.FromMinutes(1),
            timeProvider: timeProvider);

        timeProvider.Fire();

        var actual = await Assert.ThrowsAsync<AggregateException>(() => wait);

        Assert.Equal(2, actual.InnerExceptions.Count);
        Assert.IsType<TimeoutException>(actual.InnerExceptions[0]);
        Assert.Same(cleanupFailure, actual.InnerExceptions[1]);
        Assert.True(wait.IsFaulted);
    }

    [Fact]
    public async Task SuccessfulWaitWithUnsubscribeFailureFaultsWithCleanupException()
    {
        EventHandler<TestEventArgs>? handler = null;
        var cleanupFailure = new InvalidOperationException("unsubscribe failed");
        var wait = EventAwaiter.WaitAsync<TestEventArgs>(
            subscribedHandler => handler = subscribedHandler,
            _ => throw cleanupFailure);

        handler!(null, new TestEventArgs(1));

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => wait);

        Assert.Same(cleanupFailure, actual);
    }

    [Fact]
    public async Task PredicateFailureAndUnsubscribeFailureAreAggregatedInOrder()
    {
        EventHandler<TestEventArgs>? handler = null;
        var primaryFailure = new InvalidOperationException("predicate failed");
        var cleanupFailure = new ApplicationException("unsubscribe failed");
        var wait = EventAwaiter.WaitAsync<TestEventArgs>(
            subscribedHandler => handler = subscribedHandler,
            _ => throw cleanupFailure,
            _ => throw primaryFailure);

        handler!(null, new TestEventArgs(1));

        var actual = await Assert.ThrowsAsync<AggregateException>(() => wait);

        Assert.Equal(2, actual.InnerExceptions.Count);
        Assert.Same(primaryFailure, actual.InnerExceptions[0]);
        Assert.Same(cleanupFailure, actual.InnerExceptions[1]);
    }

    [Fact]
    public async Task CancellationAndUnsubscribeFailureAreAggregatedInOrder()
    {
        using var cancellation = new CancellationTokenSource();
        var cleanupFailure = new InvalidOperationException("unsubscribe failed");
        var wait = EventAwaiter.WaitAsync<TestEventArgs>(
            _ => { },
            _ => throw cleanupFailure,
            cancellationToken: cancellation.Token);

        cancellation.Cancel();

        var actual = await Assert.ThrowsAsync<AggregateException>(() => wait);

        Assert.Equal(2, actual.InnerExceptions.Count);
        Assert.IsAssignableFrom<OperationCanceledException>(actual.InnerExceptions[0]);
        Assert.Same(cleanupFailure, actual.InnerExceptions[1]);
        Assert.True(wait.IsFaulted);
    }


    private static Task<TestEventArgs> Wait(
        TestEventSource<TestEventArgs> source,
        Predicate<TestEventArgs>? predicate = null,
        CancellationToken cancellationToken = default) =>
        EventAwaiter.WaitAsync<TestEventArgs>(
            handler => source.Changed += handler,
            handler => source.Changed -= handler,
            predicate,
            cancellationToken);

    private static async Task AssertSingleEventOrCancellationOutcome(Task<TestEventArgs> wait, int expectedValue)
    {
        try
        {
            Assert.Equal(expectedValue, (await wait).Value);
            Assert.True(wait.IsCompletedSuccessfully);
        }
        catch (OperationCanceledException)
        {
            Assert.True(wait.IsCanceled);
        }
    }
}
