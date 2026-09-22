using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace AsyncEventBridge
{

public static partial class EventStreamComposition
{
    /// <summary>
    /// Delays source enumeration until the asynchronous start wait completes successfully.
    /// </summary>
    /// <remarks>
    /// The start wait is created once per enumeration and receives a cancellation token linked to both the
    /// creation-time and enumeration-time tokens. The source enumerator is not created until the start wait
    /// completes successfully, so event-backed streams do not subscribe before activation.
    ///
    /// A faulted or cancelled start wait propagates its outcome and the source is never started. Once activated,
    /// source completion, faults, cancellation, early consumer disposal, and enumerator cleanup follow the same
    /// primary-outcome-first cleanup policy used by the rest of AsyncEventBridge.
    /// </remarks>
    public static IAsyncEnumerable<T> StartAfter<T>(
        this IAsyncEnumerable<T> source,
        Func<CancellationToken, Task> startWait,
        CancellationToken cancellationToken = default)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (startWait is null)
        {
            throw new ArgumentNullException(nameof(startWait));
        }

        return new StartAfterEnumerable<T>(source, startWait, cancellationToken);
    }

    private sealed class StartAfterEnumerable<T> : IAsyncEnumerable<T>
    {
        private readonly IAsyncEnumerable<T> _source;
        private readonly Func<CancellationToken, Task> _startWait;
        private readonly CancellationToken _creationCancellationToken;

        internal StartAfterEnumerable(
            IAsyncEnumerable<T> source,
            Func<CancellationToken, Task> startWait,
            CancellationToken creationCancellationToken)
        {
            _source = source;
            _startWait = startWait;
            _creationCancellationToken = creationCancellationToken;
        }

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            new Enumerator(_source, _startWait, _creationCancellationToken, cancellationToken);

        private sealed class Enumerator : IAsyncEnumerator<T>
        {
            private readonly IAsyncEnumerable<T> _source;
            private readonly Func<CancellationToken, Task> _startWait;
            private readonly CancellationToken _creationCancellationToken;
            private readonly CancellationToken _enumerationCancellationToken;

            private CancellationTokenSource? _lifetimeCancellation;
            private IAsyncEnumerator<T>? _sourceEnumerator;
            private T _current = default!;
            private bool _started;
            private bool _terminal;
            private bool _disposed;

            internal Enumerator(
                IAsyncEnumerable<T> source,
                Func<CancellationToken, Task> startWait,
                CancellationToken creationCancellationToken,
                CancellationToken enumerationCancellationToken)
            {
                _source = source;
                _startWait = startWait;
                _creationCancellationToken = creationCancellationToken;
                _enumerationCancellationToken = enumerationCancellationToken;
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

                if (_lifetimeCancellation is null && _sourceEnumerator is null)
                {
                    return default;
                }

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

                ValueTask<bool> moveNext;

                try
                {
                    moveNext = _sourceEnumerator!.MoveNextAsync();
                }
                catch (Exception exception)
                {
                    await CompleteFromSourceAsync(exception).ConfigureAwait(false);
                    return false;
                }

                bool moved;

                try
                {
                    moved = await moveNext.ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    await CompleteFromSourceAsync(exception).ConfigureAwait(false);
                    return false;
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

                    var startTask = _startWait(lifetimeToken);
                    if (startTask is null)
                    {
                        throw new InvalidOperationException("The event-stream start wait factory returned null.");
                    }

                    await startTask.ConfigureAwait(false);

                    lifetimeToken.ThrowIfCancellationRequested();
                    _sourceEnumerator = _source.GetAsyncEnumerator(lifetimeToken);
                    _started = true;
                    return;
                }
                catch (Exception exception)
                {
                    primaryException = exception;
                }

                var cleanupErrors = await CleanupAsync().ConfigureAwait(false);
                _terminal = true;
                ThrowPrimaryWithCleanup(primaryException, cleanupErrors);
            }

            private async Task CompleteFromSourceAsync(Exception? primaryException)
            {
                var cleanupErrors = await CleanupAsync().ConfigureAwait(false);
                _terminal = true;
                ThrowPrimaryWithCleanup(primaryException, cleanupErrors);
            }

            private async Task DisposeCoreAsync()
            {
                if (_terminal)
                {
                    return;
                }

                var cleanupErrors = await CleanupAsync().ConfigureAwait(false);
                _terminal = true;
                ThrowPrimaryWithCleanup(null, cleanupErrors);
            }

            private async Task<List<Exception>?> CleanupAsync()
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

            private static void AddCleanupError(ref List<Exception>? cleanupErrors, Exception exception)
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
