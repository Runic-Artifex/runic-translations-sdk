# Runic Translations conformance corpora

The current release contract is exercised by six complementary fixtures:

- [`rmf2-v1/index.json`](rmf2-v1/index.json) is the cross-backend release oracle
  for resource syntax `rmf2-v1` executed as `rmf2-execution-v2`, grammar/AST and
  artifact 5, runtime ABI 3, ESM ABI 4, and generated-name mapping 1;
- [`rmf2-document-v1`](rmf2-document-v1/README.md) is the cross-backend oracle
  for document profile v1: block structure, skeletons, occurrences, plain-text
  projection and pack rejection;
- [`semantic-v5`](semantic-v5/README.md) isolates normalized AST and semantic
  validation cases;
- [`v5-project`](v5-project/README.md) isolates project linking, caller and
  freshness hashes, generated backends, and strict pack decoding;
- [`rmf2-list-conformance.json`](rmf2-list-conformance.json) is the
  cross-backend oracle for `:runic:list`: every supported locale family, each
  type and style, and the Spanish `y`/`e` and `o`/`u` forms. Each case must
  format exactly as expected in a linked .NET message, a loaded .NET pack,
  generated ESM and a dynamically decoded ESM artifact;
- [`transport-v2-persisted-references.json`](transport-v2-persisted-references.json)
  is the cross-backend oracle for [persisted text references](../transport-v2.md):
  references written against the `writer` catalog are resolved by the changed
  `reader` catalog, with the expected outcome, text and diagnostics. `$writer` and
  `$reader` stand for the two catalogs' contract fingerprints. The .NET
  `TranslationReferenceResolver` and the generated ESM `resolveTextReference`
  must agree with every case, .NET must write each reference back byte for byte,
  and every `rejected` reference must fail to read.

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
