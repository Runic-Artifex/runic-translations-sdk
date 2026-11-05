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
| Text from the caller (`:string`, `:runic:list`) and literals | `U+2068` FSI … `U+2069` PDI | FSI … PDI |
| Numbers, dates and times (direction of their formatting locale) | bare when left to right | `U+2067` RLI … PDI when right to left, `U+2066` LRI … PDI when left to right |
| `:runic:uuid`, `:runic:boolean` | bare | LRI … PDI |

The message's direction is the direction of its content locale, so English fallback text in an Arabic catalog keeps
the left-to-right rules. `Hello {$name}` formats as `Hello ⁨أحمد⁩` and renders as "Hello أحمد" with the name
kept in one piece. The isolate characters are invisible; they are part of the returned string, so tests compare
formatted text with them (`isolateBidi(text)` from `@runic-artifex/translations-svelte/testing` writes FSI … PDI).

`runtime.bidiIsolation` in `runic.json` turns it on:

```json
{ "runtime": { "bidiIsolation": "default" } }
```

| Value | Placeholders are isolated |
| --- | --- |
| `default` (when omitted) | when the catalog declares a right-to-left locale, such as `ar`, `he`, `fa` or `pa-PK`; never in a catalog of left-to-right locales only |
| `on` | always, for example in an English-only catalog that shows user names in any script |
| `off` | never, for example for terminal output |

The setting belongs to the catalog: overlays, external packs and dynamic artifacts format with the catalog's setting.
`CompiledTranslationCatalog.BidiIsolation` reports it in .NET.

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
| Svelte, SvelteKit | `textDirection(locale)` from `@runic-artifex/translations-svelte` or `@runic-artifex/translations-sveltekit` |

.NET and the catalog's ESM runtime use the same pinned data, independent of the operating system's culture data and
of `InvariantGlobalization`. The ESM runtime has likely subtags only for the catalog's languages and asks
`Intl.Locale.prototype.maximize()` for others; the Svelte packages always ask `Intl.Locale`.

## Content in a page

- **SvelteKit.** `createLocaleHandle` replaces `%runic.dir%` in `app.html` with the request locale's direction, beside
  `%runic.locale%`: `<html lang="%runic.locale%" dir="%runic.dir%">`. `htmlDirectionToken` renames or disables the
  token, and `direction` replaces the default direction function.
- **Documents on the web.** `LocalizedDocument` and the generated `createDomDocumentRenderer` give each top-level block
  `dir` beside `lang` when its content locale's direction differs from the page's. The page is left to right unless you
  pass `pageDirection="rtl"` (`{ pageDirection: "rtl" }`), which marks English fallback blocks in an Arabic page.
- **WPF.** `WpfDocumentRenderer` sets `FlowDocument.FlowDirection` from `LocaleDirection`.
- **Inline content and strings** carry no direction; isolation keeps their placeholders in order, and the element that
  shows them takes the page's direction.
