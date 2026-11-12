# Runic Translations SDK

Runic Translations provides deterministic RMF2 compilation, runtime libraries, build integration, editor tooling, IDE extensions, and Vite integration. It is independently versioned and released from the application SDK.

## Start with packages

Install the .NET 10 SDK and choose one exact published Translations version for
the template, runtime, build package, compiler tool, and web adapters. The source
tree's candidate version is not a promise that those packages are published.

```sh
dotnet new install Runic.Translations.Templates@<VERSION>
dotnet new runic-translations-project --name Example.Translations \
  --catalog app --class-name AppText
cd Example.Translations
dotnet tool restore
dotnet build
```

The template creates a class library in the `Example.Translations` namespace, an
English RMF2 catalog, pinned package and tool references, and generated C# APIs;
`--emit-esm` adds build-time ESM output for a web front end. No SDK source
checkout is needed. Continue with the [.NET consumer quick start](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/quickstart-dotnet.md)
to print the first message from a console application.

- [Vite quick start](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/quickstart-vite.md): install the adapter and local compiler, render a message, and build on clean-checkout CI.
- [SvelteKit quick start](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/quickstart-sveltekit.md): add localized routes, a language switcher, and request-scoped SSR to a SvelteKit app.
- [Translations Editor](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/apps/translations-editor/README.md): build and launch the companion desktop editor from source. The editor is a separate product with its own releases; it is not part of the SDK packages.
- [RMF2 guide](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/rmf2.md): source syntax, compiler contracts, and supported execution profile.
- [Migrate from .resx](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/migrate-resx.md) and [from i18next](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/migrate-i18next.md): import an existing catalog with `runic-translations import`.
- [Translate a Runic Views app](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/runic-views.md): one catalog for the page, ViewModels and native text, with the window's and the application's language from runic-sdk 0.17.
- [Ship translations in a library](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/libraries.md): a component library's own catalog in a NuGet and an npm package, overridden by applications; see the [component-library example](https://github.com/Runic-Artifex/runic-translations-sdk/tree/main/examples/component-library).
- [Work with translators and translation tools](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/translators.md): XLIFF 2.1 export and import, review state, stale translations, plurals and coverage gates.
- [Ship translation updates without a release](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/ota.md): load translation packs at run time in .NET and on the web.
- [Command-line apps](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/command-line.md): translate Runic.CommandLine help and errors with `Runic.Translations.CommandLine`, with built-in English and German text.
- [Framework text](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/framework-text.md): the catalogs of Runic's own text (`runic-command-line`, `runic-views`), how to register and override them, and the German style guide.

`Runic.Translations.CommandLine` carries the Translations version and supports
exactly one Runic Command Line minor; use the pair from one row:

| Runic Translations | Runic.CommandLine |
| --- | --- |
| 0.10.x | 0.12.x |
| 0.9.x | 0.9.x |

## Contribute from source

Read [CONTRIBUTING.md](CONTRIBUTING.md), then use the locked environment:

```sh
direnv allow
direnv exec . bun run bootstrap
direnv exec . bun run build
direnv exec . dotnet run --project tests/dotnet/Runic.Translations.Compiler.Tests --configuration Release --no-build
```

Translations components build from this repository. Runic Command Line,
Application, and Platform dependencies use independently pinned published
packages; a sibling SDK checkout is not required. Product guides are canonical
in `docs/guides/translations`; schemas are canonical in `specs/translations/schemas`.
