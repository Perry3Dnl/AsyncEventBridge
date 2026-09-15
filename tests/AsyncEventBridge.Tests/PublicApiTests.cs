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
            "AsyncEventBridge.EventComposition",
            "AsyncEventBridge.EventOccurrence`2",
            "AsyncEventBridge.EventOccurrenceAwaiter",
            "AsyncEventBridge.EventOccurrenceStream",
            "AsyncEventBridge.EventWaitAnyResult`2",
            "AsyncEventBridge.EventBridge",
            "AsyncEventBridge.EventBridge`1",
            "AsyncEventBridge.EventStream",
            "AsyncEventBridge.EventStreamBridge`1",
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
            "ToEventBridge");
        AssertMethodNames(typeof(EventAwaiter), "WaitAsync", "WaitAsync");
        AssertMethodNames(typeof(EventOccurrenceAwaiter), "WaitAsync");
        AssertMethodNames(typeof(EventOccurrenceStream), "Create");
        AssertMethodNames(typeof(EventComposition), "WaitAnyAsync");
        AssertMethodNames(typeof(EventStream), "Create", "Create");
        AssertMethodNames(typeof(EventBridge), "Connect", "Dispose");
        AssertMethodNames(typeof(EventBridge<int>), "Connect", "Dispose");
        AssertMethodNames(typeof(EventStreamBridge<int>), "Connect", "Dispose", "DisposeAsync");
    }

    [Fact]
    public void CoreRuntimeMethodSignaturesStayStable()
    {
        AssertMethodSignatures(
            typeof(EventAwaiter),
            "System.Threading.Tasks.Task<System.EventArgs> WaitAsync(System.Action<System.EventHandler> subscribe, System.Action<System.EventHandler> unsubscribe, System.Predicate<System.EventArgs> predicate optional, System.Threading.CancellationToken cancellationToken optional, System.Nullable<System.TimeSpan> timeout optional, System.TimeProvider timeProvider optional)",
            "System.Threading.Tasks.Task<TEventArgs> WaitAsync<TEventArgs>(System.Action<System.EventHandler<TEventArgs>> subscribe, System.Action<System.EventHandler<TEventArgs>> unsubscribe, System.Predicate<TEventArgs> predicate optional, System.Threading.CancellationToken cancellationToken optional, System.Nullable<System.TimeSpan> timeout optional, System.TimeProvider timeProvider optional)");

        AssertMethodSignatures(
            typeof(EventStream),
            "System.Collections.Generic.IAsyncEnumerable<System.EventArgs> Create(System.Action<System.EventHandler> subscribe, System.Action<System.EventHandler> unsubscribe, System.Predicate<System.EventArgs> predicate optional, AsyncEventBridge.EventStreamOptions options optional, System.Threading.CancellationToken cancellationToken optional)",
            "System.Collections.Generic.IAsyncEnumerable<TEventArgs> Create<TEventArgs>(System.Action<System.EventHandler<TEventArgs>> subscribe, System.Action<System.EventHandler<TEventArgs>> unsubscribe, System.Predicate<TEventArgs> predicate optional, AsyncEventBridge.EventStreamOptions options optional, System.Threading.CancellationToken cancellationToken optional)");

        AssertMethodSignatures(
            typeof(EventOccurrenceAwaiter),
            "System.Threading.Tasks.Task<AsyncEventBridge.EventOccurrence<TSender,TPayload>> WaitAsync<TSender,TPayload>(System.Action<System.EventHandler<TSender,TPayload>> subscribe, System.Action<System.EventHandler<TSender,TPayload>> unsubscribe, System.Predicate<AsyncEventBridge.EventOccurrence<TSender,TPayload>> predicate optional, System.Threading.CancellationToken cancellationToken optional, System.Nullable<System.TimeSpan> timeout optional, System.TimeProvider timeProvider optional)");

        AssertMethodSignatures(
            typeof(EventOccurrenceStream),
            "System.Collections.Generic.IAsyncEnumerable<AsyncEventBridge.EventOccurrence<TSender,TPayload>> Create<TSender,TPayload>(System.Action<System.EventHandler<TSender,TPayload>> subscribe, System.Action<System.EventHandler<TSender,TPayload>> unsubscribe, System.Predicate<AsyncEventBridge.EventOccurrence<TSender,TPayload>> predicate optional, AsyncEventBridge.EventStreamOptions options optional, System.Threading.CancellationToken cancellationToken optional)");

        AssertMethodSignatures(
            typeof(EventComposition),
            "System.Threading.Tasks.Task<AsyncEventBridge.EventWaitAnyResult<TFirst,TSecond>> WaitAnyAsync<TFirst,TSecond>(System.Func<System.Threading.CancellationToken,System.Threading.Tasks.Task<TFirst>> firstWait, System.Func<System.Threading.CancellationToken,System.Threading.Tasks.Task<TSecond>> secondWait, System.Threading.CancellationToken cancellationToken optional)");

        AssertMethodSignatures(
            typeof(AsyncEventBridgeExtensions),
            "AsyncEventBridge.EventBridge ToEventBridge(System.Threading.Tasks.Task task)",
            "AsyncEventBridge.EventBridge ToEventBridge(System.Threading.Tasks.ValueTask task)",
            "AsyncEventBridge.EventBridge<T> ToEventBridge<T>(System.Threading.Tasks.Task<T> task)",
            "AsyncEventBridge.EventBridge<T> ToEventBridge<T>(System.Threading.Tasks.ValueTask<T> task)",
            "AsyncEventBridge.EventStreamBridge<T> ToEventBridge<T>(System.Collections.Generic.IAsyncEnumerable<T> source)");

        AssertMethodSignatures(
            typeof(EventBridge),
            "System.Void Connect()",
            "System.Void Dispose()");

        AssertMethodSignatures(
            typeof(EventBridge<int>),
            "System.Void Connect()",
            "System.Void Dispose()");

        AssertMethodSignatures(
            typeof(EventStreamBridge<int>),
            "System.Void Connect(System.Threading.CancellationToken cancellationToken optional)",
            "System.Void Dispose()",
            "System.Threading.Tasks.ValueTask DisposeAsync()");
    }

    [Fact]
    public void RuntimePublicEventsAndPropertiesStayIntentional()
    {
        AssertEventNames(typeof(EventBridge), "Cancelled", "Completed", "Faulted");
        AssertEventNames(typeof(EventBridge<int>), "Cancelled", "Completed", "Faulted");
        AssertEventNames(typeof(EventStreamBridge<int>), "Cancelled", "Completed", "Faulted", "Value");

        AssertPropertyNames(typeof(AsyncValueEventArgs<int>), "Value");
        AssertPropertyNames(typeof(AsyncFaultedEventArgs), "Exception");
        AssertPropertyNames(typeof(EventStreamOptions), "Capacity", "DroppedCount", "DropObserver", "FullMode");
        AssertPropertyNames(typeof(EventOccurrence<object, int>), "Payload", "Sender");
        AssertPropertyNames(typeof(EventWaitAnyResult<int, string>), "First", "IsFirst", "IsSecond", "Second");
        AssertPropertyNames(typeof(GenerateAsyncEventsForAttribute), "TargetType");

        Assert.Equal(
            new[] { "DropNewest", "DropOldest", "Grow" },
            Enum.GetNames<EventStreamFullMode>().OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void CoreRuntimeEventAndPropertyTypesStayStable()
    {
        AssertEventSignatures(
            typeof(EventBridge),
            "Cancelled:System.EventHandler",
            "Completed:System.EventHandler",
            "Faulted:System.EventHandler<AsyncEventBridge.AsyncFaultedEventArgs>");

        AssertEventSignatures(
            typeof(EventBridge<int>),
            "Cancelled:System.EventHandler",
            "Completed:System.EventHandler<AsyncEventBridge.AsyncValueEventArgs<System.Int32>>",
            "Faulted:System.EventHandler<AsyncEventBridge.AsyncFaultedEventArgs>");

        AssertEventSignatures(
            typeof(EventStreamBridge<int>),
            "Cancelled:System.EventHandler",
            "Completed:System.EventHandler",
            "Faulted:System.EventHandler<AsyncEventBridge.AsyncFaultedEventArgs>",
            "Value:System.EventHandler<AsyncEventBridge.AsyncValueEventArgs<System.Int32>>");

        AssertPropertySignatures(
            typeof(EventStreamOptions),
            "Capacity:System.Int32:get,set",
            "DroppedCount:System.Int64:get",
            "DropObserver:System.Action<System.Int64>:get,set",
            "FullMode:AsyncEventBridge.EventStreamFullMode:get,set");

        AssertPropertySignatures(
            typeof(EventOccurrence<object, int>),
            "Payload:System.Int32:get",
            "Sender:System.Object:get");
        AssertPropertySignatures(
            typeof(EventWaitAnyResult<int, string>),
            "First:System.Int32:get",
            "IsFirst:System.Boolean:get",
            "IsSecond:System.Boolean:get",
            "Second:System.String:get");

        AssertPropertySignatures(typeof(AsyncValueEventArgs<int>), "Value:System.Int32:get");
        AssertPropertySignatures(typeof(AsyncFaultedEventArgs), "Exception:System.Exception:get");
        AssertPropertySignatures(typeof(GenerateAsyncEventsForAttribute), "TargetType:System.Type:get");
    }

    [Fact]
    public void EventStreamConfigurationDefaultsAndNumericValuesStayStable()
    {
        var options = new EventStreamOptions();

        Assert.Equal(100, options.Capacity);
        Assert.Equal(EventStreamFullMode.Grow, options.FullMode);
        Assert.Equal(0, options.DroppedCount);
        Assert.Null(options.DropObserver);
        Assert.Equal(0, (int)EventStreamFullMode.Grow);
        Assert.Equal(1, (int)EventStreamFullMode.DropOldest);
        Assert.Equal(2, (int)EventStreamFullMode.DropNewest);
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

    private static void AssertMethodSignatures(Type type, params string[] expected)
    {
        var actual = type
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Select(DescribeMethod)
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected.OrderBy(signature => signature, StringComparer.Ordinal), actual);
    }

    private static string DescribeMethod(MethodInfo method)
    {
        var genericSuffix = method.IsGenericMethodDefinition
            ? $"<{string.Join(",", method.GetGenericArguments().Select(argument => argument.Name))}>"
            : string.Empty;
        var parameters = string.Join(
            ", ",
            method.GetParameters().Select(parameter =>
                $"{DescribeType(parameter.ParameterType)} {parameter.Name}{(parameter.IsOptional ? " optional" : string.Empty)}"));

        return $"{DescribeType(method.ReturnType)} {method.Name}{genericSuffix}({parameters})";
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

    private static void AssertEventSignatures(Type type, params string[] expected)
    {
        var actual = type
            .GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(@event => $"{@event.Name}:{DescribeType(@event.EventHandlerType!)}")
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected.OrderBy(signature => signature, StringComparer.Ordinal), actual);
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

    private static void AssertPropertySignatures(Type type, params string[] expected)
    {
        var actual = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(property =>
            {
                var accessors = new List<string>(2);
                if (property.GetMethod is not null)
                {
                    accessors.Add("get");
                }

                if (property.SetMethod is not null)
                {
                    accessors.Add("set");
                }

                return $"{property.Name}:{DescribeType(property.PropertyType)}:{string.Join(",", accessors)}";
            })
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected.OrderBy(signature => signature, StringComparer.Ordinal), actual);
    }

    private static string DescribeType(Type type)
    {
        if (type.IsGenericParameter)
        {
            return type.Name;
        }

        if (!type.IsGenericType)
        {
            return type.FullName ?? type.Name;
        }

        var genericDefinitionName = type.GetGenericTypeDefinition().FullName
            ?? type.GetGenericTypeDefinition().Name;
        var tickIndex = genericDefinitionName.IndexOf('`');
        if (tickIndex >= 0)
        {
            genericDefinitionName = genericDefinitionName[..tickIndex];
        }

        return $"{genericDefinitionName}<{string.Join(",", type.GetGenericArguments().Select(DescribeType))}>";
    }
}
