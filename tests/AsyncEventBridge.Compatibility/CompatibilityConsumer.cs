using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AsyncEventBridge;

namespace AsyncEventBridge.Compatibility
{
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

    public static class CompatibilityConsumer
    {
        public static Task<SensorEventArgs> WaitAsync(
            Sensor sensor,
            CancellationToken cancellationToken = default)
        {
            return sensor.ValueChangedAsync(cancellationToken);
        }

        public static IAsyncEnumerable<SensorEventArgs> Stream(
            Sensor sensor,
            CancellationToken cancellationToken = default)
        {
            return sensor.ValueChangedStream(
                new EventStreamOptions
                {
                    Capacity = 4,
                    FullMode = EventStreamFullMode.DropOldest,
                },
                cancellationToken);
        }

        public static EventBridge<int> ToTaskBridge(Task<int> task)
        {
            return task.ToEventBridge();
        }

        public static EventStreamBridge<int> ToStreamBridge(IAsyncEnumerable<int> stream)
        {
            return stream.ToEventBridge();
        }
    }
}
