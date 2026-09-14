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
        public event EventHandler? Connected;
        public event EventHandler<SensorEventArgs>? ValueChanged;

        public void RaiseConnected()
        {
            Connected?.Invoke(this, EventArgs.Empty);
        }

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
        public static void CompileGeneratedSurface(
            Sensor sensor,
            CancellationToken cancellationToken = default)
        {
            Predicate<SensorEventArgs> highValue = eventArgs => eventArgs.Value >= 100;
            var timeout = TimeSpan.FromSeconds(5);
            var options = new EventStreamOptions
            {
                Capacity = 4,
                FullMode = EventStreamFullMode.DropOldest,
            };

            _ = sensor.ConnectedAsync();
            _ = sensor.ConnectedAsync(cancellationToken);
            _ = sensor.ConnectedAsync(timeout);
            _ = sensor.ConnectedAsync(timeout, cancellationToken);

            _ = sensor.ValueChangedAsync();
            _ = sensor.ValueChangedAsync(cancellationToken);
            _ = sensor.ValueChangedAsync(highValue);
            _ = sensor.ValueChangedAsync(highValue, cancellationToken);
            _ = sensor.ValueChangedAsync(timeout);
            _ = sensor.ValueChangedAsync(timeout, cancellationToken);
            _ = sensor.ValueChangedAsync(highValue, timeout);
            _ = sensor.ValueChangedAsync(highValue, timeout, cancellationToken);

            _ = sensor.ConnectedStream();
            _ = sensor.ConnectedStream(cancellationToken);
            _ = sensor.ConnectedStream(options);
            _ = sensor.ConnectedStream(options, cancellationToken);

            _ = sensor.ValueChangedStream();
            _ = sensor.ValueChangedStream(cancellationToken);
            _ = sensor.ValueChangedStream(options);
            _ = sensor.ValueChangedStream(options, cancellationToken);
            _ = sensor.ValueChangedStream(highValue);
            _ = sensor.ValueChangedStream(highValue, cancellationToken);
            _ = sensor.ValueChangedStream(highValue, options);
            _ = sensor.ValueChangedStream(highValue, options, cancellationToken);
        }

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

        public static EventBridge ToTaskBridge(Task task)
        {
            return task.ToEventBridge();
        }

        public static EventBridge<int> ToGenericTaskBridge(Task<int> task)
        {
            return task.ToEventBridge();
        }

        public static EventStreamBridge<int> ToStreamBridge(IAsyncEnumerable<int> stream)
        {
            return stream.ToEventBridge();
        }
    }
}
