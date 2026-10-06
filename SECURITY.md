# Security Policy

## Supported versions

Runic Translations is in preview. Only the latest published preview of the
`Runic.Translations*` NuGet packages, the `@runic-artifex/*translations*` npm
packages, the `dotnet-runic-translations` tool and the Translations Editor is
supported. Fixes ship in a new preview; earlier previews are not patched.

## Reporting a vulnerability

Report vulnerabilities privately through GitHub:

**[Report a vulnerability](https://github.com/Runic-Artifex/runic-translations-sdk/security/advisories/new)**

Do not open a public issue, pull request or discussion for a security report.

Include the package or tool version, the operating system and runtime, steps to
reproduce and the impact you expect. A minimal reproduction is the most useful
attachment.

You get an acknowledgement and status updates on the advisory thread. When a fix
is released, the advisory is published with credit to the reporter unless you
ask otherwise.

## Verifying releases

Every published NuGet package, npm package and GitHub release asset has a signed
build-provenance attestation, and each release has a CycloneDX SBOM that is
attached to the release and attested for its packages. The IDE extensions are not
released yet. Verify a file with
`gh attestation verify <file> -R Runic-Artifex/runic-translations-sdk`. Packages
downloaded from NuGet.org carry NuGet.org's repository signature and therefore
differ from the attested bytes; verify the copy attached to the GitHub release
instead. See [verifying a release](eng/release/README.md#verifying-a-release).

## Dependency monitoring

The weekly [dependency audit](.github/workflows/dependency-audit.yml) runs
`bun run dependencies:audit`, which reports known advisories for every tracked
`bun.lock` and the NuGet restore graphs of the solution and the editor. It never
upgrades dependencies; upgrades stay deliberate changes.
