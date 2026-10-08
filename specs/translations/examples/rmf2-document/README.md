# RMF2 document example

This maintained example renders the bounded document message from issue #15:
a paragraph with emphasis and a placeholder, a bulleted list with a link slot
and an action slot, and a closing paragraph, in English and German. `runic.json`
declares no custom markup; the document vocabulary (`p`, `h`, `ul`, `ol`, `li`)
is built in.

From the repository root in its development shell:

```sh
dotnet run --project tools/dotnet-runic-translations -- generate --project specs/translations/examples/rmf2-document --output specs/translations/examples/rmf2-document/generated --emit-esm
```

Serve this directory with a local static HTTP server and open `browser.html`.
Generated files are disposable and ignored. The page uses
`createDomDocumentRenderer` and shows:

- **Semantics:** `<p>`, `<strong>`, `<ul>`/`<li>`, a native `<a href>` and a
  native `<button>`, with the effective content locale as `lang` on each
  top-level block. The `.runic-leaf { white-space: pre-wrap; }` rule in the page
  keeps leaf whitespace without inline styles.
- **Keyboard navigation:** Tab reaches the link and then the action, and Enter
  activates either. The application owns the route (`#guide`) and the callback.
- **Selectable and copyable text:** the content is ordinary text. Copying the
  whole message writes its plain-text projection (`toPlainText` with
  `allowActionLabels`), with list markers and blank lines between blocks; a
  partial selection copies the browser's own text.
- **Callback retirement:** "Render again" and a locale change call
  `setContent` again. The previous render is retired: a retained link loses its
  `href`, and a retained action button is disabled and no longer calls back.

`packages/web/translations-svelte/test/verify-document-example.mjs` generates
this example with the repository CLI and checks all of the above in Chromium
(`bun run --bun test:document-example` in that package; CI job "RMF2 document
example / Chromium").

The Svelte `LocalizedDocument` component renders the same HTML for SSR and
hydration, and native .NET toolkits render the `DocumentBlock` tree from
`Rmf2DocumentRenderer`. See the
[document messages guide](../../../../docs/guides/translations/rmf2.md#document-messages).
