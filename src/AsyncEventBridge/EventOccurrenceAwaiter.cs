namespace AsyncEventBridge;

/// <summary>
/// Provides low-level sender-aware event waits.
/// </summary>
public static class EventOccurrenceAwaiter
{
    /// <summary>
    /// Waits for an event while preserving both the original sender and payload.
    /// </summary>
    public static Task<EventOccurrence<TSender, TPayload>> WaitAsync<TSender, TPayload>(
        Action<EventHandler<TSender, TPayload>> subscribe,
        Action<EventHandler<TSender, TPayload>> unsubscribe,
        Predicate<EventOccurrence<TSender, TPayload>>? predicate = null,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(subscribe);
        ArgumentNullException.ThrowIfNull(unsubscribe);

        EventHandler<TSender, TPayload>? adaptedHandler = null;

        return EventAwaiter.WaitAsync<EventOccurrence<TSender, TPayload>>(
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
            cancellationToken,
            timeout,
            timeProvider);
    }
}
