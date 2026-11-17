- npm archives are reproducible: a package's `gitHead` is now the commit that last
  changed its inputs (its directory, the workspace packages it depends on and the
  shared build files), not the commit the build ran on, so a commit elsewhere leaves
  the archive and its integrity unchanged. npm's provenance still names the release
  commit. The release check accepts such a `gitHead` when it is an ancestor of the
  release commit with the same inputs (eng/release/README.md, "npm archive source commit").
