from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
    file = Path(path)
    text = file.read_text()
    if old not in text:
        raise SystemExit(f"marker not found in {path}: {old[:100]!r}")
    file.write_text(text.replace(old, new, 1))


# Fix nullable delegate rendering and add generated occurrence streams.
generator = Path("src/AsyncEventBridge.Generators/AsyncEventBridgeOccurrenceGenerator.cs")
text = generator.read_text()
text = text.replace(
    "RenderType(delegateType, typeParameters),",
    "RenderType(delegateType.WithNullableAnnotation(NullableAnnotation.NotAnnotated), typeParameters),",
    1,
)
wait_pair = """            AppendOccurrenceWait(source, typeSymbol, item, typeParameters, includeTimeout: false);
            AppendOccurrenceWait(source, typeSymbol, item, typeParameters, includeTimeout: true);"""
stream_pair = wait_pair + """
            AppendOccurrenceStream(source, typeSymbol, item, typeParameters, includeOptions: false);
            AppendOccurrenceStream(source, typeSymbol, item, typeParameters, includeOptions: true);"""
if wait_pair not in text:
    raise SystemExit("occurrence generator loop marker missing")
text = text.replace(wait_pair, stream_pair, 1)
insert_marker = "    private static bool TryDescribeEvent(\n"
stream_method = r'''    private static void AppendOccurrenceStream(
        StringBuilder source,
        INamedTypeSymbol typeSymbol,
        EventInfo item,
        TypeParameterContext typeParameters,
        bool includeOptions)
    {
        var sourceType = RenderType(typeSymbol, typeParameters);
        var eventName = EscapeIdentifier(item.EventSymbol.Name);
        var methodName = eventName.TrimStart('@') + "OccurrenceStream";
        var occurrenceType = $"global::AsyncEventBridge.EventOccurrence<{item.SenderType}, {item.PayloadType}>";

        source.Append("    ")
            .Append(item.Accessibility)
            .Append(" static global::System.Collections.Generic.IAsyncEnumerable<")
            .Append(occurrenceType)
            .Append("> ")
            .Append(methodName);
        AppendMethodTypeParameters(source, typeParameters);
        source.Append("(this ")
            .Append(sourceType)
            .Append(" source, ");

        if (includeOptions)
        {
            source.Append("global::AsyncEventBridge.EventStreamOptions options, ");
        }

        source.Append("global::System.Threading.CancellationToken cancellationToken = default)");
        AppendMethodConstraints(source, typeParameters);
        source.AppendLine()
            .AppendLine("    {")
            .AppendLine("        if (source is null)")
            .AppendLine("        {")
            .AppendLine("            throw new global::System.ArgumentNullException(nameof(source));")
            .AppendLine("        }")
            .AppendLine();

        if (includeOptions)
        {
            source.AppendLine("        if (options is null)")
                .AppendLine("        {")
                .AppendLine("            throw new global::System.ArgumentNullException(nameof(options));")
                .AppendLine("        }")
                .AppendLine();
        }

        source.Append("        ")
            .Append(item.HandlerType)
            .AppendLine("? adaptedHandler = null;")
            .AppendLine()
            .Append("        return global::AsyncEventBridge.EventOccurrenceStream.Create<")
            .Append(item.SenderType)
            .Append(", ")
            .Append(item.PayloadType)
            .AppendLine(">(")
            .Append("            (global::System.EventHandler<")
            .Append(item.SenderType)
            .Append(", ")
            .Append(item.PayloadType)
            .AppendLine("> handler) =>")
            .AppendLine("            {")
            .Append("                adaptedHandler = new ")
            .Append(item.HandlerType)
            .AppendLine("((sender, payload) => handler(sender, payload));")
            .Append("                source.")
            .Append(eventName)
            .AppendLine(" += adaptedHandler;")
            .AppendLine("            },")
            .Append("            (global::System.EventHandler<")
            .Append(item.SenderType)
            .Append(", ")
            .Append(item.PayloadType)
            .AppendLine("> _) =>")
            .AppendLine("            {")
            .AppendLine("                if (adaptedHandler != null)")
            .AppendLine("                {")
            .Append("                    source.")
            .Append(eventName)
            .AppendLine(" -= adaptedHandler;")
            .AppendLine("                }")
            .AppendLine("            },")
            .AppendLine("            null,")
            .Append("            ")
            .AppendLine(includeOptions ? "options," : "null,")
            .AppendLine("            cancellationToken);")
            .AppendLine("    }")
            .AppendLine();
    }

'''
if insert_marker not in text:
    raise SystemExit("occurrence generator insertion marker missing")
text = text.replace(insert_marker, stream_method + insert_marker, 1)
generator.write_text(text)

# Fix test reference projection and expand runtime stubs/assertions for streams.
test = Path("tests/AsyncEventBridge.Generators.Tests/AsyncEventBridgeOccurrenceGeneratorTests.cs")
text = test.read_text()
text = text.replace(
    ".Select(MetadataReference.CreateFromFile)",
    ".Select(path => MetadataReference.CreateFromFile(path))",
    1,
)
stub_marker = """            public static class EventOccurrenceAwaiter
            {
                public static Task<EventOccurrence<TSender, TPayload>> WaitAsync<TSender, TPayload>(
                    Action<EventHandler<TSender, TPayload>> subscribe,
                    Action<EventHandler<TSender, TPayload>> unsubscribe,
                    Predicate<EventOccurrence<TSender, TPayload>>? predicate = null,
                    CancellationToken cancellationToken = default,
                    TimeSpan? timeout = null,
                    TimeProvider? timeProvider = null) => throw new NotImplementedException();
            }
"""
stub_add = stub_marker + """
            public sealed class EventStreamOptions
            {
            }

            public static class EventOccurrenceStream
            {
                public static System.Collections.Generic.IAsyncEnumerable<EventOccurrence<TSender, TPayload>> Create<TSender, TPayload>(
                    Action<EventHandler<TSender, TPayload>> subscribe,
                    Action<EventHandler<TSender, TPayload>> unsubscribe,
                    Predicate<EventOccurrence<TSender, TPayload>>? predicate = null,
                    EventStreamOptions? options = null,
                    CancellationToken cancellationToken = default) => throw new NotImplementedException();
            }
"""
if stub_marker not in text:
    raise SystemExit("occurrence test runtime stub marker missing")
text = text.replace(stub_marker, stub_add, 1)
text = text.replace(
    '        Assert.Contains("ValueChangedOccurrenceAsync", generated, StringComparison.Ordinal);',
    '        Assert.Contains("ValueChangedOccurrenceAsync", generated, StringComparison.Ordinal);\n        Assert.Contains("ValueChangedOccurrenceStream", generated, StringComparison.Ordinal);',
    1,
)
test.write_text(text)

# Lock the new public runtime surface.
api = Path("tests/AsyncEventBridge.Tests/PublicApiTests.cs")
text = api.read_text()
text = text.replace(
    '            "AsyncEventBridge.EventAwaiter",\n',
    '            "AsyncEventBridge.EventAwaiter",\n            "AsyncEventBridge.EventComposition",\n            "AsyncEventBridge.EventOccurrence`2",\n            "AsyncEventBridge.EventOccurrenceAwaiter",\n            "AsyncEventBridge.EventOccurrenceStream",\n            "AsyncEventBridge.EventWaitAnyResult`2",\n',
    1,
)
text = text.replace(
    '        AssertMethodNames(typeof(EventAwaiter), "WaitAsync", "WaitAsync");\n        AssertMethodNames(typeof(EventStream), "Create", "Create");',
    '        AssertMethodNames(typeof(EventAwaiter), "WaitAsync", "WaitAsync");\n        AssertMethodNames(typeof(EventOccurrenceAwaiter), "WaitAsync");\n        AssertMethodNames(typeof(EventOccurrenceStream), "Create");\n        AssertMethodNames(typeof(EventComposition), "WaitAnyAsync");\n        AssertMethodNames(typeof(EventStream), "Create", "Create");',
    1,
)
stream_signature_block = """        AssertMethodSignatures(
            typeof(EventStream),
            "System.Collections.Generic.IAsyncEnumerable<System.EventArgs> Create(System.Action<System.EventHandler> subscribe, System.Action<System.EventHandler> unsubscribe, System.Predicate<System.EventArgs> predicate optional, AsyncEventBridge.EventStreamOptions options optional, System.Threading.CancellationToken cancellationToken optional)",
            "System.Collections.Generic.IAsyncEnumerable<TEventArgs> Create<TEventArgs>(System.Action<System.EventHandler<TEventArgs>> subscribe, System.Action<System.EventHandler<TEventArgs>> unsubscribe, System.Predicate<TEventArgs> predicate optional, AsyncEventBridge.EventStreamOptions options optional, System.Threading.CancellationToken cancellationToken optional)");
"""
new_signatures = stream_signature_block + """
        AssertMethodSignatures(
            typeof(EventOccurrenceAwaiter),
            "System.Threading.Tasks.Task<AsyncEventBridge.EventOccurrence<TSender,TPayload>> WaitAsync<TSender,TPayload>(System.Action<System.EventHandler<TSender,TPayload>> subscribe, System.Action<System.EventHandler<TSender,TPayload>> unsubscribe, System.Predicate<AsyncEventBridge.EventOccurrence<TSender,TPayload>> predicate optional, System.Threading.CancellationToken cancellationToken optional, System.Nullable<System.TimeSpan> timeout optional, System.TimeProvider timeProvider optional)");

        AssertMethodSignatures(
            typeof(EventOccurrenceStream),
            "System.Collections.Generic.IAsyncEnumerable<AsyncEventBridge.EventOccurrence<TSender,TPayload>> Create<TSender,TPayload>(System.Action<System.EventHandler<TSender,TPayload>> subscribe, System.Action<System.EventHandler<TSender,TPayload>> unsubscribe, System.Predicate<AsyncEventBridge.EventOccurrence<TSender,TPayload>> predicate optional, AsyncEventBridge.EventStreamOptions options optional, System.Threading.CancellationToken cancellationToken optional)");

        AssertMethodSignatures(
            typeof(EventComposition),
            "System.Threading.Tasks.Task<AsyncEventBridge.EventWaitAnyResult<TFirst,TSecond>> WaitAnyAsync<TFirst,TSecond>(System.Func<System.Threading.CancellationToken,System.Threading.Tasks.Task<TFirst>> firstWait, System.Func<System.Threading.CancellationToken,System.Threading.Tasks.Task<TSecond>> secondWait, System.Threading.CancellationToken cancellationToken optional)");
"""
if stream_signature_block not in text:
    raise SystemExit("public API signature marker missing")
text = text.replace(stream_signature_block, new_signatures, 1)
text = text.replace(
    '        AssertPropertyNames(typeof(EventStreamOptions), "Capacity", "DroppedCount", "DropObserver", "FullMode");',
    '        AssertPropertyNames(typeof(EventStreamOptions), "Capacity", "DroppedCount", "DropObserver", "FullMode");\n        AssertPropertyNames(typeof(EventOccurrence<object, int>), "Payload", "Sender");\n        AssertPropertyNames(typeof(EventWaitAnyResult<int, string>), "First", "IsFirst", "IsSecond", "Second");',
    1,
)
property_marker = '        AssertPropertySignatures(typeof(AsyncValueEventArgs<int>), "Value:System.Int32:get");'
property_add = '''        AssertPropertySignatures(
            typeof(EventOccurrence<object, int>),
            "Payload:System.Int32:get",
            "Sender:System.Object:get");
        AssertPropertySignatures(
            typeof(EventWaitAnyResult<int, string>),
            "First:System.Int32:get",
            "IsFirst:System.Boolean:get",
            "IsSecond:System.Boolean:get",
            "Second:System.String:get");

''' + property_marker
if property_marker not in text:
    raise SystemExit("public API property signature marker missing")
text = text.replace(property_marker, property_add, 1)
api.write_text(text)

# Packaged runtime smoke: generated sender-aware wait/stream plus WaitAny cleanup.
smoke = Path("tests/AsyncEventBridge.PackageRuntimeSmoke/Program.cs")
text = smoke.read_text()
marker = """if (await strongSenderWait != 321)
{
    throw new InvalidOperationException("The packaged EventHandler<TSender, TPayload> bridge returned the wrong value.");
}
"""
addition = marker + """
var occurrenceWait = strongSenderSensor.ValueChangedOccurrenceAsync();
strongSenderSensor.Raise(322);
var occurrence = await occurrenceWait;
if (!ReferenceEquals(occurrence.Sender, strongSenderSensor) || occurrence.Payload != 322)
{
    throw new InvalidOperationException("The packaged sender-aware generated wait lost sender or payload information.");
}

await using (var occurrenceStream = strongSenderSensor.ValueChangedOccurrenceStream().GetAsyncEnumerator())
{
    var occurrenceMove = occurrenceStream.MoveNextAsync().AsTask();
    strongSenderSensor.Raise(323);
    if (!await occurrenceMove ||
        !ReferenceEquals(occurrenceStream.Current.Sender, strongSenderSensor) ||
        occurrenceStream.Current.Payload != 323)
    {
        throw new InvalidOperationException("The packaged sender-aware generated stream lost sender or payload information.");
    }
}

var choiceSensor = new ChoiceSensor();
var choiceWait = EventComposition.WaitAnyAsync(
    token => choiceSensor.NumberAsync(token),
    token => choiceSensor.TextAsync(token));
choiceSensor.RaiseText("ready");
var choice = await choiceWait;
if (!choice.IsSecond || choice.Second != "ready" ||
    choiceSensor.NumberHandlerCount != 0 || choiceSensor.TextHandlerCount != 0)
{
    throw new InvalidOperationException("The packaged WaitAny composition did not return the winner and clean up the loser.");
}
"""
if marker not in text:
    raise SystemExit("runtime smoke strong sender marker missing")
text = text.replace(marker, addition, 1)
text += """

[GenerateAsyncEvents]
public sealed class ChoiceSensor
{
    private EventHandler<int>? _number;
    private EventHandler<string>? _text;

    public event EventHandler<int>? Number
    {
        add => _number += value;
        remove => _number -= value;
    }

    public event EventHandler<string>? Text
    {
        add => _text += value;
        remove => _text -= value;
    }

    public int NumberHandlerCount => _number?.GetInvocationList().Length ?? 0;
    public int TextHandlerCount => _text?.GetInvocationList().Length ?? 0;

    public void RaiseText(string value) => _text?.Invoke(this, value);
}
"""
smoke.write_text(text)

# Native AOT smoke verifies the new generator and runtime path from the packed package.
aot = Path("tests/AsyncEventBridge.PackageAotSmoke/Program.cs")
text = aot.read_text()
marker = """if (await wait != 42)
{
    throw new InvalidOperationException("Native AOT generated Event -> Task bridge returned the wrong value.");
}
"""
addition = marker + """
var occurrenceWait = sensor.ValueChangedOccurrenceAsync();
sensor.Raise(46);
var occurrence = await occurrenceWait;
if (!ReferenceEquals(occurrence.Sender, sensor) || occurrence.Payload != 46)
{
    throw new InvalidOperationException("Native AOT sender-aware occurrence wait returned the wrong sender or payload.");
}

await using (var occurrenceStream = sensor.ValueChangedOccurrenceStream().GetAsyncEnumerator())
{
    var moveNext = occurrenceStream.MoveNextAsync().AsTask();
    sensor.Raise(47);
    if (!await moveNext || !ReferenceEquals(occurrenceStream.Current.Sender, sensor) || occurrenceStream.Current.Payload != 47)
    {
        throw new InvalidOperationException("Native AOT sender-aware occurrence stream returned the wrong sender or payload.");
    }
}
"""
if marker not in text:
    raise SystemExit("AOT smoke marker missing")
aot.write_text(text.replace(marker, addition, 1))

# Documentation: keep the new capability explicit and composition lifecycle semantics visible.
readme = Path("README.md")
text = readme.read_text()
marker = """AsyncEventBridge remains payload-centric: the generated task/stream carries the second event parameter. A strongly typed sender is used for event subscription but is not added to the async result.
"""
addition = marker + """
When sender identity matters, the modern package also generates an opt-in occurrence facade without changing the existing payload-only API:

```csharp
EventOccurrence<Sensor, Reading> occurrence =
    await sensor.ReadingChangedOccurrenceAsync();

Sensor sender = occurrence.Sender;
Reading reading = occurrence.Payload;

await foreach (EventOccurrence<Sensor, Reading> item in sensor.ReadingChangedOccurrenceStream())
{
    Process(item.Sender, item.Payload);
}
```

The sender-aware generator supports ordinary two-parameter `void` event delegates when both sender and payload are safe to carry across an async lifetime. Ref-like senders or payloads are deliberately excluded from occurrence APIs.
"""
if marker not in text:
    raise SystemExit("README sender marker missing")
text = text.replace(marker, addition, 1)
composition_marker = "## Async work back to events\n"
composition = """## Compose event waits

`EventComposition.WaitAnyAsync` races two cancellable event waits and cancels/observes the loser before returning. This avoids leaving a hidden event subscription behind, which is the lifecycle problem with wrapping event waits in a plain `Task.WhenAny` and ignoring the losing task.

```csharp
EventWaitAnyResult<ConnectedEventArgs, ErrorEventArgs> result =
    await EventComposition.WaitAnyAsync(
        token => client.ConnectedAsync(token),
        token => client.ErrorAsync(token),
        cancellationToken);

if (result.IsFirst)
{
    HandleConnected(result.First);
}
else
{
    HandleError(result.Second);
}
```

Wait factories are expected to honor the supplied cancellation token. Winner faults and losing cleanup faults remain observable; losing cancellation used for cleanup is not treated as an error.

""" + composition_marker
if composition_marker not in text:
    raise SystemExit("README composition marker missing")
readme.write_text(text.replace(composition_marker, composition, 1))

changelog = Path("CHANGELOG.md")
text = changelog.read_text()
marker = "### Modern event waits\n\n"
addition = marker + "- Add `EventOccurrence<TSender, TPayload>` plus low-level and generated sender-aware wait/stream facades for code where sender identity is part of the event semantics. Existing payload-only generated APIs remain unchanged.\n- Add `EventComposition.WaitAnyAsync` with heterogeneous result types and deterministic loser cancellation/observation so racing event waits do not leak losing subscriptions.\n"
if marker not in text:
    raise SystemExit("changelog marker missing")
changelog.write_text(text.replace(marker, addition, 1))

modern = Path("docs/MODERN_DOTNET.md")
text = modern.read_text()
needle = "bounded-stream drop observability"
if needle in text and "sender-aware event occurrences" not in text:
    text = text.replace(
        needle,
        needle + ", sender-aware event occurrences, and lifecycle-safe two-event composition",
        1,
    )
modern.write_text(text)
