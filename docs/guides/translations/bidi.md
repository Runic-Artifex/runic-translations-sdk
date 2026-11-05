# Right-to-left text

A placeholder whose value is written in the other direction can reorder the text around it: an Arabic name in an
English sentence, or a Latin product code in a Hebrew one. Runic Translations isolates placeholders the way Unicode
MessageFormat 2 specifies, gives every locale a direction from pinned CLDR data that .NET and the browser share, and
marks content whose direction differs from the page.

## Placeholder isolation

With isolation, each placeholder is wrapped in Unicode isolate characters, chosen by
[MF2's default bidi strategy](https://github.com/unicode-org/cldr/blob/release-48-2/docs/ldml/tr35-messageFormat.md):

| Placeholder | In a left-to-right message | In a right-to-left message |
| --- | --- | --- |
| Text from the caller (`:string`) and literals | `U+2068` FSI … `U+2069` PDI | FSI … PDI |
| Numbers, dates and times (direction of their formatting locale) | bare when left to right | `U+2067` RLI … PDI when right to left, `U+2066` LRI … PDI when left to right |
| Lists (`:runic:list`) and relative times (direction of the content locale) | bare, each list item in FSI … PDI | RLI … PDI, each list item in FSI … PDI |
| `:runic:uuid`, `:runic:boolean` | bare | LRI … PDI |

A list's separators are written in the content locale, so the list takes the message's direction and each item is
isolated on its own: `Shared with {$people}` with `["نور", "Ada"]` formats as `Shared with ⁨نور⁩ and ⁨Ada⁩`, keeping
the English "and" between the names in reading order.

The message's direction is the direction of its content locale, so English fallback text in an Arabic catalog keeps
the left-to-right rules. `Hello {$name}` formats as `Hello ⁨أحمد⁩` and renders as "Hello أحمد" with the name
kept in one piece. The isolate characters are invisible; they are part of the returned string, so tests compare
formatted text with them (`isolateBidi(text)` from `@runic-artifex/translations-svelte/testing` writes FSI … PDI).

`runtime.bidiIsolation` in `runic.json` chooses when isolation applies:

```json
{ "runtime": { "bidiIsolation": "default" } }
```

| Value | Placeholders are isolated |
| --- | --- |
| `default` (when omitted) | per format call, when the requested locale, the catalog locale it resolved to or the message's content locale is right to left, such as `ar`, `he`, `fa` or `pa-PK`; never in left-to-right locales |
| `on` | always, for example in an English-only catalog that shows user names in any script |
| `off` | never, except for an explicit `u:dir` |

`default` is decided at run time, not when the catalog is compiled, so it follows the locale a message is shown in. An
application that adds `he` or `ar` to a left-to-right library catalog through an overlay or pack gets isolated
placeholders in that locale without rebuilding the library, and English text in an `en`/`ar` catalog stays free of
isolates. English fallback text shown for an Arabic request is isolated, because the request is right to left. The
setting belongs to the catalog: overlays, external packs and dynamic artifacts format with it.
`CompiledTranslationCatalog.BidiIsolation` reports it in .NET (`null` for `default`), and `WithBidiIsolation(bool?)`
changes it.

### Plain text

The isolate characters are invisible in browsers and in WPF text, but a plain-text sink sees them: terminal output,
logs, string comparisons in tests, length limits and the clipboard. When a catalog shared with a UI writes there,
remove them per call rather than turning the catalog's isolation off:

| Where | API |
| --- | --- |
| .NET | `BidiText.RemoveIsolates(text)` removes U+2066–U+2069 |
| Generated ESM | `removeBidiIsolates(text)` from the catalog's `runtime.js` |

They also remove isolates that a value itself contained.

### `u:dir`

`u:dir` sets one placeholder's direction, on any function: `{$code :string u:dir=ltr}`. Its values are `ltr`, `rtl`,
`auto` (unknown, so FSI) and `inherit` (the message's direction, and not isolated in a left-to-right message). A
`u:dir` of `ltr`, `rtl` or `auto` isolates its placeholder even when `bidiIsolation` is `off`. It must be a literal. A
local without a function keeps its expression's `u:dir`; an explicit function replaces it:

```rmf2
order =
  .input {$customer :string}
  .local $name = {$customer :string u:dir=auto}
  {{Order for {$name}}}
```

## The direction of a locale

A locale is right to left when its explicit script subtag, else its likely script from CLDR likely subtags, is one of
CLDR's right-to-left scripts (the list is pinned in `specs/translations/cldr/runic-subset-48.2.json`). So `ar`, `he`,
`fa`, `ur`, `pa-PK` and `ku-Arab` are right to left, and `ku`, `he-Latn` and `sd-Deva` are left to right.

| Where | API |
| --- | --- |
| .NET | `LocaleDirection.Get("ar")` returns `TextDirection.RightToLeft`; `LocaleDirection.IsRightToLeft(locale)`, `LocaleDirection.ToHtml(direction)` |
| Generated ESM | `localeDirection(locale)` from the catalog's `runtime.js` |
| Svelte, SvelteKit | `textDirection(locale)` from `@runic-artifex/translations-svelte` or `@runic-artifex/translations-sveltekit`; `SvelteLocale.direction` for the provided locale |

.NET and the catalog's ESM runtime use the same pinned data, independent of the operating system's culture data and
of `InvariantGlobalization`. The ESM runtime has likely subtags only for the catalog's languages and asks
`Intl.Locale.prototype.maximize()` for others; the Svelte packages ask `Intl.Locale` for a tag without a script.
Every helper reads the script only in its BCP 47 position and ignores extension and private-use subtags, so
`ar-u-nu-latn` (Arabic with Latin digits) is right to left and `en-u-nu-arab` left to right. The generated
`createLocaleSource()` offers the runtime's `localeDirection`, and `SvelteLocale.direction` uses it when the locale
source has it.

## Content in a page

- **SvelteKit.** `createLocaleHandle` replaces `%runic.dir%` in `app.html` with the request locale's direction, beside
  `%runic.locale%`: `<html lang="%runic.locale%" dir="%runic.dir%">`. `htmlDirectionToken` renames or disables the
  token, and `direction` replaces the default direction function.
- **Documents on the web.** `LocalizedDocument` and the generated `createDomDocumentRenderer` give each top-level block
  `dir` beside `lang` when its content locale's direction differs from the page's, so English fallback blocks on an
  Arabic page get `dir="ltr"`. The page's direction defaults to the active locale's, the same source as `%runic.dir%`:
  `LocalizedDocument` uses the locale that `createLocaleContext().provide(...)` provides (else left to right), and
  `createDomDocumentRenderer` reads `<html dir>` when it is `ltr` or `rtl`, else the direction of `getLocale()`, on
  every render. Pass `pageDirection` (`{ pageDirection: "rtl" }`) to state it yourself.
- **WPF.** `WpfDocumentRenderer` sets `FlowDocument.FlowDirection` from `LocaleDirection`.
- **Inline content and strings** carry no direction; isolation keeps their placeholders in order, and the element that
  shows them takes the page's direction.
