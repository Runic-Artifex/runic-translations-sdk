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
```

They print `Hallo Ada`, German help that starts with
`Aufruf: localized greet <name> [Optionen]`, and
`RCLI1001: Unbekannte Option. (--unknown)` with exit code 2. With
`RCLI_EXAMPLE_CULTURE=en` the same commands print the framework's English text.

`bun eng/run.mjs native-aot` publishes the example with NativeAOT, with trim and
AOT warnings treated as errors, and checks these outputs. See the
[command-line guide](../../../docs/guides/translations/command-line.md) for the
key naming rule and resolution order.
