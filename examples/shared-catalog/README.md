# Shared catalog: frontend and C#

One Runic Translations catalog serves a web frontend and a C# process. The
catalog lives with the frontend in `Frontend/translations`, where the Vite
plugin compiles it for the page (`m.page_title()`). `Tray` is a small C#
program standing in for the code outside the Views, such as a tray menu or
notifications: its project names the same `runic.json`, and the build generates
`DashboardText` for every message in it.

```xml
<RunicTranslationProject Include="../Frontend/translations/runic.json" />
```

The project builds against this repository's sources. An application uses the
`Runic.Translations` and `Runic.Translations.Build` packages instead. From the
repository's development shell:

```sh
dotnet build examples/shared-catalog/Tray -c Release
dotnet run --project examples/shared-catalog/Tray -c Release --no-build -- de en
```

It prints the menu and a notification in German, then switches the manager to
English and prints them again:

```text
[de] Übersicht öffnen | Nach Aktualisierungen suchen | Beenden
[de] Eine Aktualisierung ist bereit. 3 Aktualisierungen sind bereit.
[en] Open the dashboard | Check for updates | Quit
[en] One update is ready to install. 3 updates are ready to install.
```

`Frontend` is a minimal Vite page on the same catalog. Its
[`vite.config.js`](Frontend/vite.config.js) adds the Vite plugin with this
project, and [`main.js`](Frontend/main.js) shows `m.page_title()` and
`m.page_summary({ count: 3 })` with a button that switches between English and
German:

```js
runicTranslations({ project: 'translations' })
```

```sh
cd examples/shared-catalog/Frontend
dotnet new tool-manifest --output .config
dotnet tool install dotnet-runic-translations --version <VERSION>
npm install
npm run dev
```

CI builds the page against the packages of the current commit
(`bun eng/verify-packages.mjs`, or `bun eng/verify-examples.mjs` after
`bun eng/run.mjs pack`) and checks that its bundle holds only the messages the
page calls, not the tray's.

`bun eng/run.mjs native-aot` publishes `Tray` with NativeAOT, with trim and AOT
warnings treated as errors, and checks this output. See
[Strings outside the Views](../../docs/guides/translations/runic-desktop.md#strings-outside-the-views).
