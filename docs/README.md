# AsyncEventBridge documentation

AsyncEventBridge keeps ordinary .NET events and ordinary async code usable together without forcing either side into a different programming model.

If you are new to the package, start with [Getting started](getting-started.md). The guides below are task-oriented: pick the thing you are trying to do, then follow the smallest relevant path.

## Start here

- [Getting started](getting-started.md) — install the package, generate your first async event facade, await one event, and consume a stream.
- [Events to async](events-to-async.md) — one-shot waits, filtering, cancellation, timeout, streams, and sender-aware occurrences.
- [Async to events](async-to-events.md) — expose `Task`, `Task<T>`, `ValueTask`, and `IAsyncEnumerable<T>` to event-first consumers.

## Migration guides

These guides are for teams changing architecture gradually while keeping a clear path to remove AsyncEventBridge later:

- [Migrate an event-driven codebase to async](migrating-event-driven-to-async.md) — coexistence, one-shot waits, streams, state/lifecycle migration, dependency boundaries, and an explicit exit strategy.
- [Expose async code to event consumers](migrating-async-to-events.md) — event compatibility facades over async code, gradual consumer migration, and how to remove or replace the bridge later.
- [Third-party and legacy event sources](third-party-events.md) — generate async APIs for public types and interfaces you do not own.
- [Lifecycle recipes](lifecycle-recipes.md) — connected/disconnected, ready/not-ready, start/stop, reconnecting, and explicit lifecycle markers.
- [Buffering and slow consumers](buffering.md) — choose between lossless unbounded buffering and bounded drop policies.
- [Troubleshooting and FAQ](troubleshooting.md) — generator diagnostics, missing generated APIs, cancellation, cleanup, and stream behavior.

## Reference and behavior contracts

Use these when you need exact API or behavioral semantics rather than a recipe:

- [Public API](public-api.md)
- [Support matrix](support-matrix.md)
- [Generator diagnostics](diagnostics.md)
- [Event composition](event-composition.md)
- [Metrics](metrics.md)
- [ValueTask ownership](valuetask-ownership.md)
- [Cancellation, lifecycle, and cleanup contract](1.0-cancellation-lifecycle-cleanup.md)
- [Performance baselines](performance-baselines.md)

## Unity

Unity has its own installation and Unity-native API guide:

- [AsyncEventBridge for Unity](../Packages/com.perry3d.async-event-bridge/README.md)

The Unity package uses the same product concepts but adds Unity `Awaitable`, `UnityEvent`, owner-destruction cancellation, application-exit cancellation, and Inspector-oriented publication.

## Upgrading and releases

- [Migrating to 1.0](migrating-to-1.0.md)
- [1.0 release notes](release-notes-1.0.0.md)

The remaining files in this directory document design history, release engineering, and stabilization decisions. They are useful for maintainers and contributors, but application developers normally do not need them to use the package.
