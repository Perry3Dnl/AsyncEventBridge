namespace AsyncEventBridge;

/// <summary>
/// Carries a value produced by an async operation or sequence.
/// </summary>
/// <typeparam name="T">The value type.</typeparam>
public sealed class AsyncValueEventArgs<T> : EventArgs
{
    internal AsyncValueEventArgs(T value)
    {
        Value = value;
    }

    /// <summary>
    /// Gets the produced value.
    /// </summary>
    public T Value { get; }
}

/// <summary>
/// Carries the exception produced by a faulted async operation or sequence.
/// </summary>
public sealed class AsyncFaultedEventArgs : EventArgs
{
    internal AsyncFaultedEventArgs(Exception exception)
    {
        Exception = exception;
    }

    /// <summary>
    /// Gets the exception that caused the fault.
    /// </summary>
    public Exception Exception { get; }
}
