# SvelteKit quick start smoke

This app is the result of following
[the SvelteKit quick start](../../../../docs/guides/translations/quickstart-sveltekit.md).
Keep its files in step with the guide. The guide's optional favicon is omitted,
and the home page is prerendered while `/about` stays server-rendered, so one
build covers both.

`eng/verify-packages.mjs` copies it to a temporary directory, installs the packed
npm candidates and the packed `dotnet-runic-translations` tool, builds it with
`adapter-node`, and runs `svelte-check`. It then checks the prerendered pages, SSR in
both locales, a canonical redirect, and concurrent request isolation against the
production server. Run `bun run verify-packages` from the repository root.
