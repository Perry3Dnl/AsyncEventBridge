using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace AsyncEventBridge;

/// <summary>
/// Provides the runtime engine used to expose repeated .NET event occurrences as an async stream.
/// </summary>
public static class EventStream
{
    /// <summary>
    /// Creates an async stream for an <see cref="EventHandler"/> event.
    /// </summary>
    public static IAsyncEnumerable<EventArgs> Create(
        Action<EventHandler> subscribe,
        Action<EventHandler> unsubscribe,
        Predicate<EventArgs>? predicate = null,
        EventStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscribe);
        ArgumentNullException.ThrowIfNull(unsubscribe);

        var settings = GetSettings(options);
        return Enumerate(subscribe, unsubscribe, predicate, settings, cancellationToken);
    }

    /// <summary>
    /// Creates an async stream for an <see cref="EventHandler{TEventArgs}"/> event.
    /// </summary>
    public static IAsyncEnumerable<TEventArgs> Create<TEventArgs>(
        Action<EventHandler<TEventArgs>> subscribe,
        Action<EventHandler<TEventArgs>> unsubscribe,
        Predicate<TEventArgs>? predicate = null,
        EventStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscribe);
        ArgumentNullException.ThrowIfNull(unsubscribe);

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
        using var linkedCancellation = CreateLinkedCancellation(
            creationCancellationToken,
            enumerationCancellationToken);

        var cancellationToken = linkedCancellation?.Token ??
            (creationCancellationToken.CanBeCanceled
                ? creationCancellationToken
                : enumerationCancellationToken);

        var channel = CreateChannel<EventArgs>(settings);
        var subscriptionAttempted = false;

        EventHandler handler = (_, eventArgs) =>
        {
            try
            {
                if (predicate is not null && !predicate(eventArgs))
                {
                    return;
                }

                channel.Writer.TryWrite(eventArgs);
            }
            catch (Exception exception)
            {
                channel.Writer.TryComplete(exception);
            }
        };

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            subscriptionAttempted = true;
            subscribe(handler);

            await foreach (var value in channel.Reader
                .ReadAllAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                yield return value;
            }
        }
        finally
        {
            channel.Writer.TryComplete();

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
    {
        using var linkedCancellation = CreateLinkedCancellation(
            creationCancellationToken,
            enumerationCancellationToken);

        var cancellationToken = linkedCancellation?.Token ??
            (creationCancellationToken.CanBeCanceled
                ? creationCancellationToken
                : enumerationCancellationToken);

        var channel = CreateChannel<TEventArgs>(settings);
        var subscriptionAttempted = false;

        EventHandler<TEventArgs> handler = (_, eventArgs) =>
        {
            try
            {
                if (predicate is not null && !predicate(eventArgs))
                {
                    return;
                }

                channel.Writer.TryWrite(eventArgs);
            }
            catch (Exception exception)
            {
                channel.Writer.TryComplete(exception);
            }
        };

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            subscriptionAttempted = true;
            subscribe(handler);

            await foreach (var value in channel.Reader
                .ReadAllAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                yield return value;
            }
        }
        finally
        {
            channel.Writer.TryComplete();

            if (subscriptionAttempted)
            {
                unsubscribe(handler);
            }
        }
    }

    private static Channel<T> CreateChannel<T>(EventStreamSettings settings)
    {
        if (settings.FullMode == EventStreamFullMode.Grow)
        {
            return Channel.CreateUnbounded<T>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            });
        }

        var fullMode = settings.FullMode switch
        {
            EventStreamFullMode.DropOldest => BoundedChannelFullMode.DropOldest,
            EventStreamFullMode.DropNewest => BoundedChannelFullMode.DropWrite,
            _ => throw new InvalidOperationException($"Unsupported stream full mode: {settings.FullMode}."),
        };

        var channelOptions = new BoundedChannelOptions(settings.Capacity)
        {
            FullMode = fullMode,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        };

        if (settings.Options is null)
        {
            return Channel.CreateBounded<T>(channelOptions);
        }

        return Channel.CreateBounded<T>(channelOptions, _ =>
        {
            var droppedCount = settings.Options.RecordDrop();
            var observer = settings.DropObserver;

            if (observer is null)
            {
                return;
            }

            try
            {
                observer(droppedCount);
            }
            catch (Exception exception)
            {
                Trace.TraceError("AsyncEventBridge event-stream drop observer threw: {0}", exception);
            }
        });
    }

    private static EventStreamSettings GetSettings(EventStreamOptions? options)
    {
        var capacity = options?.Capacity ?? EventStreamOptions.DefaultCapacity;
        var fullMode = options?.FullMode ?? EventStreamFullMode.Grow;

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(capacity, 0, nameof(options));

        if (!Enum.IsDefined(fullMode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                fullMode,
                "Unknown event stream full mode.");
        }

        return new EventStreamSettings(
            capacity,
            fullMode,
            options,
            options?.DropObserver);
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

    private readonly record struct EventStreamSettings(
        int Capacity,
        EventStreamFullMode FullMode,
        EventStreamOptions? Options,
        Action<long>? DropObserver);
}
