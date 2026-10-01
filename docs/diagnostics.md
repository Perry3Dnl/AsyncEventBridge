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

## AEB004 — Invalid Inspector event generation target

`[GenerateInspectorEvents]` was applied to a type that cannot safely receive generated serialized Inspector members.

Inspector event generation requires a top-level, non-generic, non-abstract `MonoBehaviour` declared with the `partial` modifier.

### Resolution

Make the component partial and keep the Inspector-enabled component itself non-generic:

```csharp
[GenerateInspectorEvents]
public sealed partial class Sensor : MonoBehaviour
{
}
```

The partial requirement is intentional: the Unity generator adds the serialized event container to the same component type so Unity can persist scene and prefab listener wiring without runtime reflection.

## AEB005 — Unsupported Inspector event delegate

A CLR event on a `[GenerateInspectorEvents]` component cannot be projected to a serialized UnityEvent.

Inspector projection supports the same Unity-safe EventHandler-style shape used by the Unity async generator: a `void` delegate with two non-ref parameters whose second parameter derives from `EventArgs`.

### Resolution

Keep unsupported events code-only, change the delegate to a supported EventHandler-style shape, or expose a separate UnityEvent manually when the event cannot safely map to the Inspector.

## AEB006 — Reserved Inspector event member collision

A `[GenerateInspectorEvents]` component already declares a member named `AsyncEventBridgeEvents`.

That name is reserved for the generated serialized event group.

### Resolution

Rename the user-defined member before enabling generated Inspector events. Keeping this generated field name stable is important because Unity stores scene and prefab serialization against field identity.

## Severity

AEB001 through AEB006 are warnings in 1.0. They do not indicate a runtime failure; they indicate that requested generated API is missing, invalid, redundant, or cannot be represented safely in the Unity Inspector.

Projects may promote warnings to errors through their normal compiler/analyzer configuration.
