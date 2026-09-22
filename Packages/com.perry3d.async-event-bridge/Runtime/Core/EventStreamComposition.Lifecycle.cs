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
            var stopSucceeded = 0;

            async Task ObserveStartAsync(CancellationToken token)
            {
                await startWait(token).ConfigureAwait(false);
                Interlocked.Exchange(ref activationSucceeded, 1);
                activationSignal.TrySetResult(true);
            }

            async Task ObserveStopAsync(CancellationToken token)
            {
                await stopWait(token).ConfigureAwait(false);
                Interlocked.Exchange(ref stopSucceeded, 1);
            }

            var window = source
                .StartAfter(ObserveStartAsync, cancellationToken)
                .TakeUntil(ObserveStopAsync, cancellationToken);

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
                        if (Volatile.Read(ref stopSucceeded) != 0)
                        {
                            yield return EventStreamLifecycleEvent<T>.Deactivated(cycle);
                        }
                        else
                        {
                            yield return EventStreamLifecycleEvent<T>.SourceCompleted(cycle);
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
