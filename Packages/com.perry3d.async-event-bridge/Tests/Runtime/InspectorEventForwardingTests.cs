using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.TestTools;

namespace AsyncEventBridge.Unity.Tests
{

public sealed class InspectorEventForwardingTests
{
    [UnityTest]
    public IEnumerator GeneratedInspectorEvent_ForwardsClrOccurrence()
    {
        var gameObject = new GameObject("AsyncEventBridge Inspector forwarding test");
        try
        {
            var source = gameObject.AddComponent<InspectorForwardingSource>();

            // RequireComponent adds the AEB host. Give its Start pass one frame to bind
            // in case the host's OnEnable ran before the source component was added.
            yield return null;

            var groupField = typeof(InspectorForwardingSource).GetField(
                "AsyncEventBridgeEvents",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(groupField, Is.Not.Null);

            var group = groupField!.GetValue(source);
            Assert.That(group, Is.Not.Null);

            var readingField = group!.GetType().GetField(
                "Reading",
                BindingFlags.Instance | BindingFlags.Public);
            Assert.That(readingField, Is.Not.Null);

            var inspectorEvent = (UnityEvent<InspectorForwardingEventArgs>)readingField!.GetValue(group)!;
            var received = -1;
            inspectorEvent.AddListener(args => received = args.Value);

            source.RaiseReading(42);

            Assert.That(received, Is.EqualTo(42));
            Assert.That(gameObject.GetComponent<AsyncEventBridgeInspectorHost>(), Is.Not.Null);
        }
        finally
        {
            UnityEngine.Object.Destroy(gameObject);
        }
    }
}

[GenerateInspectorEvents]
public sealed partial class InspectorForwardingSource : MonoBehaviour
{
    public event EventHandler<InspectorForwardingEventArgs>? Reading;

    public void RaiseReading(int value)
    {
        Reading?.Invoke(this, new InspectorForwardingEventArgs(value));
    }
}

[Serializable]
public sealed class InspectorForwardingEventArgs : EventArgs
{
    public InspectorForwardingEventArgs(int value)
    {
        Value = value;
    }

    public int Value;
}
}
