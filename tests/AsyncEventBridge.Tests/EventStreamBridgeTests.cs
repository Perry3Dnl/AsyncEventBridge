using System.Threading.Channels;

namespace AsyncEventBridge.Tests;

public sealed class EventStreamBridgeTests
{
    [Fact]
    public async Task PublishesValuesAndCompletion()
    {
        var bridge = Values().ToEventBridge();
        var values = new List<int>();
        var completed = NewCompletionSource();

        bridge.Value += (_, eventArgs) => values.Add(eventArgs.Value);
        bridge.Completed += (_, _) => completed.TrySetResult(true);
        bridge.Faulted += (_, eventArgs) => completed.TrySetException(eventArgs.Exception);
        bridge.Cancelled += (_, _) => completed.TrySetCanceled();

        bridge.Connect();
        await completed.Task;

        Assert.Equal([1, 2, 3], values);
        await bridge.DisposeAsync();
    }

    [Fact]
    public async Task PublishesFault()
    {
        var bridge = FaultingValues().ToEventBridge();
        var faulted = NewCompletionSource<Exception>();

        bridge.Faulted += (_, eventArgs) => faulted.TrySetResult(eventArgs.Exception);
        bridge.Connect();

        var exception = await faulted.Task;

        Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal("boom", exception.Message);
        await bridge.DisposeAsync();
    }

    [Fact]
    public async Task PublishesCancellation()
    {
        var channel = Channel.CreateUnbounded<int>();
        using var cancellation = new CancellationTokenSource();
        var bridge = channel.Reader.ReadAllAsync().ToEventBridge();
        var cancelled = NewCompletionSource();

        bridge.Cancelled += (_, _) => cancelled.TrySetResult(true);
        bridge.Connect(cancellation.Token);
        cancellation.Cancel();

        await cancelled.Task;
        await bridge.DisposeAsync();
    }

    [Fact]
    public async Task SourceThrownOperationCanceledExceptionPublishesFaultWhenLifetimeIsNotCancelled()
    {
        var bridge = ThrowsCancellation().ToEventBridge();
        var faulted = NewCompletionSource<Exception>();
        var cancelled = 0;

        bridge.Faulted += (_, eventArgs) => faulted.TrySetResult(eventArgs.Exception);
        bridge.Cancelled += (_, _) => Interlocked.Increment(ref cancelled);
        bridge.Connect();

        var exception = await faulted.Task;

        Assert.IsType<OperationCanceledException>(exception);
        Assert.Equal(0, cancelled);
        await bridge.DisposeAsync();
    }

    [Fact]
    public async Task DisposeStopsEnumerationWithoutPublishingCancelled()
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
        bridge.Dispose();

        await Task.Delay(50);

        Assert.Equal(0, completed);
        Assert.Equal(0, faulted);
        Assert.Equal(0, cancelled);
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
        await Task.Delay(50);
        Assert.False(disposeTask.IsCompleted);

        Assert.True(channel.Writer.TryWrite(2));
        releaseHandler.Set();
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, publishedValues);
    }

    [Fact]
    public void CannotConnectTwice()
    {
        var bridge = Values().ToEventBridge();
        bridge.Connect();

        Assert.Throws<InvalidOperationException>(() => bridge.Connect());
        bridge.Dispose();
    }

    [Fact]
    public void CannotSubscribeAfterDispose()
    {
        var bridge = Values().ToEventBridge();
        bridge.Dispose();

        Assert.Throws<ObjectDisposedException>(() => bridge.Value += (_, _) => { });
    }

    [Fact]
    public async Task SubscriberExceptionDoesNotStopRemainingValueHandlersOrEnumeration()
    {
        var bridge = Values().ToEventBridge();
        var values = new List<int>();
        var completed = NewCompletionSource();
        var faulted = 0;

        bridge.Value += (_, _) => throw new InvalidOperationException("subscriber failure");
        bridge.Value += (_, eventArgs) => values.Add(eventArgs.Value);
        bridge.Completed += (_, _) => completed.TrySetResult(true);
        bridge.Faulted += (_, _) => Interlocked.Increment(ref faulted);

        bridge.Connect();
        await completed.Task;

        Assert.Equal([1, 2, 3], values);
        Assert.Equal(0, faulted);
        await bridge.DisposeAsync();
    }

    private static async IAsyncEnumerable<int> Values()
    {
        yield return 1;
        await Task.Yield();
        yield return 2;
        yield return 3;
    }

    private static async IAsyncEnumerable<int> FaultingValues()
    {
        yield return 1;
        await Task.Yield();
        throw new InvalidOperationException("boom");
    }

    private static async IAsyncEnumerable<int> ThrowsCancellation()
    {
        yield return 1;
        await Task.Yield();
        throw new OperationCanceledException("source cancellation");
    }

    private static TaskCompletionSource<bool> NewCompletionSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TaskCompletionSource<T> NewCompletionSource<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
