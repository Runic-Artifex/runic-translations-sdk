# Public-preview translator usability test

This bounded test is the remaining human release gate for issue #4. Run it with three participants who translate software but did not contribute to the editor. One participant may be familiar with Runic Translations; at least two must not be.

## Build under test

Record the release tag, archive name, SHA-256 digest, operating system, and whether the operating system displayed a trust warning. Participants receive only the GitHub release page and a small source-language brief. Do not give them a development checkout, Node.js, the .NET SDK, or package-registry credentials.

## Tasks

1. Find the correct archive, notice its signing status, verify its checksum, and start it.
2. Open the supplied workspace with the documented launcher.
3. Find all incomplete German messages and translate a plain message.
4. Translate a message with an input and a plural selector, then inspect its compiled preview.
5. Introduce an invalid edit, explain the diagnostic, and recover without saving invalid source.
6. Mark the message reviewed, save, and identify the source diff and editor-state diff.
7. Run the documented headless validation command.

## Recording and release rule

For each task, record completion, time, wrong turns, help requested, unexpected terminology, and accessibility input method. Do not record translation text or customer paths in diagnostics.

A finding blocks the preview when a participant cannot download or launch within the documented platform policy, invalid source is saved, the CI command disagrees with the editor, a source change is hidden or unexpectedly broad, or two participants cannot complete the core translate/validate/save path without contributor help. File each blocking finding separately, link it from issue #4, fix it, and repeat the affected task with a participant who did not see the original failure.

The maintainer posts an anonymized results table to issue #4. This repository does not claim that the human test passed until that comment exists.
