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

    /// <summary>
    /// Provides conversions from async .NET APIs to event-facing bridges.
    /// </summary>
    public static class AsyncEventBridgeExtensions
    {
        /// <summary>
        /// Creates an event-facing bridge for a <see cref="Task"/>.
        /// </summary>
        public static EventBridge ToEventBridge(this Task task)
        {
            ArgumentNullException.ThrowIfNull(task);
            return new EventBridge(task);
        }

        /// <summary>
        /// Creates an event-facing bridge for a <see cref="Task"/> with explicit bridge options.
        /// </summary>
        public static EventBridge ToEventBridge(this Task task, EventBridgeOptions options)
        {
            ArgumentNullException.ThrowIfNull(task);
            ArgumentNullException.ThrowIfNull(options);
            return new EventBridge(task, options.CreateDispatchSettings());
        }

        /// <summary>
        /// Creates an event-facing bridge for a <see cref="Task{TResult}"/>.
        /// </summary>
        public static EventBridge<T> ToEventBridge<T>(this Task<T> task)
        {
            ArgumentNullException.ThrowIfNull(task);
            return new EventBridge<T>(task);
        }

        /// <summary>
        /// Creates an event-facing bridge for a <see cref="Task{TResult}"/> with explicit bridge options.
        /// </summary>
        public static EventBridge<T> ToEventBridge<T>(this Task<T> task, EventBridgeOptions options)
        {
            ArgumentNullException.ThrowIfNull(task);
            ArgumentNullException.ThrowIfNull(options);
            return new EventBridge<T>(task, options.CreateDispatchSettings());
        }

        /// <summary>
        /// Creates an event-facing bridge for a <see cref="ValueTask"/>.
        /// </summary>
        /// <remarks>
        /// The bridge takes ownership of observing the supplied value task. The caller must not consume the same
        /// <see cref="ValueTask"/> independently after creating the bridge.
        /// </remarks>
        public static EventBridge ToEventBridge(this ValueTask task) =>
            new(task);

        /// <summary>
        /// Creates an event-facing bridge for a <see cref="ValueTask"/> with explicit bridge options.
        /// </summary>
        /// <remarks>
        /// The bridge takes ownership of observing the supplied value task. The caller must not consume the same
        /// <see cref="ValueTask"/> independently after creating the bridge.
        /// </remarks>
        public static EventBridge ToEventBridge(this ValueTask task, EventBridgeOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            return new EventBridge(task, options.CreateDispatchSettings());
        }

        /// <summary>
        /// Creates an event-facing bridge for a <see cref="ValueTask{TResult}"/>.
        /// </summary>
        /// <remarks>
        /// The bridge takes ownership of observing the supplied value task. The caller must not consume the same
        /// <see cref="ValueTask{TResult}"/> independently after creating the bridge.
        /// </remarks>
        public static EventBridge<T> ToEventBridge<T>(this ValueTask<T> task) =>
            new(task);

        /// <summary>
        /// Creates an event-facing bridge for a <see cref="ValueTask{TResult}"/> with explicit bridge options.
        /// </summary>
        /// <remarks>
        /// The bridge takes ownership of observing the supplied value task. The caller must not consume the same
        /// <see cref="ValueTask{TResult}"/> independently after creating the bridge.
        /// </remarks>
        public static EventBridge<T> ToEventBridge<T>(this ValueTask<T> task, EventBridgeOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            return new EventBridge<T>(task, options.CreateDispatchSettings());
        }

        /// <summary>
        /// Creates an event-facing bridge for an <see cref="IAsyncEnumerable{T}"/>.
        /// </summary>
        public static EventStreamBridge<T> ToEventBridge<T>(this IAsyncEnumerable<T> source)
        {
            ArgumentNullException.ThrowIfNull(source);
            return new EventStreamBridge<T>(source);
        }

        /// <summary>
        /// Creates an event-facing bridge for an <see cref="IAsyncEnumerable{T}"/> with explicit bridge options.
        /// </summary>
        public static EventStreamBridge<T> ToEventBridge<T>(
            this IAsyncEnumerable<T> source,
            EventBridgeOptions options)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(options);
            return new EventStreamBridge<T>(source, options.CreateDispatchSettings());
        }
    }
}
