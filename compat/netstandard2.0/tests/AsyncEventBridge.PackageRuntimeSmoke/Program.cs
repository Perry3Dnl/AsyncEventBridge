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


var startAfterValues = new List<int>();
await foreach (var value in Values().StartAfter(_ => Task.CompletedTask))
{
    startAfterValues.Add(value);
}

if (!startAfterValues.SequenceEqual(new[] { 1, 2, 3 }))
{
    throw new InvalidOperationException("The packaged StartAfter workflow composition returned the wrong values.");
}


var repeatedValues = new List<int>();
await using (var repeated = Values()
    .RepeatBetween(
        _ => Task.CompletedTask,
        token => Task.Delay(Timeout.InfiniteTimeSpan, token))
    .GetAsyncEnumerator())
{
    for (var index = 0; index < 4; index++)
    {
        if (!await repeated.MoveNextAsync())
        {
            throw new InvalidOperationException("The packaged repeating lifecycle ended unexpectedly.");
        }

        repeatedValues.Add(repeated.Current);
    }
}

if (!repeatedValues.SequenceEqual(new[] { 1, 2, 3, 1 }))
{
    throw new InvalidOperationException("The packaged RepeatBetween workflow did not create a fresh source enumeration for the next lifecycle cycle.");
}


var lifecycleEvents = new List<(EventStreamLifecycleEventKind Kind, long Cycle, int? Value)>();
await using (var lifecycle = Values()
    .RepeatBetweenWithLifecycle(
        _ => Task.CompletedTask,
        token => Task.Delay(Timeout.InfiniteTimeSpan, token))
    .GetAsyncEnumerator())
{
    for (var index = 0; index < 6; index++)
    {
        if (!await lifecycle.MoveNextAsync())
        {
            throw new InvalidOperationException("The packaged observable lifecycle ended unexpectedly.");
        }

        var current = lifecycle.Current;
        lifecycleEvents.Add((
            current.Kind,
            current.Cycle,
            current.HasValue ? current.Value : null));
    }
}

var expectedLifecycle = new[]
{
    (EventStreamLifecycleEventKind.Activated, 1L, (int?)null),
    (EventStreamLifecycleEventKind.Value, 1L, (int?)1),
    (EventStreamLifecycleEventKind.Value, 1L, (int?)2),
    (EventStreamLifecycleEventKind.Value, 1L, (int?)3),
    (EventStreamLifecycleEventKind.SourceCompleted, 1L, (int?)null),
    (EventStreamLifecycleEventKind.Activated, 2L, (int?)null),
};

if (!lifecycleEvents.SequenceEqual(expectedLifecycle))
{
    throw new InvalidOperationException("The packaged observable lifecycle returned the wrong transition/value sequence.");
}


var conditionState = true;
var conditionWaitArmed = 0;
var observedConditionState = await EventCondition.WaitUntilAsync(
    () => conditionState,
    state => state,
    token =>
    {
        Interlocked.Increment(ref conditionWaitArmed);
        return Task.Delay(Timeout.InfiniteTimeSpan, token);
    });

if (!observedConditionState || conditionWaitArmed != 1)
{
    throw new InvalidOperationException("The packaged EventCondition wait did not arm-before-check or return the satisfied state.");
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
