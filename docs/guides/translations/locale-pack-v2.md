# Locale-pack-v2 bytes contract

`locale-pack-v2` is the bytes-first form of the existing normalized locale
artifact v2. Its JSON shape is `locale-artifact-v2.schema.json`; callers pass
UTF-8 bytes, not a pre-trusted object. The .NET runtime uses
`TranslationPackLoader.VerifyAsync`; generated ESM exports
`decodeLocalePackV2(content, expectedLocale, integrityVerifier?)`.

Both decoder paths copy the supplied byte sequence before invoking the optional
integrity hook, invoke that hook before JSON parsing, and parse only the copied
bytes. A rejected or throwing hook produces `RTR0023/integrity-rejected` without
leaking verifier details. Integrity is caller policy, not a claim that JSON
validation establishes authenticity.

Both enforce these maxima: 8 MiB document bytes, JSON depth 64, 50,000
messages, 64 KiB cumulative UTF-8 text bytes per message variant, 32 arguments,
16 selectors, 256 variants, 4,096 nodes, and markup depth 16. Both require
artifact and grammar version 2, catalog and expected canonical locale equality,
and the generated contract fingerprint equality.

Rejections are location-free `RTR0023/<reason>` IDs. Shared reasons include
`artifact-version-mismatch`, `message-grammar-version-mismatch`,
`catalog-mismatch`, `locale-mismatch`, `contract-fingerprint-mismatch`,
`limit-exceeded`, `unknown-key`, `argument-contract-mismatch`,
`malformed-pattern`, `unknown-member`, `integrity-rejected`, and `malformed`.
The .NET `TranslationPackFailure.GetRejectionId` method exposes the same ID
family. Unknown root, message, input-descriptor, selector, and variant members use
`RTR0023/unknown-member`; descriptor and argument incompatibility uses
`RTR0023/argument-contract-mismatch`; all configured structural bounds use
`RTR0023/limit-exceeded`.
