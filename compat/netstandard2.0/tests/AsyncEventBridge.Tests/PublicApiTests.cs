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
            "AsyncEventBridge.EventCondition",
            "AsyncEventBridge.EventBridge",
            "AsyncEventBridge.EventBridge`1",
            "AsyncEventBridge.EventBridgeOptions",
            "AsyncEventBridge.EventBridgeSubscriberExceptionPolicy",
            "AsyncEventBridge.EventStream",
            "AsyncEventBridge.EventStreamBridge`1",
            "AsyncEventBridge.EventStreamComposition",
            "AsyncEventBridge.EventStreamFullMode",
            "AsyncEventBridge.EventStreamLifecycleEvent`1",
            "AsyncEventBridge.EventStreamLifecycleEventKind",
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
        AssertMethodNames(typeof(EventCondition), "WaitUntilAsync", "WaitUntilAsync");
        AssertMethodNames(typeof(EventStream), "Create", "Create");
        AssertMethodNames(typeof(EventStreamComposition), "RepeatBetween", "RepeatBetweenWithLifecycle", "RepeatWhile", "RepeatWhile", "RepeatWhileWithLifecycle", "RepeatWhileWithLifecycle", "StartAfter", "TakeUntil");
        AssertMethodNames(typeof(EventBridge), "Connect", "Dispose");
        AssertMethodNames(typeof(EventBridge<int>), "Connect", "Dispose");
        AssertMethodNames(typeof(EventStreamBridge<int>), "Connect", "Dispose", "DisposeAsync");
    }

    [Fact]
    public void CoreRuntimeMethodSignaturesStayStable()
    {
        AssertMethodSignatures(
            typeof(EventAwaiter),
            "System.Threading.Tasks.Task<System.EventArgs> WaitAsync(System.Action<System.EventHandler> subscribe, System.Action<System.EventHandler> unsubscribe, System.Predicate<System.EventArgs> predicate optional, System.Threading.CancellationToken cancellationToken optional, System.Nullable<System.TimeSpan> timeout optional)",
            "System.Threading.Tasks.Task<TEventArgs> WaitAsync<TEventArgs>(System.Action<System.EventHandler<TEventArgs>> subscribe, System.Action<System.EventHandler<TEventArgs>> unsubscribe, System.Predicate<TEventArgs> predicate optional, System.Threading.CancellationToken cancellationToken optional, System.Nullable<System.TimeSpan> timeout optional)");

        AssertMethodSignatures(
            typeof(EventStream),
            "System.Collections.Generic.IAsyncEnumerable<System.EventArgs> Create(System.Action<System.EventHandler> subscribe, System.Action<System.EventHandler> unsubscribe, System.Predicate<System.EventArgs> predicate optional, AsyncEventBridge.EventStreamOptions options optional, System.Threading.CancellationToken cancellationToken optional)",
            "System.Collections.Generic.IAsyncEnumerable<TEventArgs> Create<TEventArgs>(System.Action<System.EventHandler<TEventArgs>> subscribe, System.Action<System.EventHandler<TEventArgs>> unsubscribe, System.Predicate<TEventArgs> predicate optional, AsyncEventBridge.EventStreamOptions options optional, System.Threading.CancellationToken cancellationToken optional)");

        AssertMethodSignatures(
            typeof(EventStreamComposition),
            "System.Collections.Generic.IAsyncEnumerable<T> RepeatBetween<T>(System.Collections.Generic.IAsyncEnumerable<T> source, System.Func<System.Threading.CancellationToken,System.Threading.Tasks.Task> startWait, System.Func<System.Threading.CancellationToken,System.Threading.Tasks.Task> stopWait, System.Threading.CancellationToken cancellationToken optional)",
            "System.Collections.Generic.IAsyncEnumerable<AsyncEventBridge.EventStreamLifecycleEvent<T>> RepeatBetweenWithLifecycle<T>(System.Collections.Generic.IAsyncEnumerable<T> source, System.Func<System.Threading.CancellationToken,System.Threading.Tasks.Task> startWait, System.Func<System.Threading.CancellationToken,System.Threading.Tasks.Task> stopWait, System.Threading.CancellationToken cancellationToken optional)",
            "System.Collections.Generic.IAsyncEnumerable<T> RepeatWhile<T,TState>(System.Collections.Generic.IAsyncEnumerable<T> source, System.Func<TState> getState, System.Predicate<TState> isActive, System.Func<System.Threading.CancellationToken,System.Threading.Tasks.Task> waitForStateChange, System.Threading.CancellationToken cancellationToken optional)",
            "System.Collections.Generic.IAsyncEnumerable<T> RepeatWhile<T>(System.Collections.Generic.IAsyncEnumerable<T> source, System.Func<System.Boolean> isActive, System.Func<System.Threading.CancellationToken,System.Threading.Tasks.Task> waitForStateChange, System.Threading.CancellationToken cancellationToken optional)",
            "System.Collections.Generic.IAsyncEnumerable<AsyncEventBridge.EventStreamLifecycleEvent<T>> RepeatWhileWithLifecycle<T,TState>(System.Collections.Generic.IAsyncEnumerable<T> source, System.Func<TState> getState, System.Predicate<TState> isActive, System.Func<System.Threading.CancellationToken,System.Threading.Tasks.Task> waitForStateChange, System.Threading.CancellationToken cancellationToken optional)",
            "System.Collections.Generic.IAsyncEnumerable<AsyncEventBridge.EventStreamLifecycleEvent<T>> RepeatWhileWithLifecycle<T>(System.Collections.Generic.IAsyncEnumerable<T> source, System.Func<System.Boolean> isActive, System.Func<System.Threading.CancellationToken,System.Threading.Tasks.Task> waitForStateChange, System.Threading.CancellationToken cancellationToken optional)",
            "System.Collections.Generic.IAsyncEnumerable<T> StartAfter<T>(System.Collections.Generic.IAsyncEnumerable<T> source, System.Func<System.Threading.CancellationToken,System.Threading.Tasks.Task> startWait, System.Threading.CancellationToken cancellationToken optional)",
            "System.Collections.Generic.IAsyncEnumerable<T> TakeUntil<T>(System.Collections.Generic.IAsyncEnumerable<T> source, System.Func<System.Threading.CancellationToken,System.Threading.Tasks.Task> stopWait, System.Threading.CancellationToken cancellationToken optional)");

        AssertMethodSignatures(
            typeof(EventCondition),
            "System.Threading.Tasks.Task WaitUntilAsync(System.Func<System.Boolean> condition, System.Func<System.Threading.CancellationToken,System.Threading.Tasks.Task> waitForChange, System.Threading.CancellationToken cancellationToken optional)",
            "System.Threading.Tasks.Task<TState> WaitUntilAsync<TState>(System.Func<TState> getState, System.Predicate<TState> predicate, System.Func<System.Threading.CancellationToken,System.Threading.Tasks.Task> waitForChange, System.Threading.CancellationToken cancellationToken optional)");

        AssertMethodSignatures(
            typeof(AsyncEventBridgeExtensions),
            "AsyncEventBridge.EventBridge ToEventBridge(System.Threading.Tasks.Task task)",
            "AsyncEventBridge.EventBridge ToEventBridge(System.Threading.Tasks.Task task, AsyncEventBridge.EventBridgeOptions options)",
            "AsyncEventBridge.EventBridge<T> ToEventBridge<T>(System.Threading.Tasks.Task<T> task)",
            "AsyncEventBridge.EventBridge<T> ToEventBridge<T>(System.Threading.Tasks.Task<T> task, AsyncEventBridge.EventBridgeOptions options)",
            "AsyncEventBridge.EventStreamBridge<T> ToEventBridge<T>(System.Collections.Generic.IAsyncEnumerable<T> source)",
            "AsyncEventBridge.EventStreamBridge<T> ToEventBridge<T>(System.Collections.Generic.IAsyncEnumerable<T> source, AsyncEventBridge.EventBridgeOptions options)");

        AssertMethodSignatures(typeof(EventBridge), "System.Void Connect()", "System.Void Dispose()");
        AssertMethodSignatures(typeof(EventBridge<int>), "System.Void Connect()", "System.Void Dispose()");
        AssertMethodSignatures(
            typeof(EventStreamBridge<int>),
            "System.Void Connect(System.Threading.CancellationToken cancellationToken optional)",
            "System.Void Dispose()",
            "System.Threading.Tasks.ValueTask DisposeAsync()");
    }

    [Fact]
    public void RuntimePublicConstructorsStayIntentional()
    {
        AssertConstructorSignatures(typeof(AsyncFaultedEventArgs));
        AssertConstructorSignatures(typeof(AsyncValueEventArgs<int>));
        AssertConstructorSignatures(typeof(EventBridge));
        AssertConstructorSignatures(typeof(EventBridge<int>));
        AssertConstructorSignatures(typeof(EventStreamBridge<int>));
        AssertConstructorSignatures(typeof(EventStreamLifecycleEvent<int>));

        AssertConstructorSignatures(typeof(EventBridgeOptions), "()");
        AssertConstructorSignatures(typeof(EventStreamOptions), "()");
        AssertConstructorSignatures(typeof(GenerateAsyncEventsAttribute), "()");
        AssertConstructorSignatures(
            typeof(GenerateAsyncEventsForAttribute),
            "(System.Type targetType)");
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
        AssertPropertyNames(typeof(EventStreamLifecycleEvent<int>), "Cycle", "HasValue", "Kind", "Value");
        AssertPropertyNames(typeof(EventBridgeOptions), "SubscriberExceptionObserver", "SubscriberExceptionPolicy");
        AssertPropertyNames(typeof(GenerateAsyncEventsForAttribute), "TargetType");

        Assert.Equal(
            new[] { "DropOldest", "DropWrite", "Unbounded" },
            Enum.GetNames<EventStreamFullMode>().OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(
            new[] { "IgnoreAndContinue", "ReportAndContinue", "TraceAndContinue" },
            Enum.GetNames<EventBridgeSubscriberExceptionPolicy>().OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(
            new[] { "Activated", "Deactivated", "SourceCompleted", "Unspecified", "Value" },
            Enum.GetNames<EventStreamLifecycleEventKind>().OrderBy(name => name, StringComparer.Ordinal));
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
            "FullMode:AsyncEventBridge.EventStreamFullMode:get,set");
        AssertPropertySignatures(
            typeof(EventBridgeOptions),
            "SubscriberExceptionObserver:System.Action<System.Exception>:get,set",
            "SubscriberExceptionPolicy:AsyncEventBridge.EventBridgeSubscriberExceptionPolicy:get,set");
        AssertPropertySignatures(
            typeof(EventStreamLifecycleEvent<int>),
            "Cycle:System.Int64:get",
            "HasValue:System.Boolean:get",
            "Kind:AsyncEventBridge.EventStreamLifecycleEventKind:get",
            "Value:System.Int32:get");
        AssertPropertySignatures(typeof(AsyncValueEventArgs<int>), "Value:System.Int32:get");
        AssertPropertySignatures(typeof(AsyncFaultedEventArgs), "Exception:System.Exception:get");
        AssertPropertySignatures(typeof(GenerateAsyncEventsForAttribute), "TargetType:System.Type:get");
    }

    [Fact]
    public void RuntimeNullabilityContractsStayStable()
    {
        var nullability = new NullabilityInfoContext();

        var wait = typeof(EventAwaiter)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == "WaitAsync" && !method.IsGenericMethod);
        AssertParameterNullability(nullability, wait, "subscribe", NullabilityState.NotNull);
        AssertParameterNullability(nullability, wait, "unsubscribe", NullabilityState.NotNull);
        AssertParameterNullability(nullability, wait, "predicate", NullabilityState.Nullable);

        var stream = typeof(EventStream)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == "Create" && !method.IsGenericMethod);
        AssertParameterNullability(nullability, stream, "subscribe", NullabilityState.NotNull);
        AssertParameterNullability(nullability, stream, "unsubscribe", NullabilityState.NotNull);
        AssertParameterNullability(nullability, stream, "predicate", NullabilityState.Nullable);
        AssertParameterNullability(nullability, stream, "options", NullabilityState.Nullable);

        var taskBridge = typeof(AsyncEventBridgeExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method =>
                method.Name == "ToEventBridge" &&
                !method.IsGenericMethod &&
                method.GetParameters() is [{ ParameterType: var taskType }, { ParameterType: var optionsType }] &&
                taskType == typeof(Task) &&
                optionsType == typeof(EventBridgeOptions));
        AssertParameterNullability(nullability, taskBridge, "task", NullabilityState.NotNull);
        AssertParameterNullability(nullability, taskBridge, "options", NullabilityState.NotNull);

        var observer = typeof(EventBridgeOptions).GetProperty(nameof(EventBridgeOptions.SubscriberExceptionObserver))!;
        var observerNullability = nullability.Create(observer);
        Assert.Equal(NullabilityState.Nullable, observerNullability.ReadState);
        Assert.Equal(NullabilityState.Nullable, observerNullability.WriteState);

        var targetType = typeof(GenerateAsyncEventsForAttribute)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Single();
        Assert.Equal(NullabilityState.NotNull, nullability.Create(targetType).ReadState);
    }

    [Fact]
    public void ConfigurationDefaultsAndNumericValuesStayStable()
    {
        var options = new EventStreamOptions();
        Assert.Equal(100, options.Capacity);
        Assert.Equal(EventStreamFullMode.Unbounded, options.FullMode);
        Assert.Equal(0, (int)EventStreamFullMode.Unbounded);
        Assert.Equal(1, (int)EventStreamFullMode.DropOldest);
        Assert.Equal(2, (int)EventStreamFullMode.DropWrite);

        var bridgeOptions = new EventBridgeOptions();
        Assert.Equal(EventBridgeSubscriberExceptionPolicy.TraceAndContinue, bridgeOptions.SubscriberExceptionPolicy);
        Assert.Null(bridgeOptions.SubscriberExceptionObserver);
        Assert.Equal(0, (int)EventBridgeSubscriberExceptionPolicy.TraceAndContinue);
        Assert.Equal(1, (int)EventBridgeSubscriberExceptionPolicy.ReportAndContinue);
        Assert.Equal(2, (int)EventBridgeSubscriberExceptionPolicy.IgnoreAndContinue);

        Assert.Equal(0, (int)EventStreamLifecycleEventKind.Unspecified);
        Assert.Equal(1, (int)EventStreamLifecycleEventKind.Activated);
        Assert.Equal(2, (int)EventStreamLifecycleEventKind.Value);
        Assert.Equal(3, (int)EventStreamLifecycleEventKind.Deactivated);
        Assert.Equal(4, (int)EventStreamLifecycleEventKind.SourceCompleted);
    }

    private static void AssertParameterNullability(
        NullabilityInfoContext context,
        MethodInfo method,
        string parameterName,
        NullabilityState expected)
    {
        var parameter = method.GetParameters().Single(item => item.Name == parameterName);
        Assert.Equal(expected, context.Create(parameter).ReadState);
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

    private static void AssertConstructorSignatures(Type type, params string[] expected)
    {
        var actual = type
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(constructor =>
                $"({string.Join(", ", constructor.GetParameters().Select(parameter => $"{DescribeType(parameter.ParameterType)} {parameter.Name}"))})")
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected.OrderBy(signature => signature, StringComparer.Ordinal), actual);
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
