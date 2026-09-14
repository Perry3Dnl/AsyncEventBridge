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
        return new TaskEventSource(task);
    }

    /// <summary>
    /// Exposes a <see cref="Task{TResult}"/> as an event source.
    /// </summary>
    public static TaskEventSource<T> ToEventSource<T>(this Task<T> task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return new TaskEventSource<T>(task);
    }

    /// <summary>
    /// Exposes an <see cref="IAsyncEnumerable{T}"/> as an event source.
    /// </summary>
    public static AsyncEnumerableEventSource<T> ToEventSource<T>(this IAsyncEnumerable<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new AsyncEnumerableEventSource<T>(source);
    }
}
