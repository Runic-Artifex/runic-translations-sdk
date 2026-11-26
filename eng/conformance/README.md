# Conformance driver

The language-neutral conformance driver runs a corpus against any implementation through a small adapter process in the
implementation's language. Its core is runic-sdk's
[`eng/conformance/core`](https://github.com/Runic-Artifex/runic-sdk/tree/main/eng/conformance/core), vendored here
unchanged (R7 G11, T1-15); the adapter protocol is runic-sdk's
[`eng/conformance/PROTOCOL.md`](https://github.com/Runic-Artifex/runic-sdk/blob/main/eng/conformance/PROTOCOL.md).
This repository's corpus module is [Translations](#translations) (R7 G16), with a .NET and a generated ESM adapter.

```sh
bun eng/conformance/cli.ts dotnet-translations   # build the adapter, run every case, compare with the status file
bun eng/conformance/cli.ts esm-translations      # the same for the generated ESM runtime
bun eng/conformance/cli.ts list | cases [--write] | lint
```

| Path | Content |
| --- | --- |
| [`cli.ts`](cli.ts) | the command's entry point, which registers this repository's corpus modules |
| [`implementations.json`](implementations.json) | the implementations: corpus, roles, adapter build and command, status file |
| [`translations/`](translations) | the Translations corpus module |
| [`core/`](core) | runic-sdk's driver core, vendored: never edit it here |
| [`core.vendor.json`](core.vendor.json) | the stamp: the runic-sdk commit and the SHA-256 of each vendored file |
| [`vendor.mjs`](vendor.mjs) | the freshness check and the update (the same script is in runic-cli-sdk) |

## The vendored core

`core/` is a copy of runic-sdk's `eng/conformance/core` at the commit in `core.vendor.json`. A change to the core goes
to runic-sdk first; this repository then updates its copy.

**Update it** from a runic-sdk commit or branch, from the repository root:

```sh
bun eng/conformance/vendor.mjs update <commit>   # or main; replaces core/ and rewrites the stamp
bun eng/conformance/vendor.mjs check
bun test eng/conformance/vendor.test.mjs
bun eng/conformance/cli.ts                       # and each implementation, once there are some
```

Pin a commit on runic-sdk's `main`. A stacked change may pin a runic-sdk pull request's head commit until it merges, then
moves to the merge commit. `update` and `upstream` fetch runic-sdk over the network, or read a local clone that has the
commit with `--sdk <dir>`.

**Checks:**

- `vendor.mjs check` (offline): each file of `core/` has the stamp's hash, and the stamp lists exactly the files of
  `core/`. Line endings do not count. A local edit fails with a hint to update from runic-sdk instead.
  `vendor.test.mjs` runs it in the Source checks job of every CI run, and shows that a one-byte edit fails.
- `vendor.mjs upstream [<ref>]` (network): the stamp and the copy match runic-sdk at the pinned commit, or at `<ref>`
  (`upstream main` shows whether the pin is behind). This catches a stamp edited by hand. CI runs it only when
  `core/` or `core.vendor.json` changes, as the Cargo.lock checks run only when the lock changes (R1 section 7), so an
  ordinary pull request does not depend on reaching GitHub.

## Translations

The module (`translations/`) runs the [Translations corpus](../../specs/translations/corpus/README.md) against the
committed compiled artifacts. Its case list and status files are in
[`specs/translations/conformance`](../../specs/translations/conformance/README.md), which lists the sets, the case
counts and the statuses.

| File | Content |
| --- | --- |
| [`translations/corpus.ts`](translations/corpus.ts) | the cases, keyed `<file stem>/<id>`, from the message corpora, the pack recipes, the persisted references and locale negotiation |
| [`translations/recipes.ts`](translations/recipes.ts) | the corpus's pack recipes (RFC 6902 patches and text edits) and macros, which the driver applies |
| [`translations/run.ts`](translations/run.ts) | one case through an adapter: the `tr.*` operations and the matching |

**Implementations:**

| Id | Adapter | Build |
| --- | --- | --- |
| `dotnet-translations` | [`tests/dotnet/Runic.Translations.Conformance`](../../tests/dotnet/Runic.Translations.Conformance/AdapterProgram.cs): the public API of `Runic.Translations` (`TranslationPackLoader`, `CompiledTranslationProvider`, `TranslationReference`, `LocaleNegotiation`) | `dotnet build` of the project, Release |
| `esm-translations` | [`packages/web/vite-plugin-runic-translations/test/conformance-adapter.ts`](../../packages/web/vite-plugin-runic-translations/test/conformance-adapter.ts), in Bun: the exports of the generated ESM modules (`decodeLocalePack`, `formatDynamicMessage`, `resolveTextReference`, `createTextReference`, `negotiateLocale`) | the ESM runtime is generated per catalog, so the build runs the Compiler suite's explicit `ConformanceEsmCatalogs` case: it writes to `artifacts/conformance/esm-catalogs` the ESM modules of the project behind each compiled group, and a catalog per negotiation group |

**Operations** (runic-sdk's `PROTOCOL.md` framing). The driver reads the artifacts and contracts, applies the recipes
and matches every result. Arguments travel as the corpus writes them (`{type, value}`), artifacts as base64.

| op | args | ok |
| --- | --- | --- |
| `tr.loadPack` | `{group, artifact, contract}`: the compiled group, the pack's bytes and the group's pack contract (`compiled/<group>.contract.json`) | `{pack}`, a handle, or `{rejected}`, the reason as `RTR0023/<reason>` |
| `tr.format` | `{pack, key, arguments, locale}`: the requested locale | `{text}`, or `{error}` when formatting fails |
| `tr.resolveReference` | `{pack, locale, json}`: a reader-catalog pack and the wire text | `{outcome, text, diagnostics: [{path, reason}]}` with the reasons of `transport-v2.md` |
| `tr.readReference` | `{pack, json}` | `{ok: true}`, or `{ok: false, reason?}` |
| `tr.writeReference` | `{pack, reference}`: the description with the fingerprint filled in | `{text}`; `unsupported` for version 1 without the capability `transport-v1-writer` |
| `tr.negotiate` | `{group, locales, base, preferences}` | `{locale}` |

There is no compile operation: the compiled artifacts are the compiler's committed output, which
`bun eng/generate-corpus.mjs --check` (the Compiler suite's `CompiledCorpusTests`) keeps current. A runtime with only a
pack reader, such as a future Rust or C++ runtime, runs every case.

**CI.** The Source checks job runs `cases` and `lint`. The Build and test job (Linux) runs both implementations after
the test suites, so the .NET adapter and the ESM catalogs build from that job's Release outputs. A run that differs from
its status file fails, including a newly passing case (`FIXED` blocks in CI).
