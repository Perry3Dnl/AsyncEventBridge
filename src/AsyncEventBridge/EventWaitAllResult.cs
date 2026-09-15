namespace AsyncEventBridge;

/// <summary>
/// Represents the successful results of two composed asynchronous event waits.
/// </summary>
public readonly struct EventWaitAllResult<TFirst, TSecond>
{
    internal EventWaitAllResult(TFirst first, TSecond second)
    {
        First = first;
        Second = second;
    }

    /// <summary>
    /// Gets the first wait result.
    /// </summary>
    public TFirst First { get; }

    /// <summary>
    /// Gets the second wait result.
    /// </summary>
    public TSecond Second { get; }
}
