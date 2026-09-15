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
    /// Configures buffering and drop observability for event-to-async streams. WARNING: the default
    /// <see cref="EventStreamFullMode.Grow"/> mode preserves all event values and can grow memory usage without a fixed
    /// upper bound when producers outpace consumers.
    /// </summary>
    public sealed class EventStreamOptions
    {
        private long _droppedCount;

        internal const int DefaultCapacity = 100;

        /// <summary>
        /// Gets or sets the initial buffer capacity for <see cref="EventStreamFullMode.Grow"/>, or the hard buffer limit
        /// for <see cref="EventStreamFullMode.DropOldest"/> and <see cref="EventStreamFullMode.DropNewest"/>.
        /// </summary>
        public int Capacity { get; set; } = DefaultCapacity;

        /// <summary>
        /// Gets or sets the behavior used when event production outpaces async consumption.
        /// WARNING: <see cref="EventStreamFullMode.Grow"/> can increase memory usage without a fixed upper bound.
        /// </summary>
        public EventStreamFullMode FullMode { get; set; } = EventStreamFullMode.Grow;

        /// <summary>
        /// Gets the total number of event values dropped by bounded streams created with this options instance.
        /// The value is thread-safe and aggregates across concurrent or repeated stream enumerations that share this instance.
        /// </summary>
        public long DroppedCount => Interlocked.Read(ref _droppedCount);

        /// <summary>
        /// Gets or sets an optional observer invoked synchronously whenever a bounded stream drops an event value.
        /// The supplied value is the updated lifetime <see cref="DroppedCount"/> for this options instance.
        /// Observer exceptions are isolated and do not fault the event stream. Keep observers fast because they execute on
        /// the event producer thread and may be invoked concurrently when events are raised concurrently.
        /// </summary>
        public Action<long>? DropObserver { get; set; }

        internal long RecordDrop() => Interlocked.Increment(ref _droppedCount);
    }

    /// <summary>
    /// Defines how an event-to-async stream handles a full buffer.
    /// </summary>
    public enum EventStreamFullMode
    {
        /// <summary>
        /// Preserves every event value by allowing the buffer to grow beyond its initial capacity.
        /// WARNING: sustained producer throughput above consumer throughput can grow memory usage without a fixed upper bound.
        /// </summary>
        Grow = 0,

        /// <summary>
        /// Keeps the buffer bounded by removing the oldest buffered value when a new value arrives at capacity.
        /// </summary>
        DropOldest = 1,

        /// <summary>
        /// Keeps the buffer bounded by dropping the newly arriving value when the buffer is already at capacity.
        /// </summary>
        DropNewest = 2,
    }
}
