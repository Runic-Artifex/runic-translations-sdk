# Runic.Translations.Authoring

Create validated Runic Translations projects. The assembly renders a project plan, checks it with the compiler and writes it with safe filesystem operations, without imposing a user interface.

## Install

This assembly is not published as a standalone package: it ships inside the `Runic.Translations.Tooling` package and is not a separate public package.

## Create a validated project

```csharp
using Runic.Translations.Authoring;

var request = new TranslationProjectCreationRequest(
    directory: "translations",
    catalogId: "app",
    defaultLocale: "en",
    codeNamespace: "Example.Translations",
    className: "AppText",
    additionalLocales: [new TranslationProjectLocale("de", "en")]);

TranslationProjectPlan plan = TranslationProjectScaffolder.Render(request);
string createdDirectory = TranslationProjectWriter.Create(plan);

Console.WriteLine(createdDirectory);
```

`Render` is side-effect free and returns the exact UTF-8 files plus their successful compilation. `Create` commits the complete plan to a target that does not already exist; conflicts and unsafe linked target parents fail without overwriting an existing workspace.

## When to choose this package

Consume these APIs through the `Runic.Translations.Tooling` package when building a project wizard or other tool that creates translation projects. Use `dotnet-runic-translations` or `Runic.Translations.Templates` when you only need a ready-made command or scaffold. Runtime applications do not need this assembly.

New scaffolds create one `{locale}.rmf2` source per declared locale, including
empty starter-free locales.

The workspace mutation, transaction and recovery operations, the RMF2 resource
writer and the workspace language service are internal: they serve the
`runic-translations` tool and the Translations Editor, which are built from the
same release. The editor's review state (`runic.translations.editor-state/1`)
belongs to the editor product.

## Compatibility and status

This assembly targets .NET 10. Before 1.0 a minor release may contain breaking changes, each listed with upgrade steps in the release notes (see [Versions before 1.0](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/eng/release/README.md#versions-before-10)). Keep it on the same exact release as the compiler and any CLI or editor that exchanges its project contracts.

- [Project creation example](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/tests/dotnet/Runic.Translations.Authoring.Tests/ProjectCreationTests.cs)
- [Workspace authoring examples](https://github.com/Runic-Artifex/runic-translations-sdk/tree/main/tests/dotnet/Runic.Translations.Authoring.Tests)
- [RMF2 project guide](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/rmf2.md)
- [Runic Translations Editor](https://github.com/Runic-Artifex/runic-translations-sdk/tree/main/apps/translations-editor): built from source; the editor is a separate product with its own releases.
- [Issues and support](https://github.com/Runic-Artifex/runic-translations-sdk/issues)

Licensed under the [MIT License](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/LICENSE). See [Third-Party Notices](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/specs/translations/THIRD-PARTY-NOTICES.md) for attribution.
