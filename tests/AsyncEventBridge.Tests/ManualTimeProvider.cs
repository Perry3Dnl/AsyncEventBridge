namespace AsyncEventBridge.Tests;

internal sealed class ManualTimeProvider(bool fireOnCreate = false) : TimeProvider
{
    private ManualTimer? _timer;

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);

        var timer = new ManualTimer(callback, state);

        if (Interlocked.CompareExchange(ref _timer, timer, null) is not null)
        {
            throw new InvalidOperationException("Only one timer registration is supported by this test provider.");
        }

        if (fireOnCreate)
        {
            timer.Fire();
        }

        return timer;
    }

    internal void Fire() => Volatile.Read(ref _timer)?.Fire();

    private sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
    {
        private TimerCallback? _callback = callback;
        private object? _state = state;

        public bool Change(TimeSpan dueTime, TimeSpan period) =>
            Volatile.Read(ref _callback) is not null;

        internal void Fire()
        {
            var callback = Interlocked.Exchange(ref _callback, null);
            callback?.Invoke(_state);
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _callback, null);
            _state = null;
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
