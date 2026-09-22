using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace AsyncEventBridge
{

/// <summary>
/// Provides lifecycle-safe composition helpers for async event streams.
/// </summary>
public static partial class EventStreamComposition
{
    /// <summary>
    /// Returns values from <paramref name="source"/> until the asynchronous stop wait completes.
    /// </summary>
    /// <remarks>
    /// The stop wait is created once per enumeration and receives a coordination token shared with the source
    /// enumerator. When the stop wait wins, source enumeration is cancelled, observed, and disposed before the
    /// sequence completes. When the source completes or faults first, the stop wait is cancelled and observed.
    /// If a source move and the stop wait are both complete when the race is observed, the stop wait wins and
    /// that source value is not published.
    ///
    /// Cleanup is deterministic and can therefore wait for a source or stop wait that does not honor cancellation.
    /// Cleanup failures remain observable and are aggregated after any primary stop/source failure.
    /// </remarks>
    public static IAsyncEnumerable<T> TakeUntil<T>(
        this IAsyncEnumerable<T> source,
        Func<CancellationToken, Task> stopWait,
        CancellationToken cancellationToken = default)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (stopWait is null)
        {
            throw new ArgumentNullException(nameof(stopWait));
        }

        return new TakeUntilEnumerable<T>(source, stopWait, cancellationToken, null);
    }

    private sealed class TakeUntilEnumerable<T> : IAsyncEnumerable<T>
    {
        private readonly IAsyncEnumerable<T> _source;
        private readonly Func<CancellationToken, Task> _stopWait;
        private readonly CancellationToken _creationCancellationToken;
        private readonly Action<EventStreamTakeUntilCompletion>? _completionObserver;

        internal TakeUntilEnumerable(
            IAsyncEnumerable<T> source,
            Func<CancellationToken, Task> stopWait,
            CancellationToken creationCancellationToken,
            Action<EventStreamTakeUntilCompletion>? completionObserver)
        {
            _source = source;
            _stopWait = stopWait;
            _creationCancellationToken = creationCancellationToken;
            _completionObserver = completionObserver;
        }

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            new Enumerator(
                _source,
                _stopWait,
                _creationCancellationToken,
                cancellationToken,
                _completionObserver);

        private sealed class Enumerator : IAsyncEnumerator<T>
        {
            private readonly IAsyncEnumerable<T> _source;
            private readonly Func<CancellationToken, Task> _stopWait;
            private readonly CancellationToken _creationCancellationToken;
            private readonly CancellationToken _enumerationCancellationToken;
            private readonly Action<EventStreamTakeUntilCompletion>? _completionObserver;

            private CancellationTokenSource? _lifetimeCancellation;
            private IAsyncEnumerator<T>? _sourceEnumerator;
            private Task? _stopTask;
            private Task<bool>? _pendingMoveNextTask;
            private T _current = default!;
            private bool _started;
            private bool _terminal;
            private bool _disposed;

            internal Enumerator(
                IAsyncEnumerable<T> source,
                Func<CancellationToken, Task> stopWait,
                CancellationToken creationCancellationToken,
                CancellationToken enumerationCancellationToken,
                Action<EventStreamTakeUntilCompletion>? completionObserver)
            {
                _source = source;
                _stopWait = stopWait;
                _creationCancellationToken = creationCancellationToken;
                _enumerationCancellationToken = enumerationCancellationToken;
                _completionObserver = completionObserver;
            }

            public T Current => _current;

            public ValueTask<bool> MoveNextAsync()
            {
                if (_disposed || _terminal)
                {
                    return new ValueTask<bool>(false);
                }

                return new ValueTask<bool>(MoveNextCoreAsync());
            }

            public ValueTask DisposeAsync()
            {
                if (_disposed)
                {
                    return default;
                }

                _disposed = true;
                return new ValueTask(DisposeCoreAsync());
            }

            private async Task<bool> MoveNextCoreAsync()
            {
                if (!_started)
                {
                    await StartAsync().ConfigureAwait(false);
                }

                if (_terminal || _disposed)
                {
                    return false;
                }

                if (_stopTask!.IsCompleted)
                {
                    return await CompleteFromStopAsync().ConfigureAwait(false);
                }

                Task<bool> moveTask;

                try
                {
                    moveTask = _sourceEnumerator!.MoveNextAsync().AsTask();
                    _pendingMoveNextTask = moveTask;
                }
                catch (Exception exception)
                {
                    await CompleteFromSourceAsync(exception).ConfigureAwait(false);
                    return false;
                }

                var completed = await Task.WhenAny(moveTask, _stopTask).ConfigureAwait(false);

                if (ReferenceEquals(completed, _stopTask) || _stopTask.IsCompleted)
                {
                    return await CompleteFromStopAsync().ConfigureAwait(false);
                }

                bool moved;

                try
                {
                    moved = await moveTask.ConfigureAwait(false);
                    _pendingMoveNextTask = null;
                }
                catch (Exception exception)
                {
                    _pendingMoveNextTask = null;
                    await CompleteFromSourceAsync(exception).ConfigureAwait(false);
                    return false;
                }

                if (_stopTask.IsCompleted)
                {
                    return await CompleteFromStopAsync().ConfigureAwait(false);
                }

                if (!moved)
                {
                    await CompleteFromSourceAsync(null).ConfigureAwait(false);
                    return false;
                }

                _current = _sourceEnumerator.Current;
                return true;
            }

            private async Task StartAsync()
            {
                _lifetimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    _creationCancellationToken,
                    _enumerationCancellationToken);

                var lifetimeToken = _lifetimeCancellation.Token;
                Exception? primaryException = null;

                try
                {
                    lifetimeToken.ThrowIfCancellationRequested();

                    _stopTask = _stopWait(lifetimeToken);
                    if (_stopTask is null)
                    {
                        throw new InvalidOperationException("The event-stream stop wait factory returned null.");
                    }

                    if (!_stopTask.IsCompleted)
                    {
                        _sourceEnumerator = _source.GetAsyncEnumerator(lifetimeToken);
                    }

                    _started = true;
                    return;
                }
                catch (Exception exception)
                {
                    primaryException = exception;
                }

                var cleanupErrors = await CleanupAsync(observeStopTask: true).ConfigureAwait(false);
                _terminal = true;
                ThrowPrimaryWithCleanup(primaryException, cleanupErrors);
            }

            private async Task<bool> CompleteFromStopAsync()
            {
                Exception? primaryException = null;

                try
                {
                    await _stopTask!.ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    primaryException = exception;
                }

                if (primaryException is null && _completionObserver is not null)
                {
                    _completionObserver(EventStreamTakeUntilCompletion.Stop);
                }

                var cleanupErrors = await CleanupAsync(observeStopTask: false).ConfigureAwait(false);
                _terminal = true;
                ThrowPrimaryWithCleanup(primaryException, cleanupErrors);
                return false;
            }

            private async Task CompleteFromSourceAsync(Exception? primaryException)
            {
                if (primaryException is null && _completionObserver is not null)
                {
                    _completionObserver(EventStreamTakeUntilCompletion.SourceCompleted);
                }

                var cleanupErrors = await CleanupAsync(observeStopTask: true).ConfigureAwait(false);
                _terminal = true;
                ThrowPrimaryWithCleanup(primaryException, cleanupErrors);
            }

            private async Task DisposeCoreAsync()
            {
                if (_terminal)
                {
                    return;
                }

                var cleanupErrors = await CleanupAsync(observeStopTask: true).ConfigureAwait(false);
                _terminal = true;
                ThrowPrimaryWithCleanup(null, cleanupErrors);
            }

            private async Task<List<Exception>?> CleanupAsync(bool observeStopTask)
            {
                List<Exception>? cleanupErrors = null;

                if (_lifetimeCancellation is not null)
                {
                    try
                    {
                        _lifetimeCancellation.Cancel();
                    }
                    catch (Exception exception)
                    {
                        AddCleanupError(ref cleanupErrors, exception);
                    }
                }

                if (_pendingMoveNextTask is not null)
                {
                    var exception = await ObserveCleanupTaskAsync(_pendingMoveNextTask).ConfigureAwait(false);
                    if (exception is not null)
                    {
                        AddCleanupError(ref cleanupErrors, exception);
                    }

                    _pendingMoveNextTask = null;
                }

                if (observeStopTask && _stopTask is not null)
                {
                    var exception = await ObserveCleanupTaskAsync(_stopTask).ConfigureAwait(false);
                    if (exception is not null)
                    {
                        AddCleanupError(ref cleanupErrors, exception);
                    }
                }

                if (_sourceEnumerator is not null)
                {
                    try
                    {
                        await _sourceEnumerator.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        AddCleanupError(ref cleanupErrors, exception);
                    }

                    _sourceEnumerator = null;
                }

                if (_lifetimeCancellation is not null)
                {
                    _lifetimeCancellation.Dispose();
                    _lifetimeCancellation = null;
                }

                return cleanupErrors;
            }

            private static async Task<Exception?> ObserveCleanupTaskAsync(Task task)
            {
                try
                {
                    await task.ConfigureAwait(false);
                    return null;
                }
                catch (OperationCanceledException) when (task.IsCanceled)
                {
                    return null;
                }
                catch (Exception exception)
                {
                    return exception;
                }
            }

            private static void AddCleanupError(
                ref List<Exception>? cleanupErrors,
                Exception exception)
            {
                if (cleanupErrors is null)
                {
                    cleanupErrors = new List<Exception>();
                }

                cleanupErrors.Add(exception);
            }

            private static void ThrowPrimaryWithCleanup(
                Exception? primaryException,
                IReadOnlyList<Exception>? cleanupErrors)
            {
                Exception? exception = primaryException;

                if (cleanupErrors is not null && cleanupErrors.Count > 0)
                {
                    exception = CleanupExceptionPolicy.Combine(primaryException, cleanupErrors);
                }

                if (exception is not null)
                {
                    ExceptionDispatchInfo.Capture(exception).Throw();
                }
            }
        }
    }
}

}
