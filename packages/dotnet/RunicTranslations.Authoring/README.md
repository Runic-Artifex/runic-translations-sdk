# RunicTranslations.Authoring

Build editors and workspace tools on supported Runic Translations project creation, discovery, mutation, transaction, recovery, and review-state APIs. The package validates proposed changes with the compiler and provides safe filesystem operations without imposing a user interface.

## Install

```bash
dotnet add package RunicTranslations.Authoring --version <VERSION>
```

Replace `<VERSION>` with the current preview shown on NuGet. The package targets .NET 10 and depends on the matching `RunicTranslations.Compiler` package.

## Create a validated project

```csharp
using RunicTranslations.Authoring;

var request = new TranslationProjectCreationRequest(
    directory: "Resources",
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

Choose `RunicTranslations.Authoring` when building a translation editor, project wizard, workspace migration, or other tool that must inspect and change source catalogs. Use `RunicTranslations.Tool` or `RunicTranslations.Templates` when you only need a ready-made command or scaffold. Runtime applications do not need this package.

Mutation and recovery APIs use expected revisions and contained paths to detect concurrent or unsafe changes. Callers still own user authorization, backups, source control, and any product-specific review workflow.

## Compatibility and status

This package is a public preview for .NET 10. Preview APIs and workspace operations may change with documented migrations. Keep it on the same exact release as the compiler and any CLI or editor that exchanges its project contracts.

- [Project creation example](https://github.com/Runic-Artifex/runic-translations/blob/main/dotnet/tests/RunicTranslations.Authoring.Tests/ProjectCreationTests.cs)
- [Workspace authoring examples](https://github.com/Runic-Artifex/runic-translations/tree/main/dotnet/tests/RunicTranslations.Authoring.Tests)
- [Schema-v2 guide](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/schema-v2.md)
- [Runic Translations Editor](https://github.com/Runic-Artifex/runic-translations-editor)
- [Issues and support](https://github.com/Runic-Artifex/runic-translations/issues)

Licensed under the [MIT License](https://github.com/Runic-Artifex/runic-translations/blob/main/LICENSE). See [Third-Party Notices](https://github.com/Runic-Artifex/runic-translations/blob/main/THIRD-PARTY-NOTICES.md) for attribution.
