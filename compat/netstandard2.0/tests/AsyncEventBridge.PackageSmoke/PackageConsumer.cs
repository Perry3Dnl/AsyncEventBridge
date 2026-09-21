using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AsyncEventBridge;

[assembly: GenerateAsyncEventsFor(typeof(System.Timers.Timer))]

namespace AsyncEventBridge.PackageSmoke
{
    [GenerateAsyncEvents]
    public sealed class Sensor
    {
        public event EventHandler<SensorEventArgs>? ValueChanged;

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

    public static class PackageConsumer
    {
        public static Task<SensorEventArgs> WaitForValueAsync(
            Sensor sensor,
            CancellationToken cancellationToken = default)
        {
            return sensor.ValueChangedAsync(
                eventArgs => eventArgs.Value >= 100,
                cancellationToken);
        }

        public static IAsyncEnumerable<SensorEventArgs> ReadValues(
            Sensor sensor,
            CancellationToken cancellationToken = default)
        {
            return sensor.ValueChangedStream(
                new EventStreamOptions
                {
                    Capacity = 16,
                    FullMode = EventStreamFullMode.DropOldest,
                },
                cancellationToken);
        }

        public static Task<System.Timers.ElapsedEventArgs> WaitForElapsedAsync(
            System.Timers.Timer timer,
            CancellationToken cancellationToken = default)
        {
            return timer.ElapsedAsync(cancellationToken);
        }

        public static IAsyncEnumerable<System.Timers.ElapsedEventArgs> ReadElapsed(
            System.Timers.Timer timer,
            CancellationToken cancellationToken = default)
        {
            return timer.ElapsedStream(cancellationToken);
        }

        public static EventBridge<int> BridgeTask(Task<int> task)
        {
            return task.ToEventBridge();
        }

        public static EventStreamBridge<int> BridgeStream(IAsyncEnumerable<int> stream)
        {
            return stream.ToEventBridge();
        }
    }
}
