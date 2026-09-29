using AsyncEventBridge;

var sensor = new Sensor();

Console.WriteLine("Event -> Task");
var nextHighValue = sensor.ValueChangedAsync(value => value.Value >= 100);
sensor.RaiseValue(42);
sensor.RaiseValue(125);
var highValue = await nextHighValue;
Console.WriteLine($"Observed {highValue.Value}");

Console.WriteLine();
Console.WriteLine("Event -> IAsyncEnumerable<T>");
var nextTwoValues = ReadTwoValuesAsync(sensor);
sensor.RaiseValue(10);
sensor.RaiseValue(20);

foreach (var value in await nextTwoValues)
{
    Console.WriteLine($"Stream value: {value}");
}

Console.WriteLine();
Console.WriteLine("State-driven event lifecycle");
var firstActivation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
var firstValueObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
var firstDeactivation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
var secondActivation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
var secondValueObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
var connectedValues = ObserveConnectedValuesAsync(
    sensor,
    firstActivation,
    firstValueObserved,
    firstDeactivation,
    secondActivation,
    secondValueObserved);

sensor.SetConnected(true);
await firstActivation.Task;
sensor.RaiseValue(30);
await firstValueObserved.Task;

sensor.SetConnected(false);
await firstDeactivation.Task;
sensor.RaiseValue(999); // Not observed while disconnected.

sensor.SetConnected(true);
await secondActivation.Task;
sensor.RaiseValue(40);
await secondValueObserved.Task;

foreach (var value in await connectedValues)
{
    Console.WriteLine($"Connected value: {value}");
}

Console.WriteLine();
Console.WriteLine("Task<T> -> Events");
using EventBridge<SensorConfiguration> configurationBridge =
    Task.FromResult(new SensorConfiguration("Production")).ToEventBridge();

configurationBridge.Completed += (_, eventArgs) =>
    Console.WriteLine($"Loaded configuration: {eventArgs.Value.Name}");
configurationBridge.Faulted += (_, eventArgs) =>
    Console.WriteLine($"Configuration failed: {eventArgs.Exception.Message}");
configurationBridge.Cancelled += (_, _) =>
    Console.WriteLine("Configuration load was cancelled");

configurationBridge.Connect();

Console.WriteLine();
Console.WriteLine("IAsyncEnumerable<T> -> Events");
var streamCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
await using EventStreamBridge<SensorValue> streamBridge = ReadSensorValuesAsync().ToEventBridge();

streamBridge.Value += (_, eventArgs) =>
    Console.WriteLine($"Stream value: {eventArgs.Value.Value}");
streamBridge.Completed += (_, _) => streamCompleted.TrySetResult(true);
streamBridge.Faulted += (_, eventArgs) => streamCompleted.TrySetException(eventArgs.Exception);
streamBridge.Cancelled += (_, _) => streamCompleted.TrySetCanceled();

streamBridge.Connect();
await streamCompleted.Task;

static async Task<IReadOnlyList<int>> ReadTwoValuesAsync(Sensor sensor)
{
    var values = new List<int>();

    await foreach (var value in sensor.ValueChangedStream())
    {
        values.Add(value.Value);

        if (values.Count == 2)
        {
            break;
        }
    }

    return values;
}

static async Task<IReadOnlyList<int>> ObserveConnectedValuesAsync(
    Sensor sensor,
    TaskCompletionSource<bool> firstActivation,
    TaskCompletionSource<bool> firstValueObserved,
    TaskCompletionSource<bool> firstDeactivation,
    TaskCompletionSource<bool> secondActivation,
    TaskCompletionSource<bool> secondValueObserved)
{
    var values = new List<int>();
    var activationCount = 0;

    await foreach (var item in sensor.ValueChangedStream()
        .RepeatWhileWithLifecycle(
            () => sensor.IsConnected,
            token => sensor.ConnectionChangedAsync(token)))
    {
        switch (item.Kind)
        {
            case EventStreamLifecycleEventKind.Activated:
                activationCount++;

                if (activationCount == 1)
                {
                    firstActivation.TrySetResult(true);
                }
                else if (activationCount == 2)
                {
                    secondActivation.TrySetResult(true);
                }

                break;

            case EventStreamLifecycleEventKind.Value:
                values.Add(item.Value.Value);

                if (values.Count == 1)
                {
                    firstValueObserved.TrySetResult(true);
                }
                else if (values.Count == 2)
                {
                    secondValueObserved.TrySetResult(true);
                    return values;
                }

                break;

            case EventStreamLifecycleEventKind.Deactivated:
                if (item.Cycle == 1)
                {
                    firstDeactivation.TrySetResult(true);
                }

                break;
        }
    }

    return values;
}

static async IAsyncEnumerable<SensorValue> ReadSensorValuesAsync()
{
    yield return new SensorValue(10);
    await Task.Yield();
    yield return new SensorValue(20);
}

[GenerateAsyncEvents]
public sealed class Sensor
{
    public event EventHandler? ConnectionChanged;
    public event EventHandler<SensorEventArgs>? ValueChanged;

    public bool IsConnected { get; private set; }

    public void SetConnected(bool isConnected)
    {
        if (IsConnected == isConnected)
        {
            return;
        }

        IsConnected = isConnected;
        ConnectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RaiseValue(int value)
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

public sealed class SensorConfiguration
{
    public SensorConfiguration(string name)
    {
        Name = name;
    }

    public string Name { get; }
}

public sealed class SensorValue
{
    public SensorValue(int value)
    {
        Value = value;
    }

    public int Value { get; }
}
