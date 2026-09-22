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
    /// Repeatedly consumes a fresh source enumeration between asynchronous activation and deactivation waits.
    /// </summary>
    /// <remarks>
    /// Each lifecycle cycle waits for <paramref name="startWait"/>, consumes a fresh enumeration of
    /// <paramref name="source"/> until <paramref name="stopWait"/> completes, cleans both sides, and then rearms
    /// the next cycle. A successful stop that arrives before activation closes that inactive cycle and immediately
    /// rearms the next activation wait without ever starting the source.
    ///
    /// Source completion ends only the current active cycle; the next activation creates a fresh source
    /// enumeration. Source faults, lifecycle-wait faults, cleanup failures, and external cancellation terminate
    /// the repeating workflow and remain observable.
    /// </remarks>
    public static IAsyncEnumerable<T> RepeatBetween<T>(
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

        return RepeatBetweenCore(source, startWait, stopWait, cancellationToken);
    }

    private static async IAsyncEnumerable<T> RepeatBetweenCore<T>(
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

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var cycle = source
                .StartAfter(startWait, cancellationToken)
                .TakeUntil(stopWait, cancellationToken);

            await using (var enumerator = cycle.GetAsyncEnumerator(cancellationToken))
            {
                while (await enumerator.MoveNextAsync().ConfigureAwait(false))
                {
                    yield return enumerator.Current;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Keep immediately-completing lifecycle factories or finite sources from creating a synchronous hot loop.
            await Task.Yield();
        }
    }
}

}
