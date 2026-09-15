using System.Runtime.ExceptionServices;

namespace AsyncEventBridge;

/// <summary>
/// Provides composition helpers for asynchronous event waits.
/// </summary>
public static class EventComposition
{
    /// <summary>
    /// Waits until either of two cancellable asynchronous event waits completes.
    /// </summary>
    /// <remarks>
    /// The losing wait is cancelled and observed before this method completes so event subscriptions can be cleaned up deterministically.
    /// Wait factories should honor the supplied cancellation token.
    /// </remarks>
    public static async Task<EventWaitAnyResult<TFirst, TSecond>> WaitAnyAsync<TFirst, TSecond>(
        Func<CancellationToken, Task<TFirst>> firstWait,
        Func<CancellationToken, Task<TSecond>> secondWait,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(firstWait);
        ArgumentNullException.ThrowIfNull(secondWait);

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<TFirst>? firstTask = null;
        Task<TSecond>? secondTask = null;

        try
        {
            firstTask = firstWait(linkedCancellation.Token)
                ?? throw new InvalidOperationException("The first event wait factory returned null.");
            secondTask = secondWait(linkedCancellation.Token)
                ?? throw new InvalidOperationException("The second event wait factory returned null.");
        }
        catch (Exception startException)
        {
            Exception? cleanupException = null;

            try
            {
                linkedCancellation.Cancel();
            }
            catch (Exception cancellationException)
            {
                cleanupException = cancellationException;
            }

            if (firstTask is not null)
            {
                cleanupException = Combine(
                    cleanupException,
                    await ObserveLoserAsync(firstTask).ConfigureAwait(false));
            }

            if (cleanupException is not null)
            {
                throw new AggregateException(startException, cleanupException);
            }

            ExceptionDispatchInfo.Capture(startException).Throw();
            throw;
        }

        var completed = await Task.WhenAny(firstTask, secondTask).ConfigureAwait(false);
        Exception? cancellationFailure = null;

        try
        {
            linkedCancellation.Cancel();
        }
        catch (Exception exception)
        {
            cancellationFailure = exception;
        }

        if (ReferenceEquals(completed, firstTask))
        {
            return await FinishFirstAsync(firstTask, secondTask, cancellationFailure).ConfigureAwait(false);
        }

        return await FinishSecondAsync(firstTask, secondTask, cancellationFailure).ConfigureAwait(false);
    }

    private static async Task<EventWaitAnyResult<TFirst, TSecond>> FinishFirstAsync<TFirst, TSecond>(
        Task<TFirst> winner,
        Task<TSecond> loser,
        Exception? cancellationFailure)
    {
        TFirst value = default!;
        Exception? winnerException = null;

        try
        {
            value = await winner.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            winnerException = exception;
        }

        var cleanupException = Combine(
            cancellationFailure,
            await ObserveLoserAsync(loser).ConfigureAwait(false));

        ThrowIfAny(winnerException, cleanupException);
        return EventWaitAnyResult<TFirst, TSecond>.FromFirst(value);
    }

    private static async Task<EventWaitAnyResult<TFirst, TSecond>> FinishSecondAsync<TFirst, TSecond>(
        Task<TFirst> loser,
        Task<TSecond> winner,
        Exception? cancellationFailure)
    {
        TSecond value = default!;
        Exception? winnerException = null;

        try
        {
            value = await winner.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            winnerException = exception;
        }

        var cleanupException = Combine(
            cancellationFailure,
            await ObserveLoserAsync(loser).ConfigureAwait(false));

        ThrowIfAny(winnerException, cleanupException);
        return EventWaitAnyResult<TFirst, TSecond>.FromSecond(value);
    }

    private static async Task<Exception?> ObserveLoserAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException) when (task.IsCanceled)
        {
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static Exception? Combine(Exception? first, Exception? second)
    {
        if (first is null)
        {
            return second;
        }

        if (second is null)
        {
            return first;
        }

        return new AggregateException(first, second);
    }

    private static void ThrowIfAny(Exception? winnerException, Exception? cleanupException)
    {
        if (winnerException is not null && cleanupException is not null)
        {
            throw new AggregateException(winnerException, cleanupException);
        }

        if (cleanupException is not null)
        {
            ExceptionDispatchInfo.Capture(cleanupException).Throw();
        }

        if (winnerException is not null)
        {
            ExceptionDispatchInfo.Capture(winnerException).Throw();
        }
    }
}
