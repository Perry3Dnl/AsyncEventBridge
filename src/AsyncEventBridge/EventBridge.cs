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
/// Event-facing bridge for a non-generic <see cref="Task"/>.
/// </summary>
public sealed class EventBridge : IDisposable
{
    private readonly Task? _task;
    private readonly ValueTask _valueTask;
    private readonly object _gate = new();

    private EventHandler? _completed;
    private EventHandler<AsyncFaultedEventArgs>? _faulted;
    private EventHandler? _cancelled;
    private bool _connected;
    private bool _published;
    private bool _disposed;

    internal EventBridge(Task task)
    {
        _task = task;
    }

    internal EventBridge(ValueTask task)
    {
        _valueTask = task;
    }

    /// <summary>
    /// Raised when the task completes successfully.
    /// </summary>
    public event EventHandler? Completed
    {
        add => AddHandler(ref _completed, value);
        remove => RemoveHandler(ref _completed, value);
    }

    /// <summary>
    /// Raised when the task faults.
    /// </summary>
    public event EventHandler<AsyncFaultedEventArgs>? Faulted
    {
        add => AddHandler(ref _faulted, value);
        remove => RemoveHandler(ref _faulted, value);
    }

    /// <summary>
    /// Raised when the task is cancelled.
    /// </summary>
    public event EventHandler? Cancelled
    {
        add => AddHandler(ref _cancelled, value);
        remove => RemoveHandler(ref _cancelled, value);
    }

    /// <summary>
    /// Connects the async task to the event-facing bridge and begins publishing its terminal outcome.
    /// </summary>
    /// <remarks>
    /// The underlying task may already be running or completed. A bridge can only be connected once.
    /// </remarks>
    public void Connect()
    {
        lock (_gate)
        {
            ThrowIfDisposed();

            if (_connected)
            {
                throw new InvalidOperationException("The event bridge has already been connected.");
            }

            _connected = true;
        }

        _ = ObserveAsync();
    }

    /// <summary>
    /// Stops the bridge from publishing a future outcome and releases all event handlers.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _completed = null;
            _faulted = null;
            _cancelled = null;
        }
    }

    private async Task ObserveAsync()
    {
        try
        {
            if (_task is not null)
            {
                await _task.ConfigureAwait(false);
            }
            else
            {
                await _valueTask.ConfigureAwait(false);
            }

            PublishCompleted();
        }
        catch (OperationCanceledException) when (_task?.IsCanceled ?? _valueTask.IsCanceled)
        {
            PublishCancelled();
        }
        catch (Exception exception)
        {
            PublishFaulted(exception);
        }
    }

    private void PublishCompleted()
    {
        EventHandler? handlers;

        lock (_gate)
        {
            if (!TryBeginPublication())
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
            if (!TryBeginPublication())
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
            if (!TryBeginPublication())
            {
                return;
            }

            handlers = _cancelled;
            ClearHandlers();
        }

        EventHandlerDispatcher.Invoke(handlers, this);
    }

    private bool TryBeginPublication()
    {
        if (_disposed || _published)
        {
            return false;
        }

        _published = true;
        return true;
    }

    private void ClearHandlers()
    {
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

            if (!_published)
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

            if (!_published)
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
            throw new ObjectDisposedException(nameof(EventBridge));
        }
    }
}

/// <summary>
/// Event-facing bridge for a <see cref="Task{TResult}"/>.
/// </summary>
/// <typeparam name="T">The task result type.</typeparam>
public sealed class EventBridge<T> : IDisposable
{
    private readonly Task<T>? _task;
    private readonly ValueTask<T> _valueTask;
    private readonly object _gate = new();

    private EventHandler<AsyncValueEventArgs<T>>? _completed;
    private EventHandler<AsyncFaultedEventArgs>? _faulted;
    private EventHandler? _cancelled;
    private bool _connected;
    private bool _published;
    private bool _disposed;

    internal EventBridge(Task<T> task)
    {
        _task = task;
    }

    internal EventBridge(ValueTask<T> task)
    {
        _valueTask = task;
    }

    /// <summary>
    /// Raised when the task completes successfully.
    /// </summary>
    public event EventHandler<AsyncValueEventArgs<T>>? Completed
    {
        add => AddHandler(ref _completed, value);
        remove => RemoveHandler(ref _completed, value);
    }

    /// <summary>
    /// Raised when the task faults.
    /// </summary>
    public event EventHandler<AsyncFaultedEventArgs>? Faulted
    {
        add => AddHandler(ref _faulted, value);
        remove => RemoveHandler(ref _faulted, value);
    }

    /// <summary>
    /// Raised when the task is cancelled.
    /// </summary>
    public event EventHandler? Cancelled
    {
        add => AddHandler(ref _cancelled, value);
        remove => RemoveHandler(ref _cancelled, value);
    }

    /// <summary>
    /// Connects the async task to the event-facing bridge and begins publishing its terminal outcome.
    /// </summary>
    /// <remarks>
    /// The underlying task may already be running or completed. A bridge can only be connected once.
    /// </remarks>
    public void Connect()
    {
        lock (_gate)
        {
            ThrowIfDisposed();

            if (_connected)
            {
                throw new InvalidOperationException("The event bridge has already been connected.");
            }

            _connected = true;
        }

        _ = ObserveAsync();
    }

    /// <summary>
    /// Stops the bridge from publishing a future outcome and releases all event handlers.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _completed = null;
            _faulted = null;
            _cancelled = null;
        }
    }

    private async Task ObserveAsync()
    {
        try
        {
            T result;

            if (_task is not null)
            {
                result = await _task.ConfigureAwait(false);
            }
            else
            {
                result = await _valueTask.ConfigureAwait(false);
            }

            PublishCompleted(result);
        }
        catch (OperationCanceledException) when (_task?.IsCanceled ?? _valueTask.IsCanceled)
        {
            PublishCancelled();
        }
        catch (Exception exception)
        {
            PublishFaulted(exception);
        }
    }

    private void PublishCompleted(T result)
    {
        EventHandler<AsyncValueEventArgs<T>>? handlers;

        lock (_gate)
        {
            if (!TryBeginPublication())
            {
                return;
            }

            handlers = _completed;
            ClearHandlers();
        }

        EventHandlerDispatcher.Invoke(handlers, this, new AsyncValueEventArgs<T>(result));
    }

    private void PublishFaulted(Exception exception)
    {
        EventHandler<AsyncFaultedEventArgs>? handlers;

        lock (_gate)
        {
            if (!TryBeginPublication())
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
            if (!TryBeginPublication())
            {
                return;
            }

            handlers = _cancelled;
            ClearHandlers();
        }

        EventHandlerDispatcher.Invoke(handlers, this);
    }

    private bool TryBeginPublication()
    {
        if (_disposed || _published)
        {
            return false;
        }

        _published = true;
        return true;
    }

    private void ClearHandlers()
    {
        _completed = null;
        _faulted = null;
        _cancelled = null;
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

            if (!_published)
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

    private void AddHandler(ref EventHandler? field, EventHandler? handler)
    {
        if (handler is null)
        {
            return;
        }

        lock (_gate)
        {
            ThrowIfDisposed();

            if (!_published)
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

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(EventBridge<T>));
        }
    }
}
}
