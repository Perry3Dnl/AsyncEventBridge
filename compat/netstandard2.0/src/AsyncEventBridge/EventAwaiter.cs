using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace AsyncEventBridge
{

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
        TimeSpan? timeout = null)
    {
        if (subscribe is null)
        {
            throw new ArgumentNullException(nameof(subscribe));
        }

        if (unsubscribe is null)
        {
            throw new ArgumentNullException(nameof(unsubscribe));
        }

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
            timeout);
    }

    /// <summary>
    /// Waits until an <see cref="EventHandler{TEventArgs}"/> event satisfies the optional predicate.
    /// </summary>
    public static Task<TEventArgs> WaitAsync<TEventArgs>(
        Action<EventHandler<TEventArgs>> subscribe,
        Action<EventHandler<TEventArgs>> unsubscribe,
        Predicate<TEventArgs>? predicate = null,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
        where TEventArgs : EventArgs
    {
        if (subscribe is null)
        {
            throw new ArgumentNullException(nameof(subscribe));
        }

        if (unsubscribe is null)
        {
            throw new ArgumentNullException(nameof(unsubscribe));
        }

        ValidateTimeout(timeout);

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<TEventArgs>(cancellationToken);
        }

        if (timeout == TimeSpan.Zero)
        {
            return Task.FromException<TEventArgs>(CreateTimeoutException(timeout.Value));
        }

        var state = new EventWaitState<TEventArgs>(
            subscribe,
            unsubscribe,
            predicate,
            cancellationToken,
            timeout,
            SystemTimeoutScheduler.Instance);

        return state.Start();
    }

    internal static Task<TEventArgs> WaitAsync<TEventArgs>(
        Action<EventHandler<TEventArgs>> subscribe,
        Action<EventHandler<TEventArgs>> unsubscribe,
        Predicate<TEventArgs>? predicate,
        CancellationToken cancellationToken,
        TimeSpan? timeout,
        ITimeoutScheduler timeoutScheduler)
        where TEventArgs : EventArgs
    {
        if (subscribe is null)
        {
            throw new ArgumentNullException(nameof(subscribe));
        }

        if (unsubscribe is null)
        {
            throw new ArgumentNullException(nameof(unsubscribe));
        }

        if (timeoutScheduler is null)
        {
            throw new ArgumentNullException(nameof(timeoutScheduler));
        }

        ValidateTimeout(timeout);

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<TEventArgs>(cancellationToken);
        }

        if (timeout == TimeSpan.Zero)
        {
            return Task.FromException<TEventArgs>(CreateTimeoutException(timeout.Value));
        }

        var state = new EventWaitState<TEventArgs>(
            subscribe,
            unsubscribe,
            predicate,
            cancellationToken,
            timeout,
            timeoutScheduler);

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

internal interface ITimeoutScheduler
{
    IDisposable Schedule(TimeSpan timeout, Action callback);
}

internal sealed class SystemTimeoutScheduler : ITimeoutScheduler
{
    internal static SystemTimeoutScheduler Instance { get; } = new();

    private SystemTimeoutScheduler()
    {
    }

    public IDisposable Schedule(TimeSpan timeout, Action callback)
    {
        if (callback is null)
        {
            throw new ArgumentNullException(nameof(callback));
        }

        return new Timer(
            static state => ((Action)state!).Invoke(),
            callback,
            timeout,
            Timeout.InfiniteTimeSpan);
    }
}

internal sealed class EventWaitState<TEventArgs>
    where TEventArgs : EventArgs
{
    private readonly Action<EventHandler<TEventArgs>> _subscribe;
    private readonly Action<EventHandler<TEventArgs>> _unsubscribe;
    private readonly Predicate<TEventArgs>? _predicate;
    private readonly CancellationToken _cancellationToken;
    private readonly TimeSpan? _timeout;
    private readonly ITimeoutScheduler _timeoutScheduler;
    private readonly TaskCompletionSource<TEventArgs> _completionSource;
    private readonly EventHandler<TEventArgs> _handler;
    private readonly object _predicateGate = new();

    private CancellationTokenRegistration _cancellationRegistration;
    private IDisposable? _timeoutRegistration;
    private Completion? _completion;
    private int _subscriptionAttempted;
    private int _initializationComplete;
    private int _cleanupStarted;

    internal EventWaitState(
        Action<EventHandler<TEventArgs>> subscribe,
        Action<EventHandler<TEventArgs>> unsubscribe,
        Predicate<TEventArgs>? predicate,
        CancellationToken cancellationToken,
        TimeSpan? timeout,
        ITimeoutScheduler timeoutScheduler)
    {
        _subscribe = subscribe;
        _unsubscribe = unsubscribe;
        _predicate = predicate;
        _cancellationToken = cancellationToken;
        _timeout = timeout;
        _timeoutScheduler = timeoutScheduler;
        _handler = OnEvent;
        _completionSource = new TaskCompletionSource<TEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    internal Task<TEventArgs> Start()
    {
        try
        {
            Volatile.Write(ref _subscriptionAttempted, 1);
            _subscribe(_handler);

            if (_cancellationToken.CanBeCanceled)
            {
                _cancellationRegistration = _cancellationToken.Register(
                    static state => ((EventWaitState<TEventArgs>)state!).TryCancel(),
                    this);
            }

            if (_timeout is { } timeout && timeout != Timeout.InfiniteTimeSpan)
            {
                _timeoutRegistration = _timeoutScheduler.Schedule(timeout, TryTimeout);
            }
        }
        catch (Exception exception)
        {
            TryWin(Completion.Faulted(exception));
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
        Completion completion;

        lock (_predicateGate)
        {
            if (Volatile.Read(ref _completion) is not null)
            {
                return;
            }

            try
            {
                if (_predicate is not null && !_predicate(eventArgs))
                {
                    return;
                }

                completion = Completion.Succeeded(eventArgs);
            }
            catch (Exception exception)
            {
                completion = Completion.Faulted(exception);
            }

            if (Interlocked.CompareExchange(ref _completion, completion, null) is not null)
            {
                return;
            }
        }

        TryFinalize();
    }

    private void TryCancel() => TryWin(Completion.Cancelled());

    private void TryTimeout() =>
        TryWin(Completion.TimedOut(EventAwaiter.CreateTimeoutException(_timeout!.Value)));

    private void TryWin(Completion completion)
    {
        if (Interlocked.CompareExchange(ref _completion, completion, null) is null)
        {
            TryFinalize();
        }
    }

    private void TryFinalize()
    {
        var completion = Volatile.Read(ref _completion);

        if (completion is null || Volatile.Read(ref _initializationComplete) == 0)
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
            Exception? primaryException;

            switch (completion.Kind)
            {
                case CompletionKind.Cancelled:
                    primaryException = new OperationCanceledException(_cancellationToken);
                    break;
                case CompletionKind.TimedOut:
                case CompletionKind.Faulted:
                    primaryException = completion.Exception;
                    break;
                default:
                    primaryException = null;
                    break;
            }

            _completionSource.TrySetException(CleanupExceptionPolicy.Combine(primaryException, cleanupErrors));
            return;
        }

        switch (completion.Kind)
        {
            case CompletionKind.Succeeded:
                _completionSource.TrySetResult(completion.Result!);
                break;
            case CompletionKind.Cancelled:
                _completionSource.TrySetCanceled(_cancellationToken);
                break;
            case CompletionKind.TimedOut:
            case CompletionKind.Faulted:
                _completionSource.TrySetException(completion.Exception!);
                break;
            default:
                _completionSource.TrySetException(new InvalidOperationException("Unknown event wait completion state."));
                break;
        }
    }

    private static void AddCleanupError(ref List<Exception>? cleanupErrors, Exception exception)
    {
        cleanupErrors ??= new List<Exception>();
        cleanupErrors.Add(exception);
    }

    private enum CompletionKind
    {
        Succeeded,
        Cancelled,
        TimedOut,
        Faulted,
    }

    private sealed class Completion
    {
        private Completion(CompletionKind kind, TEventArgs? result, Exception? exception)
        {
            Kind = kind;
            Result = result;
            Exception = exception;
        }

        internal CompletionKind Kind { get; }

        internal TEventArgs? Result { get; }

        internal Exception? Exception { get; }

        internal static Completion Succeeded(TEventArgs result) =>
            new(CompletionKind.Succeeded, result, null);

        internal static Completion Cancelled() =>
            new(CompletionKind.Cancelled, null, null);

        internal static Completion TimedOut(Exception exception) =>
            new(CompletionKind.TimedOut, null, exception);

        internal static Completion Faulted(Exception exception) =>
            new(CompletionKind.Faulted, null, exception);
    }
}
internal static class CleanupExceptionPolicy
{
    internal static Exception Combine(Exception? primaryException, Exception cleanupException)
    {
        if (primaryException is null)
        {
            return cleanupException;
        }

        return new AggregateException(
            "The operation failed and cleanup also failed.",
            new[] { primaryException, cleanupException });
    }

    internal static Exception Combine(
        Exception? primaryException,
        IReadOnlyList<Exception> cleanupExceptions)
    {
        if (cleanupExceptions.Count == 1)
        {
            return Combine(primaryException, cleanupExceptions[0]);
        }

        var exceptions = new List<Exception>(
            cleanupExceptions.Count + (primaryException is null ? 0 : 1));

        if (primaryException is not null)
        {
            exceptions.Add(primaryException);
        }

        for (var index = 0; index < cleanupExceptions.Count; index++)
        {
            exceptions.Add(cleanupExceptions[index]);
        }

        return new AggregateException("One or more cleanup operations failed.", exceptions);
    }
}


}
