# Third-party and legacy event sources

Use this guide when the event source comes from a library, framework, interface, or legacy component that you cannot annotate with `[GenerateAsyncEvents]`.

## Generate extensions for a type you do not own

Add an assembly-level generation request in your consuming project:

```csharp
using AsyncEventBridge;

[assembly: GenerateAsyncEventsFor(typeof(ThirdParty.LegacySensor))]
```

AsyncEventBridge generates extension methods in the consuming compilation. The target assembly is not modified.

You can then use the public events on that type through the generated async facade:

```csharp
ReadingEventArgs reading =
    await sensor.ReadingChangedAsync(cancellationToken);

await foreach (ReadingEventArgs item in
    sensor.ReadingChangedStream(cancellationToken))
{
    Process(item);
}
```

The original event remains available to existing event-first consumers.

## Interfaces are supported

Assembly-level targets can be classes or interfaces.

A common example is `INotifyPropertyChanged`:

```csharp
using System.ComponentModel;
using AsyncEventBridge;

[assembly: GenerateAsyncEventsFor(typeof(INotifyPropertyChanged))]
```

Code can remain typed to the interface:

```csharp
INotifyPropertyChanged model = GetModel();

PropertyChangedEventArgs change =
    await model.PropertyChangedAsync(
        e => e.PropertyName == "Name",
        cancellationToken);
```

This is useful when the abstraction itself is part of the design and casting to a concrete implementation would defeat the purpose of the interface.

For 1.0, interface generation covers events declared directly on the targeted interface. If a base interface declares events that also need generated APIs, target that base interface separately.

## Supported delegate shapes

Modern .NET supports:

- `EventHandler`;
- `EventHandler<TPayload>`;
- strongly typed two-parameter event handlers;
- compatible custom two-parameter `void` delegates.

This covers many established framework patterns such as `PropertyChangedEventHandler`, `NotifyCollectionChangedEventHandler`, `ElapsedEventHandler`, and similar delegates.

The payload must be safe to carry beyond the synchronous event callback. Ref-like payloads cannot be exposed through an async lifetime.

The .NET Standard 2.0 compatibility line is intentionally more conservative and requires an `EventArgs`-based payload shape. See the [support matrix](support-matrix.md).

## Choosing between the two generation attributes

Use:

```csharp
[GenerateAsyncEvents]
```

when you own the type and can annotate it.

Use:

```csharp
[assembly: GenerateAsyncEventsFor(typeof(...))]
```

when the type is public but cannot or should not be modified.

Do not request the same type through both paths. The generator reports redundant generation as `AEB003`.

## Unsupported types

Struct generation is intentionally unsupported. Extension-based subscription against copied value types would produce surprising lifetime behavior.

The assembly-level target must also be accessible to the consuming compilation.

If a target or event shape is unsupported, the generator reports a diagnostic instead of silently omitting the API:

```text
AEB001  unsupported event delegate/payload shape
AEB002  invalid GenerateAsyncEventsFor target
AEB003  duplicate or redundant generation request
```

See [Generator diagnostics](diagnostics.md) for causes and resolutions.

## Runnable example

The repository contains a complete example under:

```text
samples/PropertyChangedInterop
```

It adapts `INotifyPropertyChanged` while keeping an ordinary event subscriber active at the same time.
