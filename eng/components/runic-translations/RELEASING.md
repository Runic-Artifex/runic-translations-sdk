# Releasing Runic Translations

The `Public release` workflow builds one versioned family containing seven
NuGet packages and `@runic-artifex/vite-plugin-runic-translations` on npm. The npm
package uses the canonical Runic Translations identity alongside the repository
and product.

Every dispatch requires an explicit exact version. The next planned private
candidate is `0.1.0-preview.4.1`; this is a planning value, not a claim that the
candidate has been verified or published. Run the workflow with publication
disabled first and retain the artifact containing all eight packages and its
`SHA256SUMS` file.

Publication is accepted only from `main`, after the exact `PUBLISH PUBLIC`
confirmation and approval from the `public-release` environment. Before the
first public release:

1. complete and publish the product documentation, make the repository public,
   and add a required reviewer plus a `main` deployment policy to the
   `public-release` environment;
2. create NuGet trusted-publisher policies for owner `Runic-Artifex`, repository
   `runic-translations`, workflow `public-release.yml`, and environment
   `public-release`, then set environment variable `NUGET_USER` to the matching
   nuget.org account;
3. add a short-lived npm granular access token as environment secret
   `NPM_BOOTSTRAP_TOKEN`, limited to the `@runic-artifex` scope, and publish the
   first version with `npm_bootstrap` enabled;
4. configure npm trusted publishing for
   `@runic-artifex/vite-plugin-runic-translations` using this repository,
   workflow filename `public-release.yml`, environment `public-release`, and
   the `npm publish` allowed action;
5. delete `NPM_BOOTSTRAP_TOKEN`; all later releases use OIDC with
   `npm_bootstrap` disabled.

The workflow verifies checksums again after download. It preflights every npm
and NuGet identity before the first push. An npm retry skips an existing package
only when its registry integrity matches the candidate; NuGet retries accept an
existing immutable version only when its metadata identifies the same source
commit.

Do not create a release tag until publication has passed for that exact version.
