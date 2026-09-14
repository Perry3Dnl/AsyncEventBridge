using AsyncEventBridge;

var sensor = new Sensor();

Console.WriteLine("Event -> Task");
var nextHighValue = sensor.ValueChangedAsync(value => value.Value >= 100);
sensor.RaiseValue(42);
sensor.RaiseValue(125);
var highValue = await nextHighValue;
Console.WriteLine($"Observed {highValue.Value}");

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

static async IAsyncEnumerable<SensorValue> ReadSensorValuesAsync()
{
    yield return new SensorValue(10);
    await Task.Yield();
    yield return new SensorValue(20);
}

[GenerateAsyncEvents]
public sealed class Sensor
{
    public event EventHandler? Connected;
    public event EventHandler<SensorEventArgs>? ValueChanged;

    public void RaiseConnected()
    {
        Connected?.Invoke(this, EventArgs.Empty);
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
