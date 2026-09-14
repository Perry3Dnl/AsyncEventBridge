# Release readiness

This document tracks what must be true before the first AsyncEventBridge package is treated as a stable release.

## Technical baseline

The minimum complete runtime contract is **.NET Standard 2.0**. The package must not expose a reduced feature set on that target.

The runtime package contains:

- `lib/netstandard2.0/AsyncEventBridge.dll`;
- `lib/netstandard2.0/AsyncEventBridge.xml`;
- `analyzers/dotnet/cs/AsyncEventBridge.Generators.dll`;
- the package README and icon.

The source generator is delivered from the same `AsyncEventBridge` package so consumers do not need to install a second package for generated event APIs.

## CI release gates

Every change must keep these checks green:

1. restore the full solution;
2. build the full solution in Release configuration;
3. run runtime, generator, and stress tests;
4. compile the .NET Standard 2.0 / C# 8 compatibility consumer;
5. build and run the sensor monitoring sample;
6. create a local NuGet package;
7. inspect the package for the runtime assembly, XML documentation, source generator, README, and icon;
8. restore and compile a separate .NET Standard 2.0 consumer from that local NuGet package only;
9. restore and execute a separate runtime consumer from the local NuGet package, covering generated Event -> Task behavior, Task<T> -> Events, and IAsyncEnumerable<T> -> Events;
10. retain the generated `.nupkg` as a CI artifact for manual host validation.

The package-consumer checks are deliberately separate from project-reference tests. They verify what a real NuGet consumer receives rather than only what works inside the repository.

## Public API freeze checklist

Before a stable `1.0.0` release:

- public type names are locked by tests;
- public method, event, and property names are locked by tests;
- the complete generated one-shot and stream overload sets compile against .NET Standard 2.0 / C# 8;
- `Event -> Task`, `Event -> IAsyncEnumerable<T>`, `Task -> Events`, `Task<T> -> Events`, and `IAsyncEnumerable<T> -> Events` behavior is covered;
- cancellation, timeout, reentrancy, cleanup, disposal, race, validation, and subscriber-exception behavior is covered;
- generated accessibility never widens the source API;
- generic, nested, inherited, hidden-member, and collision cases compile correctly;
- README examples match the actual API;
- the sensor monitoring sample builds and runs from CI;
- the packed artifact can be restored, compiled, and executed by isolated consumers.

## Decisions outside the codebase

These items cannot be finalized safely as implementation details because they are release-owner decisions:

- the license or commercial licensing terms;
- the final first-release version number;
- whether package signing is required;
- NuGet.org ownership, API key, and publishing permissions;
- which external hosts are advertised as explicitly certified, for example specific Unity versions.

A host should only be called "certified" after the actual packaged artifact has been tested in that host. Compatibility by .NET Standard contract is broader than host-specific certification.

## Compatibility policy

Normal feature work must not raise the .NET Standard 2.0 minimum requirement. Newer target frameworks can be added later when they provide a concrete compatibility or performance benefit, but they must remain additive to the baseline contract rather than replacing it.
