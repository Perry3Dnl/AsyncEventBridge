# Support matrix

AsyncEventBridge has one product contract across three deployment lines, but it does not force artificial API parity where the underlying platforms differ.

## Baselines

| Edition | Baseline | Primary distribution |
| --- | --- | --- |
| Modern .NET | `net10.0` | NuGet `AsyncEventBridge` |
| .NET compatibility | `netstandard2.0` | Same NuGet package |
| Unity | Unity `2023.1.0f1+` | UPM `com.perry3d.async-event-bridge` |

The unified NuGet package selects the most capable compatible runtime and generator for the consuming target framework.

## Capability matrix

| Capability | Modern .NET | .NET Standard 2.0 | Unity 2023.1+ |
| --- | --- | --- | --- |
| One-shot CLR event wait | Generated `Task<T>` + low-level API | Generated `Task<T>` + low-level API | Generated `Awaitable<T>` + Unity-aware low-level API |
| CLR event payload model | Normal non-ref-like payloads, including value types | `EventArgs`-based payloads | `EventArgs`-based generated CLR waits |
| Third-party class generation | Yes | Yes | Yes |
| Compatible custom two-parameter delegates | Yes | Yes, with `EventArgs` payload | Yes, with `EventArgs` payload |
| Generated CLR event streams | Yes | Yes | Use portable low-level stream APIs; UnityEvent streams are Unity-native |
| `UnityEvent` waits/streams | — | — | Yes |
| `EventCondition.WaitUntilAsync` | Yes | Yes | Yes in portable core |
| `StartAfter` / `TakeUntil` | Yes | Yes | Yes in portable core |
| `RepeatBetween` / `RepeatWhile` | Yes | Yes | Yes in portable core |
| Lifecycle marker variants | Yes | Yes | Yes in portable core |
| Sender-aware `EventOccurrence` APIs | Yes | — | — |
| `EventComposition.WaitAny/WaitAll` | Yes | — | — |
| `Task` / `Task<T>` to CLR events | Yes | Yes | Yes in portable core |
| `ValueTask` / `ValueTask<T>` to CLR events | Yes | — | — |
| `IAsyncEnumerable<T>` to CLR events | Yes | Yes | Yes in portable core |
| Subscriber exception policy | Yes | Yes | Yes in portable core |
| Unbounded / bounded event buffering | Yes | Yes | Yes |
| `DroppedCount` / `DropObserver` | Yes | — | — |
| `System.Diagnostics.Metrics` instruments | Yes | — | — |
| `TimeProvider` timeout overloads | Yes | — | — |
| Native AOT/trimming verification | Yes | N/A as a library target | — |
| Unity owner-destruction / app-exit cancellation | — | — | Yes |
| IL2CPP release acceptance | — | — | Required by the 1.0 Unity release gate |

## Why the APIs differ

The compatibility line exists so applications and libraries can use the core event/async bridge on older target frameworks. It does not emulate newer BCL capabilities merely to make every public API identical.

Modern-only APIs are used where the runtime provides a materially better primitive, for example:

- `TimeProvider` for deterministic timeout control;
- `System.Threading.Channels` drop callbacks for exact drop telemetry;
- `System.Diagnostics.Metrics`;
- strongly typed sender/event payload shapes available on the modern target;
- direct `ValueTask` bridge paths.

Unity likewise uses Unity-native primitives where they improve semantics rather than pretending the engine is ordinary server/desktop .NET:

- `Awaitable`;
- `MonoBehaviour.destroyCancellationToken`;
- application-exit cancellation;
- `UnityEvent`;
- Unity main-thread publication.

## Source-generator selection

The unified NuGet package selects the compatibility generator for targets below the modern baseline and the modern generator for `net10.0`-compatible targets.

The package CI also verifies one multi-targeting consumer that builds both `netstandard2.0` and `net10.0` from the same package reference, so generator selection is tested in the shape commonly used by reusable libraries.

## Stability policy

Feature parity is not a 1.0 requirement. Behavioral confidence is.

Each edition must have:

- deterministic subscription and cleanup semantics for the APIs it exposes;
- target-appropriate cancellation behavior;
- API-lock coverage;
- packed/distributed-artifact validation;
- platform-specific release acceptance.

See [1.0 stability contract](1.0-stability-contract.md) and [release readiness](release-readiness.md).
