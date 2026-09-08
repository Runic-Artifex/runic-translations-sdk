# Runic.Translations.Authoring

Build editors and workspace tools on supported Runic Translations project creation, mutation, transaction, recovery, and review-state APIs. The package validates proposed changes with the compiler and provides safe filesystem operations without imposing a user interface.

## Install

This assembly is not published as a standalone package: it ships inside the preview `Runic.Translations.Tooling` package and is not a separate public package.

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

Consume these authoring APIs through the preview `Runic.Translations.Tooling` package when building a translation editor, project wizard, or other tool that must inspect and change `runic.json` and locale TOML sources (whose values remain MF2 messages). Use `dotnet-runic-translations` or `Runic.Translations.Templates` when you only need a ready-made command or scaffold. Runtime applications do not need this assembly.

Mutation and recovery APIs use expected revisions and contained paths to detect concurrent or unsafe changes. Callers still own user authorization, backups, source control, and any product-specific review workflow.

## Locale document edits and migration

New scaffolds explicitly select `sourceLayout: "locale-toml"` and create one `{locale}.toml` file per declared locale, including empty starter-free locales. Projects without the discriminator retain the legacy `{locale}/{key}.mf2` layout.

`TranslationLocaleWriter.Apply(source, locale, edits)` splices validated UTF-8 key/value spans. Set-value and rename operations preserve all other bytes. Delete removes the key-through-value statement and retains leading and inline comments, indentation, and line endings as document trivia. New entries append in request order using the existing newline convention. Multiple edits to the same key or colliding target keys are rejected.

`TranslationWorkspaceMutation.ApplyLocaleEdits(root, catalogId, changes)` groups edits by physical locale path, requires the containing file's SHA-256 revision, reparses and compiles the proposed project, and returns one replacement per file. Commit it with `TranslationWorkspaceTransaction.Commit(plan)` to retain conflict checks and recoverable staging.

`TranslationWorkspaceMutation.MigrateToLocaleToml(root, catalogId)` is a deterministic, side-effect-free migration preview for existing Runic MF2 catalogs. Its transaction plan creates locale files, changes the layout discriminator, and removes the original active MF2 files. Decoded message bytes are preserved, including original newline and whitespace bytes; existing MF2 formatting semantics remain unchanged. Existing TOML destinations, invalid source projects, and colliding keys/locales fail before writing. Commit uses the same transaction journal, including original bytes for rollback; source control or an external backup is the durable history after successful commit.

## Compatibility and status

This package is a public preview for .NET 10. Preview APIs and workspace operations may change with documented migrations. Keep it on the same exact release as the compiler and any CLI or editor that exchanges its project contracts.

- [Project creation example](https://github.com/Runic-Artifex/runic-sdk/blob/main/tests/dotnet/Runic.Translations.Authoring.Tests/ProjectCreationTests.cs)
- [Workspace authoring examples](https://github.com/Runic-Artifex/runic-sdk/tree/main/tests/dotnet/Runic.Translations.Authoring.Tests)
- [MF2 project convention](https://github.com/Runic-Artifex/runic-sdk/blob/main/docs/guides/translations/mf2-projects.md)
- [Runic Translations Editor](https://github.com/Runic-Artifex/runic-sdk/tree/main/apps/translations-editor)
- [Issues and support](https://github.com/Runic-Artifex/runic-sdk/issues)

Licensed under the [MIT License](https://github.com/Runic-Artifex/runic-sdk/blob/main/LICENSE). See [Third-Party Notices](https://github.com/Runic-Artifex/runic-sdk/blob/main/specs/translations/THIRD-PARTY-NOTICES.md) for attribution.
