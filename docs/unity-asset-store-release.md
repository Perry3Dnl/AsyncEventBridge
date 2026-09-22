# Unity Asset Store release track

Status: **development / not submitted**

This document tracks the commercial Unity release separately from the portable .NET release. Repository development remains public on the `unity` branch for now.

## Release identity

- Package version: `0.2.0` (currently unreleased).
- Package path: `Packages/com.perry3d.async-event-bridge`.
- Technical package name: `com.perry3d.async-event-bridge` (provisional until Unity reserves/accepts the publisher namespace).
- Unity compatibility baseline: `2023.1.0f1` (`unity: 2023.1`, `unityRelease: 0f1`).
- Asset Store target price: **$9.95 USD**.
- Development distribution: public Git install from the `unity` branch remains available during development.

The price is a Publisher Portal setting rather than package metadata, so it is intentionally not encoded in `package.json`.

## Distribution policy during development

Do not delete, privatize, or remove the current Unity package from GitHub as part of release preparation. The `unity` branch is the development channel until the Asset Store release is ready.

The eventual commercial-launch policy for GitHub distribution is a separate decision. Existing versions already distributed under their repository license must be treated according to that license even if future distribution changes.

## Package manifest state

The Asset Store UPM manifest fields are present and CI validates:

- `name`;
- `displayName`;
- `version`;
- `unity`;
- `unityRelease`;
- `author`;
- `description`.

CI also checks that the package folder name matches the manifest `name`, that the Unity package version matches the shared repository version, and that compatibility version strings are structurally valid.

## Owner-controlled decisions before submission

The following items cannot be finalized safely from repository code alone:

1. **Publisher namespace** — confirm that Unity assigns/reserves a namespace compatible with `com.perry3d.async-event-bridge`. If Unity assigns a different namespace, rename the package folder and manifest together before submission.
2. **Publisher author name** — `package.json` currently uses `Perry3Dnl`. Unity requires the manifest author to match the Asset Store publisher name, so update it if the Publisher Portal name differs.
3. **Asset Store licensing presentation** — the 1.0 package declares `MIT`. Pre-1.0 snapshots that were distributed under MPL-2.0 remain subject to the license terms under which they were distributed; the 1.0 relicensing does not rewrite that historical distribution.
4. **Publisher enrollment / identity verification** — complete Unity's UPM publisher enrollment requirements.

## Release gates

Repository-side gates:

- [x] Self-contained UPM package.
- [x] Unity-compatible bundled Roslyn source generator.
- [x] Runtime and Editor test assemblies included.
- [x] Interactive Dialogue + Live Code sample included.
- [x] Stable `.meta` files for importable sample assets.
- [x] `unityRelease` declared for the minimum supported editor release.
- [x] CI verifies shared runtime source parity and UPM metadata.
- [ ] Namespace accepted by Unity.
- [ ] Manifest `author` confirmed against Publisher Portal.
- [ ] Asset Store licensing presentation finalized.
- [ ] Official Asset Store UPM Publishing Tools validator passes.
- [ ] Unity Test Runner passes in the minimum editor (`2023.1.0f1`).
- [ ] Unity Test Runner passes in a current supported Unity 6 editor.
- [ ] Mono player smoke test completed.
- [ ] IL2CPP player smoke test completed.
- [ ] Final listing screenshots captured from the real sample/project.
- [ ] Draft uploaded to Publisher Portal and reviewed before submission.

The GitHub CI is deliberately not treated as a replacement for Unity's official UPM validator or real Editor/player testing.

## Store presentation

Keep the listing focused on four fast-to-understand capabilities:

1. **Await UnityEvents** — show `await choiceSelected.WaitAsync(this)` beside the running Interactive Dialogue sample.
2. **Automatic lifecycle cancellation** — show owner destruction / application exit cancellation without manual listener cleanup.
3. **Events ↔ async in both directions** — show UnityEvent-to-await/stream and Task/stream-to-UnityEvent publication.
4. **Source-generator powered** — show generated strongly typed async APIs with no runtime reflection/code generation.

The Interactive Dialogue + Live Code sample should be the primary source for screenshots because it demonstrates the actual package rather than staged artwork.
