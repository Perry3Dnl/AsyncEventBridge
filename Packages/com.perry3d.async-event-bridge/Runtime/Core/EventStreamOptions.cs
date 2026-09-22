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
/// Configures buffering for event-to-async streams. WARNING: the default <see cref="EventStreamFullMode.Unbounded"/>
/// mode preserves all event values and can grow memory usage without a fixed upper bound when producers outpace consumers.
/// </summary>
public sealed class EventStreamOptions
{
    internal const int DefaultCapacity = 100;

    /// <summary>
    /// Gets or sets the hard buffer limit for <see cref="EventStreamFullMode.DropOldest"/> and
    /// <see cref="EventStreamFullMode.DropWrite"/>.
    /// This value is ignored when <see cref="FullMode"/> is <see cref="EventStreamFullMode.Unbounded"/>.
    /// </summary>
    public int Capacity { get; set; } = DefaultCapacity;

    /// <summary>
    /// Gets or sets the behavior used when event production outpaces async consumption.
    /// WARNING: <see cref="EventStreamFullMode.Unbounded"/> can increase memory usage without a fixed upper bound.
    /// </summary>
    public EventStreamFullMode FullMode { get; set; } = EventStreamFullMode.Unbounded;
}

/// <summary>
/// Defines how an event-to-async stream handles a full buffer.
/// </summary>
public enum EventStreamFullMode
{
    /// <summary>
    /// Preserves every accepted event value in an unbounded buffer.
    /// <see cref="EventStreamOptions.Capacity"/> is ignored in this mode.
    /// WARNING: sustained producer throughput above consumer throughput can grow memory usage without a fixed upper bound.
    /// </summary>
    Unbounded = 0,

    /// <summary>
    /// Keeps the buffer bounded by removing the oldest buffered value when a new value arrives at capacity.
    /// </summary>
    DropOldest = 1,

    /// <summary>
    /// Keeps the buffer bounded by dropping the newly arriving value when the buffer is already at capacity.
    /// </summary>
    DropWrite = 2,
}
}
