; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md
; When a release ships, move these rows to AnalyzerReleases.Shipped.md under its header; see the
; version mapping there (0.6.0-preview.N is 0.6.0.N, the final 0.6.0 is 0.6.0.1000).

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
RTR0068 | Runic.Translations | Warning | Referenced runtime lacks the readable C# surface
RTR0069 | Runic.Translations | Warning | Readable C# name is reserved or clashes
