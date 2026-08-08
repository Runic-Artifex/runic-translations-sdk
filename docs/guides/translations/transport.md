# Cross-process text references

Backend-originated UI text uses transport version 1: catalog ID, generated
contract fingerprint, stable dotted key, canonical arguments, and optional plain
fallback text. Integer and decimal wire values are canonical decimal strings;
booleans are JSON booleans; other portable values are strings.

The .NET runtime exposes bounded immutable `TextResourceReference` and
`TextResourceReferenceArgument` types plus `TextResourceReferenceJsonContext` for
reflection-free Native-AOT-safe serialization. Generated ESM `transport.js` verifies the
version, catalog, fingerprint, known key, exact argument names/types, and size
limits before producing decoded arguments. Formatting remains explicit: pass a
handler map containing only the frontend messages the application needs, which
preserves ordinary tree-shaking. Fingerprint or catalog skew fails before message
execution; callers may deliberately choose `fallbackText` as their skew policy.

The wire intentionally omits argument type tags because the generated receiving
catalog owns the key's complete type contract. The .NET converter's read path can
only preserve the JSON distinction between strings and booleans; application
receivers that need typed .NET values should validate against their generated
catalog contract before constructing runtime `TextArgument` values.
