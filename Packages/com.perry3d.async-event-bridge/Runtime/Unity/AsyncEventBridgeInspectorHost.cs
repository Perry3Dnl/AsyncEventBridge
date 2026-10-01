using UnityEngine;

namespace AsyncEventBridge.Unity
{

/// <summary>
/// Lifecycle host for generated Inspector event projections.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("")]
public sealed class AsyncEventBridgeInspectorHost : MonoBehaviour
{
    private void OnEnable()
    {
        ConnectAll();
    }

    private void Start()
    {
        // Covers components added together at runtime after this host's OnEnable.
        ConnectAll();
    }

    private void OnDisable()
    {
        DisconnectAll();
    }

    /// <summary>
    /// Rebinds generated Inspector event projections on this GameObject.
    /// Call this after adding an Inspector-enabled component dynamically at runtime.
    /// </summary>
    public void Refresh()
    {
        DisconnectAll();
        ConnectAll();
    }

    private void ConnectAll()
    {
        var behaviours = GetComponents<MonoBehaviour>();
        for (var index = 0; index < behaviours.Length; index++)
        {
            if (behaviours[index] is IAsyncEventBridgeInspectorSource source)
            {
                source.ConnectInspectorEvents();
            }
        }
    }

    private void DisconnectAll()
    {
        var behaviours = GetComponents<MonoBehaviour>();
        for (var index = 0; index < behaviours.Length; index++)
        {
            if (behaviours[index] is IAsyncEventBridgeInspectorSource source)
            {
                source.DisconnectInspectorEvents();
            }
        }
    }
}

/// <summary>
/// Implemented by Unity source generation for components that expose CLR events to Inspector listeners.
/// </summary>
public interface IAsyncEventBridgeInspectorSource
{
    /// <summary>Connects CLR events to their serialized Inspector UnityEvents.</summary>
    void ConnectInspectorEvents();

    /// <summary>Disconnects CLR events from their serialized Inspector UnityEvents.</summary>
    void DisconnectInspectorEvents();
}
}
