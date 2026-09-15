using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

namespace AsyncEventBridge.Unity;

/// <summary>
/// Publishes task and async-stream outcomes to UnityEvents on the Unity main thread.
/// </summary>
public static class UnityAsyncBridge
{
    public static Awaitable PublishAsync(
        Task task,
        UnityEvent completed,
        UnityEvent<string>? faulted = null,
        UnityEvent? cancelled = null,
        CancellationToken cancellationToken = default)
    {
        if (task is null) throw new ArgumentNullException(nameof(task));
        if (completed is null) throw new ArgumentNullException(nameof(completed));
        return PublishTaskAsync(task, completed, faulted, cancelled, cancellationToken);
    }

    public static async Awaitable PublishAsync(
        Task task,
        MonoBehaviour owner,
        UnityEvent completed,
        UnityEvent<string>? faulted = null,
        UnityEvent? cancelled = null,
        CancellationToken cancellationToken = default)
    {
        if (owner == null) throw new ArgumentNullException(nameof(owner));
        var destroyToken = owner.destroyCancellationToken;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            destroyToken,
            Application.exitCancellationToken,
            cancellationToken);
        await PublishTaskAsync(task, completed, faulted, cancelled, linked.Token);
    }

    public static Awaitable PublishAsync<T>(
        Task<T> task,
        UnityEvent<T> completed,
        UnityEvent<string>? faulted = null,
        UnityEvent? cancelled = null,
        CancellationToken cancellationToken = default)
    {
        if (task is null) throw new ArgumentNullException(nameof(task));
        if (completed is null) throw new ArgumentNullException(nameof(completed));
        return PublishTaskAsync(task, completed, faulted, cancelled, cancellationToken);
    }

    public static async Awaitable PublishAsync<T>(
        Task<T> task,
        MonoBehaviour owner,
        UnityEvent<T> completed,
        UnityEvent<string>? faulted = null,
        UnityEvent? cancelled = null,
        CancellationToken cancellationToken = default)
    {
        if (owner == null) throw new ArgumentNullException(nameof(owner));
        var destroyToken = owner.destroyCancellationToken;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            destroyToken,
            Application.exitCancellationToken,
            cancellationToken);
        await PublishTaskAsync(task, completed, faulted, cancelled, linked.Token);
    }

    public static async Awaitable PublishAsync<T>(
        IAsyncEnumerable<T> source,
        UnityEvent<T> next,
        UnityEvent? completed = null,
        UnityEvent<string>? faulted = null,
        CancellationToken cancellationToken = default)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (next is null) throw new ArgumentNullException(nameof(next));

        try
        {
            await foreach (var value in source.WithCancellation(cancellationToken))
            {
                await Awaitable.MainThreadAsync();
                if (cancellationToken.IsCancellationRequested) return;
                next.Invoke(value);
            }

            await Awaitable.MainThreadAsync();
            if (!cancellationToken.IsCancellationRequested) completed?.Invoke();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await Awaitable.MainThreadAsync();
            if (!cancellationToken.IsCancellationRequested) faulted?.Invoke(exception.ToString());
        }
    }

    public static async Awaitable PublishAsync<T>(
        IAsyncEnumerable<T> source,
        MonoBehaviour owner,
        UnityEvent<T> next,
        UnityEvent? completed = null,
        UnityEvent<string>? faulted = null,
        CancellationToken cancellationToken = default)
    {
        if (owner == null) throw new ArgumentNullException(nameof(owner));
        var destroyToken = owner.destroyCancellationToken;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            destroyToken,
            Application.exitCancellationToken,
            cancellationToken);
        await PublishAsync(source, next, completed, faulted, linked.Token);
    }

    private static async Awaitable PublishTaskAsync(
        Task task,
        UnityEvent completed,
        UnityEvent<string>? faulted,
        UnityEvent? cancelled,
        CancellationToken cancellationToken)
    {
        try
        {
            var outcome = await ObserveAsync(task, cancellationToken);
            await Awaitable.MainThreadAsync();
            if (cancellationToken.IsCancellationRequested) return;
            switch (outcome.Kind)
            {
                case TaskOutcomeKind.Completed:
                    completed.Invoke();
                    break;
                case TaskOutcomeKind.Cancelled:
                    cancelled?.Invoke();
                    break;
                case TaskOutcomeKind.Faulted:
                    faulted?.Invoke(outcome.Exception!.ToString());
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static async Awaitable PublishTaskAsync<T>(
        Task<T> task,
        UnityEvent<T> completed,
        UnityEvent<string>? faulted,
        UnityEvent? cancelled,
        CancellationToken cancellationToken)
    {
        try
        {
            var outcome = await ObserveAsync(task, cancellationToken);
            await Awaitable.MainThreadAsync();
            if (cancellationToken.IsCancellationRequested) return;
            switch (outcome.Kind)
            {
                case TaskOutcomeKind.Completed:
                    completed.Invoke(outcome.Value!);
                    break;
                case TaskOutcomeKind.Cancelled:
                    cancelled?.Invoke();
                    break;
                case TaskOutcomeKind.Faulted:
                    faulted?.Invoke(outcome.Exception!.ToString());
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static async Task<TaskOutcome> ObserveAsync(Task task, CancellationToken cancellationToken)
    {
        await WaitForTaskOrCancellationAsync(task, cancellationToken).ConfigureAwait(false);
        if (task.IsCanceled) return TaskOutcome.Cancelled();
        if (task.IsFaulted) return TaskOutcome.Faulted(task.Exception?.InnerException ?? task.Exception!);
        return TaskOutcome.Completed();
    }

    private static async Task<TaskOutcome<T>> ObserveAsync<T>(Task<T> task, CancellationToken cancellationToken)
    {
        await WaitForTaskOrCancellationAsync(task, cancellationToken).ConfigureAwait(false);
        if (task.IsCanceled) return TaskOutcome<T>.Cancelled();
        if (task.IsFaulted) return TaskOutcome<T>.Faulted(task.Exception?.InnerException ?? task.Exception!);
        return TaskOutcome<T>.Completed(task.Result);
    }

    private static async Task WaitForTaskOrCancellationAsync(Task task, CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled)
        {
            try { await task.ConfigureAwait(false); }
            catch { }
            return;
        }

        var cancellationSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (cancellationToken.Register(() => cancellationSignal.TrySetResult(true)))
        {
            var winner = await Task.WhenAny(task, cancellationSignal.Task).ConfigureAwait(false);
            if (!ReferenceEquals(winner, task)) cancellationToken.ThrowIfCancellationRequested();
        }

        try { await task.ConfigureAwait(false); }
        catch { }
    }

    private enum TaskOutcomeKind { Completed, Cancelled, Faulted }

    private readonly struct TaskOutcome
    {
        private TaskOutcome(TaskOutcomeKind kind, Exception? exception) { Kind = kind; Exception = exception; }
        internal TaskOutcomeKind Kind { get; }
        internal Exception? Exception { get; }
        internal static TaskOutcome Completed() => new(TaskOutcomeKind.Completed, null);
        internal static TaskOutcome Cancelled() => new(TaskOutcomeKind.Cancelled, null);
        internal static TaskOutcome Faulted(Exception exception) => new(TaskOutcomeKind.Faulted, exception);
    }

    private readonly struct TaskOutcome<T>
    {
        private TaskOutcome(TaskOutcomeKind kind, T? value, Exception? exception) { Kind = kind; Value = value; Exception = exception; }
        internal TaskOutcomeKind Kind { get; }
        internal T? Value { get; }
        internal Exception? Exception { get; }
        internal static TaskOutcome<T> Completed(T value) => new(TaskOutcomeKind.Completed, value, null);
        internal static TaskOutcome<T> Cancelled() => new(TaskOutcomeKind.Cancelled, default, null);
        internal static TaskOutcome<T> Faulted(Exception exception) => new(TaskOutcomeKind.Faulted, default, exception);
    }
}
