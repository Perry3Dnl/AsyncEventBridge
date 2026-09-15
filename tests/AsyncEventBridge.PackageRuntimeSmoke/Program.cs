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
