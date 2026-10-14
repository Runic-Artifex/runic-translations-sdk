# Text-reference transport, version 2

A text reference identifies a message and its arguments so that another process,
or the same application later, can show it in the reader's language. It is the
wire contract behind the .NET `TranslationReference` and the generated ESM
`transport.js`. Writers emit version 2; readers accept versions 1 and 2. The
[persisted-reference corpus](corpus/transport-v2-persisted-references.json) is
the shared .NET and ESM oracle for this document.

## Why the contract fingerprint is not enough

Version 1 carries untyped arguments: integers, decimals, dates and GUIDs are
strings, booleans are JSON booleans, and the receiving catalog decides how to read
each string. That is only sound when the receiver has the writer's exact caller
contract, so version 1 compares the whole catalog's contract fingerprint and
fails on any difference. A catalog that adds one unrelated message therefore made
every stored reference unreadable.

Version 2 tags each argument with its type. A receiver can then check one key's
contract on its own: the key exists, and every argument has exactly the name and
type of one of the key's current inputs. A string written for a former string
input is never read as an integer, and a missing or extra input is detected. This
is the property the fingerprint protected, for the message actually shown. The
fingerprint stays in the wire as the writer's provenance, and receivers that need
an identical build, such as live messages between processes of one release, still
compare it (`TranslationReference.ValidateCatalog`). Like the fingerprint, the
check is about compatibility, not authenticity: protect stored references the
way the application protects the rest of its data.

## Wire format

```json
{
  "version": 2,
  "catalog": "operations",
  "contractFingerprint": "sha256:…",
  "key": "operation_failed",
  "arguments": {
    "step": { "type": "message", "value": {
      "key": "steps_build",
      "arguments": { "name": { "type": "string", "value": "gimp" } },
      "fallbackText": "Building gimp"
    } }
  },
  "fallbackText": "Building gimp failed"
}
```

- `version` is `2`, `catalog` the catalog ID and `contractFingerprint` the
  writer's lowercase `sha256:` caller fingerprint.
- `key` is the generated message ID (`steps_build` for the path `steps.build`),
  never a process-local integer and never the dotted path.
- `arguments` maps each input name to `{ "type", "value" }`. `fallbackText` is
  optional plain text; it is omitted rather than `null`.
- A `message` argument's value is `{ "key", "arguments", "fallbackText"? }` of
  the same catalog and fingerprint. It fills a string input.
- Writers emit members in the order above and arguments in ordinal name order.
  Readers accept any member order and reject unknown or duplicate members.

| `type` | `value` |
| --- | --- |
| `string` | any string |
| `int64` | invariant digits, optional `-`, no leading zeros, no `-0` |
| `decimal` | invariant digits with an optional `.` fraction; no exponent, `+`, leading `.` or negative zero; scale is kept (`1.50`) |
| `boolean` | JSON `true` or `false` |
| `date` | `yyyy-MM-dd` |
| `time` | `HH:mm:ss` |
| `datetime` | UTC `yyyy-MM-ddTHH:mm:ssZ` |
| `guid` | lowercase hyphenated (`D`) form |
| `list` | array of strings |
| `message` | nested reference, see above |

### Limits

| Limit | Value |
| --- | ---: |
| Arguments per reference | 32 |
| Key length | 512 |
| Scalar value length; total item length of a list | 16 KiB |
| Items per list | 256 |
| Fallback text length | 64 KiB |
| Nesting depth, the outermost reference counting as 1 | 8 |
| References in one tree, the outermost included | 32 |

A value that breaks the format or a limit rejects the whole reference when it is
read: it comes from a faulty writer, never from a catalog change. JSON input is a
tree; the ESM reader also rejects an in-memory object that contains itself
(`cycle`), while the same message object may appear more than once.

### Version 1

Version 1 is `{version: 1, catalog, contractFingerprint, key, arguments,
fallbackText?}` with arguments `name: string | boolean` and no lists or messages.
It is still read, and a version 1 reference read by .NET is written back as
version 1.

## Resolving a stored reference

Resolution produces plain text in one locale and never throws because the catalog
changed. Its outcome is `translated`, `fallback` (the stored `fallbackText`) or
`failed` (no text), with diagnostics `{path, reason}`. `path` is `$` for the
reference and `$.name` for each message argument on the way to the reference
concerned.

1. A reference of another catalog shows its fallback (`catalog-mismatch`).
2. If the fingerprint differs from the receiver's, a version 1 reference shows
   its fallback (`fingerprint-mismatch`). A version 2 reference continues, with
   the informational diagnostic `contract-changed`.
3. The reference is translated when its key exists (`unknown-key` otherwise) and
   its arguments have exactly the names and types of the key's inputs
   (`incompatible-arguments` otherwise); a `message` argument matches a string
   input. For version 1, each string must have the canonical form of the input's
   type. Keys match exactly: no dotted aliases, no locale-only extra messages.
4. Message arguments are resolved first, in ordinal name order, by the same
   rules. One that cannot be translated uses its own fallback text; without one,
   the reference containing it cannot be translated either.
5. The message is formatted. A message that produces markup or a document in the
   requested locale is not plain text (`structured-content`); a formatting error
   reports `format-failed`.
6. If the reference cannot be translated, it shows its fallback text, or fails
   without one.

The generated ESM resolver also reports `no-handler` when the handler map lacks a
key; pass the generated `m` namespace to resolve every message. Diagnostics list
every reason in evaluation order, so the outermost reason is not always first.
