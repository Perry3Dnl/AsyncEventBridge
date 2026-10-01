using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AsyncEventBridge.Unity.Tests
{

public sealed class InspectorEventGenerationTests
{
    [Test]
    public void GeneratedInspectorEvents_AreUnitySerialized()
    {
        var gameObject = new GameObject("AsyncEventBridge Inspector serialization test");
        try
        {
            var source = gameObject.AddComponent<InspectorEventSource>();
            var serializedObject = new SerializedObject(source);

            var group = serializedObject.FindProperty("AsyncEventBridgeEvents");
            Assert.That(group, Is.Not.Null, "Generated Inspector event group was not serialized.");

            var reading = group!.FindPropertyRelative("Reading");
            var tick = group.FindPropertyRelative("Tick");
            Assert.That(reading, Is.Not.Null, "Typed CLR event was not exposed as a serialized UnityEvent.");
            Assert.That(tick, Is.Not.Null, "Untyped CLR event was not exposed as a serialized UnityEvent.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(gameObject);
        }
    }
}

[GenerateInspectorEvents]
public sealed partial class InspectorEventSource : MonoBehaviour
{
    public event EventHandler<InspectorReadingEventArgs>? Reading;
    public event EventHandler? Tick;
}

[Serializable]
public sealed class InspectorReadingEventArgs : EventArgs
{
    public int Value;
}
}
