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

ITranslationSnapshot snapshot = await AppTextCatalog.CreateProvider().GetSnapshotAsync("de");
return await new CommandApp(GeneratedCommandCatalog.Create())
{
    Culture = CultureInfo.GetCultureInfo("de"),
    TextResolver = new TranslationCommandTextResolver(snapshot),
}.RunAsync(args);
```

Framework text comes from the built-in `runic-command-line` catalog (English
and German). Your catalog overlays it: it translates your `DescriptionKey`
values and can override any framework text. Precedence is locale-first: for
each locale of the requested locale's fallback chain, your message is tried
before the built-in one, and only then the next locale, so the built-in German
beats an override your catalog has only in English. A key that neither has
returns `null`, so Runic.CommandLine writes its English text.

Pass an `ITranslationManager` instead of a snapshot to follow locale switches,
or an `ITranslationContext` with your catalog's ID. To resolve in the invocation
culture (`CommandApp.Culture`) itself, pass a `TranslationContextBuilder`; the
resolver builds one context per culture. With a snapshot, manager or context
whose locale names another language than the culture, the text keeps that
locale and the resolver writes one trace warning.

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
