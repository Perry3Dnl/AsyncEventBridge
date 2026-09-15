using System;
using System.Collections;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.TestTools;

namespace AsyncEventBridge.Unity.Tests;

public sealed class UnityEventIntegrationTests
{
    [UnityTest]
    public IEnumerator WaitAsync_UsesPredicateAndReturnsMatchingValue()
    {
        var source = new UnityEvent<int>();
        var observation = AwaitableObservation<int>.Start(
            source.WaitAsync(value => value == 42, timeout: TimeSpan.FromSeconds(2)));

        source.Invoke(1);
        Assert.That(observation.IsCompleted, Is.False);

        source.Invoke(42);
        yield return observation.WaitForCompletion();

        observation.ThrowIfFaulted();
        Assert.That(observation.Result, Is.EqualTo(42));
    }

    [UnityTest]
    public IEnumerator WaitAsync_OwnerDestructionCancelsAndRemovesListener()
    {
        var source = new UnityEvent();
        var gameObject = new GameObject("AsyncEventBridge test owner");
        var owner = gameObject.AddComponent<TestOwner>();
        var observation = AwaitableObservation.Start(source.WaitAsync(owner));

        UnityEngine.Object.Destroy(gameObject);
        yield return null;
        yield return observation.WaitForCompletion();

        Assert.That(observation.Exception, Is.TypeOf<OperationCanceledException>());

        // The runtime listener must already be removed; invoking the event after destruction must be harmless.
        source.Invoke();
    }

    [UnityTest]
    public IEnumerator WaitAsync_TimeoutFaultsOnUnityMainThread()
    {
        var source = new UnityEvent();
        var mainThreadId = Thread.CurrentThread.ManagedThreadId;
        var completionThreadId = -1;
        var observation = AwaitableObservation.Start(
            source.WaitAsync(timeout: TimeSpan.FromMilliseconds(20)),
            () => completionThreadId = Thread.CurrentThread.ManagedThreadId);

        yield return observation.WaitForCompletion();

        Assert.That(observation.Exception, Is.TypeOf<TimeoutException>());
        Assert.That(completionThreadId, Is.EqualTo(mainThreadId));
    }

    [UnityTest]
    public IEnumerator AsAsyncEnumerable_BuffersEventsInsteadOfLosingEventsBetweenMoves()
    {
        var source = new UnityEvent<int>();
        var stream = source.AsAsyncEnumerable();
        var enumerator = stream.GetAsyncEnumerator();

        var firstMove = enumerator.MoveNextAsync().AsTask();
        source.Invoke(10);
        source.Invoke(20);

        yield return WaitForTask(firstMove);
        Assert.That(firstMove.Result, Is.True);
        Assert.That(enumerator.Current, Is.EqualTo(10));

        var secondMove = enumerator.MoveNextAsync().AsTask();
        yield return WaitForTask(secondMove);
        Assert.That(secondMove.Result, Is.True);
        Assert.That(enumerator.Current, Is.EqualTo(20));

        yield return WaitForTask(enumerator.DisposeAsync().AsTask());
    }

    [UnityTest]
    public IEnumerator PublishAsync_TaskCompletionInvokesUnityEventOnMainThread()
    {
        var mainThreadId = Thread.CurrentThread.ManagedThreadId;
        var callbackThreadId = -1;
        var receivedValue = -1;
        var completed = new UnityEvent<int>();
        completed.AddListener(value =>
        {
            receivedValue = value;
            callbackThreadId = Thread.CurrentThread.ManagedThreadId;
        });

        var source = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observation = AwaitableObservation.Start(UnityAsyncBridge.PublishAsync(source.Task, completed));

        Task.Run(() => source.TrySetResult(7));
        yield return observation.WaitForCompletion();

        observation.ThrowIfFaulted();
        Assert.That(receivedValue, Is.EqualTo(7));
        Assert.That(callbackThreadId, Is.EqualTo(mainThreadId));
    }

    private static IEnumerator WaitForTask(Task task, int maxFrames = 600)
    {
        for (var frame = 0; !task.IsCompleted && frame < maxFrames; frame++)
        {
            yield return null;
        }

        Assert.That(task.IsCompleted, Is.True, "Async operation did not complete within the frame budget.");
        if (task.IsFaulted)
        {
            ExceptionDispatchInfo.Capture(task.Exception?.InnerException ?? task.Exception!).Throw();
        }
    }

    private sealed class TestOwner : MonoBehaviour
    {
    }

    private sealed class AwaitableObservation
    {
        private Exception? _exception;
        private bool _isCompleted;

        internal bool IsCompleted => _isCompleted;
        internal Exception? Exception => _exception;

        internal static AwaitableObservation Start(Awaitable awaitable, Action? onCompleted = null)
        {
            var observation = new AwaitableObservation();
            observation.Observe(awaitable, onCompleted);
            return observation;
        }

        internal IEnumerator WaitForCompletion(int maxFrames = 600)
        {
            for (var frame = 0; !_isCompleted && frame < maxFrames; frame++)
            {
                yield return null;
            }

            Assert.That(_isCompleted, Is.True, "Awaitable did not complete within the frame budget.");
        }

        internal void ThrowIfFaulted()
        {
            if (_exception is not null)
            {
                ExceptionDispatchInfo.Capture(_exception).Throw();
            }
        }

        private async void Observe(Awaitable awaitable, Action? onCompleted)
        {
            try
            {
                await awaitable;
            }
            catch (Exception exception)
            {
                _exception = exception;
            }
            finally
            {
                onCompleted?.Invoke();
                _isCompleted = true;
            }
        }
    }

    private sealed class AwaitableObservation<T>
    {
        private Exception? _exception;
        private bool _isCompleted;
        private T? _result;

        internal bool IsCompleted => _isCompleted;
        internal Exception? Exception => _exception;
        internal T Result => _result!;

        internal static AwaitableObservation<T> Start(Awaitable<T> awaitable)
        {
            var observation = new AwaitableObservation<T>();
            observation.Observe(awaitable);
            return observation;
        }

        internal IEnumerator WaitForCompletion(int maxFrames = 600)
        {
            for (var frame = 0; !_isCompleted && frame < maxFrames; frame++)
            {
                yield return null;
            }

            Assert.That(_isCompleted, Is.True, "Awaitable did not complete within the frame budget.");
        }

        internal void ThrowIfFaulted()
        {
            if (_exception is not null)
            {
                ExceptionDispatchInfo.Capture(_exception).Throw();
            }
        }

        private async void Observe(Awaitable<T> awaitable)
        {
            try
            {
                _result = await awaitable;
            }
            catch (Exception exception)
            {
                _exception = exception;
            }
            finally
            {
                _isCompleted = true;
            }
        }
    }
}
