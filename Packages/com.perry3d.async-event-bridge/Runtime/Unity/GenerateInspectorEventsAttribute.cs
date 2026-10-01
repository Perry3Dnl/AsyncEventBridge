using System;

namespace AsyncEventBridge.Unity
{

/// <summary>
/// Marks a partial <see cref="UnityEngine.MonoBehaviour"/> whose supported CLR events
/// should also be exposed as serialized UnityEvents in the Unity Inspector.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class GenerateInspectorEventsAttribute : Attribute
{
}
}
