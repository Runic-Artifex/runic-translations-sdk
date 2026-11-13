# Translate a Runic.CommandLine app

`Runic.Translations.CommandLine` connects a
[Runic.CommandLine](https://github.com/Runic-Artifex/runic-cli-sdk) app to
Runic Translations. Its `TranslationCommandTextResolver` translates help
labels, parse and value errors, `CommandPrompts` hints and errors, faults and
your `DescriptionKey` values. It ships English and German text for every
framework key, so you translate only your own text and write no key mapping.

```sh
dotnet add package Runic.CommandLine
dotnet add package Runic.Translations
dotnet add package Runic.Translations.Build
dotnet add package Runic.Translations.CommandLine
```

```csharp
CultureInfo culture = CultureInfo.GetCultureInfo("de");
TranslationContext translations = await TranslationContext.CreateBuilder()
    .Add(CliTextCatalog.Registration)
    .Add(TranslationCommandTextResolver.CatalogRegistration)
    .AddOverlays(CliTextCatalog.Overlays)
    .BuildAsync(culture.Name);
return await new CommandApp(GeneratedCommandCatalog.Create())
{
    Culture = culture,
    TextResolver = new TranslationCommandTextResolver(translations, CliTextCatalog.CatalogId),
}.RunAsync(args);
```

Lead with a context: the framework text then follows the context's locale and
its overlays, including your build-time overrides of `runic-command-line`. The
constructors over a snapshot or a manager use the built-in framework text
without those overlays. With `Runic.Translations.Hosting`,
`services.AddRunicCommandLineTranslations(CliTextCatalog.CatalogId)` registers
the catalog and an `ICommandTextResolver` over the shared context.
[Framework text](framework-text.md) lists both framework catalogs, their
overrides and the German style guide.

Your catalog holds the text behind your `DescriptionKey` values:

```rmf2
commands {
  greet = Grüßt eine Person
}
arguments {
  name = Name der Person
}
```

With `de`, `greet --help` prints `Aufruf: localized greet <name> [Optionen]`
and an unknown option prints `Die Option '--unknown' ist unbekannt.`, the usage
line and `Weitere Informationen erhalten Sie mit "localized greet --help".`. The
[localized example](../../../examples/command-line/localized/README.md) is a
complete app that is also published with NativeAOT in CI.

To follow the user's display language, use `CultureInfo.CurrentUICulture`
instead of a fixed culture. Under `LANG=C` it is the invariant culture, whose
name is empty; `CreateManagerAsync("")` then uses the base locale, and the
built-in framework text is English.

## How keys are resolved

The resolver turns a text key into a message name by removing each hyphen and
capitalizing the letter after it (`TranslationCommandTextResolver.GetMessageName`):

| Text key | Message |
| --- | --- |
| `help.usage` | `help.usage` |
| `help.show-help` | `help.showHelp` |
| `help.path-kind.file` | `help.pathKind.file` |
| `diagnostics.unknown-option` | `diagnostics.unknownOption` |
| `faults.RCLI4000` | `faults.RCLI4000` |
| `hints.RCLI5000` | `hints.RCLI5000` |
| `commands.greet` (a `DescriptionKey`) | `commands.greet` |

Framework text comes from the adapter's `runic-command-line` catalog, which
ships English and German. Your catalog overlays it: a message in your catalog,
found through `ITranslationSnapshot.TryGetKey`, overrides the framework text,
such as `help { usage = Synopsis }`. Precedence is locale-first. For each locale
of the requested locale's fallback chain (`de-CH`, then `de`, then the default
locale), your message is tried before the framework's, and only then the next
locale. So when German is requested, the built-in German `Aufruf` beats an
English-only `help.usage` override that your catalog reaches only by falling
back to English (possible when `translationCompleteness` is relaxed), while a
German override in your catalog beats the built-in German. A key that neither
catalog has in any locale returns nothing, and Runic.CommandLine writes its
English text.

Before 0.9.0 your catalog always came first, so an English override could hide
the built-in German text.

The built-in catalogs have no `faults.RCLI4001` message. Runic.CommandLine shows
the more specific `diagnostics.prompt-*` text for that fault, and would show a
`faults.RCLI4001` message instead whenever that text is the English one. Your
catalog can still define it.

A message receives the framework's arguments through inputs named after them,
as listed in the
[framework text key table](https://github.com/Runic-Artifex/runic-cli-sdk/blob/main/docs/guides/command-line/text-keys.md),
or after their position, `{$arg0}`, `{$arg1}` and so on. A message may use only
some arguments. The resolver skips a message that declares an input the
framework does not supply or that fails to format, and tries the next source.

```rmf2
diagnostics {
  unknownOption =
    .input {$option :string}
    {{Die Option {$option} gibt es nicht.}}
}
```

## Choosing the locale

| Constructor | Locale |
| --- | --- |
| `new TranslationCommandTextResolver()` | The invocation culture; built-in text only. |
| `new TranslationCommandTextResolver(snapshot)` | The snapshot's requested locale. |
| `new TranslationCommandTextResolver(manager)` | The manager's current locale; follows switches. |
| `new TranslationCommandTextResolver(context, CliTextCatalog.CatalogId)` | The `ITranslationContext`'s requested locale; follows switches. |
| `new TranslationCommandTextResolver(builder, CliTextCatalog.CatalogId)` | The invocation culture: one context per culture is built from the `TranslationContextBuilder`. |

Runic.CommandLine formats values in `CommandApp.Culture`. With a snapshot,
manager or context, the text uses that object's locale instead. When the
invocation culture names another language, such as a manager created with the
default `en` and `Culture = de`, the resolver keeps the translation locale, so
the text stays in one language, and reports it once for that resolver: a
`warning:` line on standard error, which reaches the user of a shipped console
app without a trace listener, and a `Trace.TraceWarning`. Create the manager from the invocation
culture, as above, or pass a `TranslationContextBuilder` so the text always follows
the culture:

```csharp
TextResolver = new TranslationCommandTextResolver(
    TranslationContext.CreateBuilder().Add(CliTextCatalog.Registration),
    CliTextCatalog.CatalogId),
```

## Other languages

The built-in catalog has English and German. To add French, or to replace
framework text with packs that ship outside the app, register the framework
catalog in a context and attach an overlay to it. The overlay is checked key by
key (see [catalog composition](composition.md)), and only the keys it has are used;
every other key falls back along the framework catalog's chain:

```csharp
TranslationContextBuilder catalogs = TranslationContext.CreateBuilder()
    .Add(CliTextCatalog.Registration)
    .Add(TranslationCommandTextResolver.CatalogRegistration)
    .AddOverlay(TranslationOverlay.FromPacks(TranslationCommandTextResolver.CatalogId, packs, [new TranslationOverlayLocale("fr")]));
TextResolver = new TranslationCommandTextResolver(catalogs, CliTextCatalog.CatalogId),
```

The packs are the `runic-command-line.fr.locale-v5.json` artifacts that
`runic-translations generate --emit-json` writes for a project whose catalog is
`runic-command-line`; their messages use the framework message names
(`help.usage`, `diagnostics.unknownOption`) and the inputs of the built-in messages. Your own catalog can still
translate framework keys too, as above. A French message in your catalog and one
in the overlay are both French, so your catalog wins.

## Packages and versions

The adapter and Runic.CommandLine move in lockstep: each Translations minor
supports exactly one Runic.CommandLine minor, and its package depends on
`Runic.CommandLine` `[0.y.0, 0.(y+1).0-0.0)`. The adapter compiles the
`runic-command-line` catalog from the sources that the Runic.CommandLine
package carries under `translations/` (`runic.json`, `en.rmf2`, `de.rmf2` and
the key list `text-keys.json`), so it has text for every key of exactly the
Command Line release it is built against, and reads each key's argument names
from that release's `CommandTextKeys`.

| Runic.Translations.CommandLine | Runic.CommandLine |
| --- | --- |
| 0.10.x | 0.12.x |
| 0.9.x | 0.9.x |

Runic.CommandLine 0.10 and 0.11 have no adapter release. Another combination
is NuGet warning `NU1608` at restore, and keys the adapter does not know stay
English. Every Runic.CommandLine minor is followed in the same release train
by a Translations release for it, and patches add no keys.
