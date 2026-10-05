# Runic Translations SDK

Runic Translations provides deterministic RMF2 compilation, runtime libraries, build integration, editor tooling, IDE extensions, and Vite integration. It is independently versioned and released from the application SDK.

## Start with packages

Install the .NET 10 SDK and choose one exact published Translations version for
the template, runtime, build package, compiler tool, and web adapters. The source
tree's candidate version is not a promise that those packages are published.

```sh
dotnet new install Runic.Translations.Templates@<VERSION>
dotnet new runic-translations-project --name Example.Translations \
  --catalog app --namespace Example.Translations --className AppText
cd Example.Translations
dotnet tool restore
dotnet build
```

The template creates a class library, an English RMF2 catalog, pinned package and
tool references, generated C# APIs, and build-time ESM output. No SDK source
checkout is needed. Continue with the [.NET consumer quick start](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/quickstart-dotnet.md)
to print the first message from a console application.

- [Vite quick start](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/quickstart-vite.md): install the adapter and local compiler, render a message, and build on clean-checkout CI.
- [Translations Editor](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/apps/translations-editor/README.md): build and launch the companion desktop editor from source. Standalone Editor downloads are not part of this preview.
- [RMF2 guide](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/rmf2.md): source syntax, compiler contracts, and supported execution profile.

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
