# Deterministic compiler outputs

All generator, build, and CLI surfaces consume the same canonical Wave A IR.
For identical normalized inputs and options they produce byte-identical outputs
independent of input enumeration, source partitioning, absolute paths, current
directory, operating system, clock, current culture, username, or process ID.

## Output set and names

The C# generator emits these hint files per catalog class:

1. `{ClassName}.Keys.g.cs`
2. `{ClassName}.Accessors.g.cs`
3. `{ClassName}.CatalogData.g.cs`
4. `{ClassName}.Registration.g.cs`

Hint names compare ordinally. Generated C# enables nullable analysis, uses fully
qualified framework names where ambiguity is possible, uses LF line endings,
and ends with one LF. Groups mirror the resource hierarchy. Placeholder
parameters and serialized contracts are ordered by ordinal placeholder name.
Key IDs follow the Wave A ordinal dotted-key order.

Non-C# artifact names are relative safe names:

- `{catalog}.{locale}.locale-v1.json` for a resolved locale artifact;
- `{catalog}.template-manifest-v1.json` for the versioned value-free edge;
- `{catalog}.text-resources-v1.d.ts` for the versioned TypeScript declaration edge.

The asset-manifest schema is a versioned host edge, but Wave B does not freeze a
renderer filename for it. A future emitting surface must choose a version-explicit
name and add it to the corpus before publication.

Paths never come from resource source JSON. The configured output root and every
resolved child MUST remain under the allowed intermediate/output directory.

## Canonical JSON bytes

Version 1 locale artifacts use this root property order:

1. `artifactVersion`
2. `messageGrammarVersion`
3. `catalog`
4. `locale`
5. `contractFingerprint`
6. `messages`

Message keys are ordinal-sorted. Each message writes `pattern`, then `arguments`.
Arguments are ordinal-sorted by `name` and write `name`, `type`, then `format`.
Every descriptor writes its normalized default format, including `none` for
`string`, so readers never infer a missing wire value.

Release JSON is minified UTF-8 without BOM, insignificant whitespace, or a
terminal newline. Strings use JSON short escapes for backspace, form feed,
newline, carriage return, and tab; quote and reverse solidus are escaped;
remaining U+0000 through U+001F and unpaired UTF-16 surrogates use lowercase
four-hex-digit `\u` escapes. Valid non-ASCII scalars, including U+2028 and U+2029,
remain literal UTF-8. Solidus is not escaped.

The locale-artifact and external-pack version 1 payloads are structurally and
byte compatible. The complete emitted bytes have a separate asset SHA-256. The
embedded `contractFingerprint` retains the frozen Wave A definition and excludes
translations; it is not the hash of the artifact itself.

Template and asset manifests use the property order shown in their schemas,
ordinal message/path ordering, the same string encoding, and the same minified
UTF-8 envelope. Their versions are independent from the locale artifact.

## Typed generated surface

Generated keys expose stable dotted names and optimized integer IDs. Generated
accessors are instance members over an explicit manager, use Wave A namespace
and class settings, and emit strongly typed parameters:

| Descriptor | C# parameter |
|---|---|
| `string` | `string` |
| `int` | `long` |
| `number` | `decimal` |
| `bool` | `bool` |
| `date` | `System.DateOnly` |
| `time` | `System.TimeOnly` |
| `datetime` | `System.DateTimeOffset` |
| `guid` | `System.Guid` |

Leaf descriptions become XML documentation. `$deprecated` emits
`ObsoleteAttribute`; `$since` and tags are metadata and do not alter lookup.
Generator/runtime ABI version 1 is embedded in generated code. An unsafe mismatch
is `WUTTEXT0024`; it is never guessed or silently adapted.
