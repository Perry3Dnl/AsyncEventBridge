namespace AsyncEventBridge;

/// <summary>
/// Event-facing bridge for an <see cref="IAsyncEnumerable{T}"/>.
/// </summary>
/// <typeparam name="T">The stream value type.</typeparam>
public sealed class EventStreamBridge<T> : IDisposable, IAsyncDisposable
{
    internal EventStreamBridge()
    {
    }

    /// <summary>
    /// Raised for each value produced by the async sequence.
    /// </summary>
    public event EventHandler<AsyncValueEventArgs<T>>? Value
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
    /// Connects the async sequence to the event-facing bridge and begins publishing values.
    /// </summary>
    /// <param name="cancellationToken">Optional token used to stop stream consumption.</param>
    public void Connect(CancellationToken cancellationToken = default) => throw DraftOnly();

    /// <inheritdoc />
    public void Dispose() => throw DraftOnly();

    /// <inheritdoc />
    public ValueTask DisposeAsync() => throw DraftOnly();

    private static NotImplementedException DraftOnly() =>
        new("The async-stream event bridge is still a public API draft.");
}
