# Releasing Runic Text Resources

The `Public release` workflow builds and validates the five NuGet packages as
one independently versioned family. A verify-only dispatch is safe on any
branch; registry publication is accepted only from `main`, after the exact
`PUBLISH PUBLIC` confirmation and the `public-release` environment's `main`
deployment policy. Add a required reviewer when the repository becomes public.

Before the first public release:

1. complete and publish the product documentation;
2. make this repository public;
3. create NuGet trusted-publisher policies for owner `Runic-Artifex`, repository
   `runic-text-resources`, workflow `public-release.yml`, and environment
   `public-release`;
4. confirm the environment variable `NUGET_USER` names the nuget.org account;
5. run the workflow once with publication disabled and retain its artifact.

Do not create a release tag until the workflow has passed for that exact version.
