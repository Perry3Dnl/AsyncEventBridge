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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscribe);
        ArgumentNullException.ThrowIfNull(unsubscribe);

        return Enumerate(subscribe, unsubscribe, predicate, cancellationToken);
    }

    /// <summary>
    /// Creates an async stream for an <see cref="EventHandler{TEventArgs}"/> event.
    /// </summary>
    public static IAsyncEnumerable<TEventArgs> Create<TEventArgs>(
        Action<EventHandler<TEventArgs>> subscribe,
        Action<EventHandler<TEventArgs>> unsubscribe,
        Predicate<TEventArgs>? predicate = null,
        CancellationToken cancellationToken = default)
        where TEventArgs : EventArgs
    {
        ArgumentNullException.ThrowIfNull(subscribe);
        ArgumentNullException.ThrowIfNull(unsubscribe);

        return Enumerate(subscribe, unsubscribe, predicate, cancellationToken);
    }

    private static async IAsyncEnumerable<EventArgs> Enumerate(
        Action<EventHandler> subscribe,
        Action<EventHandler> unsubscribe,
        Predicate<EventArgs>? predicate,
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

        var channel = CreateChannel<EventArgs>();
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

            await foreach (var eventArgs in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return eventArgs;
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

        var channel = CreateChannel<TEventArgs>();
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

            await foreach (var eventArgs in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return eventArgs;
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

    private static Channel<T> CreateChannel<T>() =>
        Channel.CreateUnbounded<T>(new UnboundedChannelOptions
        {
            AllowSynchronousContinuations = false,
            SingleReader = true,
            SingleWriter = false,
        });

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
}
