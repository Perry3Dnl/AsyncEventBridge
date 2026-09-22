# Unity 1.0 acceptance

AsyncEventBridge 1.0 requires one real Unity Editor acceptance pass in addition to the normal repository CI.

Normal CI validates package metadata, portable-core convergence, the Unity generator build, and compilation against Unity API stubs. Those checks are intentionally not treated as substitutes for a real Unity Editor or IL2CPP run.

## Workflow

Run the GitHub Actions workflow:

```text
Unity 1.0 Acceptance
```

The workflow is manual (`workflow_dispatch`) so ordinary pushes do not depend on Unity activation credentials.

It performs three acceptance checks against Unity `2023.1.0f1`, the declared minimum editor version:

1. package EditMode tests;
2. package PlayMode tests;
3. a `StandaloneLinux64` player build using the IL2CPP scripting backend.

The test jobs use the UPM package directly in package mode.

The IL2CPP job opens `tests/UnityAcceptanceProject`, which references the repository's local UPM package and compiles:

- the portable AsyncEventBridge runtime;
- the Unity-specific API;
- a generated async event facade;
- `Task` to event bridging;
- `UnityEvent` waiting.

The acceptance build creates its smoke scene during the build and does not require a maintained binary scene asset.

## Unity credentials

GameCI requires Unity activation credentials.

Configure the repository Actions secrets appropriate to the Unity license in use:

```text
UNITY_LICENSE
UNITY_EMAIL
UNITY_PASSWORD
UNITY_SERIAL
```

A Personal-license setup normally uses the activated license file together with the account credentials. A Professional-license setup can use its serial credentials. Unused secret values may remain absent when the selected GameCI activation path does not require them.

Do not place Unity license material in repository files.

## Pass criteria

The 1.0 Unity gate is complete only when the acceptance workflow shows:

```text
Unity package editmode tests   success
Unity package playmode tests   success
Unity IL2CPP acceptance        success
```

The generated test artifacts and IL2CPP build artifact should be retained with the successful workflow run.

## Release policy

A successful stub compile alone is not enough to mark Unity release-ready.

The final stable 1.0 commit must have a successful Unity acceptance run on the same release candidate code, or on a commit whose only later changes cannot affect Unity/runtime/package behavior.

If the acceptance run exposes an engine/compiler-specific issue, fix the underlying package or explicitly narrow the supported Unity baseline before publishing 1.0.
