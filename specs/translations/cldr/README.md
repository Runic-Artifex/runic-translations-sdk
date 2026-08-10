# Pinned CLDR subset

`runic-subset-48.2.json` is the normalized, reviewable subset derived from
Unicode CLDR 48.2 (`cldr-json` tag `48.2.0`). The source archive URL and
SHA-512 digest are recorded in the file. The retained fields are plural-rule
families and relative-time patterns for Runic's explicit target locales.

The data is licensed under Unicode License v3; the complete notice is in
`LICENSE`. Update the pinned source, digest, normalized subset, generated C#,
capability matrix, and cross-runtime fixtures together. Then run:

```bash
node eng/generate-cldr.mjs
./eng/verify.sh
```

Generation is offline and deterministic. It never downloads data during a
build. Reviewers should compare normalized values to the tagged
`unicode-org/cldr-json` files before accepting a pin change.
