using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace UnityEngine
{
    [AsyncMethodBuilder(typeof(AwaitableMethodBuilder))]
    public readonly struct Awaitable
    {
        private readonly Task _task;

        public Awaitable(Task task) => _task = task;

        public TaskAwaiter GetAwaiter() => (_task ?? Task.CompletedTask).GetAwaiter();

        public static Awaitable MainThreadAsync() => new(Task.CompletedTask);
    }

    public struct AwaitableMethodBuilder
    {
        private AsyncTaskMethodBuilder _builder;

        public static AwaitableMethodBuilder Create() =>
            new() { _builder = AsyncTaskMethodBuilder.Create() };

        public Awaitable Task => new(_builder.Task);

        public void SetResult() => _builder.SetResult();

        public void SetException(Exception exception) => _builder.SetException(exception);

        public void SetStateMachine(IAsyncStateMachine stateMachine) =>
            _builder.SetStateMachine(stateMachine);

        public void Start<TStateMachine>(ref TStateMachine stateMachine)
            where TStateMachine : IAsyncStateMachine =>
            _builder.Start(ref stateMachine);

        public void AwaitOnCompleted<TAwaiter, TStateMachine>(
            ref TAwaiter awaiter,
            ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion
            where TStateMachine : IAsyncStateMachine =>
            _builder.AwaitOnCompleted(ref awaiter, ref stateMachine);

        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
            ref TAwaiter awaiter,
            ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion
            where TStateMachine : IAsyncStateMachine =>
            _builder.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine);
    }

    [AsyncMethodBuilder(typeof(AwaitableMethodBuilder<>))]
    public readonly struct Awaitable<T>
    {
        private readonly Task<T> _task;

        public Awaitable(Task<T> task) => _task = task;

        public TaskAwaiter<T> GetAwaiter() => _task.GetAwaiter();
    }

    public struct AwaitableMethodBuilder<T>
    {
        private AsyncTaskMethodBuilder<T> _builder;

        public static AwaitableMethodBuilder<T> Create() =>
            new() { _builder = AsyncTaskMethodBuilder<T>.Create() };

        public Awaitable<T> Task => new(_builder.Task);

        public void SetResult(T result) => _builder.SetResult(result);

        public void SetException(Exception exception) => _builder.SetException(exception);

        public void SetStateMachine(IAsyncStateMachine stateMachine) =>
            _builder.SetStateMachine(stateMachine);

        public void Start<TStateMachine>(ref TStateMachine stateMachine)
            where TStateMachine : IAsyncStateMachine =>
            _builder.Start(ref stateMachine);

        public void AwaitOnCompleted<TAwaiter, TStateMachine>(
            ref TAwaiter awaiter,
            ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion
            where TStateMachine : IAsyncStateMachine =>
            _builder.AwaitOnCompleted(ref awaiter, ref stateMachine);

        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
            ref TAwaiter awaiter,
            ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion
            where TStateMachine : IAsyncStateMachine =>
            _builder.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine);
    }

    public sealed class AwaitableCompletionSource<T>
    {
        private readonly TaskCompletionSource<T> _source =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Awaitable<T> Awaitable => new(_source.Task);

        public void SetCanceled() => _source.SetCanceled();

        public bool TrySetCanceled() => _source.TrySetCanceled();

        public void SetException(Exception exception) => _source.SetException(exception);

        public bool TrySetException(Exception exception) => _source.TrySetException(exception);

        public bool TrySetResult(ref T result) => _source.TrySetResult(result);
    }

    public class MonoBehaviour
    {
        public CancellationToken destroyCancellationToken => default;

        public static bool operator ==(MonoBehaviour? left, MonoBehaviour? right) =>
            ReferenceEquals(left, right);

        public static bool operator !=(MonoBehaviour? left, MonoBehaviour? right) =>
            !ReferenceEquals(left, right);

        public override bool Equals(object? obj) => ReferenceEquals(this, obj);

        public override int GetHashCode() => base.GetHashCode();
    }

    public static class Application
    {
        public static CancellationToken exitCancellationToken => default;
    }
}

namespace UnityEngine.Events
{
    public delegate void UnityAction();
    public delegate void UnityAction<T0>(T0 arg0);
    public delegate void UnityAction<T0, T1>(T0 arg0, T1 arg1);
    public delegate void UnityAction<T0, T1, T2>(T0 arg0, T1 arg1, T2 arg2);
    public delegate void UnityAction<T0, T1, T2, T3>(T0 arg0, T1 arg1, T2 arg2, T3 arg3);

    public class UnityEvent
    {
        public void AddListener(UnityAction listener) { }
        public void RemoveListener(UnityAction listener) { }
        public void Invoke() { }
    }

    public class UnityEvent<T0>
    {
        public void AddListener(UnityAction<T0> listener) { }
        public void RemoveListener(UnityAction<T0> listener) { }
        public void Invoke(T0 arg0) { }
    }

    public class UnityEvent<T0, T1>
    {
        public void AddListener(UnityAction<T0, T1> listener) { }
        public void RemoveListener(UnityAction<T0, T1> listener) { }
        public void Invoke(T0 arg0, T1 arg1) { }
    }

    public class UnityEvent<T0, T1, T2>
    {
        public void AddListener(UnityAction<T0, T1, T2> listener) { }
        public void RemoveListener(UnityAction<T0, T1, T2> listener) { }
        public void Invoke(T0 arg0, T1 arg1, T2 arg2) { }
    }

    public class UnityEvent<T0, T1, T2, T3>
    {
        public void AddListener(UnityAction<T0, T1, T2, T3> listener) { }
        public void RemoveListener(UnityAction<T0, T1, T2, T3> listener) { }
        public void Invoke(T0 arg0, T1 arg1, T2 arg2, T3 arg3) { }
    }
}
