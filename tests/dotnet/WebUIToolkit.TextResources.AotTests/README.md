# Text Resources Native-AOT consumer

This runtime-only package consumer exercises compiled snapshots, typed formatting,
fallback, concurrent hot swap, and verified external packs. RID restores and publish
must use `-p:NuGetLockFilePath=obj/aot.packages.lock.json -p:RestoreLockedMode=false`;
the committed `packages.lock.json` remains portable. Supply `-p:PublishAot=true` on
the publish command so ordinary restore never acquires RID-specific dependencies.
