; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

; Release headers must be System.Version values with four components, the same mapping as the IDE
; extension versions (eng/release/ide-versions.mjs): a plain release x.y.z, which every release
; before 1.0 is (0.8.0 is 0.8.0.1000), ends in 1000; x.y.z-preview.N is x.y.z.N and x.y.z-rc.N is
; x.y.z.(500 + N). The older headers 0.6.0.N are the former 0.6.0-preview.N releases. Headers
; then sort in release order.

## Release 0.6.0.1

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
RTR0001 | Runic.Translations | Error | Translation source is unreadable or malformed
RTR0002 | Runic.Translations | Error | Translation inputs are duplicated or ambiguous
RTR0003 | Runic.Translations | Error | Translation project schema is not supported
RTR0004 | Runic.Translations | Error | Locale is invalid or not declared
RTR0006 | Runic.Translations | Error | Catalog ID or generated code name is invalid
RTR0009 | Runic.Translations | Error | Base locale defines no messages
RTR0010 | Runic.Translations | Error | Locale lacks a translation for a base-locale message
RTR0011 | Runic.Translations | Error | Locale defines a message the base locale does not have
RTR0012 | Runic.Translations | Error | Locale fallback is invalid
RTR0013 | Runic.Translations | Error | Locale fallback does not reach the base locale
RTR0014 | Runic.Translations | Error | Message pattern is malformed
RTR0016 | Runic.Translations | Error | Translation changes the message's caller inputs
RTR0018 | Runic.Translations | Error | Generated name collides or is reserved
RTR0019 | Runic.Translations | Error | Project member or source encoding is invalid
RTR0021 | Runic.Translations | Warning | Message has an empty variant
RTR0022 | Runic.Translations | Error | Compiler limit exceeded
RTR0024 | Runic.Translations | Error | Referenced Runic.Translations runtime ABI is incompatible
RTR0031 | Runic.Translations | Error | Built-in formatter does not support the content locale
RTR0041 | Runic.Translations | Error | MF2 variable or function is invalid
RTR0050 | Runic.Translations | Error | RMF2 resource syntax is invalid
RTR0051 | Runic.Translations | Error | Message metadata is invalid or unknown
RTR0052 | Runic.Translations | Error | Translation source layout is invalid
RTR0054 | Runic.Translations | Error | Resource is declared more than once or conflicts
RTR0060 | Runic.Translations | Error | Markup contract is invalid
RTR0061 | Runic.Translations | Error | Inline markup is invalid
RTR0062 | Runic.Translations | Error | Markup slot or project markup reference is invalid
RTR0065 | Runic.Translations | Error | Message is not executable in the RMF2 profile
RTR0066 | Runic.Translations | Error | MF2 syntax error
RTR0067 | Runic.Translations | Error | MF2 data model is invalid

## Release 0.6.0.3

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
RTR0068 | Runic.Translations | Warning | Referenced runtime lacks the readable C# surface
RTR0069 | Runic.Translations | Warning | Readable C# name is reserved or clashes

## Release 0.6.0.4

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

## Release 0.6.0.5

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
RTR0080 | Runic.Translations | Error | Translation XAML declaration is invalid
RTR0081 | Runic.Translations | Error | Translation XAML message key is unknown
RTR0082 | Runic.Translations | Error | Translation XAML inputs do not match
RTR0083 | Runic.Translations | Error | Translation XAML message kind does not match

## Release 0.6.0.7

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
RTR0084 | Runic.Translations | Warning | Translation XAML binds several inputs by position
RTR0085 | Runic.Translations | Info | Translation XAML keys under an explicit source are not checked
