# Runic.Translations.CommandLine

`Runic.Translations.CommandLine` translates the help, errors, prompts and
faults of a [Runic.CommandLine](https://github.com/Runic-Artifex/runic-cli-sdk)
app with Runic Translations. It ships English and German text for every
framework key, so a German invocation shows no English framework text and you
write no key mapping.

```csharp
using System.Globalization;
using Runic.CommandLine;
using Runic.CommandLine.Generated;
using Runic.Translations;
using Runic.Translations.CommandLine;

TranslationContext translations = await TranslationContext.CreateBuilder()
    .Add(AppTextCatalog.Registration)
    .Add(TranslationCommandTextResolver.CatalogRegistration)
    .BuildAsync("de");
return await new CommandApp(GeneratedCommandCatalog.Create())
{
    Culture = CultureInfo.GetCultureInfo("de"),
    TextResolver = new TranslationCommandTextResolver(translations, AppTextCatalog.CatalogId),
}.RunAsync(args);
```

## Versions

The adapter carries the Runic Translations version and supports exactly one
Runic.CommandLine minor (lockstep): its package depends on
`Runic.CommandLine` `[0.y.0, 0.(y+1).0-0.0)` and on `Runic.Translations` and
`Runic.Translations.Hosting` of its own minor. Every Runic.CommandLine minor is
followed in the same release train by a Translations release whose adapter
covers every key of it, and patches add no keys. Use the pair from one row:

| Runic.Translations.CommandLine | Runic.CommandLine |
| --- | --- |
| 0.10.x | 0.12.x |
| 0.9.x | 0.9.x |

Runic.CommandLine 0.10 and 0.11 have no adapter release; use 0.12 with 0.10.
Another combination is NuGet warning `NU1608` at restore and can show English
framework lines. The text itself comes from the Runic.CommandLine package: it
carries the `runic-command-line` catalog sources under `translations/`
(`runic.json`, `en.rmf2`, `de.rmf2` and the key list `text-keys.json`), and this
package compiles exactly the text of the Command Line release it is built
against, so a key and its German cannot drift apart.

## Framework text and your text

Framework text comes from the `runic-command-line` catalog (English and
German). Your catalog overlays it: it translates your `DescriptionKey` values
and can override any framework text. Precedence is locale-first: for each
locale of the requested locale's fallback chain, your message is tried before
the built-in one, and only then the next locale, so the built-in German beats an
override your catalog has only in English. A key that neither has returns
`null`, so Runic.CommandLine writes its English text.

Lead with a context, as above: the resolver follows the context's locale, and
the context's overlays apply to the framework text, including the build-time
overrides of your `runic.json` (`translations/overrides/runic-command-line/<locale>.rmf2`,
added with `.AddOverlays(AppTextCatalog.Overlays)`). The constructors over an
`ITranslationSnapshot` or `ITranslationManager` use the built-in framework text
without the context's overlays, so a build-time override of
`runic-command-line` is ignored there. To resolve in the invocation culture
(`CommandApp.Culture`) itself, pass a `TranslationContextBuilder`; the resolver
builds one context per culture. When the context's locale names another
language than the culture, the text keeps that locale and the resolver reports
it once: a `warning:` line on standard error, so a shipped console app shows it,
and a trace warning. It reports the same way, once, when your text has a locale
that `runic-command-line` lacks; an override file for that locale adds it.

With `Runic.Translations.Hosting`, `services.AddRunicCommandLineTranslations(AppTextCatalog.CatalogId)`
adds the catalog to the shared context and registers an `ICommandTextResolver`
over it; give that resolver to `CommandApp.TextResolver`.

For other languages, register `TranslationCommandTextResolver.CatalogRegistration`
in the builder and attach a `TranslationOverlay` for
`TranslationCommandTextResolver.CatalogId`, such as French packs:

```csharp
TranslationContextBuilder catalogs = TranslationContext.CreateBuilder()
    .Add(AppTextCatalog.Registration)
    .Add(TranslationCommandTextResolver.CatalogRegistration)
    .AddOverlay(TranslationOverlay.FromPacks(TranslationCommandTextResolver.CatalogId, packs, [new TranslationOverlayLocale("fr")]));
var resolver = new TranslationCommandTextResolver(catalogs, AppTextCatalog.CatalogId);
```

[Framework text](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/framework-text.md)
lists every framework catalog, how to register and override it, and the German
style guide.

## Message names

A text key becomes a message name by removing each hyphen and capitalizing the
letter after it: `help.show-help` is `help.showHelp`, `diagnostics.unknown-option`
is `diagnostics.unknownOption` and `faults.RCLI4000` is unchanged.
`TranslationCommandTextResolver.GetMessageName` applies the rule. Name each
input after the framework argument, or after its position:

```rmf2
commands {
  greet = Grüßt eine Person
}
diagnostics {
  unknownOption =
    .input {$option :string}
    {{Unbekannte Option {$option}.}}
}
```

The [`Runic.CommandLine`](https://www.nuget.org/packages/Runic.CommandLine) README links the framework text key
table, which lists every key and its argument names. A message may use only some of the
arguments; `{$arg0}`, `{$arg1}` and so on name them by position, which also
covers diagnostics your app creates. A message is skipped, and the next source
of text is used, when it declares an input the framework does not supply or
cannot be formatted. Inputs are `:string`, or `:integer` and `:number` for
arguments that are invariant numbers.

Name lookup uses `ITranslationSnapshot.TryGetKey` and `TryGetPlaceholders`,
which compiled snapshots implement. The adapter is trimming- and
NativeAOT-compatible.
