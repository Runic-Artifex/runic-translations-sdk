# .NET consumer quick start

This workflow installs packages into an ordinary .NET application. It requires
the .NET 10 SDK and a published Translations release; it does not require a Runic
source checkout. Replace `<VERSION>` with one exact version available on NuGet.
Use that version for the template, runtime, build package, and compiler tool.

## 1. Create the translations library

Start in an empty application directory:

```sh
dotnet new install Runic.Translations.Templates@<VERSION>
dotnet new runic-translations-project --name Example.Translations \
  --catalog app --default-locale en --class-name AppText
cd Example.Translations
dotnet tool restore
dotnet build
cd ..
```

The template writes `translations/runic.json`, `translations/en.rmf2`, package
references, and a project-local `.config/dotnet-tools.json`. Its initial resource
is:

```rmf2
application {
  title = AppText
}
```

The build generates `AppText` and `AppTextCatalog` in `Example.Translations`,
the namespace that follows `--name` unless you pass `--namespace`. Add
`--locales de,fr` to declare more locales, then add a `translations/<locale>.rmf2`
file for each. For a web front end that consumes this project's output, pass
`--emit-esm` to also emit the ESM package at
`obj/Debug/net10.0/translations/app.esm-v5/web-module-manifest-v3.json`.
C# belongs to the bundled source generator and is not a file to copy into the
application. See [build integration](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/packages/dotnet/Runic.Translations.Build/README.md)
for output selection and nondefault source layouts.

## 2. Print the first message

```sh
dotnet new console --name Example.App --framework net10.0
dotnet add Example.App/Example.App.csproj reference Example.Translations/Example.Translations.csproj
```

Replace `Example.App/Program.cs` with:

```csharp
using Example.Translations;
using Runic.Translations;

ITranslationManager manager = await AppTextCatalog.CreateManagerAsync();
var text = new AppText(manager);
Console.WriteLine(text.Messages.application_title);
```

```sh
dotnet run --project Example.App/Example.App.csproj
```

The program prints `AppText`. `text.Messages` is the readable C# surface. Each
message is a member named by its flattened key, the same name ESM uses
(`m.application_title()`): a property when the message has no inputs, and a
method with one parameter per input otherwise.

- **Encoded members.** The generated class also keeps the encoded members,
  such as `text.r_6170706c69636174696f6e_r_7469746c65` (each path segment is
  `r_` followed by its UTF-8 bytes in hexadecimal). They are the stable
  machine-facing contract and never change; use them where a name must stay
  fixed across SDK releases, such as in generated code. The readable names
  follow a versioned policy (`AppTextMessages.ReadableNameVersion`).
- **Non-identifier names.** An input name or slot ID that is not an ASCII
  identifier, such as `user-name` or `café`, keeps its encoded name for that
  parameter only: a message with `$user-name` is called as
  `text.Messages.profile_badge(r_757365722d6e616d65: "ada")`. A key that is
  reserved by the readable-name policy (for example `ToString`, which C# does
  not reserve) stays encoded-only and reports
  [`RTR0069`](diagnostics.md#rtr0069).
- **Rich content.** A message with markup returns
  `LocalizedTextContent<AppTextSlots.key>`. Bind its link, action and icon
  slots with named arguments, because slots of the same kind share a type:

  ```csharp
  BoundLocalizedTextContent help = text.Messages.checkout_help.Bind(new(
      guide: new InlineLinkBinding(guideUri),
      retry: new InlineActionBinding(Retry)));
  var renderer = new Rmf2InlineRenderer(AppTextCatalog.Rmf2MarkupContract);
  string plain = renderer.ToPlainText(help, allowActionLabels: true);
  ```

  A wrong binding kind, a missing or misspelled slot, or another message's
  slots are compile errors at the `Bind` argument. The
  [consumer example](https://github.com/Runic-Artifex/runic-translations-sdk/tree/main/tests/fixtures/translations/readable-consumer)
  shows link, action, icon and conditional slots.
  A structured message without functional slots is bound with `Bind(new())`.

Change the title in `en.rmf2`, rebuild, and the same
typed property prints the new text. Your application's reference to its own
translations library is an ordinary project reference; all Runic dependencies
come from packages.

## 3. Restore and build in CI

Commit both projects, the authoring files, and the generated tool manifest. Keep
`bin/` and `obj/` ignored. From the application directory on a fresh checkout:

```sh
dotnet tool restore --tool-manifest Example.Translations/.config/dotnet-tools.json
dotnet restore Example.App/Example.App.csproj
dotnet build Example.App/Example.App.csproj --configuration Release --no-restore
dotnet run --project Example.App/Example.App.csproj --configuration Release --no-build
```

The build validates the catalog and generates current output. Byte-verification
is useful for intentionally retained generated artifacts; ignored `obj/` output
does not exist before the first build.

## Add translations to an existing project

Install the same template package, then run the item template from that
project's directory:

```sh
dotnet new runic-translations --output . --catalog app \
  --default-locale en --namespace Example.App --class-name AppText
dotnet add package Runic.Translations --version <VERSION>
dotnet add package Runic.Translations.Build --version <VERSION>
dotnet build
```

The item template creates authoring files. The build package discovers the
conventional `translations/` directory and generates C# without a tool manifest.
If you also enable JSON or ESM output, install the matching project-local
`dotnet-runic-translations` tool and restore it before building. See the
[template guide](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/tools/Runic.Translations.Templates/README.md)
for template parameters, or the [RMF2 guide](rmf2.md) for additional locales and
message syntax.
