# Localized command-line app

A Runic.CommandLine app translated with Runic Translations and
`Runic.Translations.CommandLine`, without a key mapping. The catalog in
`translations/` holds only the app's own text: the `DescriptionKey` values and
the greeting. Help labels, errors and faults come from the adapter's built-in
English and German catalogs.

The project builds against this repository's sources. An app uses the
`Runic.CommandLine`, `Runic.Translations`, `Runic.Translations.Build` and
`Runic.Translations.CommandLine` packages instead.

From the repository's development shell:

```sh
dotnet build examples/command-line/localized -c Release
RCLI_EXAMPLE_CULTURE=de dotnet run --project examples/command-line/localized -c Release --no-build -- greet Ada
RCLI_EXAMPLE_CULTURE=de dotnet run --project examples/command-line/localized -c Release --no-build -- greet --help
RCLI_EXAMPLE_CULTURE=de dotnet run --project examples/command-line/localized -c Release --no-build -- greet --unknown
RCLI_EXAMPLE_CULTURE=de dotnet run --project examples/command-line/localized -c Release --no-build -- greet
```

They print `Hallo Ada`, German help that starts with
`Aufruf: localized greet <name> [Optionen]`, and, with exit code 2:

```text
Die Option '--unknown' ist unbekannt.
Aufruf: localized greet <name> [Optionen]
Weitere Informationen erhalten Sie mit "localized greet --help".
```

Without the name, also with exit code 2, the error names the argument by its
placeholder, as the usage line does (Runic.CommandLine 0.13 and later):

```text
Das Argument <name> fehlt.
Aufruf: localized greet <name> [Optionen]
Weitere Informationen erhalten Sie mit "localized greet --help".
```

Since Runic.CommandLine 0.10 a usage error has no `RCLI` code prefix in human
output (`--format json` carries the code) and ends with the usage line and a
help hint. With `RCLI_EXAMPLE_CULTURE=en` the same commands print the
framework's English text.

`bun eng/run.mjs native-aot` publishes the example with NativeAOT, with trim and
AOT warnings treated as errors, and checks these outputs. See the
[command-line guide](../../../docs/guides/translations/command-line.md) for the
key naming rule and resolution order.
