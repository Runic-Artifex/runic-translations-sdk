# Cross-process and persisted text references

A text reference stores a message key, its typed arguments and optional
fallback text instead of a formatted sentence. Another process, or the same
application months later, shows it in the reader's language. Writers emit
[transport version 2](../../../specs/translations/transport-v2.md); readers also
accept version 1.

## Writing references

In .NET, create a `TranslationReference` with `TranslationReferenceArgument`
values: a canonical scalar (`new(TextArgumentType.Int, "3")`), a list
(`new(["firefox", "gimp"])`) or another message (`new(stepReference)`), which
fills a string input with that message's text. Serialize it with
`TranslationReferenceJsonContext`, which is reflection-free and NativeAOT-safe:

```csharp
var step = new TranslationReference(OperationTextCatalog.CatalogId, OperationTextCatalog.ContractFingerprint,
    "steps_build", new Dictionary<string, TranslationReferenceArgument> { ["name"] = new(TextArgumentType.String, "gimp") },
    "Building gimp");
var failed = new TranslationReference(OperationTextCatalog.CatalogId, OperationTextCatalog.ContractFingerprint,
    "operation_failed", new Dictionary<string, TranslationReferenceArgument> { ["step"] = new(step) },
    "Building gimp failed");
string json = JsonSerializer.Serialize(failed, TranslationReferenceJsonContext.Default.TranslationReference);
```

In generated ESM, `createTextReference(key, inputs, { fallbackText })` from
`transport.js` checks the inputs against the key's contract and returns the same
wire object; `JSON.stringify` of it and the .NET serializer produce the same
bytes. Pass a reference created this way as a string input to nest it:

```ts
import { createTextReference } from "./generated/operations.esm-v5/transport.js";

const step = createTextReference("steps_build", { name: "gimp" }, { fallbackText: "Building gimp" });
const failed = createTextReference("operation_failed", { step }, { fallbackText: "Building gimp failed" });
```

The key is the message's generated ID, such as `steps_build`, not the dotted
source path `steps.build`. Store fallback text when a reference must stay
readable after its message is removed.

## Showing stored references

Read stored references from their JSON text, so the size limit is checked before
anything is parsed: .NET `TranslationReference.Parse(json)` and ESM
`resolveTextReference(json, m, { locale })`, which accepts the string directly
(or `parseTextReference(json)` for the parsed value). Then
`TranslationReferenceResolver.Resolve(snapshot, contractFingerprint, reference)`
in .NET and `resolveTextReference` in ESM apply the same policy and return plain
text with an outcome and diagnostics:

| Situation | Result |
| --- | --- |
| Same catalog contract | Translated. |
| Only the contract fingerprint changed, for example a message was added | Translated, with the informational diagnostic `contract-changed`. |
| The key was removed, or has no message in the locale | The reference's fallback text (`unknown-key`). |
| An input was added, removed, renamed or changed its type | The fallback text (`incompatible-arguments`). |
| The message now has markup or is a document in that locale | The fallback text (`structured-content`). |
| A message argument cannot be translated | That argument's own fallback text; without one, the containing reference falls back. |
| No fallback text where one is needed | Outcome `failed`, no text; the application chooses what to show. |
| Version 1 reference and a different fingerprint | The fallback text (`fingerprint-mismatch`): version 1 arguments carry no types. |
| Another catalog | The fallback text (`catalog-mismatch`). |

```ts
import { m } from "./generated/operations.esm-v5/messages.js";
import { resolveTextReference } from "./generated/operations.esm-v5/transport.js";

const shown = resolveTextReference(storedJson, m, { locale: "de" });
element.textContent = shown.text ?? "(unreadable entry)";
```

The text is plain and contains stored fallback text and argument values as they
were written. Show it as text (`textContent`, a text node or a framework's text
binding), never as HTML.

Message arguments may nest eight levels deep with at most 32 references in one
tree, 64 Ki characters of text and 256 KiB encoded. Structural errors, such as a
non-canonical number, an unknown argument type, a duplicate member or a broken
limit, reject the whole reference when it is read: .NET throws `JsonException`,
and `resolveTextReference` returns `failed` with the reason. The
[specification](../../../specs/translations/transport-v2.md) lists the canonical
forms, limits and reasons.

`resolveTextReference` calls the handler of each message it formats; passing
`m` keeps every message reachable, while a smaller handler map keeps bundles
small and reports `no-handler` for the rest.

### What the check cannot see

A stored reference is translated when its key still exists with the same input
names and types. The check cannot tell that a message or an input kept its name
but changed its meaning, that a removed `select` variant now falls through to
`*`, or that an input receiving a message argument is used as a selector. Give a
message a new key when its meaning changes.

## Live references between processes of one release

`decodeTextReference` and `formatTextReference` remain for live messages, such as
a backend sending UI text to its frontend. `decodeTextReference` validates the
version, catalog, known key, exact argument names and types and the limits, and
returns typed values; message arguments decode to nested references that
`formatTextReference` formats with the same handlers, falling back to their own
fallback text. A version 2 reference is checked key by key and no longer
compared with the receiver's contract fingerprint; a version 1 reference still
is. Where both sides must be built from one catalog, call
`TranslationReference.ValidateCatalog` in .NET or compare the decoded
`contractFingerprint` with the one exported by `runtime.js`.

## Upgrading from version 1

Readers from 0.6.0-preview.7 and earlier, both the .NET `TranslationReferenceJsonContext`
and every generated `transport.js`, reject version 2 (`unsupported-version` in
ESM, `JsonException` in .NET). Upgrade in this order:

1. Upgrade and deploy every receiver: update the .NET package and regenerate the
   ESM output. New receivers read both versions.
2. Until then, keep writers on version 1 with
   `TranslationReference.CreateVersion1(...)` in .NET or
   `createTextReference(key, inputs, { version: 1 })` in ESM. Version 1 carries
   scalar arguments only; a list or message argument throws and needs version 2.
3. Once every receiver is upgraded, switch writers to the version 2 constructor
   and `createTextReference` without `version`.

`TranslationReferenceArgument` now accepts only the spellings that every
receiver accepts. Code that created other spellings must change:

- Date-times: convert to UTC and drop fractional seconds, then format with
  `yyyy-MM-dd'T'HH:mm:ss'Z'`:
  `new(TextArgumentType.DateTime, value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))`.
  An offset such as `+02:00` or a fraction such as `.5` is rejected.
- Times: whole seconds, `HH:mm:ss`:
  `time.ToString("HH:mm:ss", CultureInfo.InvariantCulture)`.
- Decimals: no negative zero (`-0`, `-0.00`); write `0` instead.

Stored version 1 data keeps working where its contract is unchanged. Where the
catalog's contract fingerprint has changed, a version 1 entry now shows its
fallback text (`fingerprint-mismatch`), or fails without one, instead of being
translated: rewrite such entries as version 2 if they must stay translatable. A
version 1 value that only earlier .NET writers accepted, such as a fractional
time or an offset date-time, never matched an ESM receiver and now does not match
in .NET either (`incompatible-arguments`). Code that rebuilds typed arguments from
stored version 1 strings, with `new TranslationReferenceArgument(type, value)`,
throws `ArgumentException` for those values; catch it, or convert the value as
shown above first.
