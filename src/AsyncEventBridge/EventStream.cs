using System.Runtime.CompilerServices;

namespace AsyncEventBridge;

/// <summary>
/// Provides the runtime engine used to expose repeated .NET event occurrences as an async stream.
/// </summary>
public static class EventStream
{
    /// <summary>
    /// Creates an async stream for an <see cref="EventHandler"/> event.
    /// </summary>
    /// <param name="subscribe">Action used to subscribe the bridge handler.</param>
    /// <param name="unsubscribe">Action used to unsubscribe the bridge handler.</param>
    /// <param name="predicate">Optional filter applied before values enter the stream buffer.</param>
    /// <param name="options">Optional buffering configuration. The default mode preserves every value and may grow memory usage.</param>
    /// <param name="cancellationToken">Optional token used to stop the stream.</param>
    public static IAsyncEnumerable<EventArgs> Create(
        Action<EventHandler> subscribe,
        Action<EventHandler> unsubscribe,
        Predicate<EventArgs>? predicate = null,
        EventStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (subscribe is null)
        {
            throw new ArgumentNullException(nameof(subscribe));
        }

        if (unsubscribe is null)
        {
            throw new ArgumentNullException(nameof(unsubscribe));
        }

        var settings = GetSettings(options);
        return Enumerate(subscribe, unsubscribe, predicate, settings, cancellationToken);
    }

    /// <summary>
    /// Creates an async stream for an <see cref="EventHandler{TEventArgs}"/> event.
    /// </summary>
    /// <param name="subscribe">Action used to subscribe the bridge handler.</param>
    /// <param name="unsubscribe">Action used to unsubscribe the bridge handler.</param>
    /// <param name="predicate">Optional filter applied before values enter the stream buffer.</param>
    /// <param name="options">Optional buffering configuration. The default mode preserves every value and may grow memory usage.</param>
    /// <param name="cancellationToken">Optional token used to stop the stream.</param>
    public static IAsyncEnumerable<TEventArgs> Create<TEventArgs>(
        Action<EventHandler<TEventArgs>> subscribe,
        Action<EventHandler<TEventArgs>> unsubscribe,
        Predicate<TEventArgs>? predicate = null,
        EventStreamOptions? options = null,
        CancellationToken cancellationToken = default)
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

        var settings = GetSettings(options);
        return Enumerate(subscribe, unsubscribe, predicate, settings, cancellationToken);
    }

    private static async IAsyncEnumerable<EventArgs> Enumerate(
        Action<EventHandler> subscribe,
        Action<EventHandler> unsubscribe,
        Predicate<EventArgs>? predicate,
        EventStreamSettings settings,
        CancellationToken creationCancellationToken,
        [EnumeratorCancellation] CancellationToken enumerationCancellationToken = default)
    {
        using var cancellation = CreateLinkedCancellation(
            creationCancellationToken,
            enumerationCancellationToken);
        var cancellationToken = cancellation?.Token ??
            (creationCancellationToken.CanBeCanceled
                ? creationCancellationToken
                : enumerationCancellationToken);

        var buffer = new EventBuffer<EventArgs>(settings);
        var subscriptionAttempted = false;

        EventHandler handler = (_, eventArgs) =>
        {
            try
            {
                if (predicate is not null && !predicate(eventArgs))
                {
                    return;
                }

                buffer.TryWrite(eventArgs);
            }
            catch (Exception exception)
            {
                buffer.Complete(exception);
            }
        };

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            subscriptionAttempted = true;
            subscribe(handler);

            while (true)
            {
                var result = await buffer.ReadAsync(cancellationToken).ConfigureAwait(false);

                if (!result.HasValue)
                {
                    yield break;
                }

                yield return result.Value!;
            }
        }
        finally
        {
            buffer.Complete();

            if (subscriptionAttempted)
            {
                unsubscribe(handler);
            }
        }
    }

    private static async IAsyncEnumerable<TEventArgs> Enumerate<TEventArgs>(
        Action<EventHandler<TEventArgs>> subscribe,
        Action<EventHandler<TEventArgs>> unsubscribe,
        Predicate<TEventArgs>? predicate,
        EventStreamSettings settings,
        CancellationToken creationCancellationToken,
        [EnumeratorCancellation] CancellationToken enumerationCancellationToken = default)
        where TEventArgs : EventArgs
    {
        using var cancellation = CreateLinkedCancellation(
            creationCancellationToken,
            enumerationCancellationToken);
        var cancellationToken = cancellation?.Token ??
            (creationCancellationToken.CanBeCanceled
                ? creationCancellationToken
                : enumerationCancellationToken);

        var buffer = new EventBuffer<TEventArgs>(settings);
        var subscriptionAttempted = false;

        EventHandler<TEventArgs> handler = (_, eventArgs) =>
        {
            try
            {
                if (predicate is not null && !predicate(eventArgs))
                {
                    return;
                }

                buffer.TryWrite(eventArgs);
            }
            catch (Exception exception)
            {
                buffer.Complete(exception);
            }
        };

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            subscriptionAttempted = true;
            subscribe(handler);

            while (true)
            {
                var result = await buffer.ReadAsync(cancellationToken).ConfigureAwait(false);

                if (!result.HasValue)
                {
                    yield break;
                }

                yield return result.Value!;
            }
        }
        finally
        {
            buffer.Complete();

            if (subscriptionAttempted)
            {
                unsubscribe(handler);
            }
        }
    }

    private static EventStreamSettings GetSettings(EventStreamOptions? options)
    {
        var capacity = options?.Capacity ?? EventStreamOptions.DefaultCapacity;
        var fullMode = options?.FullMode ?? EventStreamFullMode.Grow;

        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                capacity,
                "Event stream capacity must be greater than zero.");
        }

        if (!Enum.IsDefined(typeof(EventStreamFullMode), fullMode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                fullMode,
                "Unknown event stream full mode.");
        }

        return new EventStreamSettings(capacity, fullMode);
    }

    private static CancellationTokenSource? CreateLinkedCancellation(
        CancellationToken first,
        CancellationToken second)
    {
        if (!first.CanBeCanceled || !second.CanBeCanceled)
        {
            return null;
        }

        return CancellationTokenSource.CreateLinkedTokenSource(first, second);
    }

    private static async Task WaitWithCancellationAsync(Task task, CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled || task.IsCompleted)
        {
            await task.ConfigureAwait(false);
            return;
        }

        var cancellationSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using (cancellationToken.Register(() => cancellationSignal.TrySetResult(true)))
        {
            var completedTask = await Task.WhenAny(task, cancellationSignal.Task).ConfigureAwait(false);

            if (!ReferenceEquals(completedTask, task))
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        await task.ConfigureAwait(false);
    }

    private readonly struct EventStreamSettings
    {
        internal EventStreamSettings(int capacity, EventStreamFullMode fullMode)
        {
            Capacity = capacity;
            FullMode = fullMode;
        }

        internal int Capacity { get; }

        internal EventStreamFullMode FullMode { get; }
    }

    private readonly struct BufferReadResult<T>
    {
        private BufferReadResult(bool hasValue, T? value)
        {
            HasValue = hasValue;
            Value = value;
        }

        internal bool HasValue { get; }

        internal T? Value { get; }

        internal static BufferReadResult<T> End { get; } = new(false, default);

        internal static BufferReadResult<T> FromValue(T value) => new(true, value);
    }

    private sealed class EventBuffer<T>
    {
        private readonly object _gate = new();
        private readonly Queue<T> _queue;
        private readonly int _capacity;
        private readonly EventStreamFullMode _fullMode;

        private TaskCompletionSource<bool>? _signal;
        private Exception? _error;
        private bool _completed;

        internal EventBuffer(EventStreamSettings settings)
        {
            _queue = new Queue<T>(settings.Capacity);
            _capacity = settings.Capacity;
            _fullMode = settings.FullMode;
        }

        internal bool TryWrite(T value)
        {
            TaskCompletionSource<bool>? signal = null;

            lock (_gate)
            {
                if (_completed)
                {
                    return false;
                }

                if (_fullMode == EventStreamFullMode.DropNewest && _queue.Count >= _capacity)
                {
                    return false;
                }

                if (_fullMode == EventStreamFullMode.DropOldest && _queue.Count >= _capacity)
                {
                    _queue.Dequeue();
                }

                var wasEmpty = _queue.Count == 0;
                _queue.Enqueue(value);

                if (wasEmpty && _signal is not null)
                {
                    signal = _signal;
                    _signal = null;
                }
            }

            signal?.TrySetResult(true);
            return true;
        }

        internal void Complete(Exception? error = null)
        {
            TaskCompletionSource<bool>? signal;

            lock (_gate)
            {
                if (_completed)
                {
                    return;
                }

                _completed = true;
                _error = error;
                signal = _signal;
                _signal = null;
            }

            signal?.TrySetResult(true);
        }

        internal async ValueTask<BufferReadResult<T>> ReadAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                Task waitTask;

                lock (_gate)
                {
                    if (_queue.Count > 0)
                    {
                        return BufferReadResult<T>.FromValue(_queue.Dequeue());
                    }

                    if (_completed)
                    {
                        if (_error is not null)
                        {
                            throw _error;
                        }

                        return BufferReadResult<T>.End;
                    }

                    _signal ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    waitTask = _signal.Task;
                }

                await WaitWithCancellationAsync(waitTask, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
