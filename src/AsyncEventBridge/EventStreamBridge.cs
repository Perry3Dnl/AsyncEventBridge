namespace AsyncEventBridge;

/// <summary>
/// Event-facing bridge for an <see cref="IAsyncEnumerable{T}"/>.
/// </summary>
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
            ObjectDisposedException.ThrowIf(_disposed, this);

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
    /// Stops stream consumption and suppresses future event publication.
    /// </summary>
    /// <remarks>
    /// Synchronous disposal requests cancellation but does not wait for asynchronous enumerator cleanup.
    /// Use <see cref="DisposeAsync"/> when that cleanup must be awaited.
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
    /// Stops stream consumption, suppresses future event publication, and waits for asynchronous enumerator cleanup.
    /// </summary>
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
        try
        {
            var cancellationToken = lifetimeCts.Token;
            cancellationToken.ThrowIfCancellationRequested();

            await foreach (var value in _source.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                PublishValue(value);
            }

            cancellationToken.ThrowIfCancellationRequested();
            PublishCompleted();
        }
        catch (OperationCanceledException)
        {
            PublishCancelled();
        }
        catch (Exception exception)
        {
            PublishFaulted(exception);
        }
        finally
        {
            lifetimeCts.Dispose();
            completion.TrySetResult(true);

            lock (_gate)
            {
                if (ReferenceEquals(_lifetimeCts, lifetimeCts))
                {
                    _lifetimeCts = null;
                }

                if (ReferenceEquals(_completion, completion))
                {
                    _completion = null;
                }
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
            ObjectDisposedException.ThrowIf(_disposed, this);

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
            ObjectDisposedException.ThrowIf(_disposed, this);

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
