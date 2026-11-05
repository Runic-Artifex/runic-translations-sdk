; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md
; When a release ships, move these rows to AnalyzerReleases.Shipped.md under its header; see the
; version mapping there (the plain 0.8.0 is 0.8.0.1000, 1.0.0-rc.N is 1.0.0.(500 + N)).

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
RTR0094 | Runic.Translations | Warning | Plural key is never selected in its locale
RTR0095 | Runic.Translations | Warning | Plural categories of the locale fall to the catch-all variant
RTR0096 | Runic.Translations | Warning | Translation drops a placeholder the base displays
RTR0097 | Runic.Translations | Warning | Boolean is formatted as text
RTR0098 | Runic.Translations | Warning | Message is longer than its @maxLength
