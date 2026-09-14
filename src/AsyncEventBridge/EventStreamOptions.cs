namespace AsyncEventBridge;

/// <summary>
/// Configures buffering for event-to-async streams. WARNING: the default <see cref="EventStreamFullMode.Grow"/>
/// mode preserves all event values and can grow memory usage without a fixed upper bound when producers outpace consumers.
/// </summary>
public sealed class EventStreamOptions
{
    internal const int DefaultCapacity = 100;

    /// <summary>
    /// Gets or sets the initial buffer capacity for <see cref="EventStreamFullMode.Grow"/>, or the hard buffer limit
    /// for <see cref="EventStreamFullMode.DropOldest"/> and <see cref="EventStreamFullMode.DropNewest"/>.
    /// </summary>
    public int Capacity { get; set; } = DefaultCapacity;

    /// <summary>
    /// Gets or sets the behavior used when event production outpaces async consumption.
    /// WARNING: <see cref="EventStreamFullMode.Grow"/> can increase memory usage without a fixed upper bound.
    /// </summary>
    public EventStreamFullMode FullMode { get; set; } = EventStreamFullMode.Grow;
}

/// <summary>
/// Defines how an event-to-async stream handles a full buffer.
/// </summary>
public enum EventStreamFullMode
{
    /// <summary>
    /// Preserves every event value by allowing the buffer to grow beyond its initial capacity.
    /// WARNING: sustained producer throughput above consumer throughput can grow memory usage without a fixed upper bound.
    /// </summary>
    Grow,

    /// <summary>
    /// Keeps the buffer bounded by removing the oldest buffered value when a new value arrives at capacity.
    /// </summary>
    DropOldest,

    /// <summary>
    /// Keeps the buffer bounded by dropping the newly arriving value when the buffer is already at capacity.
    /// </summary>
    DropNewest,
}
