# Public API draft

This document defines the intended consumer-facing shape before runtime implementation details are added.

The goal is to keep the common path obvious, discoverable, and small while leaving concurrency and lifecycle complexity behind the facade.

## Events -> async

### `EventHandler`

Generated facade:

```csharp
await device.ConnectedAsync(cancellationToken);

await device.ConnectedAsync(
    TimeSpan.FromSeconds(5),
    cancellationToken);
```

Proposed generated signatures:

```csharp
Task ConnectedAsync(
    CancellationToken cancellationToken = default);

Task ConnectedAsync(
    TimeSpan timeout,
    CancellationToken cancellationToken = default);
```

### `EventHandler<TEventArgs>`

Generated facade:

```csharp
var message = await client.MessageReceivedAsync(
    x => x.Id == wantedId,
    cancellationToken);

var message = await client.MessageReceivedAsync(
    x => x.Id == wantedId,
    TimeSpan.FromSeconds(5),
    cancellationToken);
```

Proposed generated signatures:

```csharp
Task<TEventArgs> MessageReceivedAsync(
    CancellationToken cancellationToken = default);

Task<TEventArgs> MessageReceivedAsync(
    Predicate<TEventArgs> predicate,
    CancellationToken cancellationToken = default);

Task<TEventArgs> MessageReceivedAsync(
    TimeSpan timeout,
    CancellationToken cancellationToken = default);

Task<TEventArgs> MessageReceivedAsync(
    Predicate<TEventArgs> predicate,
    TimeSpan timeout,
    CancellationToken cancellationToken = default);
```

The low-level `EventAwaiter` remains available for advanced/manual bridging. The generated methods are the normal entry point.

### Event streams

Planned facade:

```csharp
await foreach (var message in client.MessageReceivedStream(cancellationToken))
{
}
```

For generic events, a predicate overload is planned as well.

## Async -> events

### `Task`

```csharp
using var source = SaveAsync().ToEventSource();

source.Completed += OnCompleted;
source.Faulted += OnFaulted;
source.Cancelled += OnCancelled;

source.Start();
```

### `Task<T>`

```csharp
using var source = LoadUserAsync().ToEventSource();

source.Completed += OnCompleted;
source.Faulted += OnFaulted;
source.Cancelled += OnCancelled;

source.Start();
```

`Completed` receives `AsyncValueEventArgs<T>` and exposes the result through `Value`.

### `IAsyncEnumerable<T>`

```csharp
await using var source = GetMessagesAsync().ToEventSource(cancellationToken);

source.Next += OnMessage;
source.Completed += OnCompleted;
source.Faulted += OnFaulted;
source.Cancelled += OnCancelled;

source.Start();
```

`Next` receives `AsyncValueEventArgs<T>`.

## Why `Start()` exists

`Start()` is deliberate. It allows legacy/event-driven consumers to attach every handler before the bridge starts publishing anything.

Without an explicit start boundary, an already-completed `Task` or a very fast `IAsyncEnumerable<T>` can finish while handlers are still being attached. Avoiding that would require hidden delays, buffering, or replay semantics. An explicit `Start()` is deterministic and keeps the event model understandable.

The underlying `Task` may already be running. `Start()` starts observation/publication by the bridge; it does not start the task itself.

## Initial public types

```text
GenerateAsyncEventsAttribute
EventAwaiter
AsyncEventSourceExtensions
TaskEventSource
TaskEventSource<T>
AsyncEnumerableEventSource<T>
AsyncValueEventArgs<T>
AsyncFaultedEventArgs
```

No Rx-style operators, event bus concepts, or messaging abstractions are part of the public surface.
