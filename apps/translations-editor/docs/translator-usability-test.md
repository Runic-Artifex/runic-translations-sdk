# Translator usability study protocol

Use this bounded study to learn whether translators can perform the editor's core
workflow without contributor knowledge. It is product research, not a release gate.
A useful study has three participants who translate software but did not contribute
to the editor; one may know Runic Translations and at least two should not.

## Build under test

Record the source revision or archive name and SHA-256 digest, operating system,
and any trust warning. Give participants a prepared runnable build and a small
source-language brief. Do not require them to use a development checkout, Node.js,
the .NET SDK, or package-registry credentials.

## Tasks

1. Start the supplied build and, when applicable, verify its documented checksum.
2. Open the supplied workspace with the documented launcher.
3. Find all incomplete German messages and translate a plain message.
4. Translate a message with an input and a plural selector, then inspect its compiled preview.
5. Introduce an invalid edit, explain the diagnostic, and recover without saving invalid source.
6. Mark the message reviewed, save, and identify the source diff and editor-state diff.
7. Run the documented headless validation command.

## Recording and follow-up

For each task, record completion, time, wrong turns, help requested, unexpected terminology, and accessibility input method. Do not record translation text or customer paths in diagnostics.

Treat an inability to start the documented build, invalid-source save, disagreement
between validation and the editor, hidden or unexpectedly broad source changes, or
repeated inability to complete translate/validate/save without help as a concrete
finding. Record the scenario and outcome without personal or workspace data, then
use the finding to prioritize a focused fix or further research.
