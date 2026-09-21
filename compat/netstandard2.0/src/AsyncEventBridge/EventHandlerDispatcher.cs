using System;
using System.Diagnostics;

namespace AsyncEventBridge
{

internal static class EventHandlerDispatcher
{
    internal static void Invoke(
        EventHandler? handlers,
        object sender,
        EventHandlerDispatchSettings dispatchSettings)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler handler in handlers.GetInvocationList())
        {
            try
            {
                handler(sender, EventArgs.Empty);
            }
            catch (Exception exception)
            {
                HandleSubscriberException(exception, dispatchSettings);
            }
        }
    }

    internal static void Invoke<TEventArgs>(
        EventHandler<TEventArgs>? handlers,
        object sender,
        TEventArgs eventArgs,
        EventHandlerDispatchSettings dispatchSettings)
        where TEventArgs : EventArgs
    {
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler<TEventArgs> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(sender, eventArgs);
            }
            catch (Exception exception)
            {
                HandleSubscriberException(exception, dispatchSettings);
            }
        }
    }

    private static void HandleSubscriberException(
        Exception exception,
        EventHandlerDispatchSettings dispatchSettings)
    {
        switch (dispatchSettings.Policy)
        {
            case EventBridgeSubscriberExceptionPolicy.TraceAndContinue:
                Trace.TraceError(
                    "AsyncEventBridge event handler threw an exception: {0}",
                    exception);
                break;

            case EventBridgeSubscriberExceptionPolicy.ReportAndContinue:
                try
                {
                    dispatchSettings.Observer!(exception);
                }
                catch (Exception observerException)
                {
                    Trace.TraceError(
                        "AsyncEventBridge subscriber exception observer threw while reporting subscriber failure. " +
                        "Subscriber exception: {0}; observer exception: {1}",
                        exception,
                        observerException);
                }

                break;

            case EventBridgeSubscriberExceptionPolicy.IgnoreAndContinue:
                break;

            default:
                throw new InvalidOperationException(
                    "Unsupported subscriber exception policy: " + dispatchSettings.Policy + ".");
        }
    }
}
}
