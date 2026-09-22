# Generator diagnostics

AsyncEventBridge source-generator diagnostics are part of the supported developer experience. Diagnostic IDs are kept stable once 1.0 is released.

## AEB001 — Unsupported event delegate

AsyncEventBridge found an event on a generation target but cannot safely expose that event as a task or async stream.

Common causes include:

- the delegate does not return `void`;
- the delegate does not have exactly two parameters;
- a delegate parameter is passed by `ref`, `out`, or `in`;
- the async payload is ref-like or may legally become ref-like;
- on the .NET Standard 2.0 compatibility runtime, the event payload does not derive from `EventArgs`.

Modern .NET supports `EventHandler`, `EventHandler<TPayload>`, `EventHandler<TSender, TPayload>`, and compatible custom two-parameter `void` delegates. The .NET Standard 2.0 compatibility line retains the older `EventArgs`-based payload requirement.

### Resolution

Use a supported event shape, leave that event on its ordinary synchronous event API, or adapt it manually with the low-level AsyncEventBridge APIs when the value can safely cross an asynchronous lifetime.

Do not suppress this warning merely to make a missing generated method compile: an AEB001 event is intentionally omitted.

## AEB002 — Invalid async-event generation target

A type supplied to:

```csharp
[assembly: GenerateAsyncEventsFor(typeof(...))]
```

cannot be used as a generated extension target.

The 1.0 generator supports class and interface targets. Struct, enum, delegate, and otherwise inaccessible targets are not accepted.

### Resolution

Target a supported class or interface type. Struct targets remain unsupported because extension-based event subscription to copied value types would have unsafe lifetime semantics.

For interfaces, generation covers events declared directly on the targeted interface. Target a base interface directly when its inherited events also need generated APIs.

## AEB003 — Redundant async-event generation request

The same type was requested for generation more than once, or a locally owned type is both directly annotated with:

```csharp
[GenerateAsyncEvents]
```

and requested through:

```csharp
[assembly: GenerateAsyncEventsFor(typeof(...))]
```

The redundant request is ignored.

### Resolution

Keep one generation request:

- prefer `[GenerateAsyncEvents]` for a type you own and can annotate;
- use `[assembly: GenerateAsyncEventsFor(typeof(...))]` for a public type you cannot annotate.

Removing the duplicate keeps generated ownership and inheritance boundaries unambiguous.

## Severity

AEB001, AEB002, and AEB003 are warnings in 1.0. They do not indicate a runtime failure; they indicate that requested generated API is missing, invalid, or redundant.

Projects may promote warnings to errors through their normal compiler/analyzer configuration.
