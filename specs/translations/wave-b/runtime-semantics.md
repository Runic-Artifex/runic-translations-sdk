# Runtime, locale, and formatting semantics

## Snapshot construction

An `ITranslationSnapshot` is an immutable, thread-safe view of exactly one
catalog and one resolved canonical locale. Construction performs these steps
before the snapshot becomes observable:

1. Canonicalize the requested locale using the Wave A locale rules.
2. Apply the catalog's `unsupportedLocale` policy.
3. Merge compiled direct values with an accepted external pack, if supplied.
4. Resolve every canonical key independently through the declared fallback
   chain and finally the default locale.
5. Freeze patterns and placeholder contracts in canonical key-ID order.

External values replace only a direct value for their declared locale. They do
not replace the fallback graph, add locales or keys, change key IDs, or change a
placeholder contract. The resulting snapshot contains no mutable reference to
pack input bytes or caller-owned collections.

`unsupportedLocale` has these exact behaviors:

- `exact`: only a declared canonical locale succeeds;
- `parentsThenDefault`: try the requested tag and successively remove its last
  subtag until a declared locale is found, then use the default locale if none
  is found;
- `default`: ignore unsupported requested tags and use the default locale.

Declared fallback is never inferred from `CultureInfo`. Parent traversal is
used only to choose a declared locale under `parentsThenDefault`; key fallback
within that snapshot always uses the manifest's explicit graph.

## Lookup and formatting

`Catalog + Id` is the in-process fast key identity. `Catalog + Name` is the
stable diagnostic and transport identity; integer IDs MUST NOT be persisted.
`TryGet`, `Get`, and `Format` reject a key whose catalog or ID/name pair does not
belong to the snapshot. The manifest `missingKey` policy applies only after
supported fallback is exhausted.

Patterns use message grammar version 1. Formatting validates the complete caller
argument set before producing text:

- names compare ordinally;
- argument order is irrelevant to dynamic callers;
- each required argument occurs exactly once;
- unknown, duplicate, missing, or incorrectly typed arguments fail with
  `TranslationFormatException`;
- generated accessors pass arguments in ordinal placeholder-name order;
- the resource locale, not current process culture, selects locale-sensitive
  formatting.

Portable exact formats are culture-independent: strings are unchanged; `int`
and `number` `plain` use invariant decimal notation without grouping or redundant
fractional trailing zeroes; booleans
are `true` or `false`; dates use `yyyy-MM-dd`; ISO times use `HH:mm:ss` plus a
fraction only when non-zero; ISO datetimes normalize the instant to UTC and emit
fixed seven-digit fractional seconds plus `Z`; GUID `d` and `n` use lowercase
hexadecimal. Grouped, fixed,
percent, short, medium, and long formats use the requested resource locale.
Their semantic value is portable, while punctuation, spacing, digits, and names
may follow the platform globalization data. The .NET-resolved string is the
authority when byte identity across runtimes matters.

Values and arguments are plain text. Formatting performs no HTML, JavaScript,
CSS, URL, shell, or template interpretation and returns no trusted-markup type.

## Manager hot swap and concurrency

`SetLocaleAsync` is transactional. It resolves and validates a replacement
snapshot first, honors cancellation until publication, atomically exchanges the
current snapshot, then raises exactly one notification containing the old and
new snapshot/locale. Failure or cancellation before publication leaves the old
snapshot active and raises no notification.

Readers take one current-snapshot reference per operation. They observe either
the complete old snapshot or the complete new snapshot, never a mixture. An
existing generated accessor reads `manager.Current` on every call and therefore
observes later successful swaps.

Concurrent requests for the same canonical locale SHOULD share one in-flight
load. A waiter may cancel its own wait without cancelling work still required by
other waiters. Failed and cancelled loads are not cached as successful. Cache
publication occurs only after pack integrity, parsing, compatibility, limits,
fallback resolution, and snapshot construction all succeed.

Notifications run after publication on the completing caller's continuation;
the core owns no UI dispatcher. Adapter code may marshal notifications. A
notification handler failure does not roll back the already published snapshot.
