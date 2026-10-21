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

The frontend adds the Vite plugin with this project:

```js
runicTranslations({ project: 'translations' })
```

`bun eng/run.mjs native-aot` publishes `Tray` with NativeAOT, with trim and AOT
warnings treated as errors, and checks this output. See
[Strings outside the Views](../../docs/guides/translations/runic-desktop.md#strings-outside-the-views).
