# Cross-process text references

Backend-originated UI text uses transport version 1: catalog ID, generated
contract fingerprint, stable dotted key, canonical arguments, and optional plain
fallback text. Integer and decimal wire values are canonical decimal strings;
booleans are JSON booleans; other portable values are strings.

The .NET runtime exposes bounded immutable `TextResourceReference` and
`TextResourceReferenceArgument` types. Generated ESM `transport.js` verifies the
version, catalog, fingerprint, known key, exact argument names/types, and size
limits before producing decoded arguments. Formatting remains explicit: pass a
handler map containing only the frontend messages the application needs, which
preserves ordinary tree-shaking. Fingerprint or catalog skew fails before message
execution; callers may deliberately choose `fallbackText` as their skew policy.
