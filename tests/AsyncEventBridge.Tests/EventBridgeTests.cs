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
    public void BridgeCanOnlyBeConnectedOnce()
    {
        using EventBridge bridge = Task.CompletedTask.ToEventBridge();

        bridge.Connect();

        var exception = Assert.Throws<InvalidOperationException>(bridge.Connect);
        Assert.Equal("The event bridge has already been connected.", exception.Message);
    }
}
