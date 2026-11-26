- The macOS and Windows jobs run only for changes that can behave differently on those platforms: `eng/ci/macos-paths.txt`
  and `eng/ci/windows-paths.txt` now list platform globalization, Windows path rules, the editor's native host and
  toolchain pins, and an ordinary push to `main` follows them as a pull request does. Nightly and dispatched runs and
  the push run of a version or release commit still run every job and upload the `ci-full-run` artifact. Publish
  preview takes a release's packages only from such a full green run of its commit, and fails with the command that
  dispatches one when the commit has only an ordinary push run.
