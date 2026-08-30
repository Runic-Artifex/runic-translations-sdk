# Pinned OASIS XLIFF 2.1 core schema

`xliff_core_2.0.xsd` is the XLIFF 2.1 OASIS Standard core schema, whose stable
core namespace and schema filename remain `2.0`. It was downloaded from
`https://docs.oasis-open.org/xliff/xliff-core/v2.1/os/schemas/` on 2026-08-20.

- `xliff_core_2.0.xsd` SHA-256: `5686d2dbe9dac95e34d1b06a805e1e0f4999db5d5a67dc8bb8514c780592a84d`
- `informativeCopiesOf3rdPartySchemas/w3c/xml.xsd` SHA-256: `61960fb3131e38022caad5360e2f33a3382578ab3c80cd58bd74320ede61b20c`

The second file is the imported W3C XML schema distributed with the OASIS
schema set. Tests validate every generated XLIFF document with this pinned core
schema; Runic data is represented with core `note` elements only.
