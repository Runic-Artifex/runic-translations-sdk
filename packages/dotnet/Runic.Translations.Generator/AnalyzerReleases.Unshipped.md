; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md
; When a release ships, move these rows to AnalyzerReleases.Shipped.md under its header; see the
; version mapping there (0.6.0-preview.N is 0.6.0.N, the final 0.6.0 is 0.6.0.1000).

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
RTR0080 | Runic.Translations | Error | Translation XAML declaration is invalid
RTR0081 | Runic.Translations | Error | Translation XAML message key is unknown
RTR0082 | Runic.Translations | Error | Translation XAML inputs do not match
RTR0083 | Runic.Translations | Error | Translation XAML message kind does not match
