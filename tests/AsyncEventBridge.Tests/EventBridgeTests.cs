namespace AsyncEventBridge.Tests;

public sealed class EventBridgeTests
{
    [Fact]
    public void GenericBridgePublishesCompletedValueAfterConnect()
    {
        using EventBridge<int> bridge = Task.FromResult(42).ToEventBridge();
        int? value = null;

        bridge.Completed += (_, e) => value = e.Value;

        bridge.Connect();

        Assert.Equal(42, value);
    }

    [Fact]
    public void NonGenericBridgePublishesCompletedAfterConnect()
    {
        using EventBridge bridge = Task.CompletedTask.ToEventBridge();
        var completed = false;

        bridge.Completed += (_, _) => completed = true;

        bridge.Connect();

        Assert.True(completed);
    }

    [Fact]
    public void GenericValueTaskBridgePublishesCompletedValueAfterConnect()
    {
        using EventBridge<int> bridge = new ValueTask<int>(42).ToEventBridge();
        int? value = null;

        bridge.Completed += (_, e) => value = e.Value;
        bridge.Connect();

        Assert.Equal(42, value);
    }

    [Fact]
    public void NonGenericValueTaskBridgePublishesCompletedAfterConnect()
    {
        using EventBridge bridge = ValueTask.CompletedTask.ToEventBridge();
        var completed = false;

        bridge.Completed += (_, _) => completed = true;
        bridge.Connect();

        Assert.True(completed);
    }

    [Fact]
    public void GenericValueTaskBridgePublishesFaultWithoutOtherTerminalEvents()
    {
        var expected = new InvalidOperationException("value task failed");
        var valueTask = new ValueTask<int>(Task.FromException<int>(expected));
        using EventBridge<int> bridge = valueTask.ToEventBridge();
        Exception? observed = null;
        var completed = 0;
        var cancelled = 0;

        bridge.Completed += (_, _) => Interlocked.Increment(ref completed);
        bridge.Faulted += (_, e) => observed = e.Exception;
        bridge.Cancelled += (_, _) => Interlocked.Increment(ref cancelled);

        bridge.Connect();

        Assert.Same(expected, observed);
        Assert.Equal(0, completed);
        Assert.Equal(0, cancelled);
    }

    [Fact]
    public void ValueTaskFaultedWithOperationCanceledExceptionPublishesFaulted()
    {
        var expected = new OperationCanceledException("faulted ValueTask");
        var valueTask = new ValueTask<int>(Task.FromException<int>(expected));
        using EventBridge<int> bridge = valueTask.ToEventBridge();
        Exception? observed = null;
        var completed = 0;
        var cancelled = 0;

        bridge.Completed += (_, _) => Interlocked.Increment(ref completed);
        bridge.Faulted += (_, e) => observed = e.Exception;
        bridge.Cancelled += (_, _) => Interlocked.Increment(ref cancelled);
        bridge.Connect();

        Assert.Same(expected, observed);
        Assert.Equal(0, completed);
        Assert.Equal(0, cancelled);
    }

    [Fact]
    public void NonGenericValueTaskBridgePublishesCancelledWithoutOtherTerminalEvents()
    {
        var valueTask = new ValueTask(Task.FromCanceled(new CancellationToken(canceled: true)));
        using EventBridge bridge = valueTask.ToEventBridge();
        var completed = 0;
        var faulted = 0;
        var cancelled = 0;

        bridge.Completed += (_, _) => Interlocked.Increment(ref completed);
        bridge.Faulted += (_, _) => Interlocked.Increment(ref faulted);
        bridge.Cancelled += (_, _) => Interlocked.Increment(ref cancelled);

        bridge.Connect();

        Assert.Equal(0, completed);
        Assert.Equal(0, faulted);
        Assert.Equal(1, cancelled);
    }

    [Fact]
    public void GenericBridgePublishesFaultWithoutOtherTerminalEvents()
    {
        var expected = new InvalidOperationException("sensor configuration failed");
        using EventBridge<int> bridge = Task.FromException<int>(expected).ToEventBridge();
        Exception? observed = null;
        var completed = 0;
        var cancelled = 0;

        bridge.Completed += (_, _) => Interlocked.Increment(ref completed);
        bridge.Faulted += (_, e) => observed = e.Exception;
        bridge.Cancelled += (_, _) => Interlocked.Increment(ref cancelled);

        bridge.Connect();

        Assert.Same(expected, observed);
        Assert.Equal(0, completed);
        Assert.Equal(0, cancelled);
    }

    [Fact]
    public void NonGenericBridgePublishesCancelledWithoutOtherTerminalEvents()
    {
        using EventBridge bridge = Task.FromCanceled(new CancellationToken(canceled: true)).ToEventBridge();
        var completed = 0;
        var faulted = 0;
        var cancelled = 0;

        bridge.Completed += (_, _) => Interlocked.Increment(ref completed);
        bridge.Faulted += (_, _) => Interlocked.Increment(ref faulted);
        bridge.Cancelled += (_, _) => Interlocked.Increment(ref cancelled);

        bridge.Connect();

        Assert.Equal(0, completed);
        Assert.Equal(0, faulted);
        Assert.Equal(1, cancelled);
    }

    [Fact]
    public void ThrowingSubscriberDoesNotBlockLaterSubscribers()
    {
        using EventBridge<int> bridge = Task.FromResult(7).ToEventBridge();
        var observed = new List<int>();

        bridge.Completed += (_, _) => throw new InvalidOperationException("subscriber failed");
        bridge.Completed += (_, e) => observed.Add(e.Value);

        bridge.Connect();

        Assert.Equal([7], observed);
    }

    [Fact]
    public void RemovedSubscriberIsNotInvoked()
    {
        using EventBridge<int> bridge = Task.FromResult(7).ToEventBridge();
        var calls = 0;
        EventHandler<AsyncValueEventArgs<int>> handler = (_, _) => calls++;

        bridge.Completed += handler;
        bridge.Completed -= handler;

        bridge.Connect();

        Assert.Equal(0, calls);
    }

    [Fact]
    public void SubscriberAddedAfterPublicationReceivesNoReplay()
    {
        using EventBridge<int> bridge = Task.FromResult(7).ToEventBridge();
        var earlyCalls = 0;
        var lateCalls = 0;

        bridge.Completed += (_, _) => earlyCalls++;
        bridge.Connect();
        bridge.Completed += (_, _) => lateCalls++;

        Assert.Equal(1, earlyCalls);
        Assert.Equal(0, lateCalls);
    }

    [Fact]
    public void BridgeCanOnlyBeConnectedOnce()
    {
        using EventBridge bridge = Task.CompletedTask.ToEventBridge();

        bridge.Connect();

        var exception = Assert.Throws<InvalidOperationException>(bridge.Connect);
        Assert.Equal("The event bridge has already been connected.", exception.Message);
    }

    [Fact]
    public void ConnectAfterDisposeThrows()
    {
        EventBridge bridge = Task.CompletedTask.ToEventBridge();
        bridge.Dispose();

        Assert.Throws<ObjectDisposedException>(bridge.Connect);
    }

    [Fact]
    public void AddingHandlerAfterDisposeThrows()
    {
        EventBridge bridge = Task.CompletedTask.ToEventBridge();
        bridge.Dispose();

        Assert.Throws<ObjectDisposedException>(() => bridge.Completed += (_, _) => { });
    }

    [Fact]
    public void NullAsyncSourcesAreRejected()
    {
        Task? task = null;
        Task<int>? genericTask = null;
        IAsyncEnumerable<int>? stream = null;

        Assert.Throws<ArgumentNullException>(() => task!.ToEventBridge());
        Assert.Throws<ArgumentNullException>(() => genericTask!.ToEventBridge());
        Assert.Throws<ArgumentNullException>(() => stream!.ToEventBridge());
    }
}
