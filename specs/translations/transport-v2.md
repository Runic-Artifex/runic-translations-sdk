# Text-reference transport, version 2

A text reference identifies a message and its arguments so that another process,
or the same application later, can show it in the reader's language. It is the
wire contract behind the .NET `TranslationReference` and the generated ESM
`transport.js`. Writers emit version 2 unless asked for version 1; readers
accept versions 1 and 2. The
[persisted-reference corpus](corpus/transport-v2-persisted-references.json) is
the shared .NET and ESM oracle for this document: resolution, rejection and
byte-exact writing.

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

### What the per-key check cannot detect

The check compares input names and types, not meaning. It cannot tell that:

- a message keeps its key and inputs but now says something else, or an input of
  the same name and type now means something else (`$count` of files becomes
  `$count` of folders);
- a `select` variant was removed, so a stored value that used to match it now
  falls through to `*`;
- a string input that receives a message argument is used as a selector, so the
  translated text of the inner message, not a stable value, chooses the variant.

Give a message a new key when its meaning changes, and do not select on inputs
that may receive message arguments.

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

- `version` is the integer `2`, `catalog` the catalog ID and
  `contractFingerprint` the writer's lowercase `sha256:` caller fingerprint.
- `key` is the message's key name (.NET `TranslationKey.Name`). For an RMF2
  catalog that is the generated ID (`steps_build` for the source path
  `steps.build`); the dotted path is not a key and is never matched. It is never
  a process-local integer.
- `arguments` maps each input name to `{ "type", "value" }`. `fallbackText` is
  optional plain text; version 2 omits it rather than writing `null`.
- A `message` argument's value is `{ "key", "arguments", "fallbackText"? }` of
  the same catalog and fingerprint. It fills a string input.
- Catalog IDs, keys and argument names must not be blank. Blank means empty or
  made only of Unicode `White_Space` characters (U+0009–U+000D, U+0020, U+0085,
  U+00A0, U+1680, U+2000–U+200A, U+2028, U+2029, U+202F, U+205F, U+3000), which
  is what .NET `string.IsNullOrWhiteSpace` tests; U+FEFF is not white space.
- Every string must be well-formed UTF-16: a lone surrogate is rejected by both
  writers and both readers.

| `type` | `value` |
| --- | --- |
| `string` | any string |
| `int64` | invariant digits, optional `-`, no leading zeros, no `-0`, within the signed 64-bit range |
| `decimal` | invariant digits with an optional `.` and 1 to 28 fraction digits; no exponent, `+`, leading `.` or negative zero; all digits together, scale kept (`1.50` is `150`), at most 79228162514264337593543950335 |
| `boolean` | JSON `true` or `false` |
| `date` | `yyyy-MM-dd`, year 0001 or later |
| `time` | `HH:mm:ss`, no fractional seconds |
| `datetime` | UTC `yyyy-MM-ddTHH:mm:ssZ`, no fractional seconds or offset |
| `guid` | lowercase hyphenated (`D`) form |
| `list` | array of strings |
| `message` | nested reference, see above |

A decimal keeps its scale on the wire; formatting uses its value, so `1.50` and
`1.5` format the same in both runtimes.

### Writing

Writers emit members in the order above and arguments in ordinal name order, and
escape strings exactly as ECMAScript `JSON.stringify` does: `"` and `\` as `\"`
and `\\`, the controls U+0008, U+0009, U+000A, U+000C and U+000D as `\b`, `\t`,
`\n`, `\f` and `\r`, the other controls up to U+001F as lowercase `\u00xx`, and
every other character literally, including non-ASCII text, `<`, `>`, `&`, `'`,
`+`, U+007F, U+2028 and U+2029. The .NET writer produces the same bytes as
`JSON.stringify` of the ESM writer's object, whatever encoder the serializer
options name.

### Reading

Readers accept any member order. They reject unknown members, duplicate members
(including duplicate argument names), a `version` that is not an integer token
(`2.0` and `2e0` are rejected), and input larger than the encoded limit, which is
checked before parsing: .NET `TranslationReference.Parse` and the JSON converter
measure the input first, and ESM `parseTextReference(json)`, which
`resolveTextReference` and `decodeTextReference` use for a string, checks the
length before reading JSON. An object that the application parsed itself is
checked against every limit except the encoded size.

### Limits

| Limit | Value |
| --- | ---: |
| Encoded reference, in UTF-8 bytes | 256 KiB |
| Text in one tree: every scalar value (booleans as `true` or `false`), list item and fallback text, in UTF-16 code units | 64 Ki |
| Arguments per reference | 32 |
| Key length | 512 |
| Scalar value length; total item length of a list | 16 Ki |
| Items per list | 256 |
| Fallback text length | 64 Ki |
| Nesting depth, the outermost reference counting as 1 | 8 |
| References in one tree, the outermost included | 32 |

The text limit keeps ordinary references well inside the encoded limit; a
writer still rejects a reference whose encoding exceeds 256 KiB, for example
text made mostly of escaped control characters. A value that breaks the format
or a limit rejects the whole reference when it is read: it comes from a faulty
writer, never from a catalog change. JSON input is a tree; the ESM reader also
rejects an in-memory object that contains itself (`cycle`), while the same
message object may appear more than once. .NET references are immutable and
built from their arguments, so a .NET reference cannot contain itself.

### Version 1

Version 1 is `{version: 1, catalog, contractFingerprint, key, arguments,
fallbackText?}` with arguments `name: string | boolean` and no lists or messages.
It is still read: a `null` fallback is read as no fallback, as earlier readers
did. A version 1 reference read by .NET is written back as version 1.

A version 1 string is read for the receiver's input type as `decodeTextReference`
has always read it: integers may be `-0`, decimals may use an exponent and are
reduced to their value (`1.5e1` is 15; trailing zeros beyond 28 fraction digits
are dropped), GUIDs may be uppercase, a boolean input needs a JSON boolean, and
dates, times and date-times need the version 2 forms. A time with fractional
seconds or a date-time with an offset, which earlier .NET writers accepted, does
not match its input.

Writers emit only version 2 since 0.8: the .NET `TranslationReference.CreateVersion1`
and ESM `createTextReference(key, inputs, { version: 1 })` writers were removed.
Readers on both sides still read version 1. Version 1 carries scalar
arguments only, each value as its canonical string (booleans as JSON booleans);
a list or message argument throws.

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
3. The reference is translated when its key exists and has a message in the
   requested locale (`unknown-key` otherwise, whatever the snapshot's
   missing-key policy would format), and its arguments have exactly the names
   and types of the key's inputs (`incompatible-arguments` otherwise); a
   `message` argument matches a string input. Version 1 strings are read as
   described above. Keys match exactly: no dotted aliases, no locale-only extra
   messages.
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

### Output safety

The resolved text is plain text that contains stored fallback text and argument
values verbatim, unescaped. Render it as text (`textContent`, a text node, a
framework's text binding), never as HTML.
