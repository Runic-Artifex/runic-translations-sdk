# Cross-process and persisted text references

A text reference stores a message key, its typed arguments and optional English
(or any) fallback text instead of a formatted sentence. Another process, or the
same application months later, shows it in the reader's language. Writers emit
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
wire object. Pass a reference created this way as a string input to nest it:

```ts
import { createTextReference } from "./generated/operations.esm-v5/transport.js";

const step = createTextReference("steps_build", { name: "gimp" }, { fallbackText: "Building gimp" });
const failed = createTextReference("operation_failed", { step }, { fallbackText: "Building gimp failed" });
```

Store fallback text when a reference must stay readable after its message is
removed. Keys are generated message IDs such as `steps_build`.

## Showing stored references

`TranslationReferenceResolver.Resolve(snapshot, contractFingerprint, reference)`
in .NET and `resolveTextReference(value, m, { locale })` in ESM apply the same
policy and return plain text with an outcome and diagnostics:

| Situation | Result |
| --- | --- |
| Same catalog contract | Translated. |
| Only the contract fingerprint changed, for example a message was added | Translated, with the informational diagnostic `contract-changed`. |
| The key was removed | The reference's fallback text (`unknown-key`). |
| An input was added, removed, renamed or changed its type | The fallback text (`incompatible-arguments`). |
| The message now has markup or is a document in that locale | The fallback text (`structured-content`). |
| A message argument cannot be translated | That argument's own fallback text; without one, the containing reference falls back. |
| No fallback text where one is needed | Outcome `failed`, no text; the application chooses what to show. |
| Version 1 reference and a different fingerprint | The fallback text (`fingerprint-mismatch`): version 1 arguments carry no types. |
| Another catalog | The fallback text (`catalog-mismatch`). |

Message arguments may nest eight levels deep with at most 32 references in one
tree. Structural errors, such as a non-canonical number, an unknown argument type
or a limit, reject the whole reference when it is read: .NET deserialization
throws `JsonException`, and `resolveTextReference` returns `failed` with the
reason. The [specification](../../../specs/translations/transport-v2.md) lists the
canonical forms, limits and reasons.

```ts
import { m } from "./generated/operations.esm-v5/messages.js";
import { resolveTextReference } from "./generated/operations.esm-v5/transport.js";

const shown = resolveTextReference(JSON.parse(stored), m, { locale: "de" });
console.log(shown.text ?? "(unreadable entry)", shown.outcome, shown.diagnostics);
```

`resolveTextReference` calls the handler of each message it formats; passing
`m` keeps every message reachable, while a smaller handler map keeps bundles
small and reports `no-handler` for the rest.

## Live references between processes of one release

`decodeTextReference` and `formatTextReference` remain for live messages, such as
a backend sending UI text to its frontend. `decodeTextReference` validates the
version, catalog, known key, exact argument names and types and the limits, and
returns typed values; message arguments decode to nested references that
`formatTextReference` formats with the same handlers, falling back to their own
fallback text. A version 2 reference is checked key by key; a version 1
reference still requires the receiver's exact fingerprint. Where both sides must
be built from one catalog, call `TranslationReference.ValidateCatalog` in .NET or
compare `contractFingerprint` from `runtime.js`.

## Updating from version 1

New .NET and ESM writers emit version 2. Regenerate the receivers' ESM before or
together with the writers: a `transport.js` generated by an earlier release
rejects version 2 as `unsupported-version`. Stored version 1 references keep
working wherever the contract is unchanged, and fall back otherwise.
`TranslationReferenceArgument` now accepts only the forms receivers accept:
`HH:mm:ss` times, UTC `yyyy-MM-ddTHH:mm:ssZ` date-times, and decimals without
negative zero.
