# ValueTask bridge ownership

`ValueTask` and `ValueTask<T>` can represent single-consumption operations backed by `IValueTaskSource`.

When a `ValueTask` is passed to `ToEventBridge()`, AsyncEventBridge takes ownership of observing that operation. The original `ValueTask` should not also be awaited, converted with `AsTask()`, or otherwise consumed by the caller.

The bridge converts the operation once when it is created, preserving the existing connect-once event publication model. Task-backed `ValueTask` instances can reuse their underlying `Task`; source-backed instances may require a task representation.

Use `Task` when an operation must naturally support multiple consumers. Use `ValueTask` when the producer already exposes it and ownership can be transferred to the bridge.