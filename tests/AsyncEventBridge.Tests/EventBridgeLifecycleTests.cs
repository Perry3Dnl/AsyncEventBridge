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
