# Readable C# consumer example

This fixture consumes the readable C# surface (`text.Messages`) and typed
rich-content slots generated from `translations/`. It covers link, action and
icon slots, a conditional (`min: 0`) slot that the German translation omits, an
input name that is not a C# identifier (`user-name`), and named slot arguments.
It builds from the source tree with warnings as errors and runs in CI with the
managed tests.

From the SDK root, in the locked development environment:

```sh
dotnet build Runic.Translations.slnx -c Release
dotnet run --project tests/fixtures/translations/readable-consumer/ReadableConsumer.csproj -c Release
```

The fixture `bin` and `obj` directories are disposable.

## Compile-time rejection

Invalid bindings fail at the `Bind` argument, not at runtime. Each of these
wrong calls gives the error shown, and `GeneratorReadableTests.CompileFailures`
in `tests/dotnet/Runic.Translations.Generator.Tests` proves each one:

| Call | Error |
|---|---|
| `text.Messages.help.Bind(new(guide: action, retry: action))` (wrong kind) | CS1503 |
| `text.Messages.help.Bind(new(guide: link))` (missing slot) | CS7036 |
| `text.Messages.help.Bind(new(guide: link, retyr: action))` (misspelled slot) | CS1739 |
| `text.Messages.help.Bind(new AppTextSlots.other(guide: link))` (another message's slots) | CS1503 |
