using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace AsyncEventBridge
{

/// <summary>
/// Provides race-safe coordination between current state and event-driven state-change waits.
/// </summary>
public static class EventCondition
{
    /// <summary>
    /// Waits until a state snapshot satisfies the supplied predicate.
    /// </summary>
    /// <remarks>
    /// The change wait is armed before the state is read on every attempt. This closes the usual
    /// check-then-subscribe race: a transition that happens during setup is either visible in the
    /// state snapshot or captured by the already-armed wait.
    ///
    /// If the condition is already satisfied, the temporary change wait is cancelled and observed
    /// before this method completes. Spurious change notifications are supported by rearming and
    /// checking again. Wait factories should honor the supplied cancellation token so cleanup can
    /// complete deterministically.
    /// </remarks>
    public static async Task<TState> WaitUntilAsync<TState>(
        Func<TState> getState,
        Predicate<TState> predicate,
        Func<CancellationToken, Task> waitForChange,
        CancellationToken cancellationToken = default)
    {
        if (getState is null)
        {
            throw new ArgumentNullException(nameof(getState));
        }

        if (predicate is null)
        {
            throw new ArgumentNullException(nameof(predicate));
        }

        if (waitForChange is null)
        {
            throw new ArgumentNullException(nameof(waitForChange));
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var attemptCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            Task? changeTask = null;
            Exception? primaryException = null;
            TState state = default!;
            var satisfied = false;

            try
            {
                changeTask = waitForChange(attemptCancellation.Token);
                if (changeTask is null)
                {
                    throw new InvalidOperationException(
                        "The event condition change-wait factory returned null.");
                }

                state = getState();
                satisfied = predicate(state);

                if (!satisfied)
                {
                    try
                    {
                        await changeTask.ConfigureAwait(false);
                    }
                    catch
                    {
                        // The wait itself is the primary observed outcome. Do not observe it again as cleanup.
                        changeTask = null;
                        throw;
                    }

                    continue;
                }
            }
            catch (Exception exception)
            {
                primaryException = exception;
            }

            if (satisfied)
            {
                var cleanupErrors = await CancelAndObserveAsync(
                    attemptCancellation,
                    changeTask).ConfigureAwait(false);

                ThrowPrimaryWithCleanup(primaryException, cleanupErrors);
                return state;
            }

            if (primaryException is not null)
            {
                var cleanupErrors = await CancelAndObserveAsync(
                    attemptCancellation,
                    changeTask).ConfigureAwait(false);

                ThrowPrimaryWithCleanup(primaryException, cleanupErrors);
            }
        }
    }

    private static async Task<List<Exception>?> CancelAndObserveAsync(
        CancellationTokenSource cancellation,
        Task? task)
    {
        List<Exception>? cleanupErrors = null;

        try
        {
            cancellation.Cancel();
        }
        catch (Exception exception)
        {
            AddCleanupError(ref cleanupErrors, exception);
        }

        if (task is not null)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (task.IsCanceled)
            {
            }
            catch (Exception exception)
            {
                AddCleanupError(ref cleanupErrors, exception);
            }
        }

        return cleanupErrors;
    }

    private static void AddCleanupError(
        ref List<Exception>? cleanupErrors,
        Exception exception)
    {
        if (cleanupErrors is null)
        {
            cleanupErrors = new List<Exception>();
        }

        cleanupErrors.Add(exception);
    }

    private static void ThrowPrimaryWithCleanup(
        Exception? primaryException,
        IReadOnlyList<Exception>? cleanupErrors)
    {
        Exception? exception = primaryException;

        if (cleanupErrors is not null && cleanupErrors.Count > 0)
        {
            exception = CleanupExceptionPolicy.Combine(primaryException, cleanupErrors);
        }

        if (exception is not null)
        {
            ExceptionDispatchInfo.Capture(exception).Throw();
        }
    }
}

}
