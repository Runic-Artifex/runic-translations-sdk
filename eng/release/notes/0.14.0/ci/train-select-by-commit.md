- Publish preview's train gate (row P-tr) takes runic-sdk's newest successful
  `main` run at or after a pinned commit (`sdk: run@<commit>`), never simply the
  newest run, and refuses to publish if Translations `main` moved since the commit
  that runic-sdk's train feed served.
