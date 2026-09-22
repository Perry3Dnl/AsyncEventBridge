; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|--------------------
AEB001 | AsyncEventBridge | Warning | Reports event delegates that cannot be bridged by generated Unity APIs.
AEB002 | AsyncEventBridge | Warning | Reports invalid `GenerateAsyncEventsFor` target types.
AEB003 | AsyncEventBridge | Warning | Reports duplicate or redundant async-event generation requests.
