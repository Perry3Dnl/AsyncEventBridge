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
    /// Event-facing bridge for an <see cref="IAsyncEnumerable{T}"/>.
    /// </summary>
    /// <remarks>
    /// While connected and not owner-disposed, the bridge publishes values sequentially and publishes at most one
    /// terminal outcome. If cancellation races with natural completion or a fault, no outcome is given artificial
    /// priority; the outcome that reaches terminal publication first wins.
    /// </remarks>
    /// <typeparam name="T">The stream value type.</typeparam>
    public sealed class EventStreamBridge<T> : IDisposable, IAsyncDisposable
    {
        private readonly IAsyncEnumerable<T> _source;
        private readonly object _gate = new();

        private EventHandler<AsyncValueEventArgs<T>>? _value;
        private EventHandler? _completed;
        private EventHandler<AsyncFaultedEventArgs>? _faulted;
        private EventHandler? _cancelled;
        private CancellationTokenSource? _lifetimeCts;
        private TaskCompletionSource<bool>? _completion;
        private bool _connected;
        private bool _terminalPublished;
        private bool _disposed;
        private bool _asyncDisposeRequested;

        internal EventStreamBridge(IAsyncEnumerable<T> source)
        {
            _source = source;
        }

        /// <summary>
        /// Raised for each value produced by the async sequence.
        /// </summary>
        public event EventHandler<AsyncValueEventArgs<T>>? Value
        {
            add => AddHandler(ref _value, value);
            remove => RemoveHandler(ref _value, value);
        }

        /// <summary>
        /// Raised when the async sequence completes successfully.
        /// </summary>
        public event EventHandler? Completed
        {
            add => AddHandler(ref _completed, value);
            remove => RemoveHandler(ref _completed, value);
        }

        /// <summary>
        /// Raised when enumeration faults.
        /// </summary>
        public event EventHandler<AsyncFaultedEventArgs>? Faulted
        {
            add => AddHandler(ref _faulted, value);
            remove => RemoveHandler(ref _faulted, value);
        }

        /// <summary>
        /// Raised when enumeration is cancelled.
        /// </summary>
        public event EventHandler? Cancelled
        {
            add => AddHandler(ref _cancelled, value);
            remove => RemoveHandler(ref _cancelled, value);
        }

        /// <summary>
        /// Connects the async sequence to the event-facing bridge and begins publishing values.
        /// </summary>
        /// <param name="cancellationToken">Optional token used to stop stream consumption.</param>
        /// <remarks>A bridge can only be connected once.</remarks>
        public void Connect(CancellationToken cancellationToken = default)
        {
            CancellationTokenSource lifetimeCts;
            TaskCompletionSource<bool> completion;

            lock (_gate)
            {
                ThrowIfDisposed();

                if (_connected)
                {
                    throw new InvalidOperationException("The event bridge has already been connected.");
                }

                _connected = true;
                lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _lifetimeCts = lifetimeCts;
                _completion = completion;
            }

            _ = EnumerateAsync(lifetimeCts, completion);
        }

        /// <summary>
        /// Stops stream consumption and suppresses future event publication without waiting for asynchronous cleanup.
        /// </summary>
        /// <remarks>
        /// An event publication that was already in progress is allowed to finish after this method returns.
        /// Use <see cref="DisposeAsync"/> when the caller needs a completion boundary after which no further bridge
        /// event handler can still be running.
        /// </remarks>
        public void Dispose()
        {
            CancellationTokenSource? lifetimeCts;

            lock (_gate)
            {
                if (!_disposed)
                {
                    _disposed = true;
                    ClearHandlers();
                }

                lifetimeCts = _lifetimeCts;
            }

            CancelSafely(lifetimeCts);
        }

        /// <summary>
        /// Stops stream consumption, suppresses future event publication, and waits for in-flight publication and
        /// asynchronous enumerator cleanup to finish.
        /// </summary>
        /// <remarks>
        /// After the returned operation completes, the bridge will not invoke any further event handlers.
        /// Owner disposal does not publish <see cref="Cancelled"/>.
        /// </remarks>
        public async ValueTask DisposeAsync()
        {
            CancellationTokenSource? lifetimeCts;
            Task? completionTask;

            lock (_gate)
            {
                if (!_disposed)
                {
                    _disposed = true;
                    ClearHandlers();
                }

                _asyncDisposeRequested = true;
                lifetimeCts = _lifetimeCts;
                completionTask = _completion?.Task;
            }

            CancelSafely(lifetimeCts);

            if (completionTask is not null)
            {
                await completionTask.ConfigureAwait(false);
            }
        }

        private async Task EnumerateAsync(
            CancellationTokenSource lifetimeCts,
            TaskCompletionSource<bool> completion)
        {
            Exception? primaryException = null;
            Exception? cleanupException = null;

            try
            {
                var cancellationToken = lifetimeCts.Token;
                IAsyncEnumerator<T>? enumerator = null;

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    enumerator = _source.GetAsyncEnumerator(cancellationToken);

                    while (await enumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        PublishValue(enumerator.Current);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                }
                catch (Exception exception)
                {
                    primaryException = exception;
                }

                if (enumerator is not null)
                {
                    try
                    {
                        await enumerator.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        cleanupException = exception;
                    }
                }

                var terminalException = cleanupException is null
                    ? primaryException
                    : CleanupExceptionPolicy.Combine(primaryException, cleanupException);

                if (terminalException is null)
                {
                    PublishCompleted();
                }
                else if (cleanupException is null &&
                         terminalException is OperationCanceledException &&
                         lifetimeCts.IsCancellationRequested)
                {
                    PublishCancelled();
                }
                else
                {
                    PublishFaulted(terminalException);
                }
            }
            finally
            {
                lifetimeCts.Dispose();

                bool asyncDisposeRequested;

                lock (_gate)
                {
                    asyncDisposeRequested = _asyncDisposeRequested;

                    if (ReferenceEquals(_lifetimeCts, lifetimeCts))
                    {
                        _lifetimeCts = null;
                    }

                    if (ReferenceEquals(_completion, completion))
                    {
                        _completion = null;
                    }
                }

                if (cleanupException is not null && asyncDisposeRequested)
                {
                    completion.TrySetException(CleanupExceptionPolicy.Combine(primaryException, cleanupException));
                }
                else
                {
                    if (cleanupException is not null && _disposed)
                    {
                        Trace.TraceError(
                            "AsyncEventBridge stream bridge cleanup failed after synchronous disposal: {0}",
                            cleanupException);
                    }

                    completion.TrySetResult(true);
                }
            }
        }

        private void PublishValue(T value)
        {
            EventHandler<AsyncValueEventArgs<T>>? handlers;

            lock (_gate)
            {
                if (_disposed || _terminalPublished)
                {
                    return;
                }

                handlers = _value;
            }

            EventHandlerDispatcher.Invoke(handlers, this, new AsyncValueEventArgs<T>(value));
        }

        private void PublishCompleted()
        {
            EventHandler? handlers;

            lock (_gate)
            {
                if (!TryBeginTerminalPublication())
                {
                    return;
                }

                handlers = _completed;
                ClearHandlers();
            }

            EventHandlerDispatcher.Invoke(handlers, this);
        }

        private void PublishFaulted(Exception exception)
        {
            EventHandler<AsyncFaultedEventArgs>? handlers;

            lock (_gate)
            {
                if (!TryBeginTerminalPublication())
                {
                    return;
                }

                handlers = _faulted;
                ClearHandlers();
            }

            EventHandlerDispatcher.Invoke(handlers, this, new AsyncFaultedEventArgs(exception));
        }

        private void PublishCancelled()
        {
            EventHandler? handlers;

            lock (_gate)
            {
                if (!TryBeginTerminalPublication())
                {
                    return;
                }

                handlers = _cancelled;
                ClearHandlers();
            }

            EventHandlerDispatcher.Invoke(handlers, this);
        }

        private bool TryBeginTerminalPublication()
        {
            if (_disposed || _terminalPublished)
            {
                return false;
            }

            _terminalPublished = true;
            return true;
        }

        private void ClearHandlers()
        {
            _value = null;
            _completed = null;
            _faulted = null;
            _cancelled = null;
        }

        private void AddHandler(ref EventHandler? field, EventHandler? handler)
        {
            if (handler is null)
            {
                return;
            }

            lock (_gate)
            {
                ThrowIfDisposed();

                if (!_terminalPublished)
                {
                    field += handler;
                }
            }
        }

        private void RemoveHandler(ref EventHandler? field, EventHandler? handler)
        {
            if (handler is null)
            {
                return;
            }

            lock (_gate)
            {
                if (!_disposed)
                {
                    field -= handler;
                }
            }
        }

        private void AddHandler<TEventArgs>(ref EventHandler<TEventArgs>? field, EventHandler<TEventArgs>? handler)
            where TEventArgs : EventArgs
        {
            if (handler is null)
            {
                return;
            }

            lock (_gate)
            {
                ThrowIfDisposed();

                if (!_terminalPublished)
                {
                    field += handler;
                }
            }
        }

        private void RemoveHandler<TEventArgs>(ref EventHandler<TEventArgs>? field, EventHandler<TEventArgs>? handler)
            where TEventArgs : EventArgs
        {
            if (handler is null)
            {
                return;
            }

            lock (_gate)
            {
                if (!_disposed)
                {
                    field -= handler;
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(EventStreamBridge<T>));
            }
        }

        private static void CancelSafely(CancellationTokenSource? cancellationTokenSource)
        {
            if (cancellationTokenSource is null)
            {
                return;
            }

            try
            {
                cancellationTokenSource.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Enumeration completed and disposed the linked source concurrently.
            }
        }
    }
}
