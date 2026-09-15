using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace AsyncEventBridge
{

    internal static class EventHandlerDispatcher
    {
        internal static void Invoke(EventHandler? handlers, object sender)
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
                    System.Diagnostics.Trace.TraceError(
                        "AsyncEventBridge event handler threw an exception: {0}",
                        exception);
                }
            }
        }

        internal static void Invoke<TEventArgs>(
            EventHandler<TEventArgs>? handlers,
            object sender,
            TEventArgs eventArgs)
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
                    System.Diagnostics.Trace.TraceError(
                        "AsyncEventBridge event handler threw an exception: {0}",
                        exception);
                }
            }
        }
    }
}
