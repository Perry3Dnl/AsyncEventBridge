using System.Runtime.CompilerServices;

namespace AsyncEventBridge;

/// <summary>
/// Provides low-level sender-aware event streams.
/// </summary>
public static class EventOccurrenceStream
{
    /// <summary>
    /// Creates an async stream that preserves both the original event sender and payload.
    /// </summary>
    public static IAsyncEnumerable<EventOccurrence<TSender, TPayload>> Create<TSender, TPayload>(
        Action<EventHandler<TSender, TPayload>> subscribe,
        Action<EventHandler<TSender, TPayload>> unsubscribe,
        Predicate<EventOccurrence<TSender, TPayload>>? predicate = null,
        EventStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscribe);
        ArgumentNullException.ThrowIfNull(unsubscribe);

        return Enumerate(
            subscribe,
            unsubscribe,
            predicate,
            options,
            cancellationToken);
    }

    private static async IAsyncEnumerable<EventOccurrence<TSender, TPayload>> Enumerate<TSender, TPayload>(
        Action<EventHandler<TSender, TPayload>> subscribe,
        Action<EventHandler<TSender, TPayload>> unsubscribe,
        Predicate<EventOccurrence<TSender, TPayload>>? predicate,
        EventStreamOptions? options,
        CancellationToken creationCancellationToken,
        [EnumeratorCancellation] CancellationToken enumerationCancellationToken = default)
    {
        EventHandler<TSender, TPayload>? adaptedHandler = null;
        var stream = EventStream.Create<EventOccurrence<TSender, TPayload>>(
            handler =>
            {
                adaptedHandler = (sender, payload) =>
                    handler(null, new EventOccurrence<TSender, TPayload>(sender, payload));
                subscribe(adaptedHandler);
            },
            _ =>
            {
                if (adaptedHandler is not null)
                {
                    unsubscribe(adaptedHandler);
                }
            },
            predicate,
            options,
            creationCancellationToken);

        await foreach (var occurrence in stream
            .WithCancellation(enumerationCancellationToken)
            .ConfigureAwait(false))
        {
            yield return occurrence;
        }
    }
}
