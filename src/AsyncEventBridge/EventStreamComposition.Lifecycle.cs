using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace AsyncEventBridge
{

public static partial class EventStreamComposition
{
    private enum EventStreamTakeUntilCompletion
    {
        Stop = 1,
        SourceCompleted = 2,
    }

    private static IAsyncEnumerable<T> TakeUntilWithCompletion<T>(
        IAsyncEnumerable<T> source,
        Func<CancellationToken, Task> stopWait,
        Action<EventStreamTakeUntilCompletion> completionObserver,
        CancellationToken cancellationToken)
    {
        return new TakeUntilEnumerable<T>(
            source,
            stopWait,
            cancellationToken,
            completionObserver,
            false);
    }

    /// <summary>
    /// Repeats an activation/deactivation lifecycle while exposing activation, value, and cycle-end markers.
    /// </summary>
    /// <remarks>
    /// Cycle numbers are one-based and advance only after a successful activation. A successful stop observed
    /// before activation closes the inactive attempt and rearms without emitting a marker or consuming a cycle
    /// number. The active cycle emits <see cref="EventStreamLifecycleEventKind.Deactivated"/> when the stop wait
    /// wins and <see cref="EventStreamLifecycleEventKind.SourceCompleted"/> when the source ends naturally.
    ///
    /// Source faults, lifecycle-wait faults, cleanup failures, and external cancellation terminate the returned
    /// stream and are not converted into lifecycle markers.
    /// </remarks>
    public static IAsyncEnumerable<EventStreamLifecycleEvent<T>> RepeatBetweenWithLifecycle<T>(
        this IAsyncEnumerable<T> source,
        Func<CancellationToken, Task> startWait,
        Func<CancellationToken, Task> stopWait,
        CancellationToken cancellationToken = default)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (startWait is null)
        {
            throw new ArgumentNullException(nameof(startWait));
        }

        if (stopWait is null)
        {
            throw new ArgumentNullException(nameof(stopWait));
        }

        return RepeatBetweenWithLifecycleCore(
            source,
            startWait,
            stopWait,
            cancellationToken);
    }

    private static async IAsyncEnumerable<EventStreamLifecycleEvent<T>> RepeatBetweenWithLifecycleCore<T>(
        IAsyncEnumerable<T> source,
        Func<CancellationToken, Task> startWait,
        Func<CancellationToken, Task> stopWait,
        CancellationToken creationCancellationToken,
        [EnumeratorCancellation] CancellationToken enumerationCancellationToken = default)
    {
        using var lifetimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            creationCancellationToken,
            enumerationCancellationToken);

        var cancellationToken = lifetimeCancellation.Token;
        long cycle = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var activationSignal = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var activationSucceeded = 0;
            var completion = 0;

            async Task ObserveStartAsync(CancellationToken token)
            {
                await startWait(token).ConfigureAwait(false);
                Interlocked.Exchange(ref activationSucceeded, 1);
                activationSignal.TrySetResult(true);
            }

            void ObserveCompletion(EventStreamTakeUntilCompletion value)
            {
                Volatile.Write(ref completion, (int)value);
            }

            var started = source.StartAfter(ObserveStartAsync, cancellationToken);
            var window = TakeUntilWithCompletion(
                started,
                stopWait,
                ObserveCompletion,
                cancellationToken);

            await using (var enumerator = window.GetAsyncEnumerator(cancellationToken))
            {
                var moveTask = enumerator.MoveNextAsync().AsTask();

                await Task.WhenAny(moveTask, activationSignal.Task).ConfigureAwait(false);

                if (Volatile.Read(ref activationSucceeded) == 0)
                {
                    // Stop-before-start, startup failure, cancellation, or another terminal condition.
                    // Awaiting the move preserves the one-shot composition outcome and cleanup semantics.
                    if (await moveTask.ConfigureAwait(false))
                    {
                        throw new InvalidOperationException(
                            "The lifecycle source produced a value before activation completed.");
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Yield();
                    continue;
                }

                cycle++;
                yield return EventStreamLifecycleEvent<T>.Activated(cycle);

                while (true)
                {
                    var moved = await moveTask.ConfigureAwait(false);

                    if (!moved)
                    {
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
                                "The lifecycle window completed without a recorded completion boundary.");
                        }

                        break;
                    }

                    yield return EventStreamLifecycleEvent<T>.FromValue(
                        cycle,
                        enumerator.Current);

                    moveTask = enumerator.MoveNextAsync().AsTask();
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
        }
    }
}

}
