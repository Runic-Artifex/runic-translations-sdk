# Text Resources Native-AOT consumer

This runtime-only package consumer exercises compiled snapshots, typed formatting,
fallback, concurrent hot swap, and verified external packs. Restore the intended
RID with `-p:PublishAot=true -p:PublishTrimmed=true` before publishing. Supply
`-p:PublishAot=true` on the publish command so ordinary restore never acquires
RID-specific dependencies.
