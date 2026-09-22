using System;
using System.Threading.Tasks;
using AsyncEventBridge;
using AsyncEventBridge.Unity;
using UnityEngine;
using UnityEngine.Events;

public sealed class AsyncEventBridgeAcceptanceSmoke : MonoBehaviour
{
    private readonly AcceptanceSensor _sensor = new();
    private readonly UnityEvent<int> _confirmed = new();

    private void Awake()
    {
        using EventBridge bridge = Task.CompletedTask.ToEventBridge();
        bridge.Completed += (_, _) => { };
        bridge.Connect();

        _ = _sensor.ChangedAsync(this);
        _ = _confirmed.WaitAsync(
            this,
            value => value >= 0,
            timeout: TimeSpan.FromSeconds(1));
    }
}

[GenerateAsyncEvents]
public sealed class AcceptanceSensor
{
    public event EventHandler<AcceptanceEventArgs>? Changed;

    public void Raise(int value) =>
        Changed?.Invoke(this, new AcceptanceEventArgs(value));
}

public sealed class AcceptanceEventArgs : EventArgs
{
    public AcceptanceEventArgs(int value)
    {
        Value = value;
    }

    public int Value { get; }
}
