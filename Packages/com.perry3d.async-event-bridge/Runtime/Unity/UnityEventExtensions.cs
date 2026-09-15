using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

namespace AsyncEventBridge.Unity
{

/// <summary>
/// AsyncEventBridge integration for serialized and runtime <see cref="UnityEvent"/> instances.
/// </summary>
public static class UnityEventExtensions
{
    public static async Awaitable WaitAsync(
        this UnityEvent source,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        UnityAction? listener = null;
        await UnityEventAwaiter.WaitAsync(
            handler =>
            {
                listener = () => handler(source, EventArgs.Empty);
                source.AddListener(listener);
            },
            _ => source.RemoveListener(listener!),
            cancellationToken: cancellationToken,
            timeout: timeout);
    }

    public static async Awaitable WaitAsync(
        this UnityEvent source,
        MonoBehaviour owner,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (owner == null) throw new ArgumentNullException(nameof(owner));
        UnityAction? listener = null;
        await UnityEventAwaiter.WaitAsync(
            owner,
            handler =>
            {
                listener = () => handler(source, EventArgs.Empty);
                source.AddListener(listener);
            },
            _ => source.RemoveListener(listener!),
            cancellationToken: cancellationToken,
            timeout: timeout);
    }

    public static async Awaitable<T0> WaitAsync<T0>(
        this UnityEvent<T0> source,
        Predicate<T0>? predicate = null,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        UnityAction<T0>? listener = null;
        var args = await UnityEventAwaiter.WaitAsync<UnityEventArgs<T0>>(
            handler =>
            {
                listener = value => handler(source, new UnityEventArgs<T0>(value));
                source.AddListener(listener);
            },
            _ => source.RemoveListener(listener!),
            predicate is null ? null : args => predicate(args.Item0),
            cancellationToken,
            timeout);
        return args.Item0;
    }

    public static async Awaitable<T0> WaitAsync<T0>(
        this UnityEvent<T0> source,
        MonoBehaviour owner,
        Predicate<T0>? predicate = null,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (owner == null) throw new ArgumentNullException(nameof(owner));
        UnityAction<T0>? listener = null;
        var args = await UnityEventAwaiter.WaitAsync<UnityEventArgs<T0>>(
            owner,
            handler =>
            {
                listener = value => handler(source, new UnityEventArgs<T0>(value));
                source.AddListener(listener);
            },
            _ => source.RemoveListener(listener!),
            predicate is null ? null : args => predicate(args.Item0),
            cancellationToken,
            timeout);
        return args.Item0;
    }

    public static async Awaitable<(T0, T1)> WaitAsync<T0, T1>(
        this UnityEvent<T0, T1> source,
        Func<T0, T1, bool>? predicate = null,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        UnityAction<T0, T1>? listener = null;
        var args = await UnityEventAwaiter.WaitAsync<UnityEventArgs<T0, T1>>(
            handler =>
            {
                listener = (item0, item1) => handler(source, new UnityEventArgs<T0, T1>(item0, item1));
                source.AddListener(listener);
            },
            _ => source.RemoveListener(listener!),
            predicate is null ? null : args => predicate(args.Item0, args.Item1),
            cancellationToken,
            timeout);
        return (args.Item0, args.Item1);
    }

    public static async Awaitable<(T0, T1)> WaitAsync<T0, T1>(
        this UnityEvent<T0, T1> source,
        MonoBehaviour owner,
        Func<T0, T1, bool>? predicate = null,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (owner == null) throw new ArgumentNullException(nameof(owner));
        UnityAction<T0, T1>? listener = null;
        var args = await UnityEventAwaiter.WaitAsync<UnityEventArgs<T0, T1>>(
            owner,
            handler =>
            {
                listener = (item0, item1) => handler(source, new UnityEventArgs<T0, T1>(item0, item1));
                source.AddListener(listener);
            },
            _ => source.RemoveListener(listener!),
            predicate is null ? null : args => predicate(args.Item0, args.Item1),
            cancellationToken,
            timeout);
        return (args.Item0, args.Item1);
    }

    public static async Awaitable<(T0, T1, T2)> WaitAsync<T0, T1, T2>(
        this UnityEvent<T0, T1, T2> source,
        Func<T0, T1, T2, bool>? predicate = null,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        UnityAction<T0, T1, T2>? listener = null;
        var args = await UnityEventAwaiter.WaitAsync<UnityEventArgs<T0, T1, T2>>(
            handler =>
            {
                listener = (item0, item1, item2) => handler(source, new UnityEventArgs<T0, T1, T2>(item0, item1, item2));
                source.AddListener(listener);
            },
            _ => source.RemoveListener(listener!),
            predicate is null ? null : args => predicate(args.Item0, args.Item1, args.Item2),
            cancellationToken,
            timeout);
        return (args.Item0, args.Item1, args.Item2);
    }

    public static async Awaitable<(T0, T1, T2)> WaitAsync<T0, T1, T2>(
        this UnityEvent<T0, T1, T2> source,
        MonoBehaviour owner,
        Func<T0, T1, T2, bool>? predicate = null,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (owner == null) throw new ArgumentNullException(nameof(owner));
        UnityAction<T0, T1, T2>? listener = null;
        var args = await UnityEventAwaiter.WaitAsync<UnityEventArgs<T0, T1, T2>>(
            owner,
            handler =>
            {
                listener = (item0, item1, item2) => handler(source, new UnityEventArgs<T0, T1, T2>(item0, item1, item2));
                source.AddListener(listener);
            },
            _ => source.RemoveListener(listener!),
            predicate is null ? null : args => predicate(args.Item0, args.Item1, args.Item2),
            cancellationToken,
            timeout);
        return (args.Item0, args.Item1, args.Item2);
    }

    public static async Awaitable<(T0, T1, T2, T3)> WaitAsync<T0, T1, T2, T3>(
        this UnityEvent<T0, T1, T2, T3> source,
        Func<T0, T1, T2, T3, bool>? predicate = null,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        UnityAction<T0, T1, T2, T3>? listener = null;
        var args = await UnityEventAwaiter.WaitAsync<UnityEventArgs<T0, T1, T2, T3>>(
            handler =>
            {
                listener = (item0, item1, item2, item3) => handler(source, new UnityEventArgs<T0, T1, T2, T3>(item0, item1, item2, item3));
                source.AddListener(listener);
            },
            _ => source.RemoveListener(listener!),
            predicate is null ? null : args => predicate(args.Item0, args.Item1, args.Item2, args.Item3),
            cancellationToken,
            timeout);
        return (args.Item0, args.Item1, args.Item2, args.Item3);
    }

    public static async Awaitable<(T0, T1, T2, T3)> WaitAsync<T0, T1, T2, T3>(
        this UnityEvent<T0, T1, T2, T3> source,
        MonoBehaviour owner,
        Func<T0, T1, T2, T3, bool>? predicate = null,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (owner == null) throw new ArgumentNullException(nameof(owner));
        UnityAction<T0, T1, T2, T3>? listener = null;
        var args = await UnityEventAwaiter.WaitAsync<UnityEventArgs<T0, T1, T2, T3>>(
            owner,
            handler =>
            {
                listener = (item0, item1, item2, item3) => handler(source, new UnityEventArgs<T0, T1, T2, T3>(item0, item1, item2, item3));
                source.AddListener(listener);
            },
            _ => source.RemoveListener(listener!),
            predicate is null ? null : args => predicate(args.Item0, args.Item1, args.Item2, args.Item3),
            cancellationToken,
            timeout);
        return (args.Item0, args.Item1, args.Item2, args.Item3);
    }

    public static IAsyncEnumerable<EventArgs> AsAsyncEnumerable(
        this UnityEvent source,
        EventStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        var dispatcher = UnityMainThreadDispatcher.Capture();
        UnityAction? listener = null;
        return EventStream.Create(
            handler => dispatcher.Invoke(() =>
            {
                listener = () => handler(source, EventArgs.Empty);
                source.AddListener(listener);
            }),
            _ => dispatcher.Invoke(() => source.RemoveListener(listener!)),
            options: options,
            cancellationToken: cancellationToken);
    }

    public static IAsyncEnumerable<T0> AsAsyncEnumerable<T0>(
        this UnityEvent<T0> source,
        Predicate<T0>? predicate = null,
        EventStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        var dispatcher = UnityMainThreadDispatcher.Capture();
        UnityAction<T0>? listener = null;
        var stream = EventStream.Create<UnityEventArgs<T0>>(
            handler => dispatcher.Invoke(() =>
            {
                listener = value => handler(source, new UnityEventArgs<T0>(value));
                source.AddListener(listener);
            }),
            _ => dispatcher.Invoke(() => source.RemoveListener(listener!)),
            predicate is null ? null : args => predicate(args.Item0),
            options,
            cancellationToken);
        return Project(stream, static args => args.Item0);
    }

    public static IAsyncEnumerable<T0> AsAsyncEnumerable<T0>(
        this UnityEvent<T0> source,
        MonoBehaviour owner,
        Predicate<T0>? predicate = null,
        EventStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (owner == null) throw new ArgumentNullException(nameof(owner));
        var destroyToken = owner.destroyCancellationToken;
        var exitToken = Application.exitCancellationToken;
        return WithLifetime(
            destroyToken,
            exitToken,
            token => source.AsAsyncEnumerable(predicate, options, token),
            cancellationToken);
    }

    public static IAsyncEnumerable<EventArgs> AsAsyncEnumerable(
        this UnityEvent source,
        MonoBehaviour owner,
        EventStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (owner == null) throw new ArgumentNullException(nameof(owner));
        var destroyToken = owner.destroyCancellationToken;
        var exitToken = Application.exitCancellationToken;
        return WithLifetime(
            destroyToken,
            exitToken,
            token => source.AsAsyncEnumerable(options, token),
            cancellationToken);
    }

    public static IAsyncEnumerable<(T0, T1)> AsAsyncEnumerable<T0, T1>(
        this UnityEvent<T0, T1> source,
        Func<T0, T1, bool>? predicate = null,
        EventStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        var dispatcher = UnityMainThreadDispatcher.Capture();
        UnityAction<T0, T1>? listener = null;
        var stream = EventStream.Create<UnityEventArgs<T0, T1>>(
            handler => dispatcher.Invoke(() =>
            {
                listener = (item0, item1) => handler(source, new UnityEventArgs<T0, T1>(item0, item1));
                source.AddListener(listener);
            }),
            _ => dispatcher.Invoke(() => source.RemoveListener(listener!)),
            predicate is null ? null : args => predicate(args.Item0, args.Item1),
            options,
            cancellationToken);
        return Project(stream, static args => (args.Item0, args.Item1));
    }

    public static IAsyncEnumerable<(T0, T1)> AsAsyncEnumerable<T0, T1>(
        this UnityEvent<T0, T1> source,
        MonoBehaviour owner,
        Func<T0, T1, bool>? predicate = null,
        EventStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (owner == null) throw new ArgumentNullException(nameof(owner));
        var destroyToken = owner.destroyCancellationToken;
        var exitToken = Application.exitCancellationToken;
        return WithLifetime(
            destroyToken,
            exitToken,
            token => source.AsAsyncEnumerable(predicate, options, token),
            cancellationToken);
    }

    public static IAsyncEnumerable<(T0, T1, T2)> AsAsyncEnumerable<T0, T1, T2>(
        this UnityEvent<T0, T1, T2> source,
        Func<T0, T1, T2, bool>? predicate = null,
        EventStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        var dispatcher = UnityMainThreadDispatcher.Capture();
        UnityAction<T0, T1, T2>? listener = null;
        var stream = EventStream.Create<UnityEventArgs<T0, T1, T2>>(
            handler => dispatcher.Invoke(() =>
            {
                listener = (item0, item1, item2) => handler(source, new UnityEventArgs<T0, T1, T2>(item0, item1, item2));
                source.AddListener(listener);
            }),
            _ => dispatcher.Invoke(() => source.RemoveListener(listener!)),
            predicate is null ? null : args => predicate(args.Item0, args.Item1, args.Item2),
            options,
            cancellationToken);
        return Project(stream, static args => (args.Item0, args.Item1, args.Item2));
    }

    public static IAsyncEnumerable<(T0, T1, T2)> AsAsyncEnumerable<T0, T1, T2>(
        this UnityEvent<T0, T1, T2> source,
        MonoBehaviour owner,
        Func<T0, T1, T2, bool>? predicate = null,
        EventStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (owner == null) throw new ArgumentNullException(nameof(owner));
        var destroyToken = owner.destroyCancellationToken;
        var exitToken = Application.exitCancellationToken;
        return WithLifetime(
            destroyToken,
            exitToken,
            token => source.AsAsyncEnumerable(predicate, options, token),
            cancellationToken);
    }

    public static IAsyncEnumerable<(T0, T1, T2, T3)> AsAsyncEnumerable<T0, T1, T2, T3>(
        this UnityEvent<T0, T1, T2, T3> source,
        Func<T0, T1, T2, T3, bool>? predicate = null,
        EventStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        var dispatcher = UnityMainThreadDispatcher.Capture();
        UnityAction<T0, T1, T2, T3>? listener = null;
        var stream = EventStream.Create<UnityEventArgs<T0, T1, T2, T3>>(
            handler => dispatcher.Invoke(() =>
            {
                listener = (item0, item1, item2, item3) => handler(source, new UnityEventArgs<T0, T1, T2, T3>(item0, item1, item2, item3));
                source.AddListener(listener);
            }),
            _ => dispatcher.Invoke(() => source.RemoveListener(listener!)),
            predicate is null ? null : args => predicate(args.Item0, args.Item1, args.Item2, args.Item3),
            options,
            cancellationToken);
        return Project(stream, static args => (args.Item0, args.Item1, args.Item2, args.Item3));
    }

    public static IAsyncEnumerable<(T0, T1, T2, T3)> AsAsyncEnumerable<T0, T1, T2, T3>(
        this UnityEvent<T0, T1, T2, T3> source,
        MonoBehaviour owner,
        Func<T0, T1, T2, T3, bool>? predicate = null,
        EventStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (owner == null) throw new ArgumentNullException(nameof(owner));
        var destroyToken = owner.destroyCancellationToken;
        var exitToken = Application.exitCancellationToken;
        return WithLifetime(
            destroyToken,
            exitToken,
            token => source.AsAsyncEnumerable(predicate, options, token),
            cancellationToken);
    }

    private static async IAsyncEnumerable<T> WithLifetime<T>(
        CancellationToken destroyToken,
        CancellationToken exitToken,
        Func<CancellationToken, IAsyncEnumerable<T>> streamFactory,
        CancellationToken callerToken,
        [EnumeratorCancellation] CancellationToken enumerationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            destroyToken,
            exitToken,
            callerToken,
            enumerationToken);

        await foreach (var value in streamFactory(linked.Token).WithCancellation(linked.Token))
        {
            yield return value;
        }
    }

    private static async IAsyncEnumerable<TResult> Project<TSource, TResult>(
        IAsyncEnumerable<TSource> source,
        Func<TSource, TResult> selector,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var value in source.WithCancellation(cancellationToken))
        {
            yield return selector(value);
        }
    }

    private sealed class UnityEventArgs<T0> : EventArgs
    {
        internal UnityEventArgs(T0 item0) => Item0 = item0;
        internal T0 Item0 { get; }
    }

    private sealed class UnityEventArgs<T0, T1> : EventArgs
    {
        internal UnityEventArgs(T0 item0, T1 item1) { Item0 = item0; Item1 = item1; }
        internal T0 Item0 { get; }
        internal T1 Item1 { get; }
    }

    private sealed class UnityEventArgs<T0, T1, T2> : EventArgs
    {
        internal UnityEventArgs(T0 item0, T1 item1, T2 item2) { Item0 = item0; Item1 = item1; Item2 = item2; }
        internal T0 Item0 { get; }
        internal T1 Item1 { get; }
        internal T2 Item2 { get; }
    }

    private sealed class UnityEventArgs<T0, T1, T2, T3> : EventArgs
    {
        internal UnityEventArgs(T0 item0, T1 item1, T2 item2, T3 item3) { Item0 = item0; Item1 = item1; Item2 = item2; Item3 = item3; }
        internal T0 Item0 { get; }
        internal T1 Item1 { get; }
        internal T2 Item2 { get; }
        internal T3 Item3 { get; }
    }
}

internal sealed class UnityMainThreadDispatcher
{
    private readonly SynchronizationContext _context;
    private readonly int _threadId;

    private UnityMainThreadDispatcher(SynchronizationContext context, int threadId)
    {
        _context = context;
        _threadId = threadId;
    }

    internal static UnityMainThreadDispatcher Capture()
    {
        var context = SynchronizationContext.Current;
        if (context is null)
        {
            throw new InvalidOperationException("Unity async event integration must be created from the Unity main thread.");
        }

        return new UnityMainThreadDispatcher(context, Thread.CurrentThread.ManagedThreadId);
    }

    internal void Invoke(Action action)
    {
        if (Thread.CurrentThread.ManagedThreadId == _threadId)
        {
            action();
            return;
        }

        Exception? error = null;
        _context.Send(_ =>
        {
            try { action(); }
            catch (Exception exception) { error = exception; }
        }, null);

        if (error is not null)
        {
            throw error;
        }
    }
}
}
