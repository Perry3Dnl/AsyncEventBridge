namespace AsyncEventBridge;

/// <summary>
/// Provides conversions from async .NET APIs to event-driven sources.
/// </summary>
public static class AsyncEventSourceExtensions
{
    /// <summary>
    /// Exposes a <see cref="Task"/> as an event source.
    /// </summary>
    public static TaskEventSource ToEventSource(this Task task)
    {
        ArgumentNullException.ThrowIfNull(task);
        throw new NotImplementedException("Public API draft only. Runtime behavior is not implemented yet.");
    }

    /// <summary>
    /// Exposes a <see cref="Task{TResult}"/> as an event source.
    /// </summary>
    public static TaskEventSource<T> ToEventSource<T>(this Task<T> task)
    {
        ArgumentNullException.ThrowIfNull(task);
        throw new NotImplementedException("Public API draft only. Runtime behavior is not implemented yet.");
    }

    /// <summary>
    /// Exposes an <see cref="IAsyncEnumerable{T}"/> as an event source.
    /// </summary>
    public static AsyncEnumerableEventSource<T> ToEventSource<T>(
        this IAsyncEnumerable<T> source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        throw new NotImplementedException("Public API draft only. Runtime behavior is not implemented yet.");
    }
}
