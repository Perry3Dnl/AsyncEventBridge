# Release readiness

This document records the release contract for AsyncEventBridge `0.4.0`.

Version 0.4.0 continues the convergence release model established in 0.3. Modern .NET, .NET Standard 2.0 compatibility, and Unity are developed from one release line and must reach the same applicable readiness gate before the version is released.

See [0.3-convergence.md](0.3-convergence.md) for the historical convergence checklist; the 0.4-specific behavioral contracts are documented in the `0.4-*` documents in this directory.

## Release identity

- Version: `0.4.0`
- NuGet package ID: `AsyncEventBridge`
- Modern runtime target: `.NET 10` (`net10.0`)
- Compatibility runtime target: `.NET Standard 2.0` (`netstandard2.0`)
- Roslyn generator host target: `netstandard2.0`
- Unity package ID: `com.perry3d.async-event-bridge`
- Unity baseline: `2023.1.0f1+`
- License: `MPL-2.0`

The release version comes from the root `Directory.Build.props`. The Unity manifest must match it. Compatibility projects inherit it from the repository root.

## Branch policy

Starting with 0.3.0, `main` is the product release line; `release/**` branches are temporary release-integration lines and are covered by CI.

The compatibility implementation lives under `compat/netstandard2.0`; the Unity package lives under `Packages/com.perry3d.async-event-bridge`. Historical split branches may remain available for reference, but fixes and release work should converge on `main`.

## NuGet release target

The intended final NuGet artifact is one `AsyncEventBridge.0.4.0.nupkg` containing both runtime assets:

```text
lib/net10.0/AsyncEventBridge.dll
lib/net10.0/AsyncEventBridge.xml
lib/netstandard2.0/AsyncEventBridge.dll
lib/netstandard2.0/AsyncEventBridge.xml
tools/generators/modern/AsyncEventBridge.Generators.dll
tools/generators/compat/AsyncEventBridge.Generators.dll
buildTransitive/AsyncEventBridge.targets
README.md
assets/AsyncEventBridge.png
```

CI still builds the modern and compatibility packages separately to validate each asset in isolation, then builds a unified package through `packaging/AsyncEventBridge.Package`. The unified package is consumed by modern and compatibility smoke/runtime consumers and by the modern Native AOT consumer. Release closure still requires the final symbol/source-metadata shape to cover both runtime assets.

A matching symbol package must cover the shipped runtime assets and repository/source metadata.

## Modern .NET gate

CI must:

1. restore/build the modern solution in Release;
2. run runtime, generator, lifecycle, race, metrics, API-lock, and stress tests;
3. execute the sensor-monitoring sample;
4. build the modern validation package and symbol package;
5. validate package metadata and contents;
6. compile an isolated package consumer;
7. execute a packaged runtime consumer;
8. publish and execute the Native AOT package consumer;
9. reject trimming/AOT warnings;
10. restore/build/test on Windows and macOS.

Modern-only APIs remain valid when they solve a concrete problem using current runtime capabilities.

## .NET Standard 2.0 gate

CI must:

1. restore/build the compatibility solution from `compat/netstandard2.0`;
2. run compatibility runtime, generator, lifecycle, race, public-API, and stress tests;
3. execute the compatibility sensor sample;
4. build the compatibility validation package at the shared version;
5. compile a `netstandard2.0` consumer from that package;
6. execute a runtime consumer on a compatible .NET runtime;
7. validate the compatibility solution on Windows and macOS;
8. verify the final combined NuGet selects the compatibility asset for consumers that cannot use the modern asset.

Compatibility is a complete supported edition even when it does not expose modern-only APIs.

## Unity gate

CI/release validation must:

1. verify `package.json` matches the shared version;
2. verify Unity portable core source matches the compatibility baseline;
3. verify Unity asset metadata;
4. build the Unity generator source;
5. compile the package runtime against Unity API stubs;
6. verify the bundled Unity generator is reproduced from the current 0.4 source;
7. run Runtime and Editor Unity Test Framework tests in an actual supported Unity Editor;
8. run an IL2CPP acceptance build;
9. validate the importable package sample.

Stub compilation is a repository gate, not a substitute for real Unity/IL2CPP acceptance.

## Feature parity policy

Release-readiness parity is required; feature parity is not.

For example, `TimeProvider`, Native AOT-specific validation, current-BCL metrics, or other modern runtime capabilities do not need compatibility shims merely to make the API lists identical. Unity-native `Awaitable`, `UnityEvent`, Inspector, and lifecycle APIs likewise remain Unity-specific.

Every edition must instead have equivalent confidence in the behavior it actually promises.

## Owner-controlled publication decisions

Repository-side implementation and verification can be automated. These remain owner-controlled:

- NuGet.org package ownership and publishing credentials;
- package signing, if desired;
- release/tag publication;
- Unity Asset Store submission and commercial metadata;
- external certification claims.

Merging validated 0.4 release source to `main` does not itself publish any package.
