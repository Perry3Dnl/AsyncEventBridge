using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using AsyncEventBridge;

[assembly: GenerateAsyncEventsFor(typeof(System.Timers.Timer))]
[assembly: GenerateAsyncEventsFor(typeof(INotifyPropertyChanged))]

namespace AsyncEventBridge.PackageMultiTargetSmoke
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
            return sensor.ValueChangedAsync(cancellationToken);
        }

        public static IAsyncEnumerable<SensorEventArgs> ReadValues(
            Sensor sensor,
            CancellationToken cancellationToken = default)
        {
            return sensor.ValueChangedStream(cancellationToken);
        }

        public static Task<PropertyChangedEventArgs> WaitForPropertyChangeAsync(
            INotifyPropertyChanged model,
            CancellationToken cancellationToken = default)
        {
            return model.PropertyChangedAsync(cancellationToken);
        }

        public static IAsyncEnumerable<PropertyChangedEventArgs> ReadPropertyChanges(
            INotifyPropertyChanged model,
            CancellationToken cancellationToken = default)
        {
            return model.PropertyChangedStream(cancellationToken);
        }

        public static Task<System.Timers.ElapsedEventArgs> WaitForElapsedAsync(
            System.Timers.Timer timer,
            CancellationToken cancellationToken = default)
        {
            return timer.ElapsedAsync(cancellationToken);
        }
    }
}
