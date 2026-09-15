# Release readiness

This document records the release contract for AsyncEventBridge `0.2.0`, the native modern-.NET release that becomes the primary `main` line.

## Release identity

- Version: `0.2.0`
- Runtime target: `.NET 10` (`net10.0`)
- Package ID: `AsyncEventBridge`
- Source generator: included in the same package
- Generator host target: `netstandard2.0`
- License: `MPL-2.0`
- Native AOT/trimming: verified in CI against the packed NuGet package

Compatibility-focused development remains available on `base/netstandard2.0`; Unity-specific work remains on `unity`.

## Package contents

The package is expected to contain:

- `lib/net10.0/AsyncEventBridge.dll`;
- `lib/net10.0/AsyncEventBridge.xml`;
- `analyzers/dotnet/cs/AsyncEventBridge.Generators.dll`;
- `README.md`;
- `assets/AsyncEventBridge.png`.

A matching `.snupkg` must contain `lib/net10.0/AsyncEventBridge.pdb`. Package metadata must declare `MPL-2.0` and the Git repository URL.

## Automated release gates

CI verifies the release path by:

1. restoring and building the full solution in Release configuration;
2. running runtime, generator, lifecycle, race, metrics, observability, API-lock, and stress tests;
3. executing the sensor-monitoring sample;
4. creating the runtime NuGet package and symbol package;
5. inspecting package contents and repository/license metadata;
6. restoring and compiling an isolated consumer from the generated `.nupkg`;
7. executing a separate packaged runtime consumer that covers generated waits/streams, sender-aware occurrences, event composition, `ValueTask`, backpressure telemetry, and async-stream bridges;
8. publishing a separate packaged consumer with Native AOT for `linux-x64`;
9. rejecting any `ILxxxx` trimming/AOT warnings from that publish;
10. executing the resulting native binary;
11. independently restoring, building, and testing the solution on Windows and macOS;
12. retaining the `.nupkg` and `.snupkg` as CI artifacts.

These checks consume the built package rather than relying only on project references.

## Public API contract

The primary bridge matrix is:

```text
Event                -> Task
Event                -> IAsyncEnumerable<T>
Task                 -> EventBridge
Task<T>              -> EventBridge<T>
ValueTask            -> EventBridge
ValueTask<T>         -> EventBridge<T>
IAsyncEnumerable<T>  -> EventStreamBridge<T>
```

Modern additions include:

- non-`EventArgs` payloads;
- `EventHandler<TSender, TPayload>` and supported custom two-parameter delegates;
- sender-aware `EventOccurrence<TSender, TPayload>` waits and streams;
- lifecycle-safe `WaitAnyAsync` / `WaitAllAsync` event composition;
- `TimeProvider` timeout control;
- bounded stream drop counters/observers;
- built-in `System.Diagnostics.Metrics` instrumentation.

The public API is protected by reflection-based lock tests covering exported types, method signatures, parameter order/optionality, events, properties, enum values, and configuration defaults.

## Generator contract

The package generator supports both `[GenerateAsyncEvents]` and assembly-level `[GenerateAsyncEventsFor(typeof(...))]` requests. It handles modern payloads, strongly typed senders, supported custom event delegates, generic source constraints, inherited accessible events, and sender-aware occurrence generation.

Diagnostics currently include:

- `AEB001` — unsupported event delegate/payload shape;
- `AEB002` — invalid assembly-level target;
- `AEB003` — duplicate or redundant generation request.

Ref-like values and generic payloads that may legally become ref-like are rejected because they cannot safely cross an asynchronous lifetime boundary.

## Compatibility and branch policy

`main` is the native modern-.NET product line. It may use current BCL/runtime facilities where they materially improve correctness, performance, testability, or operability.

`base/netstandard2.0` remains the broad compatibility line. `unity` remains the Unity-specific line. Shared fixes should still land in the compatibility base first when they genuinely apply to all editions; modern-only work belongs on `main`/`dotnet-latest`.

## Owner-controlled publication decisions

Repository-side implementation and verification are automated. These remain owner-controlled:

- NuGet.org package ownership and publishing credentials;
- package signing, if desired;
- release/tag publication;
- external host certification claims.

Moving the verified source to `main` does not itself publish a NuGet package.
