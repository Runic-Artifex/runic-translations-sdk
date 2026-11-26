# Conformance driver

The language-neutral conformance driver runs a corpus against any implementation through a small adapter process in the
implementation's language. Its core is runic-sdk's
[`eng/conformance/core`](https://github.com/Runic-Artifex/runic-sdk/tree/main/eng/conformance/core), vendored here
unchanged (R7 G11, T1-15); the adapter protocol is runic-sdk's
[`eng/conformance/PROTOCOL.md`](https://github.com/Runic-Artifex/runic-sdk/blob/main/eng/conformance/PROTOCOL.md).
This repository has no corpus module yet: R7 G16 adds the Translations module and its .NET and ESM adapters.

```sh
bun eng/conformance/cli.ts                  # usage; reports that there are no corpus modules yet
bun eng/conformance/cli.ts <implementation> # once implementations.json lists one
bun eng/conformance/cli.ts list | cases | lint
```

| Path | Content |
| --- | --- |
| [`cli.ts`](cli.ts) | the command's entry point, which registers this repository's corpus modules |
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
