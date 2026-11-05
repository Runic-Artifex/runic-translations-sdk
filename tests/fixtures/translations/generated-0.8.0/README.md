# Output of Runic Translations 0.8.0

`output` is what the published 0.8.0 tool (`dotnet-runic-translations` 0.8.0 from NuGet) generated for the library
catalog in `source`, with every emission (C#, locale-v5 JSON with its asset manifest, ESM):

```sh
dotnet <nuget>/dotnet-runic-translations/0.8.0/tools/net10.0/any/dotnet-runic-translations.dll \
  generate --project source --output output
```

The files are frozen: never regenerate them. They stand for a library or an application release built before 0.9, so
tests run current output against a real pre-0.9 runtime instead of one rendered by today's compiler:

- `Rmf2EsmCompositionTests` renders this catalog beside a 0.9 catalog in one server process (no shared locale channel,
  its own request storage) and resolves its references with `createReferenceResolver`, whose 0.8 transport module does
  not export its catalog ID.

The catalog has a plural, a markup message, a number, a date and a key without German text (relaxed completeness).
