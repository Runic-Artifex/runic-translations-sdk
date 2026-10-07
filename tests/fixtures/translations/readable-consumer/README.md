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
