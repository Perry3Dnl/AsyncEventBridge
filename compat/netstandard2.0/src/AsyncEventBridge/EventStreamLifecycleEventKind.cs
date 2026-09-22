namespace AsyncEventBridge
{

/// <summary>
/// Identifies one observable event in a repeated event-stream lifecycle.
/// </summary>
public enum EventStreamLifecycleEventKind
{
    /// <summary>
    /// The lifecycle entered an active cycle.
    /// </summary>
    Activated = 0,

    /// <summary>
    /// The active source produced a value.
    /// </summary>
    Value = 1,

    /// <summary>
    /// The active cycle ended because the configured stop wait completed successfully.
    /// </summary>
    Deactivated = 2,

    /// <summary>
    /// The active cycle ended because the source enumeration completed naturally.
    /// </summary>
    SourceCompleted = 3,
}

}
