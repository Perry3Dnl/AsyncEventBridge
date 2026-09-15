namespace AsyncEventBridge;

/// <summary>
/// Represents the winning result of an indexed asynchronous event race.
/// </summary>
/// <typeparam name="T">The result type shared by the event waits.</typeparam>
public readonly struct EventWaitAnyResult<T>
{
    internal EventWaitAnyResult(int index, T value)
    {
        Index = index;
        Value = value;
    }

    /// <summary>
    /// Gets the zero-based index of the wait that completed first.
    /// </summary>
    public int Index { get; }

    /// <summary>
    /// Gets the winning wait result.
    /// </summary>
    public T Value { get; }
}
