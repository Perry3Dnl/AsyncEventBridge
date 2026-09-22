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
            "AsyncEventBridge.EventBridgeOptions",
            "AsyncEventBridge.EventBridgeSubscriberExceptionPolicy",
            "AsyncEventBridge.EventStream",
            "AsyncEventBridge.EventStreamBridge`1",
            "AsyncEventBridge.EventStreamComposition",
            "AsyncEventBridge.EventStreamFullMode",
            "AsyncEventBridge.EventStreamOptions",
            "AsyncEventBridge.GenerateAsyncEventsAttribute",
            "AsyncEventBridge.GenerateAsyncEventsForAttribute",
        }.OrderBy(name => name, StringComparer.Ordinal).ToArray();

        Assert.Equal(expectedTypes, exportedTypes);
    }

    [Fact]
    public void RuntimePublicMethodsStaySmallAndIntentional()
    {
        AssertMethodNames(
            typeof(AsyncEventBridgeExtensions),
            "ToEventBridge",
            "ToEventBridge",
            "ToEventBridge",
            "ToEventBridge",
            "ToEventBridge",
            "ToEventBridge");
        AssertMethodNames(typeof(EventAwaiter), "WaitAsync", "WaitAsync");
        AssertMethodNames(typeof(EventStream), "Create", "Create");
        AssertMethodNames(typeof(EventStreamComposition), "StartAfter", "TakeUntil");
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
        AssertPropertyNames(
            typeof(EventBridgeOptions),
            "SubscriberExceptionObserver",
            "SubscriberExceptionPolicy");
        AssertPropertyNames(typeof(GenerateAsyncEventsForAttribute), "TargetType");

        Assert.Equal(
            new[] { "DropNewest", "DropOldest", "Unbounded" },
            Enum.GetNames<EventStreamFullMode>().OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(
            new[] { "IgnoreAndContinue", "ReportAndContinue", "TraceAndContinue" },
            Enum.GetNames<EventBridgeSubscriberExceptionPolicy>().OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void EventStreamConfigurationDefaultsAndNumericValuesStayStable()
    {
        var options = new EventStreamOptions();

        Assert.Equal(100, options.Capacity);
        Assert.Equal(EventStreamFullMode.Unbounded, options.FullMode);
        Assert.Equal(0, (int)EventStreamFullMode.Unbounded);
        Assert.Equal(1, (int)EventStreamFullMode.DropOldest);
        Assert.Equal(2, (int)EventStreamFullMode.DropNewest);

        var bridgeOptions = new EventBridgeOptions();
        Assert.Equal(
            EventBridgeSubscriberExceptionPolicy.TraceAndContinue,
            bridgeOptions.SubscriberExceptionPolicy);
        Assert.Null(bridgeOptions.SubscriberExceptionObserver);
        Assert.Equal(0, (int)EventBridgeSubscriberExceptionPolicy.TraceAndContinue);
        Assert.Equal(1, (int)EventBridgeSubscriberExceptionPolicy.ReportAndContinue);
        Assert.Equal(2, (int)EventBridgeSubscriberExceptionPolicy.IgnoreAndContinue);
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
