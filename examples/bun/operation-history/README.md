# Operation history under Bun

A Bun server and a separate tray process share one generated catalogue, without
Vite. The server records operations as text references (a message key and its
arguments, with English fallback text) and both processes translate them where
they are shown. The history in `history/2026-04.log` was written by an earlier
release of the catalogue: some of its messages have since been removed, one
reference uses transport version 1, and every entry still displays, translated
where possible and with its stored fallback otherwise. See
[cross-process text references](../../../docs/guides/translations/transport.md)
for the policy.

- `translations/` is the catalogue. `generate --emit-esm` writes plain ES modules
  to `generated/operations.esm-v5/`; nothing in them depends on Vite.
- `server.ts` writes references with `createTextReference` and serves
  `GET /operations?locale=de` with `resolveTextReference`.
- `tray.ts` prints the history in a locale, reading only the files.
- `history.ts` keeps each reference as its own JSON text, one per line after a
  timestamp, and passes that text to `resolveTextReference`, which checks its
  size before parsing it. The resolved text is shown as text, never as HTML.
- `verify.ts` generates the catalogue, starts the server, records an operation,
  checks the German and English text from the server, the tray and a tray bundled
  with `bun build`, and removes what it generated.

From the repository root, in the development shell, after `bun run build`:

```sh
bun examples/bun/operation-history/verify.ts
```

It prints `BUN_OPERATION_HISTORY_OK`. CI runs it in the `Build and test` job. To
try it by hand, generate the catalogue and start the server:

```sh
dotnet run --project tools/dotnet-runic-translations -c Release -- generate \
  --project examples/bun/operation-history/translations \
  --output examples/bun/operation-history/generated --emit-esm
PORT=3000 bun examples/bun/operation-history/server.ts
curl -X POST localhost:3000/operations -d '{"package":"gimp"}'
curl 'localhost:3000/operations?locale=de'
bun examples/bun/operation-history/tray.ts de examples/bun/operation-history/history/*.log
```

A Vite frontend of the same application passes the generated
`generated/operations.esm-v5/web-module-manifest-v3.json` to the Vite plugin's
`manifest` option, so all three use one catalogue.
