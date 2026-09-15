using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace AsyncEventBridge.Unity
{

/// <summary>
/// Unity-native event waiting helpers that complete through <see cref="Awaitable{T}"/>
/// and marshal cleanup/completion back to the Unity synchronization context.
/// </summary>
public static class UnityEventAwaiter
{
    /// <summary>
    /// Waits for a non-generic .NET event from the Unity main thread.
    /// </summary>
    public static Awaitable<EventArgs> WaitAsync(
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
    /// Waits for a generic .NET event from the Unity main thread.
    /// </summary>
    public static Awaitable<TEventArgs> WaitAsync<TEventArgs>(
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

        var synchronizationContext = SynchronizationContext.Current;
        if (synchronizationContext is null)
        {
            throw new InvalidOperationException(
                "UnityEventAwaiter.WaitAsync must be started from the Unity main thread so cleanup and completion can be marshalled safely.");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return FromCanceled<TEventArgs>();
        }

        if (timeout == TimeSpan.Zero)
        {
            return FromException<TEventArgs>(CreateTimeoutException(timeout.Value));
        }

        var state = new UnityEventWaitState<TEventArgs>(
            subscribe,
            unsubscribe,
            predicate,
            cancellationToken,
            timeout,
            synchronizationContext,
            Thread.CurrentThread.ManagedThreadId);

        return state.Start();
    }

    /// <summary>
    /// Waits for a generic .NET event and automatically cancels when the owning behaviour is destroyed,
    /// play mode exits, the application quits, or the caller token is cancelled.
    /// </summary>
    public static async Awaitable<TEventArgs> WaitAsync<TEventArgs>(
        MonoBehaviour owner,
        Action<EventHandler<TEventArgs>> subscribe,
        Action<EventHandler<TEventArgs>> unsubscribe,
        Predicate<TEventArgs>? predicate = null,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
        where TEventArgs : EventArgs
    {
        if (owner == null)
        {
            throw new ArgumentNullException(nameof(owner));
        }

        var destroyToken = owner.destroyCancellationToken;
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            destroyToken,
            Application.exitCancellationToken,
            cancellationToken);

        return await WaitAsync(
            subscribe,
            unsubscribe,
            predicate,
            linkedCancellation.Token,
            timeout);
    }

    /// <summary>
    /// Waits for a non-generic .NET event and automatically cancels when the owning behaviour is destroyed,
    /// play mode exits, the application quits, or the caller token is cancelled.
    /// </summary>
    public static async Awaitable<EventArgs> WaitAsync(
        MonoBehaviour owner,
        Action<EventHandler> subscribe,
        Action<EventHandler> unsubscribe,
        Predicate<EventArgs>? predicate = null,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        if (owner == null)
        {
            throw new ArgumentNullException(nameof(owner));
        }

        var destroyToken = owner.destroyCancellationToken;
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            destroyToken,
            Application.exitCancellationToken,
            cancellationToken);

        return await WaitAsync(
            subscribe,
            unsubscribe,
            predicate,
            linkedCancellation.Token,
            timeout);
    }

    private static void ValidateTimeout(TimeSpan? timeout)
    {
        if (timeout is { } value && value < TimeSpan.Zero && value != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                "Timeout must be non-negative or Timeout.InfiniteTimeSpan.");
        }
    }

    private static TimeoutException CreateTimeoutException(TimeSpan timeout) =>
        new($"The event wait timed out after {timeout}.");

    private static Awaitable<T> FromCanceled<T>()
    {
        var completionSource = new AwaitableCompletionSource<T>();
        completionSource.SetCanceled();
        return completionSource.Awaitable;
    }

    private static Awaitable<T> FromException<T>(Exception exception)
    {
        var completionSource = new AwaitableCompletionSource<T>();
        completionSource.SetException(exception);
        return completionSource.Awaitable;
    }

    private sealed class UnityEventWaitState<TEventArgs>
        where TEventArgs : EventArgs
    {
        private readonly Action<EventHandler<TEventArgs>> _subscribe;
        private readonly Action<EventHandler<TEventArgs>> _unsubscribe;
        private readonly Predicate<TEventArgs>? _predicate;
        private readonly CancellationToken _cancellationToken;
        private readonly TimeSpan? _timeout;
        private readonly SynchronizationContext _synchronizationContext;
        private readonly int _mainThreadId;
        private readonly AwaitableCompletionSource<TEventArgs> _completionSource = new();
        private readonly EventHandler<TEventArgs> _handler;
        private readonly object _predicateGate = new();

        private CancellationTokenRegistration _cancellationRegistration;
        private Timer? _timeoutTimer;
        private Completion? _completion;
        private int _subscriptionAttempted;
        private int _initializationComplete;
        private int _cleanupStarted;

        internal UnityEventWaitState(
            Action<EventHandler<TEventArgs>> subscribe,
            Action<EventHandler<TEventArgs>> unsubscribe,
            Predicate<TEventArgs>? predicate,
            CancellationToken cancellationToken,
            TimeSpan? timeout,
            SynchronizationContext synchronizationContext,
            int mainThreadId)
        {
            _subscribe = subscribe;
            _unsubscribe = unsubscribe;
            _predicate = predicate;
            _cancellationToken = cancellationToken;
            _timeout = timeout;
            _synchronizationContext = synchronizationContext;
            _mainThreadId = mainThreadId;
            _handler = OnEvent;
        }

        internal Awaitable<TEventArgs> Start()
        {
            try
            {
                Volatile.Write(ref _subscriptionAttempted, 1);
                _subscribe(_handler);

                if (_cancellationToken.CanBeCanceled)
                {
                    _cancellationRegistration = _cancellationToken.Register(
                        static state => ((UnityEventWaitState<TEventArgs>)state!).TryCancel(),
                        this);
                }

                if (_timeout is { } timeout && timeout != Timeout.InfiniteTimeSpan)
                {
                    _timeoutTimer = new Timer(
                        static state => ((UnityEventWaitState<TEventArgs>)state!).TryTimeout(),
                        this,
                        timeout,
                        Timeout.InfiniteTimeSpan);
                }
            }
            catch (Exception exception)
            {
                TryWin(Completion.Faulted(exception));
            }
            finally
            {
                Volatile.Write(ref _initializationComplete, 1);
                RequestFinalize();
            }

            return _completionSource.Awaitable;
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

            RequestFinalize();
        }

        private void TryCancel() => TryWin(Completion.Cancelled());

        private void TryTimeout() =>
            TryWin(Completion.TimedOut(CreateTimeoutException(_timeout!.Value)));

        private void TryWin(Completion completion)
        {
            if (Interlocked.CompareExchange(ref _completion, completion, null) is null)
            {
                RequestFinalize();
            }
        }

        private void RequestFinalize()
        {
            if (Volatile.Read(ref _completion) is null || Volatile.Read(ref _initializationComplete) == 0)
            {
                return;
            }

            if (Thread.CurrentThread.ManagedThreadId == _mainThreadId)
            {
                FinalizeOnMainThread();
                return;
            }

            _synchronizationContext.Post(
                static state => ((UnityEventWaitState<TEventArgs>)state!).FinalizeOnMainThread(),
                this);
        }

        private void FinalizeOnMainThread()
        {
            if (Interlocked.CompareExchange(ref _cleanupStarted, 1, 0) != 0)
            {
                return;
            }

            var completion = Volatile.Read(ref _completion)!;
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

            if (_timeoutTimer is not null)
            {
                try
                {
                    _timeoutTimer.Dispose();
                }
                catch (Exception exception)
                {
                    AddCleanupError(ref cleanupErrors, exception);
                }
            }

            if (cleanupErrors is not null)
            {
                if ((completion.Kind is CompletionKind.Faulted or CompletionKind.TimedOut) &&
                    completion.Exception is not null)
                {
                    cleanupErrors.Insert(0, completion.Exception);
                }

                _completionSource.TrySetException(new AggregateException(cleanupErrors));
                return;
            }

            switch (completion.Kind)
            {
                case CompletionKind.Succeeded:
                    var result = completion.Result!;
                    _completionSource.TrySetResult(ref result);
                    break;
                case CompletionKind.Cancelled:
                    _completionSource.TrySetCanceled();
                    break;
                case CompletionKind.TimedOut:
                case CompletionKind.Faulted:
                    _completionSource.TrySetException(completion.Exception!);
                    break;
                default:
                    _completionSource.TrySetException(
                        new InvalidOperationException("Unknown Unity event wait completion state."));
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
}
}
