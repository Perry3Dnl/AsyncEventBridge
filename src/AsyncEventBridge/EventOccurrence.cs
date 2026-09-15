namespace AsyncEventBridge;

/// <summary>
/// Represents one event occurrence with both the original sender and payload.
/// </summary>
public readonly struct EventOccurrence<TSender, TPayload>
{
    /// <summary>
    /// Initializes a new event occurrence.
    /// </summary>
    public EventOccurrence(TSender sender, TPayload payload)
    {
        Sender = sender;
        Payload = payload;
    }

    /// <summary>
    /// Gets the sender supplied by the event source.
    /// </summary>
    public TSender Sender { get; }

    /// <summary>
    /// Gets the event payload.
    /// </summary>
    public TPayload Payload { get; }
}
