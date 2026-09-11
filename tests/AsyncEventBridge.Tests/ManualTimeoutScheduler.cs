namespace AsyncEventBridge.Tests;

internal sealed class ManualTimeoutScheduler : ITimeoutScheduler
{
    private Registration? _registration;

    public IDisposable Schedule(TimeSpan timeout, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        var registration = new Registration(callback);

        if (Interlocked.CompareExchange(ref _registration, registration, null) is not null)
        {
            throw new InvalidOperationException("Only one timeout registration is supported by this test scheduler.");
        }

        return registration;
    }

    internal void Fire() => Volatile.Read(ref _registration)?.Fire();

    private sealed class Registration(Action callback) : IDisposable
    {
        private Action? _callback = callback;

        internal void Fire() => Interlocked.Exchange(ref _callback, null)?.Invoke();

        public void Dispose() => Interlocked.Exchange(ref _callback, null);
    }
}
