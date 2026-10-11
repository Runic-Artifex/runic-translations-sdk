# Runic.Translations.CommandLine (preview)

`Runic.Translations.CommandLine` translates the help, errors and faults of a
[Runic.CommandLine](https://github.com/Runic-Artifex/runic-cli-sdk) app with
Runic Translations. It ships English and German text for every framework key,
so a German invocation shows no English framework text and you write no key
mapping.

```csharp
using Runic.CommandLine;
using Runic.Translations;
using Runic.Translations.CommandLine;

ITranslationSnapshot snapshot = await AppTextCatalog.CreateProvider().GetSnapshotAsync("de");
return await new CommandApp(GeneratedCommandCatalog.Create())
{
    Culture = CultureInfo.GetCultureInfo("de"),
    TextResolver = new TranslationCommandTextResolver(snapshot),
}.RunAsync(args);
```

`TranslationCommandTextResolver` resolves each key in this order:

1. Your catalog, which can translate your `DescriptionKey` values and override
   any framework text.
2. The built-in English and German framework text, in the locale of your
   snapshot (or of the invocation culture for the parameterless constructor).
   Other locales use English.
3. `null`, so Runic.CommandLine writes its English text.

Pass an `ITranslationManager` instead of a snapshot to follow locale switches.

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
