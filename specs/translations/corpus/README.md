# Runic Translations conformance corpora

The current release contract is exercised by three complementary fixtures:

- [`rmf2-v1/index.json`](rmf2-v1/index.json) is the cross-backend release oracle
  for resource syntax `rmf2-v1` executed as `rmf2-execution-v2`, grammar/AST and
  artifact 5, runtime ABI 2, ESM ABI 4, and generated-name mapping 1;
- [`semantic-v5`](semantic-v5/README.md) isolates normalized AST and semantic
  validation cases;
- [`v5-project`](v5-project/README.md) isolates project linking, caller and
  freshness hashes, generated backends, and strict pack decoding.

The name `rmf2-v1` denotes the resource-file syntax. It does not select a retired
runtime, artifact, or emitter. Current runners compile every fixture through the
typed v5 project model.

## Published 0.3 historical fixture boundary

The root [`index.json`](index.json), `valid/`, `invalid/`, `shared/`, and `import/`
trees are frozen evidence for the published 0.3 JSON catalog/resource compiler.
They remain executable only where a test explicitly verifies that historical
release boundary. They are not current authoring examples, are not accepted by
the v5 project compiler, and must not be used to reintroduce an old reader or
writer into the current package graph.

The root index carries explicit `fixtureStatus` and `publishedReleaseBoundary`
metadata so tooling cannot mistake that evidence for the current release oracle.
Its paths are relative to this directory; its diagnostic locations are one-based,
start-inclusive, end-exclusive, and count UTF-16 code units.
