namespace AsyncEventBridge
{

/// <summary>
/// Identifies one observable event in a repeated event-stream lifecycle.
/// </summary>
public enum EventStreamLifecycleEventKind
{
    /// <summary>
    /// No lifecycle event has been produced. This is the value of a default-initialized lifecycle event.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// The lifecycle entered an active cycle.
    /// </summary>
    Activated = 1,

    /// <summary>
    /// The active source produced a value.
    /// </summary>
    Value = 2,

    /// <summary>
    /// The active cycle ended because the configured stop wait completed successfully.
    /// </summary>
    Deactivated = 3,

    /// <summary>
    /// The active cycle ended because the source enumeration completed naturally.
    /// </summary>
    SourceCompleted = 4,
}

}
