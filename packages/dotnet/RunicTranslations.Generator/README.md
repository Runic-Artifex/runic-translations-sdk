# RunicTranslations.Generator

This analyzer package turns explicitly marked translation `AdditionalFiles`
into deterministic, strongly typed C# sources. Build integration supplies the
`RunicTextResourceKind` metadata with the value `Catalog` or `Document`.

The generator writes no files. Non-C# locale and web edge artifacts are emitted
by the separate build and CLI surfaces.
