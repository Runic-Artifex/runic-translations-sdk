- The language server stays responsive in large catalogs ([#271](https://github.com/Runic-Artifex/runic-translations-sdk/issues/271)):
  - It validates a burst of edits once, at the last one. Before, every keystroke queued a full validation of the
    catalog, and in a catalog of 20,000 messages in 10 locales a hover after five keystrokes waited almost five minutes.
  - It validates in the background, cancelling a validation that a newer edit makes outdated. Hover, completion, the
    outline and the code actions are answered while it runs. Diagnostics arrive when the validation of the latest text
    finishes. Hover shows the contract of the latest finished validation.
  - A catalog that does not compile is no longer compiled a second time for its review state unless it has a review
    file, and saved sources are not read again for every request.
