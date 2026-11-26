# Translations: intentional differences between implementations

Each row is an intentional difference (D011): the shared rule, what an implementation does instead, and why. A
conformance status file cites a row as `specs/translations/conformance/differences.md#<anchor>` in a `deviation`
entry ([README](README.md)); the driver's `lint` checks that the anchor exists.

| Difference | Shared rule | Implementations | Reason |
| --- | --- | --- | --- |
| <a id="platform-date-data"></a>Localized dates from platform data | A localized `:date`, `:time` or `:datetime` writes the case's `expected` text, within the date and percent corpus's tolerance (U+0020, U+00A0 and U+202F are one space; U+0027 and U+2019 one apostrophe). | `dotnet` and `esm` take date patterns and names from the platform's ICU (.NET's globalization; the JavaScript engine's `Intl`). Where that ICU's locale data differs, they write the text the case lists in `alternates`, with a `note` naming the platform: Apple's ICU for `ja-date-full`, ICU 72 to 77 (CI's Linux libicu74, Windows) for `sv-ax-over-sv-medium`, ICU 72 to 75 for `yo-date-full-weekday`. The driver does not accept an alternate as a pass: it reports the case as failing with the alternate it saw, and the status file records the case as this deviation, which accepts a pass or that failure. | Localized dates and times are `semantic` in the [capability matrix](../capabilities.md) (R2 TR-1): each implementation's `dateTimeSource` decides them. A deviation row replaces per-runner alternates, so an implementation with its own date data (a Rust runtime on ICU4X) records its own differences the same way, case by case, instead of the corpus listing every platform's output. |
