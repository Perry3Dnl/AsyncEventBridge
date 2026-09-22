# Release readiness — 1.0.0

This document is the release gate for AsyncEventBridge 1.0.0.

Version 1.0 is a stability and adoption release. New functionality is accepted only when it closes a concrete interoperability, safety, compatibility, or adoption gap. The product boundary and feature-freeze rules are defined in [1.0-stability-contract.md](1.0-stability-contract.md).

## Release identity

- Release target: `1.0.0`
- Current stabilization package version: `1.0.0-preview.1`
- NuGet package ID: `AsyncEventBridge`
- Modern runtime target: `.NET 10` (`net10.0`)
- Compatibility runtime target: `.NET Standard 2.0` (`netstandard2.0`)
- Roslyn generator host target: `netstandard2.0`
- Unity package ID: `com.perry3d.async-event-bridge`
- Unity baseline: `2023.1.0f1+`
- License: `MPL-2.0`

The repository may use prerelease package versions while this gate is incomplete. The final stable version must not be published until every applicable gate below is satisfied.

## Branch policy

`release/1.0.0-stabilization` is the 1.0 integration line.

Before final publication:

1. the stabilization line must be green;
2. the intended 1.0 commit must be merged to `main`;
3. `main` must rerun the complete release gate successfully;
4. the stable tag/release must point at that validated commit.

After 1.0, `main` is the stable product line. Release branches are temporary integration lines rather than permanent product forks.

## Public API freeze

Before 1.0 final:

- exported types, members, optional parameters, enum values, and defaults are covered by API-lock tests;
- public nullability has been reviewed;
- cancellation, timeout, disposal, buffering, subscriber-exception, and cleanup semantics are documented;
- generator names, collision behavior, accessibility, inheritance behavior, and diagnostics are locked;
- no known rename/removal is postponed until after 1.0;
- every public type has XML documentation suitable for IDE consumption.

Any public API that is not ready to be supported for years must change before this gate closes.

## Unified NuGet gate

The final NuGet artifact is one `AsyncEventBridge.1.0.0.nupkg` with at least:

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

CI must validate that:

- `net10.0` and future compatible modern TFMs select the modern generator;
- older compatible consumers select the compatibility generator;
- dependency groups match their runtime assets;
- the package builds clean consumers without project references;
- runtime smoke consumers execute successfully;
- repository/source metadata, license, icon, and README are present;
- symbol/source support covers all shipped runtime assets;
- package contents are deterministic and contain no accidental development artifacts.

## Modern .NET gate

CI must:

1. restore/build the modern solution in Release;
2. run runtime, generator, lifecycle, race, metrics, public-API, and stress tests;
3. execute the maintained samples;
4. pack and inspect the modern validation package;
5. compile an isolated package consumer;
6. execute a packaged runtime consumer;
7. publish and execute the Native AOT package consumer;
8. reject trimming/AOT warnings;
9. validate on Windows, Linux, and macOS;
10. retain benchmark projects and review material regressions before release.

## .NET Standard 2.0 gate

CI must:

1. restore/build the compatibility solution;
2. run compatibility runtime, generator, lifecycle, race, public-API, and stress tests;
3. execute compatibility samples;
4. pack and inspect the compatibility asset;
5. compile and execute consumers from the packed artifact;
6. validate on Windows, Linux, and macOS;
7. verify the unified package selects the compatibility runtime/generator for consumers below the modern baseline.

Compatibility is a complete supported edition for the behavior it promises; it does not need artificial shims for modern-only runtime facilities.

## Unity gate

Before 1.0 final, repository validation must cover:

1. package manifest/version consistency;
2. portable core convergence with the compatibility baseline;
3. Unity asset metadata;
4. Unity generator build and packaging;
5. compile-smoke validation against Unity API stubs;
6. Runtime and Editor Unity Test Framework tests in a supported Unity Editor;
7. IL2CPP acceptance;
8. import and execution of the shipped sample;
9. documented installation and supported-editor baseline.

Stub compilation is not a substitute for actual Unity/IL2CPP acceptance.

## Reliability gate

Regression coverage must explicitly exercise:

- event/setup races;
- event/cancellation races;
- event/timeout races;
- disposal/publication races;
- concurrent event producers;
- bounded-buffer drop behavior;
- source completion versus lifecycle stop;
- stop-before-start behavior;
- reconnect/repeat cycles;
- subscriber failures;
- cleanup failures;
- source generators over generics, inheritance, custom delegates, third-party targets, and naming collisions.

Stress tests should focus on synchronization boundaries and resource cleanup, not merely high iteration counts.

## Documentation and adoption gate

Before 1.0 final:

- the README leads with the problem and before/after developer experience;
- installation instructions match the actually published package;
- common one-shot, stream, lifecycle, reverse-bridge, third-party, and Unity scenarios have compiling examples;
- framework and Unity support are obvious;
- migration notes from pre-1.0 are available;
- release notes summarize behavioral contracts and any breaking pre-1.0 cleanup;
- the default branch reflects the stable product;
- a GitHub release/tag exists for the final version;
- NuGet metadata is complete and searchable.

Download count is not itself a release criterion. The release criterion is that avoidable adoption friction has been removed.

## Owner-controlled publication decisions

These remain owner-controlled even after automated gates are green:

- NuGet.org ownership and publishing credentials;
- package signing, if used;
- stable tag/GitHub Release publication;
- Unity Asset Store submission;
- external certification or compatibility claims.

Passing CI means the repository is release-ready; it does not itself publish the package.
