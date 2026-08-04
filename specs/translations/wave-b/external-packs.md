# External locale packs

External packs are optional, caller-supplied, untrusted UTF-8 bytes. The runtime
does not open a path, URI, socket, embedded resource, or assembly to discover
them. `IExternalTextResourceSource` is the only acquisition boundary.

Version 1 packs use `external-pack-v1.schema.json`, which is deliberately the
same six-field payload as `locale-artifact-v1.schema.json`:

```text
artifactVersion, messageGrammarVersion, catalog, locale,
contractFingerprint, messages
```

Every root, message, and argument member is required. Unknown or duplicate
members are rejected. A pack may contain a subset of known messages; every
present key must be a known compiled key. Its pattern must be valid grammar v1,
and its ordinal argument list must exactly match the compiled name/type/format
contract. A pack cannot add a key, descriptor, locale, layer, or fallback edge.

Validation order is security-significant:

1. Acquire bounded raw bytes from the caller interface.
2. Invoke the optional integrity callback over the complete raw bytes.
3. Parse strict UTF-8 JSON with duplicate detection and cancellation.
4. Check artifact/grammar version, catalog, canonical locale, and Wave A contract
   fingerprint.
5. Enforce known-key, pattern, descriptor, count, size, and depth rules.
6. Build a complete immutable candidate snapshot with compiled fallback.
7. Publish to cache or manager only after every step succeeds.

Integrity metadata is outside the JSON payload. The callback decides signature,
hash, trust anchor, and key-rollover policy and MUST run before parsing when
configured. An accepted signature does not bypass schema, compatibility, or
limit checks.

## Default runtime limits

| Limit | Default |
|---|---:|
| Raw pack bytes | 8 MiB |
| JSON nesting depth | 64 |
| Messages | 50,000 |
| Decoded UTF-8 pattern bytes | 64 KiB |
| Arguments per message | 32 |

Runtime options may tighten these values. Raising a runtime external-pack limit
requires explicit opt-in and remains subject to address-space and output limits.
JSON Schema `maxLength` counts characters and therefore does not replace the
decoded UTF-8 byte check.

Malformed, incompatible, over-limit, integrity-rejected, cancelled, or otherwise
failed packs throw `TextResourcePackException` at the pack boundary (with
cancellation preserved as cancellation where applicable). They never enter the
successful cache, never partially overlay compiled data, never replace the
active snapshot, and never raise a locale-changed notification.

Diagnostics and logs may include catalog, locale, key, version, limit name, and
correlation details. They redact resource patterns and argument values by
default. Pack incompatibility uses `RTR0023` on build/tool surfaces; runtime
exceptions carry the same stable reason category without pretending untrusted
runtime bytes have a source-file span.
