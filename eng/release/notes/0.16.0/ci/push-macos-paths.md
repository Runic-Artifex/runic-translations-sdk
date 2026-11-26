- An ordinary push to `main` runs the two macOS jobs only when it changes a macOS path, as a pull request does;
  nightly and dispatched runs and the push run of a version or release commit still run every job and upload the
  `ci-full-run` artifact. Publish preview takes a release's packages only from such a full green run of its commit,
  and fails with the command that dispatches one when the commit has only an ordinary push run.
