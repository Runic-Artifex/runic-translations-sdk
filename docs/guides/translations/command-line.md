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

It then tries, in order:

1. Your catalog, through `ITranslationSnapshot.TryGetKey`. A message here
   overrides the framework text, such as `help { usage = Synopsis }`.
2. The built-in English and German framework text, in the locale of your
   current snapshot. Other locales use English.
3. Nothing: Runic.CommandLine writes its English text.

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

Create the resolver from an `ITranslationManager` to follow locale switches,
from an `ITranslationSnapshot` for a fixed locale, or with no arguments to use
only the built-in text in the invocation culture.

## Packages and versions

The adapter depends on `Runic.CommandLine` through `ICommandTextResolver`
only, but `Runic.Translations.CommandLine` requires Runic.CommandLine 0.9.1,
within the tested minor `[0.9.1, 0.10)`, whose prompt, JSON output alias,
output selector and response-file keys the built-in catalogs translate. A Runic.CommandLine minor
may change its API until 1.0, so the range moves in a Translations release
after testing. A project that adds only the adapter therefore restores a
Runic.CommandLine that names the cause of a command catalog error instead of
aborting at startup. Releases
before 0.6.0-preview.4, the one that adds `CommandTextKeys`, report value
errors as `faults.RCLI2xxx` without arguments; preview.4 and later ask for
`diagnostics.invalid-integer`, `diagnostics.out-of-range` and the other keys,
which the built-in catalogs translate.
