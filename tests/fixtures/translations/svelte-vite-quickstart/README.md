# Svelte and Vite quick start smoke

This app follows the "Svelte without SvelteKit" section of
[the Vite quick start](../../../../docs/guides/translations/quickstart-vite.md):
a plain Vite and Svelte app with the Translations plugin, `useMessageLocale`
for a locale the application owns, `LocalizedInline` for a message with a link and for one with a project tag
declared in `runic.json` and rendered by a snippet,
and a `:number` input given a JavaScript number. Keep its files in step with
the guide.

`eng/verify-packages.mjs` copies it to a temporary directory without
`.runic/`, as a clean checkout has it, installs the packed npm candidates and
the packed `dotnet-runic-translations` tool, and runs `vite build && svelte-check`,
the order the guide gives. It then checks the built page for both locales'
text. Run `bun run verify-packages` from the repository root.
