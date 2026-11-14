# Framework text

Runic's own packages write some text that the user of an application sees: command-line help and errors,
prompts, validation messages, dialog titles, standard menus. That text comes in two framework catalogs, each
with English and German, which you register next to your own catalog. This page lists them, shows how to
register and override them, lists what stays English on purpose, and ends with the
[German style guide](#german) for both catalogs.

## The catalogs

| Text of | Catalog id | Ships in (NuGet / npm) | Register | Override `from` | Key list |
| --- | --- | --- | --- | --- | --- |
| `Runic.CommandLine`, `.Processes`, `.Spectre`: help, usage errors and their hints, help links, prompts, faults (including the process faults `RCLI6001`–`RCLI6011`), redaction notes | `runic-command-line` | `Runic.Translations.CommandLine` / none | `.Add(TranslationCommandTextResolver.CatalogRegistration)` and `new TranslationCommandTextResolver(context, AppTextCatalog.CatalogId)`; or `AddRunicCommandLineTranslations()` | `"Runic.Translations.CommandLine"` | runic-cli-sdk [`text-keys.md`](https://github.com/Runic-Artifex/runic-cli-sdk/blob/main/docs/guides/command-line/text-keys.md); `translations/text-keys.json` in the `Runic.CommandLine` package |
| `Runic.Platform`, `Runic.Desktop`, `Runic.Application.Views`, `@runic-artifex/views`: validation, field errors, dialog titles, "Untitled", shortcut key names, browser-window error pages, standard macOS menus | `runic-views` | `Runic.Application.Views.Translations` / `@runic-artifex/views-translations` | `AddRunicViewsTranslations()`; or `.Add(RunicViewsTranslations.CatalogRegistration)` with `FollowPlatformTexts` and `MenuTitles`; page: `configureRunicViewsText()` | `["Runic.Application.Views.Translations", "@runic-artifex/views-translations"]` | [Keys](https://github.com/Runic-Artifex/runic-sdk/blob/main/packages/dotnet/Runic.Application.Views.Translations/README.md#keys) in the package README |

Keys and their inputs are API: an override names them, and the build checks it. A message whose meaning
changes gets a new key, and keys are added only in minor releases.

**Versions.** `runic-views` has the Runic SDK's version and depends on the `Runic.Application.Views` of the
same release, so it always matches. The `runic-command-line` sources (`runic.json`, `en.rmf2`, `de.rmf2`,
`text-keys.json`) ship under `translations/` in the `Runic.CommandLine` package, and
`Runic.Translations.CommandLine` compiles them from the exact Command Line release it references. The two
move in lockstep: one Translations minor supports exactly one Command Line minor.

| Runic.CommandLine | Runic.Translations.CommandLine |
| --- | --- |
| 0.13.x | 0.11.x |
| 0.12.x | 0.10.x |
| 0.9.x (from 0.9.1) | 0.9.1 |

Command Line 0.10 and 0.11 have no adapter release. For the resolver itself, see
[Translate a Runic.CommandLine app](command-line.md).

## Register both catalogs in one context

One `TranslationContext` holds your catalog and both framework catalogs, so one locale switch changes all of
them. Without dependency injection, for a command-line app or a GUI app with a command line:

```csharp
TranslationContext translations = TranslationContext.CreateBuilder()
    .Add(AppTextCatalog.Registration)
    .Add(TranslationCommandTextResolver.CatalogRegistration)   // runic-command-line
    .Add(RunicViewsTranslations.CatalogRegistration)           // runic-views (desktop apps)
    .AddOverlays(AppTextCatalog.Overlays)                      // your build-time overrides of all three
    .Build("de");

var app = new CommandApp(GeneratedCommandCatalog.Create())
{
    Culture = CultureInfo.GetCultureInfo("de"),
    TextResolver = new TranslationCommandTextResolver(translations, AppTextCatalog.CatalogId),
};
using var platformTexts = RunicViewsTranslations.FollowPlatformTexts(translations);
await translations.SetLocaleAsync("de");   // application, command-line and Views text together
```

With `Runic.Translations.Hosting`:

```csharp
services.AddRunicTranslations(options => options.InitialLocale = settings.Language)
    .AddCatalog(AppTextCatalog.Registration, context => new AppText(context))
    .AddOverlays(AppTextCatalog.Overlays);
services.AddRunicCommandLineTranslations(AppTextCatalog.CatalogId);   // runic-command-line + ICommandTextResolver
services.AddRunicViewsTranslations();                                 // runic-views
```

`AddRunicCommandLineTranslations(applicationCatalogId)` adds `runic-command-line` to the shared context and
registers an `ICommandTextResolver` over that context; set it as `CommandApp.TextResolver`. The application
catalog you name must be registered with the context. For what `AddRunicViewsTranslations()` sets up, see the
[Runic.Application.Views.Translations README](https://github.com/Runic-Artifex/runic-sdk/blob/main/packages/dotnet/Runic.Application.Views.Translations/README.md#use-it).

**Use the context or builder constructor.** The snapshot and manager constructors of
`TranslationCommandTextResolver` use the adapter's built-in `runic-command-line` snapshot, without the
context's overlays, so a build-time override or a run-time overlay of the command-line text is ignored there.
Use `new TranslationCommandTextResolver(context, applicationCatalogId)`, or the `TranslationContextBuilder`
constructor to resolve in the invocation culture.

## Overrides

### Build time

List the framework catalogs in your `runic.json` and write override files beside your sources:

```json
"overrides": [
  { "catalog": "runic-command-line", "from": "Runic.Translations.CommandLine" },
  { "catalog": "runic-views", "from": ["Runic.Application.Views.Translations", "@runic-artifex/views-translations"] }
]
```

```text
translations/
  runic.json
  overrides/runic-command-line/de.rmf2
  overrides/runic-views/en.rmf2
```

An override file uses the catalog's message names and keeps each message's inputs. To reword the hint line
after a usage error (`hints.usage`):

```rmf2
# translations/overrides/runic-command-line/de.rmf2
hints {
  usage =
    .input {$command :string}
    {{Hilfe zum Aufruf: "{$command}".}}
}
```

The build validates each override against the packed catalog and compiles it into `AppTextCatalog.Overlays`;
register those with `.AddOverlays(...)` as above. A file for a locale the catalog lacks, such as `fr.rmf2`,
adds that locale. Precedence is locale-first: a German user sees the built-in German where you override only
the English text. `runic-translations overrides add <catalog> --locale <tag> --key <key>` scaffolds an
override. Details: [Build-time overrides](composition.md#build-time-overrides).

### Run time

Attach an overlay to a framework catalog, for example packs that ship outside the application:

```csharp
.AddOverlay(TranslationOverlay.FromPacks(TranslationCommandTextResolver.CatalogId, packs, [new TranslationOverlayLocale("fr")]))
```

Overlays are checked key by key; see [Overriding library text](composition.md#overriding-library-text). On the
page, `installLocalePack` from `virtual:runic-translations/runic-views/dynamic` does the same for `runic-views`.

### Removed keys

When a framework release removes a key you override, your build fails with `RTR0103` until you delete the
override. The same error catches a typo: "Catalog 'runic-command-line' has no message 'hints.usgae' to
override. Did you mean 'hints.usage'?". At run time, an overlay or pack message whose key the running catalog
lacks is skipped and listed in `TranslationCatalogStatus.OverlayDiagnostics` with the reason
`TranslationOverlayDiagnosticReason.UnknownKey`, and the framework's text is used. Removed keys are named in
the release notes and in the key list.

### The second path for command-line text

A message in your own catalog named after a framework key, such as `diagnostics.unknownOption`, also
overrides the command-line text through `TranslationCommandTextResolver` (see
[How keys are resolved](command-line.md#how-keys-are-resolved)). It is kept for compatibility, but it is not
checked at build time and has no removed-key diagnostic: a removed key just stops being asked for. Change
framework text with `overrides/runic-command-line/<locale>.rmf2`, and use your own catalog for your
`DescriptionKey` text. Whether this path stays after 1.0 is decided with the 1.0 scope.

## What has no text, and what stays English

**No end-user text**, so nothing to translate: `Runic.Translations`, `.Hosting`, `.Wpf`, `.Build`,
`.Generator`, `.Authoring` and `.Tooling`; the npm packages `translations-svelte`, `translations-sveltekit`
and `vite-plugin-runic-translations`; `Runic.CommandLine.Testing`; the Runic SDK's asset,
server, test and analyzer packages, and its platform packages beyond the dialog titles above.

**Stays English on purpose:**

- exception messages, logs, traces and events, and build diagnostics (`RTRxxxx`, `RCLI9xxx`, `RUNIC*`);
- the `message` fields of the Bridge and the host protocol, which are diagnostics;
- development-only UI, such as the missing-view placeholders of the page outlets and the Vite dev overlay;
- developer and translator tools: `dotnet runic`, `runic-translations`, the Translations Editor and the IDE
  extensions;
- the `[key]` missing-message marker of `Runic.Translations.Wpf`, a deliberate developer signal;
- CS-WebUI's own pages, which live in the CS-WebUI repository; see
  [Runic's own text](https://github.com/Runic-Artifex/runic-sdk/blob/main/packages/dotnet/Runic.Application.Views.CsWebUi/README.md#runics-own-text).

Text your application supplies is yours to translate: menu items, notifications, `[Command]` descriptions,
hosted-region fallbacks, and polkit and macOS authorization messages.

## Check it in your tests

```csharp
foreach (string id in new[] { TranslationCommandTextResolver.CatalogId, RunicViewsTranslations.CatalogId })
    Assert.True(translations.TryGetManager(id, out _), id + " is not registered");
Assert.Contains("de", translations.SupportedLocales);   // every catalog, framework included, has German
```

`SupportedLocales` lists the locales in which every catalog shows its own text, so a locale you add only to
your catalog is missing from it. Both framework catalogs also report such a locale once at run time:

- `runic-views` logs event 1261 (`LocalesWithoutRunicViewsText`) when the first window opens;
- `runic-command-line`: when your catalog has text in the requested language and the framework catalog does
  not, the resolver writes a `warning:` line to standard error and a trace warning, once per resolver.

An override file for that locale, as above, fixes either.

## German

Both catalogs follow one style, so an application's German reads as if it were written in German. Follow it
for new framework text and for your overrides.

- **Formal *Sie*:** "Geben Sie eine Zahl ein.", "Meinten Sie '…'?".
- **Platform terms:** *Abbrechen*, *Datei öffnen*, *Ordner*, *Strg*, *Umschalt*, *Alt*, *Unbenannt*.
- **Command-line terms:** *Aufruf* (usage), *Befehle*, *Optionen*, *Argumente*.
- ***Verzeichnis* on the command line, *Ordner* in the GUI:** path kinds and errors say *Verzeichnis*, dialog
  titles say *Ordner*.
- **Quotes:** ASCII `'…'` and `"…"` in terminal text, which is copied and pasted and may reach consoles without
  Unicode; „…" in GUI text.
- **Punctuation:** full sentences end with a period; labels and headings do not ("Hilfe anzeigen",
  "Datei speichern").

| English | German | Where |
| --- | --- | --- |
| Usage | Aufruf | command line |
| command, Commands | Befehl, Befehle | command line |
| option, Options | Option, Optionen | command line |
| argument, Arguments | Argument, Argumente | command line |
| default | Standard | command line |
| required | erforderlich | command line |
| file | Datei | both |
| directory | Verzeichnis | command line |
| Open Folder | Ordner öffnen | GUI |
| Open File, Save File | Datei öffnen, Datei speichern | GUI |
| Untitled | Unbenannt | GUI |
| Ctrl, Shift, Alt | Strg, Umschalt, Alt | GUI |
| command line | Befehlszeile | command line |
| response file | Antwortdatei | command line |
| environment variable | Umgebungsvariable | command line |
| completions | Vervollständigungen | command line |
| is not recognized | ist unbekannt | command line |
| Did you mean …? | Meinten Sie …? | command line |
| whole number | ganze Zahl | both |
| prompts (are disabled) | Abfragen (sind deaktiviert) | command line |
| Arrow keys, Enter, Space | Pfeiltasten, Eingabetaste, Leertaste | command line |
| Cancelled, Done | Abgebrochen, Fertig | command line |

New or changed German in either catalog carries a `@context` comment saying where the text appears. The
maintainer reviews all new and changed German of both catalogs once per release train, on one generated page,
before the release is approved.
