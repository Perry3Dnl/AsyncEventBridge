using System.Threading.Channels;

namespace AsyncEventBridge.Tests;

public sealed class EventStreamBridgeTests
{
    [Fact]
    public async Task PublishesValuesInOrderAndCompletes()
    {
        await using EventStreamBridge<int> bridge = Values(1, 2, 3).ToEventBridge();
        var values = new List<int>();
        var completed = NewCompletionSource();
        var faulted = 0;
        var cancelled = 0;

        bridge.Value += (_, e) => values.Add(e.Value);
        bridge.Completed += (_, _) => completed.TrySetResult(true);
        bridge.Faulted += (_, _) => Interlocked.Increment(ref faulted);
        bridge.Cancelled += (_, _) => Interlocked.Increment(ref cancelled);

        bridge.Connect();

        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal([1, 2, 3], values);
        Assert.Equal(0, faulted);
        Assert.Equal(0, cancelled);
    }

    [Fact]
    public async Task EmptyStreamPublishesCompleted()
    {
        await using EventStreamBridge<int> bridge = EmptyValues().ToEventBridge();
        var completed = NewCompletionSource();
        var values = 0;
        var faulted = 0;
        var cancelled = 0;

        bridge.Value += (_, _) => Interlocked.Increment(ref values);
        bridge.Completed += (_, _) => completed.TrySetResult(true);
        bridge.Faulted += (_, _) => Interlocked.Increment(ref faulted);
        bridge.Cancelled += (_, _) => Interlocked.Increment(ref cancelled);

        bridge.Connect();

        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, values);
        Assert.Equal(0, faulted);
        Assert.Equal(0, cancelled);
    }

    [Fact]
    public async Task PublishesFaultAndStopsWithSingleTerminalOutcome()
    {
        var expected = new InvalidOperationException("sensor stream failed");
        await using EventStreamBridge<int> bridge = FaultingValues(expected).ToEventBridge();
        var values = new List<int>();
        var faulted = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = 0;
        var cancelled = 0;

        bridge.Value += (_, e) => values.Add(e.Value);
        bridge.Completed += (_, _) => Interlocked.Increment(ref completed);
        bridge.Faulted += (_, e) => faulted.TrySetResult(e.Exception);
        bridge.Cancelled += (_, _) => Interlocked.Increment(ref cancelled);

        bridge.Connect();

        var exception = await faulted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(expected, exception);
        Assert.Equal([1], values);
        Assert.Equal(0, completed);
        Assert.Equal(0, cancelled);
    }

    [Fact]
    public async Task CancellationPublishesExactlyOneCancelledTerminalOutcome()
    {
        var channel = Channel.CreateUnbounded<int>();
        using var cancellation = new CancellationTokenSource();
        await using EventStreamBridge<int> bridge = channel.Reader.ReadAllAsync().ToEventBridge();
        var cancelled = NewCompletionSource();
        var completed = 0;
        var faulted = 0;
        var cancelledCount = 0;

        bridge.Completed += (_, _) => Interlocked.Increment(ref completed);
        bridge.Faulted += (_, _) => Interlocked.Increment(ref faulted);
        bridge.Cancelled += (_, _) =>
        {
            Interlocked.Increment(ref cancelledCount);
            cancelled.TrySetResult(true);
        };

        bridge.Connect(cancellation.Token);
        cancellation.Cancel();

        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        channel.Writer.TryComplete();
        await bridge.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, completed);
        Assert.Equal(0, faulted);
        Assert.Equal(1, Volatile.Read(ref cancelledCount));
    }

    [Fact]
    public async Task CancellationAfterCompletionDoesNotPublishSecondTerminalOutcome()
    {
        var channel = Channel.CreateUnbounded<int>();
        using var cancellation = new CancellationTokenSource();
        await using EventStreamBridge<int> bridge = channel.Reader.ReadAllAsync().ToEventBridge();
        var completed = NewCompletionSource();
        var completedCount = 0;
        var faulted = 0;
        var cancelled = 0;

        bridge.Completed += (_, _) =>
        {
            Interlocked.Increment(ref completedCount);
            completed.TrySetResult(true);
        };
        bridge.Faulted += (_, _) => Interlocked.Increment(ref faulted);
        bridge.Cancelled += (_, _) => Interlocked.Increment(ref cancelled);

        bridge.Connect(cancellation.Token);
        channel.Writer.TryComplete();

        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();
        await bridge.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, Volatile.Read(ref completedCount));
        Assert.Equal(0, Volatile.Read(ref faulted));
        Assert.Equal(0, Volatile.Read(ref cancelled));
    }


    [Fact]
    public async Task DisposeAsyncStopsEnumerationWithoutPublishingCancelled()
    {
        var channel = Channel.CreateUnbounded<int>();
        var bridge = channel.Reader.ReadAllAsync().ToEventBridge();
        var cancelled = 0;
        var completed = 0;
        var faulted = 0;

        bridge.Completed += (_, _) => Interlocked.Increment(ref completed);
        bridge.Faulted += (_, _) => Interlocked.Increment(ref faulted);
        bridge.Cancelled += (_, _) => Interlocked.Increment(ref cancelled);

        bridge.Connect();
        await bridge.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, completed);
        Assert.Equal(0, faulted);
        Assert.Equal(0, cancelled);
    }

    [Fact]
    public async Task DisposeReturnsWhileInFlightValueHandlerIsStillRunning()
    {
        var channel = Channel.CreateUnbounded<int>();
        var bridge = channel.Reader.ReadAllAsync().ToEventBridge();
        var handlerEntered = NewCompletionSource();
        var handlerFinished = NewCompletionSource();
        using var releaseHandler = new ManualResetEventSlim(false);

        bridge.Value += (_, _) =>
        {
            handlerEntered.TrySetResult(true);
            releaseHandler.Wait();
            handlerFinished.TrySetResult(true);
        };

        bridge.Connect();
        Assert.True(channel.Writer.TryWrite(1));
        await handlerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var disposeTask = Task.Factory.StartNew(
            bridge.Dispose,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(handlerFinished.Task.IsCompleted);

        releaseHandler.Set();
        await handlerFinished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await bridge.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task DisposeAsyncWaitsForInFlightValueHandlerAndStopsFuturePublication()
    {
        var channel = Channel.CreateUnbounded<int>();
        var bridge = channel.Reader.ReadAllAsync().ToEventBridge();
        var handlerEntered = NewCompletionSource();
        using var releaseHandler = new ManualResetEventSlim(false);
        var publishedValues = 0;

        bridge.Value += (_, _) =>
        {
            handlerEntered.TrySetResult(true);
            releaseHandler.Wait();
            Interlocked.Increment(ref publishedValues);
        };

        bridge.Connect();
        Assert.True(channel.Writer.TryWrite(1));
        await handlerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var disposeTask = bridge.DisposeAsync().AsTask();

        try
        {
            Assert.False(disposeTask.IsCompleted);
        }
        finally
        {
            releaseHandler.Set();
        }

        await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, Volatile.Read(ref publishedValues));

        Assert.True(channel.Writer.TryWrite(2));
        Assert.Equal(1, Volatile.Read(ref publishedValues));
    }

    [Fact]
    public async Task SubscriberAddedDuringValuePublicationStartsWithNextValue()
    {
        await using EventStreamBridge<int> bridge = Values(1, 2).ToEventBridge();
        var completed = NewCompletionSource();
        var lateValues = new List<int>();
        EventHandler<AsyncValueEventArgs<int>> lateHandler =
            (_, eventArgs) => lateValues.Add(eventArgs.Value);

        bridge.Value += (_, eventArgs) =>
        {
            if (eventArgs.Value == 1)
            {
                bridge.Value += lateHandler;
            }
        };
        bridge.Completed += (_, _) => completed.TrySetResult(true);

        bridge.Connect();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new[] { 2 }, lateValues);
    }

    [Fact]
    public async Task SubscriberRemovedDuringValuePublicationStillReceivesCurrentValueOnly()
    {
        await using EventStreamBridge<int> bridge = Values(1, 2).ToEventBridge();
        var completed = NewCompletionSource();
        var observed = new List<int>();
        EventHandler<AsyncValueEventArgs<int>> removable =
            (_, eventArgs) => observed.Add(eventArgs.Value);

        bridge.Value += (_, eventArgs) =>
        {
            if (eventArgs.Value == 1)
            {
                bridge.Value -= removable;
            }
        };
        bridge.Value += removable;
        bridge.Completed += (_, _) => completed.TrySetResult(true);

        bridge.Connect();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new[] { 1 }, observed);
    }

    [Fact]
    public async Task DisposeFromValueSubscriberDoesNotInterruptCapturedSubscribers()
    {
        var bridge = Values(1, 2).ToEventBridge();
        var currentSnapshotFinished = NewCompletionSource();
        var observed = new List<int>();

        bridge.Value += (_, eventArgs) =>
        {
            observed.Add(eventArgs.Value);
            bridge.Dispose();
        };
        bridge.Value += (_, eventArgs) =>
        {
            observed.Add(eventArgs.Value);
            currentSnapshotFinished.TrySetResult(true);
        };

        bridge.Connect();

        await currentSnapshotFinished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await bridge.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new[] { 1, 1 }, observed);
    }

    [Fact]
    public async Task DisposeAsyncWaitsForInFlightTerminalHandler()
    {
        var bridge = EmptyValues().ToEventBridge();
        var currentSnapshotFinished = NewCompletionSource();
        Task? disposeTask = null;
        var disposeCompletedInsideHandler = 1;

        bridge.Completed += (_, _) =>
        {
            disposeTask = bridge.DisposeAsync().AsTask();
            Volatile.Write(ref disposeCompletedInsideHandler, disposeTask.IsCompleted ? 1 : 0);
        };
        bridge.Completed += (_, _) => currentSnapshotFinished.TrySetResult(true);

        bridge.Connect();

        await currentSnapshotFinished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var capturedDisposeTask = Assert.IsType<Task>(disposeTask);
        Assert.Equal(0, Volatile.Read(ref disposeCompletedInsideHandler));
        await capturedDisposeTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SubscriberAddedAfterTerminalPublicationReceivesNoReplay()
    {
        var bridge = EmptyValues().ToEventBridge();
        var completed = NewCompletionSource();
        var lateCalls = 0;

        bridge.Completed += (_, _) => completed.TrySetResult(true);
        bridge.Connect();

        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        bridge.Completed += (_, _) => Interlocked.Increment(ref lateCalls);

        Assert.Equal(0, Volatile.Read(ref lateCalls));
        await bridge.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }


    [Fact]
    public async Task RemovedValueSubscriberIsNotInvoked()
    {
        var channel = Channel.CreateUnbounded<int>();
        await using EventStreamBridge<int> bridge = channel.Reader.ReadAllAsync().ToEventBridge();
        var completed = NewCompletionSource();
        var calls = 0;
        EventHandler<AsyncValueEventArgs<int>> handler = (_, _) => Interlocked.Increment(ref calls);

        bridge.Value += handler;
        bridge.Value -= handler;
        bridge.Completed += (_, _) => completed.TrySetResult(true);

        bridge.Connect();
        Assert.True(channel.Writer.TryWrite(1));
        channel.Writer.TryComplete();

        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task LateValueSubscriberReceivesNoReplay()
    {
        var channel = Channel.CreateUnbounded<int>();
        await using EventStreamBridge<int> bridge = channel.Reader.ReadAllAsync().ToEventBridge();
        var firstValuePublished = NewCompletionSource();
        var completed = NewCompletionSource();
        var earlyValues = new List<int>();
        var lateValues = new List<int>();

        bridge.Value += (_, e) =>
        {
            earlyValues.Add(e.Value);

            if (e.Value == 1)
            {
                firstValuePublished.TrySetResult(true);
            }
        };
        bridge.Completed += (_, _) => completed.TrySetResult(true);

        bridge.Connect();
        Assert.True(channel.Writer.TryWrite(1));
        await firstValuePublished.Task.WaitAsync(TimeSpan.FromSeconds(5));

        bridge.Value += (_, e) => lateValues.Add(e.Value);
        Assert.True(channel.Writer.TryWrite(2));
        channel.Writer.TryComplete();

        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal([1, 2], earlyValues);
        Assert.Equal([2], lateValues);
    }

    [Fact]
    public void NullBridgeOptionsAreRejected()
    {
        EventBridgeOptions? options = null;

        Assert.Throws<ArgumentNullException>(() => Values(1).ToEventBridge(options!));
    }


    [Fact]
    public async Task ReportPolicyReportsValueSubscriberFailureAndContinuesStream()
    {
        var subscriberFailure = new InvalidOperationException("subscriber failed");
        var reported = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = NewCompletionSource();
        var observed = new List<int>();
        var options = new EventBridgeOptions
        {
            SubscriberExceptionPolicy = EventBridgeSubscriberExceptionPolicy.ReportAndContinue,
            SubscriberExceptionObserver = exception => reported.TrySetResult(exception),
        };
        await using EventStreamBridge<int> bridge = Values(7, 8).ToEventBridge(options);

        bridge.Value += (_, _) => throw subscriberFailure;
        bridge.Value += (_, eventArgs) => observed.Add(eventArgs.Value);
        bridge.Completed += (_, _) => completed.TrySetResult(true);

        bridge.Connect();

        Assert.Same(
            subscriberFailure,
            await reported.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { 7, 8 }, observed);
    }


    [Fact]
    public async Task ThrowingValueSubscriberDoesNotBlockOtherSubscribersOrCompletion()
    {
        await using EventStreamBridge<int> bridge = Values(7, 8).ToEventBridge();
        var observed = new List<int>();
        var completed = NewCompletionSource();

        bridge.Value += (_, _) => throw new InvalidOperationException("subscriber failure");
        bridge.Value += (_, e) => observed.Add(e.Value);
        bridge.Completed += (_, _) => completed.TrySetResult(true);

        bridge.Connect();

        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal([7, 8], observed);
    }

    [Fact]
    public async Task BridgeCanOnlyBeConnectedOnce()
    {
        var channel = Channel.CreateUnbounded<int>();
        await using EventStreamBridge<int> bridge = channel.Reader.ReadAllAsync().ToEventBridge();

        bridge.Connect();

        var exception = Assert.Throws<InvalidOperationException>(() => bridge.Connect());
        Assert.Equal("The event bridge has already been connected.", exception.Message);
    }

    [Fact]
    public void ConnectAfterDisposeThrows()
    {
        var channel = Channel.CreateUnbounded<int>();
        EventStreamBridge<int> bridge = channel.Reader.ReadAllAsync().ToEventBridge();
        bridge.Dispose();

        Assert.Throws<ObjectDisposedException>(() => bridge.Connect());
    }

    [Fact]
    public void AddingHandlerAfterDisposeThrows()
    {
        var channel = Channel.CreateUnbounded<int>();
        EventStreamBridge<int> bridge = channel.Reader.ReadAllAsync().ToEventBridge();
        bridge.Dispose();

        Assert.Throws<ObjectDisposedException>(() => bridge.Value += (_, _) => { });
        Assert.Throws<ObjectDisposedException>(() => bridge.Completed += (_, _) => { });
    }

    [Fact]
    public async Task SourceFailureAndEnumeratorCleanupFailureAreAggregatedInOrder()
    {
        var primaryFailure = new InvalidOperationException("source failed");
        var cleanupFailure = new ApplicationException("enumerator cleanup failed");
        await using EventStreamBridge<int> bridge =
            new FaultAndCleanupAsyncEnumerable(primaryFailure, cleanupFailure).ToEventBridge();
        var faulted = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);

        bridge.Faulted += (_, eventArgs) => faulted.TrySetResult(eventArgs.Exception);
        bridge.Connect();

        var actual = Assert.IsType<AggregateException>(
            await faulted.Task.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal(2, actual.InnerExceptions.Count);
        Assert.Same(primaryFailure, actual.InnerExceptions[0]);
        Assert.Same(cleanupFailure, actual.InnerExceptions[1]);
    }

    [Fact]
    public async Task DisposeAsyncPreservesCancellationWhenEnumeratorCleanupFails()
    {
        var cleanupFailure = new InvalidOperationException("enumerator cleanup failed");
        var source = new CancellationCleanupFailingAsyncEnumerable(cleanupFailure);
        var bridge = source.ToEventBridge();

        bridge.Connect();
        await source.MoveNextStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var actual = await Assert.ThrowsAsync<AggregateException>(
            async () => await bridge.DisposeAsync());

        Assert.Equal(2, actual.InnerExceptions.Count);
        Assert.IsAssignableFrom<OperationCanceledException>(actual.InnerExceptions[0]);
        Assert.Same(cleanupFailure, actual.InnerExceptions[1]);
    }

    private sealed class FaultAndCleanupAsyncEnumerable(
        Exception primaryFailure,
        Exception cleanupFailure) : IAsyncEnumerable<int>, IAsyncEnumerator<int>
    {
        public int Current => 0;

        public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default) => this;

        public ValueTask<bool> MoveNextAsync() => ValueTask.FromException<bool>(primaryFailure);

        public ValueTask DisposeAsync() => ValueTask.FromException(cleanupFailure);
    }

    private sealed class CancellationCleanupFailingAsyncEnumerable(
        Exception cleanupFailure) : IAsyncEnumerable<int>, IAsyncEnumerator<int>
    {
        private CancellationToken _cancellationToken;

        internal TaskCompletionSource<bool> MoveNextStarted { get; } = NewCompletionSource();

        public int Current => 0;

        public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            _cancellationToken = cancellationToken;
            return this;
        }

        public async ValueTask<bool> MoveNextAsync()
        {
            MoveNextStarted.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, _cancellationToken);
            return false;
        }

        public ValueTask DisposeAsync() => ValueTask.FromException(cleanupFailure);
    }


    private static TaskCompletionSource<bool> NewCompletionSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async IAsyncEnumerable<int> Values(params int[] values)
    {
        foreach (var value in values)
        {
            await Task.Yield();
            yield return value;
        }
    }

    private static async IAsyncEnumerable<int> EmptyValues()
    {
        await Task.Yield();
        yield break;
    }

    private static async IAsyncEnumerable<int> FaultingValues(Exception exception)
    {
        await Task.Yield();
        yield return 1;
        throw exception;
    }
}
