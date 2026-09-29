# Migration guide: async to events

This guide is for teams whose implementation already uses `Task`, `ValueTask`, or `IAsyncEnumerable<T>`, while some existing consumers still expect ordinary .NET events.

The recommended architecture is to keep the **async implementation as the source of truth** and adapt it to events only at the compatibility boundary.

AsyncEventBridge can later be removed either by migrating the remaining consumers to async or by replacing the boundary adapter with your own event facade.

## The migration shape

A typical migration looks like this:

```text
async implementation
      |
      +--------------------> async consumers
      |
      +-> AsyncEventBridge -> legacy event consumers
```

Later:

```text
option A
async implementation -> async consumers
                        (event facade deleted)

option B
async implementation -> your own event facade -> legacy consumers
                        (AsyncEventBridge deleted)
```

The important rule is that the core implementation should not become event-driven again just because some callers still need events.

## Example starting point

Assume the modern implementation already looks like this:

```csharp
public sealed class SensorService
{
    public Task<SensorConfiguration> LoadConfigurationAsync(
        CancellationToken cancellationToken = default)
    {
        // Native async implementation.
    }

    public IAsyncEnumerable<Reading> ReadingsAsync(
        CancellationToken cancellationToken = default)
    {
        // Native async stream.
    }
}
```

A legacy UI, plugin, or public API may still expect:

```csharp
service.ConfigurationLoaded += OnConfigurationLoaded;
service.ReadingReceived += OnReadingReceived;
```

Do not rewrite the async implementation into callback code. Put a compatibility facade around it.

## Step 1: bridge a Task to events

For a direct/local adaptation:

```csharp
using EventBridge<SensorConfiguration> bridge =
    service.LoadConfigurationAsync(cancellationToken)
        .ToEventBridge();

bridge.Completed += (_, e) => Apply(e.Value);
bridge.Faulted += (_, e) => Log(e.Exception);
bridge.Cancelled += (_, _) => HandleCancellation();

bridge.Connect();
```

Subscribe first, then call `Connect()`.

That order is intentional because the task may already be complete. Explicit connection ensures event handlers are attached before publication begins.

## Step 2: do not expose bridge-specific event args from your public API

If future package removal matters, avoid making `EventBridge<T>` or `AsyncValueEventArgs<T>` part of your application's public contract.

Instead, expose events you own:

```csharp
public sealed class ConfigurationLoadedEventArgs(
    SensorConfiguration configuration) : EventArgs
{
    public SensorConfiguration Configuration { get; } = configuration;
}

public sealed class LoadFailedEventArgs(Exception exception) : EventArgs
{
    public Exception Exception { get; } = exception;
}
```

Then keep AsyncEventBridge inside the facade implementation.

Conceptually:

```text
SensorService
   Task<T>
      |
AsyncEventBridge
      |
your event facade
      |
legacy callers
```

That boundary is what makes the package easy to remove later.

## Step 3: build an event compatibility facade

For example:

```csharp
public sealed class SensorEventFacade : IDisposable
{
    private readonly SensorService _service;
    private EventBridge<SensorConfiguration>? _loadBridge;

    public SensorEventFacade(SensorService service)
    {
        _service = service;
    }

    public event EventHandler<ConfigurationLoadedEventArgs>? ConfigurationLoaded;
    public event EventHandler<LoadFailedEventArgs>? LoadFailed;
    public event EventHandler? LoadCancelled;

    public void LoadConfiguration(
        CancellationToken cancellationToken = default)
    {
        _loadBridge?.Dispose();

        var bridge =
            _service.LoadConfigurationAsync(cancellationToken)
                .ToEventBridge();

        bridge.Completed += (_, e) =>
            ConfigurationLoaded?.Invoke(
                this,
                new ConfigurationLoadedEventArgs(e.Value));

        bridge.Faulted += (_, e) =>
            LoadFailed?.Invoke(
                this,
                new LoadFailedEventArgs(e.Exception));

        bridge.Cancelled += (_, _) =>
            LoadCancelled?.Invoke(this, EventArgs.Empty);

        _loadBridge = bridge;
        bridge.Connect();
    }

    public void Dispose()
    {
        _loadBridge?.Dispose();
        _loadBridge = null;
    }
}
```

Legacy callers now depend on **your events**, not on AsyncEventBridge types.

Modern callers should continue to use `SensorService.LoadConfigurationAsync(...)` directly.

## Step 4: bridge an async stream to repeated events

For `IAsyncEnumerable<T>`:

```csharp
await using EventStreamBridge<Reading> bridge =
    service.ReadingsAsync(cancellationToken)
        .ToEventBridge();

bridge.Value += (_, e) => Process(e.Value);
bridge.Completed += (_, _) => OnCompleted();
bridge.Faulted += (_, e) => Log(e.Exception);
bridge.Cancelled += (_, _) => OnCancelled();

bridge.Connect(cancellationToken);
```

After `Connect()`, the bridge owns enumeration of the async stream.

Use `DisposeAsync()` or `await using` when you need to know that stream enumeration and asynchronous cleanup have actually finished.

A synchronous `Dispose()` requests shutdown but does not wait for asynchronous source cleanup.

## Step 5: keep ownership clear

A compatibility facade should own:

- the `EventBridge` or `EventStreamBridge<T>`;
- bridge connection;
- cancellation lifetime;
- disposal;
- translation from bridge event args to your own event args.

The underlying service should continue to own:

- the real asynchronous operation;
- business logic;
- retries or persistence if applicable;
- the native `Task`, `ValueTask`, or `IAsyncEnumerable<T>` contract.

Do not let the event facade become the new implementation layer.

## Step 6: migrate event consumers gradually

Once both APIs exist, migrate consumers one at a time.

Legacy:

```csharp
facade.ConfigurationLoaded += OnConfigurationLoaded;
facade.LoadConfiguration();
```

Modern:

```csharp
SensorConfiguration configuration =
    await service.LoadConfigurationAsync(cancellationToken);

Apply(configuration);
```

For repeated values:

Legacy:

```csharp
facade.ReadingReceived += OnReadingReceived;
```

Modern:

```csharp
await foreach (Reading reading in
    service.ReadingsAsync(cancellationToken))
{
    Process(reading);
}
```

Nothing requires every consumer to move in the same release.

## Removing AsyncEventBridge later

There are again two clean exit strategies.

### Exit A: all event consumers have migrated to async

Delete the event compatibility facade.

Then remove:

1. calls to `ToEventBridge()`;
2. `EventBridge` and `EventStreamBridge<T>` fields;
3. any bridge-only `EventBridgeOptions`;
4. the `AsyncEventBridge` package reference.

The async implementation itself does not change.

This is the ideal end state:

```text
SensorService
    |
Task / IAsyncEnumerable
    |
all consumers
```

### Exit B: event consumers must remain, but you want to remove the package

Because your public event API uses your own event args and events, only the internal adapter must change.

A small owned adapter for a one-shot task can look like this:

```csharp
public sealed class SensorEventFacade : IDisposable
{
    private readonly SensorService _service;
    private readonly CancellationTokenSource _lifetime = new();

    public SensorEventFacade(SensorService service)
    {
        _service = service;
    }

    public event EventHandler<ConfigurationLoadedEventArgs>? ConfigurationLoaded;
    public event EventHandler<LoadFailedEventArgs>? LoadFailed;
    public event EventHandler? LoadCancelled;

    public void LoadConfiguration()
    {
        _ = PublishLoadAsync(_lifetime.Token);
    }

    private async Task PublishLoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            SensorConfiguration configuration =
                await _service
                    .LoadConfigurationAsync(cancellationToken)
                    .ConfigureAwait(false);

            ConfigurationLoaded?.Invoke(
                this,
                new ConfigurationLoadedEventArgs(configuration));
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            LoadCancelled?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            LoadFailed?.Invoke(
                this,
                new LoadFailedEventArgs(exception));
        }
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
```

That removes the package dependency while preserving your public event contract.

For an async stream, the equivalent owned adapter is a pump:

```csharp
private async Task PublishReadingsAsync(
    CancellationToken cancellationToken)
{
    try
    {
        await foreach (Reading reading in
            _service.ReadingsAsync(cancellationToken))
        {
            ReadingReceived?.Invoke(
                this,
                new ReadingEventArgs(reading));
        }

        ReadingStreamCompleted?.Invoke(
            this,
            EventArgs.Empty);
    }
    catch (OperationCanceledException)
        when (cancellationToken.IsCancellationRequested)
    {
        ReadingStreamCancelled?.Invoke(
            this,
            EventArgs.Empty);
    }
    catch (Exception exception)
    {
        ReadingStreamFailed?.Invoke(
            this,
            new LoadFailedEventArgs(exception));
    }
}
```

In production, the facade should also track that pump task and await it during asynchronous disposal if deterministic cleanup is required.

## What you take responsibility for after removing the package

Replacing AsyncEventBridge with a handwritten facade is possible, but the semantics now belong to you.

Review at least:

- exactly-once terminal publication;
- already-completed tasks;
- cancellation classification;
- disposal racing with completion;
- source-enumerator disposal;
- subscriber exceptions;
- whether one subscriber is allowed to stop later subscribers;
- publication after disposal;
- thread/context expectations;
- deterministic shutdown;
- exceptions raised during cleanup.

AsyncEventBridge exists largely to make those edge cases consistent.

Removing the package is therefore an architectural choice, not just deleting one extension-method call.

## ValueTask note

`ValueTask` and `ValueTask<T>` can be bridged directly on modern .NET.

When a `ValueTask` is passed to `ToEventBridge()`, the bridge takes ownership of observing it. Do not independently await or otherwise consume the same `ValueTask` afterward.

This differs from ordinary `Task`, which can safely have multiple observers.

See [ValueTask ownership](valuetask-ownership.md).

## Subscriber exception policy

AsyncEventBridge isolates subscriber failures so one event handler does not prevent later subscribers from running or rewrite the underlying async operation's outcome.

If you replace the bridge with your own event publication code, ordinary multicast delegate behavior applies unless you deliberately implement equivalent isolation.

This is an important compatibility detail when removing the package.

## Migration order that scales well

For a real codebase, use this order:

1. keep the async implementation as the canonical API;
2. add event facades only where legacy consumers require them;
3. expose your own event contracts rather than AsyncEventBridge-specific event args;
4. migrate event consumers to the original async API one at a time;
5. delete event facades that no longer have callers;
6. remove AsyncEventBridge when the last bridge disappears, or replace the remaining boundary adapters with owned code.

## Completion checklist

Before declaring an async-to-events migration stable, verify:

- event facades live at the compatibility boundary, not in the async core;
- handlers are attached before `Connect()`;
- bridge ownership and disposal are explicit;
- async streams use asynchronous disposal where deterministic cleanup matters;
- public event contracts use application-owned event args if future package removal matters;
- `ValueTask` ownership is respected;
- event subscriber failure behavior is understood;
- the underlying async API remains directly available to modern consumers;
- removing AsyncEventBridge would require replacing only the compatibility facade.

## Related guides

- [Async to events API guide](async-to-events.md)
- [ValueTask ownership](valuetask-ownership.md)
- [Cancellation, lifecycle, and cleanup contract](1.0-cancellation-lifecycle-cleanup.md)
- [Troubleshooting and FAQ](troubleshooting.md)
