using System;
using System.Collections;
using System.Threading;
using NUnit.Framework;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.TestTools;

namespace AsyncEventBridge.Unity.Tests;

public sealed class UnityEventInspectorTests
{
    [UnityTest]
    public IEnumerator WaitAsync_DoesNotModifyInspectorPersistentListeners()
    {
        var source = new UnityEvent();
        var receiver = ScriptableObject.CreateInstance<PersistentReceiver>();
        UnityEventTools.AddPersistentListener(source, receiver.OnInvoked);
        Assert.That(source.GetPersistentEventCount(), Is.EqualTo(1));

        using var cancellation = new CancellationTokenSource();
        var completed = false;
        Exception? error = null;
        Observe(source.WaitAsync(cancellationToken: cancellation.Token));
        cancellation.Cancel();

        for (var frame = 0; !completed && frame < 60; frame++)
        {
            yield return null;
        }

        Assert.That(completed, Is.True);
        Assert.That(error, Is.TypeOf<OperationCanceledException>());
        Assert.That(source.GetPersistentEventCount(), Is.EqualTo(1));

        source.Invoke();
        Assert.That(receiver.InvocationCount, Is.EqualTo(1));

        UnityEngine.Object.DestroyImmediate(receiver);

        async void Observe(Awaitable awaitable)
        {
            try
            {
                await awaitable;
            }
            catch (Exception exception)
            {
                error = exception;
            }
            finally
            {
                completed = true;
            }
        }
    }

    private sealed class PersistentReceiver : ScriptableObject
    {
        internal int InvocationCount { get; private set; }

        public void OnInvoked()
        {
            InvocationCount++;
        }
    }
}
