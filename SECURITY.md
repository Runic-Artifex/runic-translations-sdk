# Security Policy

## Supported versions

Only the latest minor release of the `Runic.Translations*` NuGet packages, the
`@runic-artifex/*translations*` npm packages and the `dotnet-runic-translations`
tool is supported. Fixes ship in a patch of that minor or in the next release;
earlier minors are not patched. See
[Versions before 1.0](eng/release/README.md#versions-before-10) and, from 1.0,
[Versioning and support from 1.0](eng/release/README.md#versioning-and-support-from-10).
The Translations Editor is a separate product with its own releases (see
[its README](apps/translations-editor/README.md)).

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

Every published NuGet package, npm package and GitHub release asset, including
the VS Code and Visual Studio VSIX files, has a signed build-provenance
attestation, and each release has a CycloneDX SBOM that is attached to the
release and attested for its packages and extensions. Verify a file with
`gh attestation verify <file> -R Runic-Artifex/runic-translations-sdk --signer-workflow Runic-Artifex/runic-translations-sdk/.github/workflows/publish-preview.yml`.
The attestation shows that the publish workflow of this repository, which runs
only on `main`, published those bytes. It does not build them: it takes the
packages from a successful CI run of the same commit and checks them by hash
before attesting them. Packages
downloaded from NuGet.org carry NuGet.org's repository signature and therefore
differ from the attested bytes; verify the copy attached to the GitHub release
instead. See [verifying a release](eng/release/README.md#verifying-a-release).

## Dependency monitoring

The weekly [dependency audit](.github/workflows/dependency-audit.yml) runs
`bun run dependencies:audit`, which reports known advisories for every tracked
`bun.lock` and the NuGet restore graphs of the solution and the editor. It never
upgrades dependencies; upgrades stay deliberate changes.
