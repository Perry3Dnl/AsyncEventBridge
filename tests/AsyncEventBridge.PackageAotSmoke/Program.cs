using AsyncEventBridge;

[assembly: GenerateAsyncEventsFor(typeof(ExternalSensor))]

var sensor = new ModernSensor();
var wait = sensor.ValueChangedAsync();
sensor.Raise(42);
if (await wait != 42)
{
    throw new InvalidOperationException("Native AOT generated Event -> Task bridge returned the wrong value.");
}

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

await using (var stream = sensor.ValueChangedStream().GetAsyncEnumerator())
{
    var moveNext = stream.MoveNextAsync().AsTask();
    sensor.Raise(43);

    if (!await moveNext || stream.Current != 43)
    {
        throw new InvalidOperationException("Native AOT generated Event -> async-stream bridge returned the wrong value.");
    }
}

var compositionSensor = new AotCompositionSensor();
var pairAll = EventComposition.WaitAllAsync(
    token => compositionSensor.NumberAsync(token),
    token => compositionSensor.TextAsync(token));
compositionSensor.RaiseText("ready");
compositionSensor.RaiseNumber(48);
var pairResult = await pairAll;
if (pairResult.First != 48 || pairResult.Second != "ready" || compositionSensor.HandlerCount != 0)
{
    throw new InvalidOperationException("Native AOT heterogeneous WaitAll returned the wrong values or leaked subscriptions.");
}

Func<CancellationToken, Task<int>>[] indexedWaits =
[
    token => compositionSensor.FirstAsync(token),
    token => compositionSensor.SecondAsync(token),
    token => compositionSensor.ThirdAsync(token),
];

var indexedAnyWait = EventComposition.WaitAnyAsync(indexedWaits);
compositionSensor.RaiseThird(51);
var indexedAny = await indexedAnyWait;
if (indexedAny.Index != 2 || indexedAny.Value != 51 || compositionSensor.HandlerCount != 0)
{
    throw new InvalidOperationException("Native AOT indexed WaitAny returned the wrong winner or leaked subscriptions.");
}

var indexedAllWait = EventComposition.WaitAllAsync(indexedWaits);
compositionSensor.RaiseSecond(50);
compositionSensor.RaiseFirst(49);
compositionSensor.RaiseThird(51);
if (!(await indexedAllWait).SequenceEqual([49, 50, 51]) || compositionSensor.HandlerCount != 0)
{
    throw new InvalidOperationException("Native AOT indexed WaitAll returned the wrong values or leaked subscriptions.");
}

var external = new ExternalSensor();
var externalWait = external.ChangedAsync();
external.Raise(44);
if (await externalWait != 44 || external.HandlerCount != 0)
{
    throw new InvalidOperationException("Native AOT assembly-level generated adapter did not complete and unsubscribe correctly.");
}

var bridgedValue = 0;
using (EventBridge<int> bridge = new ValueTask<int>(45).ToEventBridge())
{
    bridge.Completed += (_, eventArgs) => bridgedValue = eventArgs.Value;
    bridge.Connect();
}

if (bridgedValue != 45)
{
    throw new InvalidOperationException("Native AOT ValueTask<T> -> event bridge returned the wrong value.");
}

var streamValues = new List<int>();
var streamCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
await using (EventStreamBridge<int> streamBridge = Values().ToEventBridge())
{
    streamBridge.Value += (_, eventArgs) => streamValues.Add(eventArgs.Value);
    streamBridge.Completed += (_, _) => streamCompleted.TrySetResult();
    streamBridge.Faulted += (_, eventArgs) => streamCompleted.TrySetException(eventArgs.Exception);
    streamBridge.Cancelled += (_, _) => streamCompleted.TrySetCanceled();

    streamBridge.Connect();
    await streamCompleted.Task;
}

if (!streamValues.SequenceEqual([1, 2, 3]))
{
    throw new InvalidOperationException("Native AOT async-stream -> event bridge returned values out of order.");
}

Console.WriteLine("AsyncEventBridge Native AOT package smoke test passed.");

static async IAsyncEnumerable<int> Values()
{
    yield return 1;
    await Task.Yield();
    yield return 2;
    yield return 3;
}

[GenerateAsyncEvents]
public sealed class ModernSensor
{
    public event EventHandler<int>? ValueChanged;

    public void Raise(int value) => ValueChanged?.Invoke(this, value);
}

[GenerateAsyncEvents]
public sealed class AotCompositionSensor
{
    private EventHandler<int>? _number;
    private EventHandler<string>? _text;
    private EventHandler<int>? _first;
    private EventHandler<int>? _second;
    private EventHandler<int>? _third;

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

    public event EventHandler<int>? First
    {
        add => _first += value;
        remove => _first -= value;
    }

    public event EventHandler<int>? Second
    {
        add => _second += value;
        remove => _second -= value;
    }

    public event EventHandler<int>? Third
    {
        add => _third += value;
        remove => _third -= value;
    }

    public int HandlerCount =>
        (_number?.GetInvocationList().Length ?? 0) +
        (_text?.GetInvocationList().Length ?? 0) +
        (_first?.GetInvocationList().Length ?? 0) +
        (_second?.GetInvocationList().Length ?? 0) +
        (_third?.GetInvocationList().Length ?? 0);

    public void RaiseNumber(int value) => _number?.Invoke(this, value);
    public void RaiseText(string value) => _text?.Invoke(this, value);
    public void RaiseFirst(int value) => _first?.Invoke(this, value);
    public void RaiseSecond(int value) => _second?.Invoke(this, value);
    public void RaiseThird(int value) => _third?.Invoke(this, value);
}

public delegate void ExternalChangedHandler(object? sender, int value);

public sealed class ExternalSensor
{
    private ExternalChangedHandler? _changed;

    public event ExternalChangedHandler? Changed
    {
        add => _changed += value;
        remove => _changed -= value;
    }

    public int HandlerCount => _changed?.GetInvocationList().Length ?? 0;

    public void Raise(int value) => _changed?.Invoke(this, value);
}
