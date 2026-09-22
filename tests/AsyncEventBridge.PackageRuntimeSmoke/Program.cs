using AsyncEventBridge;

[assembly: GenerateAsyncEventsFor(typeof(CustomSensor))]

var sensor = new Sensor();
var wait = sensor.ValueChangedAsync();
sensor.Raise(42);
var observed = await wait;

if (observed.Value != 42)
{
    throw new InvalidOperationException("The packaged Event -> Task bridge returned the wrong value.");
}

var customSensor = new CustomSensor();
var customWait = customSensor.ChangedAsync();
customSensor.Raise(99);
var customObserved = await customWait;

if (customObserved.Value != 99 || customSensor.HandlerCount != 0)
{
    throw new InvalidOperationException("The packaged custom-delegate Event -> Task adapter did not complete and clean up correctly.");
}

await using (var customEnumerator = customSensor.ChangedStream().GetAsyncEnumerator())
{
    var moveNext = customEnumerator.MoveNextAsync().AsTask();
    customSensor.Raise(100);

    if (!await moveNext || customEnumerator.Current.Value != 100)
    {
        throw new InvalidOperationException("The packaged custom-delegate Event -> async-stream adapter returned the wrong value.");
    }
}

if (customSensor.HandlerCount != 0)
{
    throw new InvalidOperationException("The packaged custom-delegate stream adapter did not unsubscribe.");
}

var modernSensor = new ModernPayloadSensor();
var modernWait = modernSensor.ValueChangedAsync(
    TimeSpan.FromSeconds(1),
    timeProvider: TimeProvider.System);
modernSensor.Raise(123);
if (await modernWait != 123)
{
    throw new InvalidOperationException("The packaged EventHandler<int> bridge returned the wrong value.");
}

var strongSenderSensor = new StrongSenderSensor();
var strongSenderWait = strongSenderSensor.ValueChangedAsync(
    TimeSpan.FromSeconds(1),
    timeProvider: TimeProvider.System);
strongSenderSensor.Raise(321);
if (await strongSenderWait != 321)
{
    throw new InvalidOperationException("The packaged EventHandler<TSender, TPayload> bridge returned the wrong value.");
}

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

var waitAll = EventComposition.WaitAllAsync(
    token => choiceSensor.NumberAsync(token),
    token => choiceSensor.TextAsync(token));
choiceSensor.RaiseText("both");
choiceSensor.RaiseNumber(7);
var all = await waitAll;
if (all.First != 7 || all.Second != "both" ||
    choiceSensor.NumberHandlerCount != 0 || choiceSensor.TextHandlerCount != 0)
{
    throw new InvalidOperationException("The packaged heterogeneous WaitAll composition returned the wrong values or leaked a subscription.");
}

var indexedSensor = new IndexedChoiceSensor();
Func<CancellationToken, Task<int>>[] indexedWaits =
[
    token => indexedSensor.FirstAsync(token),
    token => indexedSensor.SecondAsync(token),
    token => indexedSensor.ThirdAsync(token),
];

var indexedAnyWait = EventComposition.WaitAnyAsync(indexedWaits);
indexedSensor.RaiseSecond(20);
var indexedAny = await indexedAnyWait;
if (indexedAny.Index != 1 || indexedAny.Value != 20 || indexedSensor.HandlerCount != 0)
{
    throw new InvalidOperationException("The packaged indexed WaitAny composition returned the wrong winner or leaked subscriptions.");
}

var indexedAllWait = EventComposition.WaitAllAsync(indexedWaits);
indexedSensor.RaiseThird(30);
indexedSensor.RaiseFirst(10);
indexedSensor.RaiseSecond(20);
var indexedAll = await indexedAllWait;
if (!indexedAll.SequenceEqual([10, 20, 30]) || indexedSensor.HandlerCount != 0)
{
    throw new InvalidOperationException("The packaged indexed WaitAll composition did not preserve input ordering or leaked subscriptions.");
}

var droppedCounts = new List<long>();
var dropOptions = new EventStreamOptions
{
    Capacity = 1,
    FullMode = EventStreamFullMode.DropNewest,
    DropObserver = droppedCounts.Add,
};
await using (var modernStream = modernSensor.ValueChangedStream(dropOptions).GetAsyncEnumerator())
{
    var firstMove = modernStream.MoveNextAsync().AsTask();
    modernSensor.Raise(10);
    if (!await firstMove || modernStream.Current != 10)
    {
        throw new InvalidOperationException("The packaged generated stream failed before backpressure verification.");
    }

    modernSensor.Raise(20);
    modernSensor.Raise(30);

    if (dropOptions.DroppedCount != 1 || !droppedCounts.SequenceEqual(new long[] { 1 }))
    {
        throw new InvalidOperationException("The packaged bounded stream did not report the dropped event.");
    }

    if (!await modernStream.MoveNextAsync() || modernStream.Current != 20)
    {
        throw new InvalidOperationException("DropNewest changed the buffered event ordering.");
    }
}

var taskValue = 0;
using (EventBridge<int> taskBridge = Task.FromResult(7).ToEventBridge())
{
    taskBridge.Completed += (_, eventArgs) => taskValue = eventArgs.Value;
    taskBridge.Connect();
}

if (taskValue != 7)
{
    throw new InvalidOperationException("The packaged Task<T> -> Events bridge returned the wrong value.");
}

var valueTaskValue = 0;
using (EventBridge<int> valueTaskBridge = new ValueTask<int>(8).ToEventBridge())
{
    valueTaskBridge.Completed += (_, eventArgs) => valueTaskValue = eventArgs.Value;
    valueTaskBridge.Connect();
}

if (valueTaskValue != 8)
{
    throw new InvalidOperationException("The packaged ValueTask<T> -> Events bridge returned the wrong value.");
}

var streamValues = new List<int>();
var streamCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
await using (EventStreamBridge<int> streamBridge = Values().ToEventBridge())
{
    streamBridge.Value += (_, eventArgs) => streamValues.Add(eventArgs.Value);
    streamBridge.Completed += (_, _) => streamCompleted.TrySetResult(true);
    streamBridge.Faulted += (_, eventArgs) => streamCompleted.TrySetException(eventArgs.Exception);
    streamBridge.Cancelled += (_, _) => streamCompleted.TrySetCanceled();

    streamBridge.Connect();
    await streamCompleted.Task;
}

if (!streamValues.SequenceEqual(new[] { 1, 2, 3 }))
{
    throw new InvalidOperationException("The packaged async-stream bridge returned values out of order.");
}


var startAfterValues = new List<int>();
await foreach (var value in Values().StartAfter(_ => Task.CompletedTask))
{
    startAfterValues.Add(value);
}

if (!startAfterValues.SequenceEqual(new[] { 1, 2, 3 }))
{
    throw new InvalidOperationException("The packaged StartAfter workflow composition returned the wrong values.");
}

Console.WriteLine("AsyncEventBridge packaged runtime smoke test passed.");

static async IAsyncEnumerable<int> Values()
{
    yield return 1;
    await Task.Yield();
    yield return 2;
    yield return 3;
}

[GenerateAsyncEvents]
public sealed class Sensor
{
    public event EventHandler<SensorEventArgs>? ValueChanged;

    public void Raise(int value)
    {
        ValueChanged?.Invoke(this, new SensorEventArgs(value));
    }
}

public delegate void CustomSensorChangedHandler(object? sender, SensorEventArgs eventArgs);

public sealed class CustomSensor
{
    private CustomSensorChangedHandler? _changed;

    public event CustomSensorChangedHandler? Changed
    {
        add => _changed += value;
        remove => _changed -= value;
    }

    public int HandlerCount => _changed?.GetInvocationList().Length ?? 0;

    public void Raise(int value)
    {
        _changed?.Invoke(this, new SensorEventArgs(value));
    }
}

public sealed class SensorEventArgs : EventArgs
{
    public SensorEventArgs(int value)
    {
        Value = value;
    }

    public int Value { get; }
}

[GenerateAsyncEvents]
public sealed class ModernPayloadSensor
{
    public event EventHandler<int>? ValueChanged;

    public void Raise(int value) => ValueChanged?.Invoke(this, value);
}

[GenerateAsyncEvents]
public sealed class StrongSenderSensor
{
    public event EventHandler<StrongSenderSensor, int>? ValueChanged;

    public void Raise(int value) => ValueChanged?.Invoke(this, value);
}

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

    public void RaiseNumber(int value) => _number?.Invoke(this, value);
    public void RaiseText(string value) => _text?.Invoke(this, value);
}

[GenerateAsyncEvents]
public sealed class IndexedChoiceSensor
{
    private EventHandler<int>? _first;
    private EventHandler<int>? _second;
    private EventHandler<int>? _third;

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
        (_first?.GetInvocationList().Length ?? 0) +
        (_second?.GetInvocationList().Length ?? 0) +
        (_third?.GetInvocationList().Length ?? 0);

    public void RaiseFirst(int value) => _first?.Invoke(this, value);
    public void RaiseSecond(int value) => _second?.Invoke(this, value);
    public void RaiseThird(int value) => _third?.Invoke(this, value);
}
