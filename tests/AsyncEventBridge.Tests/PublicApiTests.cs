using System.Reflection;

namespace AsyncEventBridge.Tests;

public sealed class PublicApiTests
{
    [Fact]
    public void RuntimeExportsOnlyIntendedPublicTypes()
    {
        var exportedTypes = typeof(EventAwaiter).Assembly
            .GetExportedTypes()
            .Select(type => type.FullName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var expectedTypes = new[]
        {
            "AsyncEventBridge.AsyncEventBridgeExtensions",
            "AsyncEventBridge.AsyncFaultedEventArgs",
            "AsyncEventBridge.AsyncValueEventArgs`1",
            "AsyncEventBridge.EventAwaiter",
            "AsyncEventBridge.EventBridge",
            "AsyncEventBridge.EventBridge`1",
            "AsyncEventBridge.EventStream",
            "AsyncEventBridge.EventStreamBridge`1",
            "AsyncEventBridge.EventStreamFullMode",
            "AsyncEventBridge.EventStreamOptions",
            "AsyncEventBridge.GenerateAsyncEventsAttribute",
        }.OrderBy(name => name, StringComparer.Ordinal).ToArray();

        Assert.Equal(expectedTypes, exportedTypes);
    }

    [Fact]
    public void RuntimePublicMethodsStaySmallAndIntentional()
    {
        AssertMethodNames(typeof(AsyncEventBridgeExtensions), "ToEventBridge", "ToEventBridge", "ToEventBridge");
        AssertMethodNames(typeof(EventAwaiter), "WaitAsync", "WaitAsync");
        AssertMethodNames(typeof(EventStream), "Create", "Create");
        AssertMethodNames(typeof(EventBridge), "Connect", "Dispose");
        AssertMethodNames(typeof(EventBridge<int>), "Connect", "Dispose");
        AssertMethodNames(typeof(EventStreamBridge<int>), "Connect", "Dispose", "DisposeAsync");
    }

    [Fact]
    public void RuntimePublicEventsAndPropertiesStayIntentional()
    {
        AssertEventNames(typeof(EventBridge), "Cancelled", "Completed", "Faulted");
        AssertEventNames(typeof(EventBridge<int>), "Cancelled", "Completed", "Faulted");
        AssertEventNames(typeof(EventStreamBridge<int>), "Cancelled", "Completed", "Faulted", "Value");

        AssertPropertyNames(typeof(AsyncValueEventArgs<int>), "Value");
        AssertPropertyNames(typeof(AsyncFaultedEventArgs), "Exception");
        AssertPropertyNames(typeof(EventStreamOptions), "Capacity", "FullMode");

        Assert.Equal(
            new[] { "DropNewest", "DropOldest", "Grow" },
            Enum.GetNames<EventStreamFullMode>().OrderBy(name => name, StringComparer.Ordinal));
    }

    private static void AssertMethodNames(Type type, params string[] expected)
    {
        var methods = type
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected.OrderBy(name => name, StringComparer.Ordinal), methods);
    }

    private static void AssertEventNames(Type type, params string[] expected)
    {
        var events = type
            .GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(@event => @event.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected.OrderBy(name => name, StringComparer.Ordinal), events);
    }

    private static void AssertPropertyNames(Type type, params string[] expected)
    {
        var properties = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected.OrderBy(name => name, StringComparer.Ordinal), properties);
    }
}
