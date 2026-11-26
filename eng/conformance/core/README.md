# Driver core

The parts of the conformance driver that every corpus and every repository share: the adapter process client
([`adapter.ts`](adapter.ts)), cases and `cases.json` ([`cases.ts`](cases.ts)), outcomes, receipts and status files
([`results.ts`](results.ts)), the format names ([`names.ts`](names.ts)) and the command itself
([`driver.ts`](driver.ts)). [`index.ts`](index.ts) lists what a repository may use.

runic-sdk owns this directory. runic-cli-sdk and runic-translations-sdk vendor it unchanged at a pinned runic-sdk
commit, with a stamp of each file's hash and a freshness check (R7 G11, T1-15). So the core keeps to these rules, which
`eng/conformance.test.mjs` checks:

- it imports only its own files, `node:*` and `bun`;
- it names no corpus module and no repository path: a repository's `eng/conformance/cli.ts` passes its root, its
  `eng/conformance` directory (with `implementations.json`) and its corpus modules to `createDriver`, then calls `main`.

A change here reaches the other repositories when they update their copy (their `eng/conformance/README.md`).
