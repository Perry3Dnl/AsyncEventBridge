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
    public async Task SourceOperationCanceledExceptionWithoutBridgeCancellationPublishesFaulted()
    {
        var expected = new OperationCanceledException("source failed independently");
        await using EventStreamBridge<int> bridge = FaultingValues(expected).ToEventBridge();
        var faulted = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = 0;
        var cancelled = 0;

        bridge.Completed += (_, _) => Interlocked.Increment(ref completed);
        bridge.Faulted += (_, e) => faulted.TrySetResult(e.Exception);
        bridge.Cancelled += (_, _) => Interlocked.Increment(ref cancelled);

        bridge.Connect();

        var actual = await faulted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(expected, actual);
        Assert.Equal(0, completed);
        Assert.Equal(0, cancelled);
    }

    [Fact]
    public async Task CancellationPublishesCancelled()
    {
        var channel = Channel.CreateUnbounded<int>();
        using var cancellation = new CancellationTokenSource();
        await using EventStreamBridge<int> bridge = channel.Reader.ReadAllAsync().ToEventBridge();
        var cancelled = NewCompletionSource();
        var completed = 0;
        var faulted = 0;

        bridge.Completed += (_, _) => Interlocked.Increment(ref completed);
        bridge.Faulted += (_, _) => Interlocked.Increment(ref faulted);
        bridge.Cancelled += (_, _) => cancelled.TrySetResult(true);

        bridge.Connect(cancellation.Token);
        cancellation.Cancel();

        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, completed);
        Assert.Equal(0, faulted);
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
        await bridge.DisposeAsync();

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
        await bridge.DisposeAsync();
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
