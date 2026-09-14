namespace AsyncEventBridge;

/// <summary>
/// Provides conversions from async .NET APIs to event-facing bridges.
/// </summary>
public static class AsyncEventBridgeExtensions
{
    /// <summary>
    /// Creates an event-facing bridge for a <see cref="Task"/>.
    /// </summary>
    public static EventBridge ToEventBridge(this Task task)
    {
        if (task is null)
        {
            throw new ArgumentNullException(nameof(task));
        }

        return new EventBridge(task);
    }

    /// <summary>
    /// Creates an event-facing bridge for a <see cref="Task{TResult}"/>.
    /// </summary>
    public static EventBridge<T> ToEventBridge<T>(this Task<T> task)
    {
        if (task is null)
        {
            throw new ArgumentNullException(nameof(task));
        }

        return new EventBridge<T>(task);
    }

    /// <summary>
    /// Creates an event-facing bridge for an <see cref="IAsyncEnumerable{T}"/>.
    /// </summary>
    public static EventStreamBridge<T> ToEventBridge<T>(this IAsyncEnumerable<T> source)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        return new EventStreamBridge<T>(source);
    }
}
