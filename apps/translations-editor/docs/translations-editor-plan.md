# Runic Translations Editor v1 plan

Status: complete
Last updated: 2 September 2026

## Outcome

The editor is the visual authoring surface for a convention-based Runic translation project. It opens one `runic.json` declaration and the `{locale}/{message_id}.mf2` files beneath it. The compiler remains authoritative for diagnostics, preview, generation, and saves.

## Delivered v1 scope

- create or open a conventional MF2 project;
- edit simple and structured MF2 messages without an intermediate JSON format;
- add and remove locales, configure fallbacks, and create, duplicate, rename, or remove message IDs through transactional workspace mutations;
- validate drafts with the same compiler used by builds;
- preserve request-safe locale behavior and generated message identifiers used by Runic applications;
- keep review state separate from authored translation sources;
- export XLIFF and review JSON, preview their changes, and apply XLIFF translations back to conventional MF2 files;
- detect external changes, recover interrupted transactions, and keep saves atomic;
- package the Svelte UI and native host as a self-contained editor archive.

## Project shape

```text
translations/
  runic.json
  en/
    application_title.mf2
  de/
    application_title.mf2
```

`runic.json` is the only project declaration. Locale folders and MF2 filenames carry the remaining identity by convention. The editor does not select among manifests or author an alternative resource-document format.

## Deferred to v2

The language server is deliberately a v2 milestone. It will add inline diagnostics, hover, rename, references, and quick fixes after the v1 compiler and authoring contracts are stable enough to support a high-quality editor protocol implementation.
