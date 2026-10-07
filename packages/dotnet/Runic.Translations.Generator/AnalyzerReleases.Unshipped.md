; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md
; When a release ships, move these rows to AnalyzerReleases.Shipped.md under its header; see the
; version mapping there (0.6.0-preview.N is 0.6.0.N, the final 0.6.0 is 0.6.0.1000).

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
RTR0070 | Runic.Translations | Error | Message content kind is invalid
RTR0071 | Runic.Translations | Warning | Variant uses another variant's document structure
RTR0072 | Runic.Translations | Error | Document element is in an invalid position
RTR0073 | Runic.Translations | Error | Document exceeds a structure limit
RTR0074 | Runic.Translations | Error | Translated document structure does not match the source
RTR0076 | Runic.Translations | Warning | Document block or list is empty
RTR0077 | Runic.Translations | Warning | Document heading skips a level
RTR0078 | Runic.Translations | Warning | Line break between Southeast Asian characters became a space
