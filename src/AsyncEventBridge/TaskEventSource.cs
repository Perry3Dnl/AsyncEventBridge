namespace AsyncEventBridge;

/// <summary>
/// Event-driven facade for a non-generic <see cref="Task"/>.
/// </summary>
public sealed class TaskEventSource : IDisposable
{
    internal TaskEventSource()
    {
    }

    /// <summary>
    /// Raised when the task completes successfully.
    /// </summary>
    public event EventHandler? Completed
    {
        add => throw DraftOnly();
        remove => throw DraftOnly();
    }

    /// <summary>
    /// Raised when the task faults.
    /// </summary>
    public event EventHandler<AsyncFaultedEventArgs>? Faulted
    {
        add => throw DraftOnly();
        remove => throw DraftOnly();
    }

    /// <summary>
    /// Raised when the task is cancelled.
    /// </summary>
    public event EventHandler? Cancelled
    {
        add => throw DraftOnly();
        remove => throw DraftOnly();
    }

    /// <summary>
    /// Starts publishing the terminal task outcome to subscribers.
    /// </summary>
    public void Start() => throw DraftOnly();

    /// <inheritdoc />
    public void Dispose() => throw DraftOnly();

    private static NotImplementedException DraftOnly() =>
        new("Public API draft only. Runtime behavior is not implemented yet.");
}

/// <summary>
/// Event-driven facade for a <see cref="Task{TResult}"/>.
/// </summary>
/// <typeparam name="T">The task result type.</typeparam>
public sealed class TaskEventSource<T> : IDisposable
{
    internal TaskEventSource()
    {
    }

    /// <summary>
    /// Raised when the task completes successfully.
    /// </summary>
    public event EventHandler<AsyncValueEventArgs<T>>? Completed
    {
        add => throw DraftOnly();
        remove => throw DraftOnly();
    }

    /// <summary>
    /// Raised when the task faults.
    /// </summary>
    public event EventHandler<AsyncFaultedEventArgs>? Faulted
    {
        add => throw DraftOnly();
        remove => throw DraftOnly();
    }

    /// <summary>
    /// Raised when the task is cancelled.
    /// </summary>
    public event EventHandler? Cancelled
    {
        add => throw DraftOnly();
        remove => throw DraftOnly();
    }

    /// <summary>
    /// Starts publishing the terminal task outcome to subscribers.
    /// </summary>
    public void Start() => throw DraftOnly();

    /// <inheritdoc />
    public void Dispose() => throw DraftOnly();

    private static NotImplementedException DraftOnly() =>
        new("Public API draft only. Runtime behavior is not implemented yet.");
}
