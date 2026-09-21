namespace AsyncEventBridge.Tests;

public sealed class EventBridgeLifecycleTests
{
    [Fact]
    public async Task DisposeAllowsTerminalPublicationAlreadyInProgressToFinish()
    {
        var taskCompletion = new TaskCompletionSource<int>();
        EventBridge<int> bridge = taskCompletion.Task.ToEventBridge();
        var firstHandlerEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseFirstHandler = new ManualResetEventSlim(false);
        var secondHandlerCalls = 0;

        bridge.Completed += (_, _) =>
        {
            firstHandlerEntered.TrySetResult(true);
            releaseFirstHandler.Wait();
        };
        bridge.Completed += (_, _) => Interlocked.Increment(ref secondHandlerCalls);
        bridge.Connect();

        var completeTask = Task.Run(() => taskCompletion.SetResult(42));
        await firstHandlerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        bridge.Dispose();
        Assert.Equal(0, Volatile.Read(ref secondHandlerCalls));

        releaseFirstHandler.Set();
        await completeTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, Volatile.Read(ref secondHandlerCalls));
    }

    [Fact]
    public async Task SubscriberAddedDuringTerminalPublicationIsNotInvoked()
    {
        var taskCompletion = new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        EventBridge<int> bridge = taskCompletion.Task.ToEventBridge();
        var firstHandlerEntered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var publicationFinished = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseFirstHandler = new ManualResetEventSlim(false);
        var lateHandlerCalls = 0;

        bridge.Completed += (_, _) =>
        {
            firstHandlerEntered.TrySetResult(true);
            releaseFirstHandler.Wait();
        };
        bridge.Completed += (_, _) => publicationFinished.TrySetResult(true);
        bridge.Connect();

        taskCompletion.SetResult(42);
        await firstHandlerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        bridge.Completed += (_, _) => Interlocked.Increment(ref lateHandlerCalls);
        releaseFirstHandler.Set();

        await publicationFinished.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, Volatile.Read(ref lateHandlerCalls));
    }

    [Fact]
    public async Task SubscriberRemovedDuringTerminalPublicationStillReceivesCapturedPublication()
    {
        var taskCompletion = new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        EventBridge<int> bridge = taskCompletion.Task.ToEventBridge();
        var firstHandlerEntered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseFirstHandler = new ManualResetEventSlim(false);
        var secondHandlerCalls = 0;
        EventHandler<AsyncValueEventArgs<int>> secondHandler =
            (_, _) => Interlocked.Increment(ref secondHandlerCalls);

        bridge.Completed += (_, _) =>
        {
            firstHandlerEntered.TrySetResult(true);
            releaseFirstHandler.Wait();
        };
        var publicationFinished = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        bridge.Completed += secondHandler;
        bridge.Completed += (_, _) => publicationFinished.TrySetResult(true);
        bridge.Connect();

        taskCompletion.SetResult(42);
        await firstHandlerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        bridge.Completed -= secondHandler;
        releaseFirstHandler.Set();

        await publicationFinished.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, Volatile.Read(ref secondHandlerCalls));
    }

    [Fact]
    public async Task DisposeFromTerminalSubscriberDoesNotInterruptCapturedSubscribers()
    {
        var taskCompletion = new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        EventBridge<int> bridge = taskCompletion.Task.ToEventBridge();
        var allHandlersRan = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        bridge.Completed += (_, _) =>
        {
            Interlocked.Increment(ref calls);
            bridge.Dispose();
        };
        bridge.Completed += (_, _) =>
        {
            Interlocked.Increment(ref calls);
            allHandlersRan.TrySetResult(true);
        };
        bridge.Connect();

        taskCompletion.SetResult(42);

        await allHandlersRan.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, Volatile.Read(ref calls));
        Assert.Throws<ObjectDisposedException>(() => bridge.Completed += (_, _) => { });
    }

    [Fact]
    public void DisposeIsIdempotent()
    {
        EventBridge bridge = Task.CompletedTask.ToEventBridge();

        bridge.Dispose();
        bridge.Dispose();

        Assert.Throws<ObjectDisposedException>(bridge.Connect);
    }


    [Fact]
    public void DisposeBeforeTerminalPublicationSuppressesOutcome()
    {
        var taskCompletion = new TaskCompletionSource<int>();
        EventBridge<int> bridge = taskCompletion.Task.ToEventBridge();
        var publications = 0;

        bridge.Completed += (_, _) => publications++;
        bridge.Faulted += (_, _) => publications++;
        bridge.Cancelled += (_, _) => publications++;
        bridge.Connect();
        bridge.Dispose();

        taskCompletion.SetResult(42);

        Assert.Equal(0, publications);
    }
}
