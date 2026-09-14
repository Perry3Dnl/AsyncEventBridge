namespace AsyncEventBridge;

/// <summary>
/// Event-driven facade for an <see cref="IAsyncEnumerable{T}"/>.
/// </summary>
/// <typeparam name="T">The stream item type.</typeparam>
public sealed class AsyncEnumerableEventSource<T> : IDisposable, IAsyncDisposable
{
    internal AsyncEnumerableEventSource()
    {
    }

    /// <summary>
    /// Raised for each item produced by the async sequence.
    /// </summary>
    public event EventHandler<AsyncValueEventArgs<T>>? Next
    {
        add => throw DraftOnly();
        remove => throw DraftOnly();
    }

    /// <summary>
    /// Raised when the async sequence completes successfully.
    /// </summary>
    public event EventHandler? Completed
    {
        add => throw DraftOnly();
        remove => throw DraftOnly();
    }

    /// <summary>
    /// Raised when enumeration faults.
    /// </summary>
    public event EventHandler<AsyncFaultedEventArgs>? Faulted
    {
        add => throw DraftOnly();
        remove => throw DraftOnly();
    }

    /// <summary>
    /// Raised when enumeration is cancelled.
    /// </summary>
    public event EventHandler? Cancelled
    {
        add => throw DraftOnly();
        remove => throw DraftOnly();
    }

    /// <summary>
    /// Starts enumerating and publishing items to subscribers.
    /// </summary>
    public void Start() => throw DraftOnly();

    /// <inheritdoc />
    public void Dispose() => throw DraftOnly();

    /// <inheritdoc />
    public ValueTask DisposeAsync() => throw DraftOnly();

    private static NotImplementedException DraftOnly() =>
        new("Public API draft only. Runtime behavior is not implemented yet.");
}
