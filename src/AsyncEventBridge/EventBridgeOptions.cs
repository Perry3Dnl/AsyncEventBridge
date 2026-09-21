using System;

namespace AsyncEventBridge
{
    /// <summary>
    /// Configures event-facing bridge behavior.
    /// </summary>
    public sealed class EventBridgeOptions
    {
        /// <summary>
        /// Gets or sets how exceptions thrown by bridge event subscribers are handled.
        /// The default is <see cref="EventBridgeSubscriberExceptionPolicy.TraceAndContinue"/>.
        /// </summary>
        public EventBridgeSubscriberExceptionPolicy SubscriberExceptionPolicy { get; set; } =
            EventBridgeSubscriberExceptionPolicy.TraceAndContinue;

        /// <summary>
        /// Gets or sets the callback used by
        /// <see cref="EventBridgeSubscriberExceptionPolicy.ReportAndContinue"/>.
        /// The callback is invoked synchronously on the bridge publication path.
        /// Exceptions thrown by the observer are isolated, traced, and do not interrupt bridge dispatch.
        /// </summary>
        public Action<Exception>? SubscriberExceptionObserver { get; set; }

        internal EventHandlerDispatchSettings CreateDispatchSettings()
        {
            if (!Enum.IsDefined(typeof(EventBridgeSubscriberExceptionPolicy), SubscriberExceptionPolicy))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(SubscriberExceptionPolicy),
                    SubscriberExceptionPolicy,
                    "Unknown subscriber exception policy.");
            }

            if (SubscriberExceptionPolicy == EventBridgeSubscriberExceptionPolicy.ReportAndContinue &&
                SubscriberExceptionObserver is null)
            {
                throw new ArgumentException(
                    "ReportAndContinue requires SubscriberExceptionObserver to be configured.",
                    nameof(SubscriberExceptionObserver));
            }

            return new EventHandlerDispatchSettings(
                SubscriberExceptionPolicy,
                SubscriberExceptionObserver);
        }
    }

    /// <summary>
    /// Defines how event-facing bridges handle exceptions thrown by their event subscribers.
    /// All policies isolate the throwing subscriber and continue dispatching remaining subscribers.
    /// </summary>
    public enum EventBridgeSubscriberExceptionPolicy
    {
        /// <summary>
        /// Writes the subscriber exception through <see cref="System.Diagnostics.Trace.TraceError(string, object[])"/>
        /// and continues dispatching remaining subscribers.
        /// </summary>
        TraceAndContinue = 0,

        /// <summary>
        /// Invokes <see cref="EventBridgeOptions.SubscriberExceptionObserver"/> with the subscriber exception and
        /// continues dispatching remaining subscribers.
        /// </summary>
        ReportAndContinue = 1,

        /// <summary>
        /// Ignores the subscriber exception and continues dispatching remaining subscribers.
        /// </summary>
        IgnoreAndContinue = 2,
    }

    internal readonly struct EventHandlerDispatchSettings
    {
        internal EventHandlerDispatchSettings(
            EventBridgeSubscriberExceptionPolicy policy,
            Action<Exception>? observer)
        {
            Policy = policy;
            Observer = observer;
        }

        internal EventBridgeSubscriberExceptionPolicy Policy { get; }

        internal Action<Exception>? Observer { get; }

        internal static EventHandlerDispatchSettings Default { get; } =
            new(EventBridgeSubscriberExceptionPolicy.TraceAndContinue, null);
    }
}
