# Translate a Runic.CommandLine app

`Runic.Translations.CommandLine` connects a
[Runic.CommandLine](https://github.com/Runic-Artifex/runic-cli-sdk) app to
Runic Translations. Its `TranslationCommandTextResolver` translates help
labels, parse and value errors, faults and your `DescriptionKey` values. It
ships English and German text for every framework key, so you translate only
your own text and write no key mapping.

```sh
dotnet add package Runic.CommandLine
dotnet add package Runic.Translations
dotnet add package Runic.Translations.Build
dotnet add package Runic.Translations.CommandLine
```

```csharp
CultureInfo culture = CultureInfo.GetCultureInfo("de");
ITranslationManager manager = await CliTextCatalog.CreateManagerAsync(culture.Name);
return await new CommandApp(GeneratedCommandCatalog.Create())
{
    Culture = culture,
    TextResolver = new TranslationCommandTextResolver(manager),
}.RunAsync(args);
```

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
and an unknown option prints `RCLI1001: Unbekannte Option. (--unknown)`. The
[localized example](../../../examples/command-line/localized/README.md) is a
complete app that is also published with NativeAOT in CI.

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

It then tries, in order:

1. Your catalog, through `ITranslationSnapshot.TryGetKey`. A message here
   overrides the framework text, such as `help { usage = Synopsis }`.
2. The built-in English and German framework text, in the locale of your
   current snapshot. Other locales use English.
3. Nothing: Runic.CommandLine writes its English text.

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

Create the resolver from an `ITranslationManager` to follow locale switches,
from an `ITranslationSnapshot` for a fixed locale, or with no arguments to use
only the built-in text in the invocation culture.

## Packages and versions

The adapter depends on `Runic.CommandLine` through `ICommandTextResolver`
only, so it works with Runic.CommandLine 0.6.0-preview.3 and later. Releases
before the one that adds `CommandTextKeys` report value errors as
`faults.RCLI2xxx` without arguments; later releases ask for
`diagnostics.invalid-integer`, `diagnostics.out-of-range` and the other keys,
which the built-in catalogs translate.
