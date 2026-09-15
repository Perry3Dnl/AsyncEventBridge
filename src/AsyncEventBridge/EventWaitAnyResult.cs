namespace AsyncEventBridge;

/// <summary>
/// Represents the winning result of a two-event asynchronous race.
/// </summary>
public readonly struct EventWaitAnyResult<TFirst, TSecond>
{
    private readonly byte _winner;
    private readonly TFirst _first;
    private readonly TSecond _second;

    private EventWaitAnyResult(byte winner, TFirst first, TSecond second)
    {
        _winner = winner;
        _first = first;
        _second = second;
    }

    /// <summary>
    /// Gets whether the first wait completed first.
    /// </summary>
    public bool IsFirst => _winner == 1;

    /// <summary>
    /// Gets whether the second wait completed first.
    /// </summary>
    public bool IsSecond => _winner == 2;

    /// <summary>
    /// Gets the first result when <see cref="IsFirst"/> is true.
    /// </summary>
    public TFirst First => IsFirst
        ? _first
        : throw new InvalidOperationException("The first event did not win this wait.");

    /// <summary>
    /// Gets the second result when <see cref="IsSecond"/> is true.
    /// </summary>
    public TSecond Second => IsSecond
        ? _second
        : throw new InvalidOperationException("The second event did not win this wait.");

    internal static EventWaitAnyResult<TFirst, TSecond> FromFirst(TFirst value) =>
        new(1, value, default!);

    internal static EventWaitAnyResult<TFirst, TSecond> FromSecond(TSecond value) =>
        new(2, default!, value);
}
