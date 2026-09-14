# Release readiness

This document records the release contract for AsyncEventBridge `0.1.0`, the first release.

## Release identity

- Version: `0.1.0`
- Baseline target: `.NET Standard 2.0`
- Package ID: `AsyncEventBridge`
- Source generator: included in the same package

Version `0.1.0` is the first public release of the .NET Standard 2.0 baseline. The baseline is the complete runtime contract, not a reduced compatibility build.

## Package contents

The package is expected to contain:

- `lib/netstandard2.0/AsyncEventBridge.dll`;
- `lib/netstandard2.0/AsyncEventBridge.xml`;
- `analyzers/dotnet/cs/AsyncEventBridge.Generators.dll`;
- `README.md`;
- `assets/AsyncEventBridge.png`.

Consumers install one package. A second source-generator package is not required.

## Automated release gates

CI verifies the release path by:

1. restoring the full solution;
2. building the full solution in Release configuration;
3. running runtime, generator, lifecycle, race, and stress tests;
4. compiling the .NET Standard 2.0 / C# 8 compatibility consumer;
5. building and executing the sensor-monitoring sample;
6. creating a local NuGet package;
7. inspecting the package for the runtime assembly, XML documentation, source generator, README, and icon;
8. restoring a separate .NET Standard 2.0 consumer from that local package;
9. compiling generated `<EventName>Async(...)` and `<EventName>Stream(...)` APIs from the package;
10. restoring and executing a separate packaged runtime consumer;
11. retaining the generated package as a CI artifact for manual validation.

These checks deliberately consume the built `.nupkg` instead of relying only on project references.

## Public API contract

The `0.1.0` bridge matrix is:

```text
Event                -> Task
Event                -> IAsyncEnumerable<T>
Task                 -> EventBridge
Task<T>              -> EventBridge<T>
IAsyncEnumerable<T>  -> EventStreamBridge<T>
```

The public API is protected by tests covering exported types, public methods, events, properties, event-stream defaults, and enum numeric values.

Correctness coverage includes cancellation, timeout, reentrancy, cleanup, subscription failure, disposal, subscriber exceptions, terminal races, concurrent waits, concurrent event-stream producers, inheritance, generics, nested types, accessibility, hidden members, and generated-name collisions.

## Compatibility policy

Normal feature work should not raise the `.NET Standard 2.0` baseline requirement for ordinary functionality.

Newer target frameworks can be added later when they provide a concrete compatibility or performance benefit. Those targets should remain additive to the baseline unless a future major-version compatibility decision explicitly changes that policy.

Host-specific certification is separate from .NET Standard compatibility. A host such as Unity should only be advertised as explicitly certified after the actual packaged artifact has been tested in the relevant host/version.

## Publication-owner decisions

Repository-side implementation and package verification can be completed automatically. The following remain owner-controlled publication decisions:

- licensing or commercial licensing terms;
- NuGet.org package ownership and publishing credentials;
- optional package signing;
- external-host certification claims.

These decisions do not change the `0.1.0` .NET Standard 2.0 runtime baseline.
