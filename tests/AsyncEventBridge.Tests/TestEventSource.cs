namespace AsyncEventBridge.Tests;

internal sealed class TestEventSource<TEventArgs>
    where TEventArgs : EventArgs
{
    private readonly object _gate = new();
    private EventHandler<TEventArgs>? _changed;
    private int _addCount;
    private int _removeCount;

    internal event EventHandler<TEventArgs> Changed
    {
        add
        {
            lock (_gate)
            {
                _changed += value;
                _addCount++;
            }
        }
        remove
        {
            lock (_gate)
            {
                _changed -= value;
                _removeCount++;
            }
        }
    }

    internal int HandlerCount
    {
        get
        {
            lock (_gate)
            {
                return _changed?.GetInvocationList().Length ?? 0;
            }
        }
    }

    internal int AddCount => Volatile.Read(ref _addCount);

    internal int RemoveCount => Volatile.Read(ref _removeCount);

    internal void Raise(TEventArgs eventArgs)
    {
        EventHandler<TEventArgs>? handlers;

        lock (_gate)
        {
            handlers = _changed;
        }

        handlers?.Invoke(this, eventArgs);
    }
}

internal sealed class TestEventArgs(int value) : EventArgs
{
    internal int Value { get; } = value;
}

internal sealed class ReentrantSubscriptionSource
{
    private EventHandler<TestEventArgs>? _changed;

    internal event EventHandler<TestEventArgs> Changed
    {
        add
        {
            _changed += value;
            value(this, new TestEventArgs(42));
        }
        remove => _changed -= value;
    }

    internal int HandlerCount => _changed?.GetInvocationList().Length ?? 0;
}
