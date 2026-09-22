using System;

namespace AsyncEventBridge
{

/// <summary>
/// Represents one activation, source value, or termination marker from a repeated event-stream lifecycle.
/// </summary>
public readonly struct EventStreamLifecycleEvent<T>
{
    private readonly T _value;

    private EventStreamLifecycleEvent(
        EventStreamLifecycleEventKind kind,
        long cycle,
        T value)
    {
        Kind = kind;
        Cycle = cycle;
        _value = value;
    }

    /// <summary>
    /// Gets the lifecycle event kind.
    /// </summary>
    public EventStreamLifecycleEventKind Kind { get; }

    /// <summary>
    /// Gets the one-based activation cycle number.
    /// </summary>
    public long Cycle { get; }

    /// <summary>
    /// Gets whether this lifecycle event carries a source value.
    /// </summary>
    public bool HasValue => Kind == EventStreamLifecycleEventKind.Value;

    /// <summary>
    /// Gets the source value when <see cref="HasValue"/> is true.
    /// </summary>
    public T Value => HasValue
        ? _value
        : throw new InvalidOperationException("This lifecycle event does not carry a source value.");

    internal static EventStreamLifecycleEvent<T> Activated(long cycle) =>
        new EventStreamLifecycleEvent<T>(
            EventStreamLifecycleEventKind.Activated,
            cycle,
            default!);

    internal static EventStreamLifecycleEvent<T> FromValue(long cycle, T value) =>
        new EventStreamLifecycleEvent<T>(
            EventStreamLifecycleEventKind.Value,
            cycle,
            value);

    internal static EventStreamLifecycleEvent<T> Deactivated(long cycle) =>
        new EventStreamLifecycleEvent<T>(
            EventStreamLifecycleEventKind.Deactivated,
            cycle,
            default!);

    internal static EventStreamLifecycleEvent<T> SourceCompleted(long cycle) =>
        new EventStreamLifecycleEvent<T>(
            EventStreamLifecycleEventKind.SourceCompleted,
            cycle,
            default!);
}

}
