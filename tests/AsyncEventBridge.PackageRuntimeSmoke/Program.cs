using AsyncEventBridge;

var sensor = new Sensor();
var wait = sensor.ValueChangedAsync();
sensor.Raise(42);
var observed = await wait;

if (observed.Value != 42)
{
    throw new InvalidOperationException("The packaged Event -> Task bridge returned the wrong value.");
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

public sealed class SensorEventArgs : EventArgs
{
    public SensorEventArgs(int value)
    {
        Value = value;
    }

    public int Value { get; }
}
