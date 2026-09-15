using BenchmarkDotNet.Attributes;

namespace AsyncEventBridge.Benchmarks;

[MemoryDiagnoser]
public class EventWaitBenchmarks
{
    private static readonly TimeSpan TimeoutDuration = TimeSpan.FromMinutes(1);

    private readonly GeneratedBenchmarkEventSource _source = new();
    private readonly ReusableTimeProvider _timeProvider = new();

    [Benchmark(Baseline = true)]
    public async Task<int> LowLevelCompletion()
    {
        var wait = EventAwaiter.WaitAsync<int>(
            handler => _source.ValueChanged += handler,
            handler => _source.ValueChanged -= handler);

        _source.Raise(42);
        return await wait;
    }

    [Benchmark]
    public async Task<int> GeneratedCompletion()
    {
        var wait = _source.ValueChangedAsync();
        _source.Raise(42);
        return await wait;
    }

    [Benchmark]
    public async Task<bool> LowLevelCancellation()
    {
        using var cancellationSource = new CancellationTokenSource();
        var wait = EventAwaiter.WaitAsync<int>(
            handler => _source.ValueChanged += handler,
            handler => _source.ValueChanged -= handler,
            cancellationToken: cancellationSource.Token);

        cancellationSource.Cancel();

        try
        {
            await wait;
            return false;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
    }

    [Benchmark]
    public async Task<bool> GeneratedCancellation()
    {
        using var cancellationSource = new CancellationTokenSource();
        var wait = _source.ValueChangedAsync(cancellationToken: cancellationSource.Token);

        cancellationSource.Cancel();

        try
        {
            await wait;
            return false;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
    }

    [Benchmark]
    public async Task<bool> LowLevelTimeout()
    {
        var wait = EventAwaiter.WaitAsync<int>(
            handler => _source.ValueChanged += handler,
            handler => _source.ValueChanged -= handler,
            timeout: TimeoutDuration,
            timeProvider: _timeProvider);

        _timeProvider.Fire();

        try
        {
            await wait;
            return false;
        }
        catch (TimeoutException)
        {
            return true;
        }
    }

    [Benchmark]
    public async Task<bool> GeneratedTimeout()
    {
        var wait = _source.ValueChangedAsync(
            TimeoutDuration,
            timeProvider: _timeProvider);

        _timeProvider.Fire();

        try
        {
            await wait;
            return false;
        }
        catch (TimeoutException)
        {
            return true;
        }
    }

    private sealed class ReusableTimeProvider : TimeProvider
    {
        private readonly ReusableTimer _timer = new();

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            ArgumentNullException.ThrowIfNull(callback);
            _timer.Reset(callback, state);
            return _timer;
        }

        internal void Fire() => _timer.Fire();

        private sealed class ReusableTimer : ITimer
        {
            private TimerCallback? _callback;
            private object? _state;

            internal void Reset(TimerCallback callback, object? state)
            {
                _state = state;
                Volatile.Write(ref _callback, callback);
            }

            internal void Fire()
            {
                var state = Volatile.Read(ref _state);
                var callback = Interlocked.Exchange(ref _callback, null);
                callback?.Invoke(state);
            }

            public bool Change(TimeSpan dueTime, TimeSpan period) =>
                Volatile.Read(ref _callback) is not null;

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
}

[GenerateAsyncEvents]
public sealed class GeneratedBenchmarkEventSource
{
    public event EventHandler<int>? ValueChanged;

    public void Raise(int value) => ValueChanged?.Invoke(this, value);
}
