namespace AsyncEventBridge;

/// <summary>
/// Provides the runtime engine used to turn a single .NET event occurrence into a task.
/// </summary>
public static class EventAwaiter
{
    /// <summary>
    /// Waits until an <see cref="EventHandler"/> event satisfies the optional predicate.
    /// </summary>
    public static Task<EventArgs> WaitAsync(
        Action<EventHandler> subscribe,
        Action<EventHandler> unsubscribe,
        Predicate<EventArgs>? predicate = null,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(subscribe);
        ArgumentNullException.ThrowIfNull(unsubscribe);

        EventHandler? adaptedHandler = null;

        return WaitAsync<EventArgs>(
            handler =>
            {
                adaptedHandler = (sender, eventArgs) => handler(sender, eventArgs);
                subscribe(adaptedHandler);
            },
            _ => unsubscribe(adaptedHandler!),
            predicate,
            cancellationToken,
            timeout,
            timeProvider);
    }

    /// <summary>
    /// Waits until an <see cref="EventHandler{TEventArgs}"/> event satisfies the optional predicate.
    /// </summary>
    public static Task<TEventArgs> WaitAsync<TEventArgs>(
        Action<EventHandler<TEventArgs>> subscribe,
        Action<EventHandler<TEventArgs>> unsubscribe,
        Predicate<TEventArgs>? predicate = null,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(subscribe);
        ArgumentNullException.ThrowIfNull(unsubscribe);

        ValidateTimeout(timeout);

        if (cancellationToken.IsCancellationRequested)
        {
            AsyncEventBridgeMetrics.RecordWaitCancelled();
            return Task.FromCanceled<TEventArgs>(cancellationToken);
        }

        if (timeout == TimeSpan.Zero)
        {
            AsyncEventBridgeMetrics.RecordWaitTimeout();
            return Task.FromException<TEventArgs>(CreateTimeoutException(timeout.Value));
        }

        if (predicate is null &&
            !cancellationToken.CanBeCanceled &&
            (timeout is null || timeout == Timeout.InfiniteTimeSpan))
        {
            return new SimpleEventWaitState<TEventArgs>(subscribe, unsubscribe).Start();
        }

        var state = new EventWaitState<TEventArgs>(
            subscribe,
            unsubscribe,
            predicate,
            cancellationToken,
            timeout,
            timeProvider ?? TimeProvider.System);

        return state.Start();
    }

    private static void ValidateTimeout(TimeSpan? timeout)
    {
        if (timeout is { } value && value < TimeSpan.Zero && value != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be non-negative or Timeout.InfiniteTimeSpan.");
        }
    }

    internal static TimeoutException CreateTimeoutException(TimeSpan timeout) =>
        new($"The event wait timed out after {timeout}.");
}

internal sealed class SimpleEventWaitState<TEventArgs>
{
    private readonly Action<EventHandler<TEventArgs>> _subscribe;
    private readonly Action<EventHandler<TEventArgs>> _unsubscribe;
    private readonly TaskCompletionSource<TEventArgs> _completionSource;
    private readonly EventHandler<TEventArgs> _handler;

    private TEventArgs _result = default!;
    private Exception? _exception;
    private int _winnerClaimed;
    private int _completionKind;
    private int _initializationComplete;
    private int _cleanupStarted;

    internal SimpleEventWaitState(
        Action<EventHandler<TEventArgs>> subscribe,
        Action<EventHandler<TEventArgs>> unsubscribe)
    {
        _subscribe = subscribe;
        _unsubscribe = unsubscribe;
        _handler = OnEvent;
        _completionSource = new TaskCompletionSource<TEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    internal Task<TEventArgs> Start()
    {
        try
        {
            _subscribe(_handler);
        }
        catch (Exception exception)
        {
            TryFault(exception);
        }
        finally
        {
            Volatile.Write(ref _initializationComplete, 1);
            TryFinalize();
        }

        return _completionSource.Task;
    }

    private void OnEvent(object? sender, TEventArgs eventArgs)
    {
        if (!TryClaimWinner())
        {
            return;
        }

        _result = eventArgs;
        Volatile.Write(ref _completionKind, (int)CompletionKind.Succeeded);
        TryFinalize();
    }

    private void TryFault(Exception exception)
    {
        if (!TryClaimWinner())
        {
            return;
        }

        _exception = exception;
        Volatile.Write(ref _completionKind, (int)CompletionKind.Faulted);
        TryFinalize();
    }

    private bool TryClaimWinner() =>
        Interlocked.CompareExchange(ref _winnerClaimed, 1, 0) == 0;

    private void TryFinalize()
    {
        var completionKind = (CompletionKind)Volatile.Read(ref _completionKind);

        if (completionKind == CompletionKind.Pending || Volatile.Read(ref _initializationComplete) == 0)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _cleanupStarted, 1, 0) != 0)
        {
            return;
        }

        Exception? cleanupException = null;

        try
        {
            _unsubscribe(_handler);
        }
        catch (Exception exception)
        {
            cleanupException = exception;
        }

        if (cleanupException is not null)
        {
            AsyncEventBridgeMetrics.RecordWaitFaulted();

            if (completionKind == CompletionKind.Faulted && _exception is not null)
            {
                _completionSource.TrySetException([_exception, cleanupException]);
            }
            else
            {
                _completionSource.TrySetException(cleanupException);
            }

            return;
        }

        switch (completionKind)
        {
            case CompletionKind.Succeeded:
                AsyncEventBridgeMetrics.RecordWaitSuccess();
                _completionSource.TrySetResult(_result);
                break;
            case CompletionKind.Faulted:
                AsyncEventBridgeMetrics.RecordWaitFaulted();
                _completionSource.TrySetException(_exception!);
                break;
            default:
                AsyncEventBridgeMetrics.RecordWaitFaulted();
                _completionSource.TrySetException(new InvalidOperationException("Unknown simple event wait completion state."));
                break;
        }
    }

    private enum CompletionKind
    {
        Pending = 0,
        Succeeded = 1,
        Faulted = 2,
    }
}

internal sealed class EventWaitState<TEventArgs>
{
    private readonly Action<EventHandler<TEventArgs>> _subscribe;
    private readonly Action<EventHandler<TEventArgs>> _unsubscribe;
    private readonly Predicate<TEventArgs>? _predicate;
    private readonly CancellationToken _cancellationToken;
    private readonly TimeSpan? _timeout;
    private readonly TimeProvider _timeProvider;
    private readonly TaskCompletionSource<TEventArgs> _completionSource;
    private readonly EventHandler<TEventArgs> _handler;
    private readonly object? _predicateGate;

    private CancellationTokenRegistration _cancellationRegistration;
    private ITimer? _timeoutRegistration;
    private TEventArgs _result = default!;
    private Exception? _exception;
    private int _winnerClaimed;
    private int _completionKind;
    private int _subscriptionAttempted;
    private int _initializationComplete;
    private int _cleanupStarted;

    internal EventWaitState(
        Action<EventHandler<TEventArgs>> subscribe,
        Action<EventHandler<TEventArgs>> unsubscribe,
        Predicate<TEventArgs>? predicate,
        CancellationToken cancellationToken,
        TimeSpan? timeout,
        TimeProvider timeProvider)
    {
        _subscribe = subscribe;
        _unsubscribe = unsubscribe;
        _predicate = predicate;
        _cancellationToken = cancellationToken;
        _timeout = timeout;
        _timeProvider = timeProvider;
        _handler = OnEvent;
        _completionSource = new TaskCompletionSource<TEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        _predicateGate = predicate is null ? null : new object();
    }

    internal Task<TEventArgs> Start()
    {
        try
        {
            Volatile.Write(ref _subscriptionAttempted, 1);
            _subscribe(_handler);

            if (_cancellationToken.CanBeCanceled)
            {
                _cancellationRegistration = _cancellationToken.UnsafeRegister(
                    static (state, _) => ((EventWaitState<TEventArgs>)state!).TryCancel(),
                    this);
            }

            if (_timeout is { } timeout && timeout != Timeout.InfiniteTimeSpan)
            {
                _timeoutRegistration = _timeProvider.CreateTimer(
                    static state => ((EventWaitState<TEventArgs>)state!).TryTimeout(),
                    this,
                    timeout,
                    Timeout.InfiniteTimeSpan);
            }
        }
        catch (Exception exception)
        {
            TryFault(exception);
        }
        finally
        {
            Volatile.Write(ref _initializationComplete, 1);
            TryFinalize();
        }

        return _completionSource.Task;
    }

    private void OnEvent(object? sender, TEventArgs eventArgs)
    {
        if (_predicate is null)
        {
            if (!TryClaimWinner())
            {
                return;
            }

            _result = eventArgs;
            Volatile.Write(ref _completionKind, (int)CompletionKind.Succeeded);
            TryFinalize();
            return;
        }

        CompletionKind completionKind;
        Exception? exception = null;

        lock (_predicateGate!)
        {
            if (Volatile.Read(ref _winnerClaimed) != 0)
            {
                return;
            }

            try
            {
                if (!_predicate(eventArgs))
                {
                    return;
                }

                completionKind = CompletionKind.Succeeded;
            }
            catch (Exception caughtException)
            {
                completionKind = CompletionKind.Faulted;
                exception = caughtException;
            }

            if (!TryClaimWinner())
            {
                return;
            }

            _result = eventArgs;
            _exception = exception;
            Volatile.Write(ref _completionKind, (int)completionKind);
        }

        TryFinalize();
    }

    private void TryCancel()
    {
        if (!TryClaimWinner())
        {
            return;
        }

        Volatile.Write(ref _completionKind, (int)CompletionKind.Cancelled);
        TryFinalize();
    }

    private void TryTimeout()
    {
        if (!TryClaimWinner())
        {
            return;
        }

        _exception = EventAwaiter.CreateTimeoutException(_timeout!.Value);
        Volatile.Write(ref _completionKind, (int)CompletionKind.TimedOut);
        TryFinalize();
    }

    private void TryFault(Exception exception)
    {
        if (!TryClaimWinner())
        {
            return;
        }

        _exception = exception;
        Volatile.Write(ref _completionKind, (int)CompletionKind.Faulted);
        TryFinalize();
    }

    private bool TryClaimWinner() =>
        Interlocked.CompareExchange(ref _winnerClaimed, 1, 0) == 0;

    private void TryFinalize()
    {
        var completionKind = (CompletionKind)Volatile.Read(ref _completionKind);

        if (completionKind == CompletionKind.Pending || Volatile.Read(ref _initializationComplete) == 0)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _cleanupStarted, 1, 0) != 0)
        {
            return;
        }

        List<Exception>? cleanupErrors = null;

        if (Volatile.Read(ref _subscriptionAttempted) != 0)
        {
            try
            {
                _unsubscribe(_handler);
            }
            catch (Exception exception)
            {
                AddCleanupError(ref cleanupErrors, exception);
            }
        }

        try
        {
            _cancellationRegistration.Dispose();
        }
        catch (Exception exception)
        {
            AddCleanupError(ref cleanupErrors, exception);
        }

        if (_timeoutRegistration is not null)
        {
            try
            {
                _timeoutRegistration.Dispose();
            }
            catch (Exception exception)
            {
                AddCleanupError(ref cleanupErrors, exception);
            }
        }

        if (cleanupErrors is not null)
        {
            if (completionKind == CompletionKind.Faulted && _exception is not null)
            {
                cleanupErrors.Insert(0, _exception);
            }

            AsyncEventBridgeMetrics.RecordWaitFaulted();
            _completionSource.TrySetException(cleanupErrors);
            return;
        }

        switch (completionKind)
        {
            case CompletionKind.Succeeded:
                AsyncEventBridgeMetrics.RecordWaitSuccess();
                _completionSource.TrySetResult(_result);
                break;
            case CompletionKind.Cancelled:
                AsyncEventBridgeMetrics.RecordWaitCancelled();
                _completionSource.TrySetCanceled(_cancellationToken);
                break;
            case CompletionKind.TimedOut:
                AsyncEventBridgeMetrics.RecordWaitTimeout();
                _completionSource.TrySetException(_exception!);
                break;
            case CompletionKind.Faulted:
                AsyncEventBridgeMetrics.RecordWaitFaulted();
                _completionSource.TrySetException(_exception!);
                break;
            default:
                AsyncEventBridgeMetrics.RecordWaitFaulted();
                _completionSource.TrySetException(new InvalidOperationException("Unknown event wait completion state."));
                break;
        }
    }

    private static void AddCleanupError(ref List<Exception>? cleanupErrors, Exception exception)
    {
        cleanupErrors ??= [];
        cleanupErrors.Add(exception);
    }

    private enum CompletionKind
    {
        Pending = 0,
        Succeeded = 1,
        Cancelled = 2,
        TimedOut = 3,
        Faulted = 4,
    }
}
