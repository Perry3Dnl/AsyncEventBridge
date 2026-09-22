using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace AsyncEventBridge
{

public static partial class EventStreamComposition
{
    /// <summary>
    /// Repeatedly consumes the source while a boolean state is true.
    /// </summary>
    public static IAsyncEnumerable<T> RepeatWhile<T>(
        this IAsyncEnumerable<T> source,
        Func<bool> isActive,
        Func<CancellationToken, Task> waitForStateChange,
        CancellationToken cancellationToken = default)
    {
        if (isActive is null)
        {
            throw new ArgumentNullException(nameof(isActive));
        }

        return RepeatWhile(
            source,
            isActive,
            state => state,
            waitForStateChange,
            cancellationToken);
    }

    /// <summary>
    /// Repeatedly consumes the source while a boolean state is true and emits lifecycle markers.
    /// </summary>
    public static IAsyncEnumerable<EventStreamLifecycleEvent<T>> RepeatWhileWithLifecycle<T>(
        this IAsyncEnumerable<T> source,
        Func<bool> isActive,
        Func<CancellationToken, Task> waitForStateChange,
        CancellationToken cancellationToken = default)
    {
        if (isActive is null)
        {
            throw new ArgumentNullException(nameof(isActive));
        }

        return RepeatWhileWithLifecycle(
            source,
            isActive,
            state => state,
            waitForStateChange,
            cancellationToken);
    }

    /// <summary>
    /// Repeatedly consumes the source while the supplied state predicate is true.
    /// </summary>
    /// <remarks>
    /// Current state and future state changes are coordinated through <see cref="EventCondition.WaitUntilAsync{TState}"/>.
    /// If the state is already active, the source starts without waiting for another change event. While inactive,
    /// the source is not enumerated. Each transition back into the active state creates a fresh source enumeration.
    /// </remarks>
    public static IAsyncEnumerable<T> RepeatWhile<T, TState>(
        this IAsyncEnumerable<T> source,
        Func<TState> getState,
        Predicate<TState> isActive,
        Func<CancellationToken, Task> waitForStateChange,
        CancellationToken cancellationToken = default)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (getState is null)
        {
            throw new ArgumentNullException(nameof(getState));
        }

        if (isActive is null)
        {
            throw new ArgumentNullException(nameof(isActive));
        }

        if (waitForStateChange is null)
        {
            throw new ArgumentNullException(nameof(waitForStateChange));
        }

        return RepeatWhileCore(
            source,
            getState,
            isActive,
            waitForStateChange,
            cancellationToken);
    }

    /// <summary>
    /// Repeatedly consumes the source while the supplied state predicate is true and emits lifecycle markers.
    /// </summary>
    public static IAsyncEnumerable<EventStreamLifecycleEvent<T>> RepeatWhileWithLifecycle<T, TState>(
        this IAsyncEnumerable<T> source,
        Func<TState> getState,
        Predicate<TState> isActive,
        Func<CancellationToken, Task> waitForStateChange,
        CancellationToken cancellationToken = default)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (getState is null)
        {
            throw new ArgumentNullException(nameof(getState));
        }

        if (isActive is null)
        {
            throw new ArgumentNullException(nameof(isActive));
        }

        if (waitForStateChange is null)
        {
            throw new ArgumentNullException(nameof(waitForStateChange));
        }

        return RepeatWhileWithLifecycleCore(
            source,
            getState,
            isActive,
            waitForStateChange,
            cancellationToken);
    }

    private static async IAsyncEnumerable<T> RepeatWhileCore<T, TState>(
        IAsyncEnumerable<T> source,
        Func<TState> getState,
        Predicate<TState> isActive,
        Func<CancellationToken, Task> waitForStateChange,
        CancellationToken creationCancellationToken,
        [EnumeratorCancellation] CancellationToken enumerationCancellationToken = default)
    {
        using var lifetimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            creationCancellationToken,
            enumerationCancellationToken);

        var cancellationToken = lifetimeCancellation.Token;
        Predicate<TState> isInactive = state => !isActive(state);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await EventCondition.WaitUntilAsync(
                    getState,
                    isActive,
                    waitForStateChange,
                    cancellationToken)
                .ConfigureAwait(false);

            var window = source.TakeUntil(
                token => EventCondition.WaitUntilAsync(
                    getState,
                    isInactive,
                    waitForStateChange,
                    token),
                cancellationToken);

            await using (var enumerator = window.GetAsyncEnumerator(cancellationToken))
            {
                while (await enumerator.MoveNextAsync().ConfigureAwait(false))
                {
                    yield return enumerator.Current;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            // A finite source can complete while the state remains active. Yield before starting the next cycle
            // so an immediately-completing source cannot create a synchronous retry loop.
            await Task.Yield();
        }
    }

    private static async IAsyncEnumerable<EventStreamLifecycleEvent<T>> RepeatWhileWithLifecycleCore<T, TState>(
        IAsyncEnumerable<T> source,
        Func<TState> getState,
        Predicate<TState> isActive,
        Func<CancellationToken, Task> waitForStateChange,
        CancellationToken creationCancellationToken,
        [EnumeratorCancellation] CancellationToken enumerationCancellationToken = default)
    {
        using var lifetimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            creationCancellationToken,
            enumerationCancellationToken);

        var cancellationToken = lifetimeCancellation.Token;
        Predicate<TState> isInactive = state => !isActive(state);
        long cycle = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await EventCondition.WaitUntilAsync(
                    getState,
                    isActive,
                    waitForStateChange,
                    cancellationToken)
                .ConfigureAwait(false);

            cycle++;

            var completion = 0;

            void ObserveCompletion(EventStreamTakeUntilCompletion value)
            {
                Volatile.Write(ref completion, (int)value);
            }

            var window = TakeUntilWithCompletion(
                source,
                token => EventCondition.WaitUntilAsync(
                    getState,
                    isInactive,
                    waitForStateChange,
                    token),
                ObserveCompletion,
                cancellationToken);

            await using (var enumerator = window.GetAsyncEnumerator(cancellationToken))
            {
                var moveTask = enumerator.MoveNextAsync().AsTask();

                // The active window is armed before Activated is exposed so event-backed sources cannot miss
                // values while the consumer handles the lifecycle marker.
                yield return EventStreamLifecycleEvent<T>.Activated(cycle);

                while (true)
                {
                    var moved = await moveTask.ConfigureAwait(false);

                    if (!moved)
                    {
                        break;
                    }

                    yield return EventStreamLifecycleEvent<T>.FromValue(
                        cycle,
                        enumerator.Current);

                    moveTask = enumerator.MoveNextAsync().AsTask();
                }
            }

            var completedBy = (EventStreamTakeUntilCompletion)Volatile.Read(ref completion);

            if (completedBy == EventStreamTakeUntilCompletion.Stop)
            {
                yield return EventStreamLifecycleEvent<T>.Deactivated(cycle);
            }
            else if (completedBy == EventStreamTakeUntilCompletion.SourceCompleted)
            {
                yield return EventStreamLifecycleEvent<T>.SourceCompleted(cycle);
            }
            else
            {
                throw new InvalidOperationException(
                    "The state-driven lifecycle window completed without a recorded completion boundary.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
        }
    }
}

}
