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

Invalid bindings fail at the `Bind` argument, not at runtime. For the
`checkout_help` message of this fixture (slots `guide` as a link and `retry` as
an action) and `checkout_rating` (`star` icon, `reviews` link), with `link`,
`action` and `icon` bindings in scope, these calls give the errors shown:

| Call | Error |
|---|---|
| `text.Messages.checkout_help.Bind(new(guide: action, retry: action))` (wrong kind) | CS1503 |
| `text.Messages.checkout_help.Bind(new(guide: link))` (missing slot, even the conditional `retry`) | CS7036 |
| `text.Messages.checkout_help.Bind(new(guide: link, retyr: action))` (misspelled slot) | CS1739 |
| `text.Messages.checkout_help.Bind(new ShopTextSlots.checkout_rating(star: icon, reviews: link))` (another message's slots) | CS1503 |

These errors are proven by
[`GeneratorReadableTests.CompileFailures`](../../../dotnet/Runic.Translations.Generator.Tests/GeneratorReadableTests.cs),
which uses its own test catalog: there the messages are `help` and `other`, and
the slot types are `AppTextSlots.help` and `AppTextSlots.other`. The calls above
have the same shape against this fixture's `ShopText` catalog.
