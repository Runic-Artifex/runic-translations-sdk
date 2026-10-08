# `@runic-artifex/translations-svelte`

Svelte 5 helpers for Runic Translations locale state and RMF2 inline and
document content.

```ts
import { createLocaleContext } from "@runic-artifex/translations-svelte/translations";
```

The browser-safe `@runic-artifex/translations-svelte/translations/testing`
entry point provides pseudo-localization, RTL isolation, plural boundary values,
and accessibility stress fixtures. `@runic-artifex/translations-svelte/inline`
exports `LocalizedInline` and `inlineFactory` for localized structured content.
`@runic-artifex/translations-svelte/document` exports `LocalizedDocument` and
`documentFactory` for RMF2 document messages: semantic paragraphs, headings and
lists that hydrate by occurrence path, with no inline styles. Pass
`documentFactory` to the catalog's `createDocumentRenderer` once, then render
its output with `<LocalizedDocument {nodes} />`. See
[Web document adapters](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/rmf2.md#web-document-adapters).
