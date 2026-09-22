using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AsyncEventBridge.Unity;
using UnityEngine;
using UnityEngine.Events;

namespace AsyncEventBridge.UnityCompileSmoke
{
    internal static class UnityPublicApiContract
    {
        internal static void Compile(
            MonoBehaviour owner,
            Task task,
            Task<int> taskOfInt,
            IAsyncEnumerable<int> asyncValues,
            UnityEvent signal,
            UnityEvent<int> one,
            UnityEvent<int, string> two,
            UnityEvent<int, string, bool> three,
            UnityEvent<int, string, bool, double> four,
            CancellationToken cancellationToken)
        {
            Action<EventHandler> subscribe = _ => { };
            Action<EventHandler> unsubscribe = _ => { };
            Action<EventHandler<SmokeEventArgs>> subscribeGeneric = _ => { };
            Action<EventHandler<SmokeEventArgs>> unsubscribeGeneric = _ => { };

            Awaitable<EventArgs> lowLevel =
                UnityEventAwaiter.WaitAsync(
                    subscribe,
                    unsubscribe,
                    predicate: null,
                    cancellationToken,
                    timeout: TimeSpan.FromSeconds(1));

            Awaitable<EventArgs> ownedLowLevel =
                UnityEventAwaiter.WaitAsync(
                    owner,
                    subscribe,
                    unsubscribe,
                    predicate: null,
                    cancellationToken,
                    timeout: TimeSpan.FromSeconds(1));

            Awaitable<SmokeEventArgs> genericLowLevel =
                UnityEventAwaiter.WaitAsync<SmokeEventArgs>(
                    subscribeGeneric,
                    unsubscribeGeneric,
                    predicate: _ => true,
                    cancellationToken,
                    timeout: TimeSpan.FromSeconds(1));

            Awaitable<SmokeEventArgs> ownedGenericLowLevel =
                UnityEventAwaiter.WaitAsync<SmokeEventArgs>(
                    owner,
                    subscribeGeneric,
                    unsubscribeGeneric,
                    predicate: _ => true,
                    cancellationToken,
                    timeout: TimeSpan.FromSeconds(1));

            Awaitable wait0 = signal.WaitAsync(cancellationToken, TimeSpan.FromSeconds(1));
            Awaitable ownedWait0 = signal.WaitAsync(owner, cancellationToken, TimeSpan.FromSeconds(1));

            Awaitable<int> wait1 =
                one.WaitAsync(
                    predicate: value => value >= 0,
                    cancellationToken,
                    timeout: TimeSpan.FromSeconds(1));

            Awaitable<int> ownedWait1 =
                one.WaitAsync(
                    owner,
                    predicate: value => value >= 0,
                    cancellationToken,
                    timeout: TimeSpan.FromSeconds(1));

            Awaitable<(int, string)> wait2 =
                two.WaitAsync(
                    predicate: (left, right) => left >= 0 && right != null,
                    cancellationToken,
                    timeout: TimeSpan.FromSeconds(1));

            Awaitable<(int, string)> ownedWait2 =
                two.WaitAsync(
                    owner,
                    predicate: (left, right) => left >= 0 && right != null,
                    cancellationToken,
                    timeout: TimeSpan.FromSeconds(1));

            Awaitable<(int, string, bool)> wait3 =
                three.WaitAsync(
                    predicate: (a, b, c) => a >= 0 && b != null && c,
                    cancellationToken,
                    timeout: TimeSpan.FromSeconds(1));

            Awaitable<(int, string, bool)> ownedWait3 =
                three.WaitAsync(
                    owner,
                    predicate: (a, b, c) => a >= 0 && b != null && c,
                    cancellationToken,
                    timeout: TimeSpan.FromSeconds(1));

            Awaitable<(int, string, bool, double)> wait4 =
                four.WaitAsync(
                    predicate: (a, b, c, d) => a >= 0 && b != null && c && d >= 0,
                    cancellationToken,
                    timeout: TimeSpan.FromSeconds(1));

            Awaitable<(int, string, bool, double)> ownedWait4 =
                four.WaitAsync(
                    owner,
                    predicate: (a, b, c, d) => a >= 0 && b != null && c && d >= 0,
                    cancellationToken,
                    timeout: TimeSpan.FromSeconds(1));

            IAsyncEnumerable<EventArgs> stream0 =
                signal.AsAsyncEnumerable(
                    options: null,
                    cancellationToken);

            IAsyncEnumerable<EventArgs> ownedStream0 =
                signal.AsAsyncEnumerable(
                    owner,
                    options: null,
                    cancellationToken);

            IAsyncEnumerable<int> stream1 =
                one.AsAsyncEnumerable(
                    predicate: value => value >= 0,
                    options: null,
                    cancellationToken);

            IAsyncEnumerable<int> ownedStream1 =
                one.AsAsyncEnumerable(
                    owner,
                    predicate: value => value >= 0,
                    options: null,
                    cancellationToken);

            IAsyncEnumerable<(int, string)> stream2 =
                two.AsAsyncEnumerable(
                    predicate: (left, right) => left >= 0 && right != null,
                    options: null,
                    cancellationToken);

            IAsyncEnumerable<(int, string)> ownedStream2 =
                two.AsAsyncEnumerable(
                    owner,
                    predicate: (left, right) => left >= 0 && right != null,
                    options: null,
                    cancellationToken);

            IAsyncEnumerable<(int, string, bool)> stream3 =
                three.AsAsyncEnumerable(
                    predicate: (a, b, c) => a >= 0 && b != null && c,
                    options: null,
                    cancellationToken);

            IAsyncEnumerable<(int, string, bool)> ownedStream3 =
                three.AsAsyncEnumerable(
                    owner,
                    predicate: (a, b, c) => a >= 0 && b != null && c,
                    options: null,
                    cancellationToken);

            IAsyncEnumerable<(int, string, bool, double)> stream4 =
                four.AsAsyncEnumerable(
                    predicate: (a, b, c, d) => a >= 0 && b != null && c && d >= 0,
                    options: null,
                    cancellationToken);

            IAsyncEnumerable<(int, string, bool, double)> ownedStream4 =
                four.AsAsyncEnumerable(
                    owner,
                    predicate: (a, b, c, d) => a >= 0 && b != null && c && d >= 0,
                    options: null,
                    cancellationToken);

            var completed = new UnityEvent();
            var completedInt = new UnityEvent<int>();
            var faulted = new UnityEvent<string>();
            var cancelled = new UnityEvent();

            Awaitable publishTask =
                UnityAsyncBridge.PublishAsync(
                    task,
                    completed,
                    faulted,
                    cancelled,
                    cancellationToken);

            Awaitable ownedPublishTask =
                UnityAsyncBridge.PublishAsync(
                    task,
                    owner,
                    completed,
                    faulted,
                    cancelled,
                    cancellationToken);

            Awaitable publishTaskOfInt =
                UnityAsyncBridge.PublishAsync(
                    taskOfInt,
                    completedInt,
                    faulted,
                    cancelled,
                    cancellationToken);

            Awaitable ownedPublishTaskOfInt =
                UnityAsyncBridge.PublishAsync(
                    taskOfInt,
                    owner,
                    completedInt,
                    faulted,
                    cancelled,
                    cancellationToken);

            Awaitable publishStream =
                UnityAsyncBridge.PublishAsync(
                    asyncValues,
                    completedInt,
                    completed,
                    faulted,
                    cancellationToken);

            Awaitable ownedPublishStream =
                UnityAsyncBridge.PublishAsync(
                    asyncValues,
                    owner,
                    completedInt,
                    completed,
                    faulted,
                    cancellationToken);

            _ = lowLevel;
            _ = ownedLowLevel;
            _ = genericLowLevel;
            _ = ownedGenericLowLevel;
            _ = wait0;
            _ = ownedWait0;
            _ = wait1;
            _ = ownedWait1;
            _ = wait2;
            _ = ownedWait2;
            _ = wait3;
            _ = ownedWait3;
            _ = wait4;
            _ = ownedWait4;
            _ = stream0;
            _ = ownedStream0;
            _ = stream1;
            _ = ownedStream1;
            _ = stream2;
            _ = ownedStream2;
            _ = stream3;
            _ = ownedStream3;
            _ = stream4;
            _ = ownedStream4;
            _ = publishTask;
            _ = ownedPublishTask;
            _ = publishTaskOfInt;
            _ = ownedPublishTaskOfInt;
            _ = publishStream;
            _ = ownedPublishStream;
        }

        private sealed class SmokeEventArgs : EventArgs
        {
        }
    }
}
