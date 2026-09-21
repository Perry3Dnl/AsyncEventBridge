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

    /// <summary>
    /// Waits until any indexed cancellable asynchronous event wait completes.
    /// </summary>
    /// <remarks>
    /// All losing waits are cancelled and observed before this method completes. The returned index is zero-based and corresponds to the input list.
    /// </remarks>
    public static async Task<EventWaitAnyResult<T>> WaitAnyAsync<T>(
        IReadOnlyList<Func<CancellationToken, Task<T>>> waits,
        CancellationToken cancellationToken = default)
    {
        ValidateWaitList(waits);

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var tasks = new Task<T>[waits.Count];
        var startedTasks = new List<Task>(waits.Count);

        try
        {
            for (var index = 0; index < waits.Count; index++)
            {
                var task = waits[index](linkedCancellation.Token)
                    ?? throw new InvalidOperationException($"The event wait factory at index {index} returned null.");
                tasks[index] = task;
                startedTasks.Add(task);
            }
        }
        catch (Exception startException)
        {
            var cleanupException = await CancelAndObserveAsync(linkedCancellation, startedTasks).ConfigureAwait(false);
            ThrowPrimaryWithCleanup(startException, cleanupException);
            throw;
        }

        var winner = await Task.WhenAny(tasks).ConfigureAwait(false);
        var winnerIndex = FindTaskIndex(tasks, winner);
        Exception? winnerException = null;
        T value = default!;

        try
        {
            value = await winner.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            winnerException = exception;
        }

        var cleanupFailure = await CancelAndObserveAsync(linkedCancellation, startedTasks, winner).ConfigureAwait(false);
        ThrowIfAny(winnerException, cleanupFailure);

        return new EventWaitAnyResult<T>(winnerIndex, value);
    }

    /// <summary>
    /// Waits until both cancellable asynchronous event waits complete successfully.
    /// </summary>
    /// <remarks>
    /// If either wait faults or is cancelled before the other completes, the remaining wait is cancelled and observed before this method completes.
    /// </remarks>
    public static async Task<EventWaitAllResult<TFirst, TSecond>> WaitAllAsync<TFirst, TSecond>(
        Func<CancellationToken, Task<TFirst>> firstWait,
        Func<CancellationToken, Task<TSecond>> secondWait,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(firstWait);
        ArgumentNullException.ThrowIfNull(secondWait);

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<TFirst>? firstTask = null;
        Task<TSecond>? secondTask = null;
        var startedTasks = new List<Task>(2);

        try
        {
            firstTask = firstWait(linkedCancellation.Token)
                ?? throw new InvalidOperationException("The first event wait factory returned null.");
            startedTasks.Add(firstTask);

            secondTask = secondWait(linkedCancellation.Token)
                ?? throw new InvalidOperationException("The second event wait factory returned null.");
            startedTasks.Add(secondTask);
        }
        catch (Exception startException)
        {
            var cleanupException = await CancelAndObserveAsync(linkedCancellation, startedTasks).ConfigureAwait(false);
            ThrowPrimaryWithCleanup(startException, cleanupException);
            throw;
        }

        var first = default(TFirst)!;
        var second = default(TSecond)!;
        var completed = await Task.WhenAny(firstTask, secondTask).ConfigureAwait(false);

        if (ReferenceEquals(completed, firstTask))
        {
            try
            {
                first = await firstTask.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                var cleanupException = await CancelAndObserveAsync(linkedCancellation, startedTasks, firstTask).ConfigureAwait(false);
                ThrowPrimaryWithCleanup(exception, cleanupException);
            }

            second = await secondTask.ConfigureAwait(false);
        }
        else
        {
            try
            {
                second = await secondTask.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                var cleanupException = await CancelAndObserveAsync(linkedCancellation, startedTasks, secondTask).ConfigureAwait(false);
                ThrowPrimaryWithCleanup(exception, cleanupException);
            }

            first = await firstTask.ConfigureAwait(false);
        }

        return new EventWaitAllResult<TFirst, TSecond>(first, second);
    }

    /// <summary>
    /// Waits until all indexed cancellable asynchronous event waits complete successfully.
    /// </summary>
    /// <remarks>
    /// Results preserve input ordering. The first observed fault or cancellation cancels and observes all remaining waits before this method completes.
    /// </remarks>
    public static async Task<T[]> WaitAllAsync<T>(
        IReadOnlyList<Func<CancellationToken, Task<T>>> waits,
        CancellationToken cancellationToken = default)
    {
        ValidateWaitList(waits);

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var tasks = new Task<T>[waits.Count];
        var startedTasks = new List<Task>(waits.Count);

        try
        {
            for (var index = 0; index < waits.Count; index++)
            {
                var task = waits[index](linkedCancellation.Token)
                    ?? throw new InvalidOperationException($"The event wait factory at index {index} returned null.");
                tasks[index] = task;
                startedTasks.Add(task);
            }
        }
        catch (Exception startException)
        {
            var cleanupException = await CancelAndObserveAsync(linkedCancellation, startedTasks).ConfigureAwait(false);
            ThrowPrimaryWithCleanup(startException, cleanupException);
            throw;
        }

        var results = new T[tasks.Length];
        var pendingIndices = Enumerable.Range(0, tasks.Length).ToList();

        while (pendingIndices.Count > 0)
        {
            var pendingTasks = new Task<T>[pendingIndices.Count];
            for (var pendingIndex = 0; pendingIndex < pendingIndices.Count; pendingIndex++)
            {
                pendingTasks[pendingIndex] = tasks[pendingIndices[pendingIndex]];
            }

            var completed = await Task.WhenAny(pendingTasks).ConfigureAwait(false);
            T value;

            try
            {
                value = await completed.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                var cleanupException = await CancelAndObserveAsync(linkedCancellation, startedTasks, completed).ConfigureAwait(false);
                ThrowPrimaryWithCleanup(exception, cleanupException);
                throw;
            }

            for (var pendingIndex = pendingIndices.Count - 1; pendingIndex >= 0; pendingIndex--)
            {
                var resultIndex = pendingIndices[pendingIndex];
                if (!ReferenceEquals(tasks[resultIndex], completed))
                {
                    continue;
                }

                results[resultIndex] = value;
                pendingIndices.RemoveAt(pendingIndex);
            }
        }

        return results;
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

    private static void ValidateWaitList<T>(IReadOnlyList<Func<CancellationToken, Task<T>>> waits)
    {
        ArgumentNullException.ThrowIfNull(waits);

        if (waits.Count == 0)
        {
            throw new ArgumentException("At least one event wait factory is required.", nameof(waits));
        }

        for (var index = 0; index < waits.Count; index++)
        {
            if (waits[index] is null)
            {
                throw new ArgumentException($"The event wait factory at index {index} is null.", nameof(waits));
            }
        }
    }

    private static int FindTaskIndex<T>(IReadOnlyList<Task<T>> tasks, Task<T> winner)
    {
        for (var index = 0; index < tasks.Count; index++)
        {
            if (ReferenceEquals(tasks[index], winner))
            {
                return index;
            }
        }

        throw new InvalidOperationException("The completed event wait was not present in the input set.");
    }

    private static async Task<Exception?> CancelAndObserveAsync(
        CancellationTokenSource cancellation,
        IEnumerable<Task> tasks,
        Task? excludedTask = null)
    {
        Exception? cleanupException = null;

        try
        {
            cancellation.Cancel();
        }
        catch (Exception exception)
        {
            cleanupException = exception;
        }

        foreach (var task in tasks)
        {
            if (ReferenceEquals(task, excludedTask))
            {
                continue;
            }

            cleanupException = Combine(
                cleanupException,
                await ObserveLoserAsync(task).ConfigureAwait(false));
        }

        return cleanupException;
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
        catch (AggregateException exception)
            when (exception.InnerExceptions.Count > 1 &&
                  exception.InnerExceptions[0] is OperationCanceledException)
        {
            if (exception.InnerExceptions.Count == 2)
            {
                return exception.InnerExceptions[1];
            }

            return new AggregateException(exception.InnerExceptions.Skip(1));
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

    private static void ThrowPrimaryWithCleanup(Exception primaryException, Exception? cleanupException)
    {
        if (cleanupException is not null)
        {
            throw new AggregateException(primaryException, cleanupException);
        }

        ExceptionDispatchInfo.Capture(primaryException).Throw();
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
