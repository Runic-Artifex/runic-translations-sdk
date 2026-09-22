# Deterministic compiler outputs

All generator, build, and CLI surfaces consume the same linked v5 project model.
For identical normalized inputs and options they produce byte-identical outputs
independent of input enumeration, source partitioning, absolute paths, current
directory, operating system, clock, current culture, username, or process ID.

## Output groups and names

The C# source generator and `--emit-csharp` renderer emit four hint files per
catalog class:

1. `{ClassName}.Keys.g.cs`
2. `{ClassName}.Accessors.g.cs`
3. `{ClassName}.CatalogData.g.cs`
4. `{ClassName}.Registration.g.cs`

Generated C# uses the v5 caller and markup contracts, generated-name mapping 1,
and requires RMF2 runtime ABI 2. Hint names and generated members compare
ordinally. Source-generator output enters the current compilation; the Build
package does not copy those files into its generated-asset directory.

The JSON output group contains:

- `{catalog}.{locale}.locale-v5.json` for each declared locale;
- `{catalog}.asset-manifest-v1.json`, which inventories exactly those locale
  artifact bytes with their SHA-256, byte length, media type, and locale.

The ESM output group is rooted at `{catalog}.esm-v5/`. It contains the runtime,
transport, dynamic-pack, server, message modules, their declaration files, and
`web-module-manifest-v3.json`. The manifest declares ESM ABI 4, RMF2 runtime ABI
2, grammar 5, `rmf2-execution-v2`, generated-name mapping 1, caller fingerprint,
source hash, exact entrypoints, and every owned asset.

Standalone TypeScript, template-manifest, and C++ output groups are not part of
the current contract. Their retired switches fail with `RTR0065`; a host must not
substitute old output bytes or infer a legacy renderer.

Paths never come from translated message content. The configured output root and
every resolved child MUST remain under the allowed intermediate/output directory.

## Canonical locale artifact bytes

Locale artifact v5 uses this root property order:

1. `artifactVersion` (`5`)
2. `messageGrammarVersion` (`5`)
3. `profile` (`rmf2-execution-v2`)
4. `catalog`
5. `locale`
6. `contractFingerprint`
7. `messages`
8. `markupContract`

Message keys are ordinal-sorted. Each message writes `contentLocale`, then its
normalized AST v5. The AST carries the canonical caller input array, full typed
declaration and selector graph, ordered annotations, structured markup events,
and functional-slot contracts. `markupContract` is the linked v1 markup envelope
for the complete catalog.

Release JSON is minified UTF-8 without BOM, insignificant whitespace, or a
terminal newline. Strings use JSON short escapes for backspace, form feed,
newline, carriage return, and tab; quote and reverse solidus are escaped;
remaining U+0000 through U+001F and unpaired UTF-16 surrogates use lowercase
four-hex-digit `\u` escapes. Valid non-ASCII scalars, including U+2028 and U+2029,
remain literal UTF-8. Solidus is not escaped.

The locale artifact and external-pack v5 payloads share the same closed envelope.
The complete emitted bytes have a separate asset SHA-256. The embedded
`contractFingerprint` is the caller-compatibility hash, not the artifact hash;
translated text and source freshness are intentionally separate concerns.

## Typed generated surface

Generated keys expose stable logical names and optimized integer IDs. Generated
accessors are instance members over an explicit manager and emit strongly typed
parameters from the linked caller contract. Leaf descriptions become XML
documentation. Generated-name mapping 1 encodes NFC UTF-8 names injectively for
C# and JavaScript rather than relying on language-keyword exceptions.

Generated code embeds its literal ABI requirements. An unsafe mismatch is
`RTR0024`; it is never guessed, downgraded, or silently adapted.
