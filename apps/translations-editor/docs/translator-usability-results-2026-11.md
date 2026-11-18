# Translator usability study: pilot results (2026-11)

Results of a pilot run of the [study protocol](translator-usability-test.md). The
findings, their causes and the fixes are tracked in
[#225](https://github.com/Runic-Artifex/runic-translations-sdk/issues/225).

## Build and method

| | |
| --- | --- |
| Build | `0.15.0+a53bafe54c7401bac3cd8c5fa2e87ca09030be7c`, channel preview, `linux-x64` framework-dependent publish |
| Platform | Linux (Wayland desktop), editor in a Chromium app window, English interface (German checked separately) |
| Workspace | A 13-message notes catalog: English base, complete French, German with 6 missing messages (one plural with two inputs, one date, one `@maxLength`) and one stale approval |
| Participant | An agent-driven walkthrough using screenshots, pointer and keyboard input, followed by free-form maintainer findings |

The pilot participant knows the code base, so this run does not replace the three
participants the protocol asks for. It tests the setup and finds problems early.

## Task results

| Task | Completed | Notes |
| --- | --- | --- |
| 1 Start the build | yes | |
| 2 Open the workspace | yes | |
| 3 Find incomplete German messages, translate a plain one | yes | The Missing filter (6) matches `runic-translations status`. The missing message opened as raw MF2 pre-filled with the English text. |
| 4 Plural message with an input, inspect the preview | yes | Raw MF2 had to be written by hand. The preview was correct for 1 and 2. |
| 5 Invalid edit, explain, recover without saving | yes | Save was disabled and nothing invalid was written. Undo did not revert the draft, so the text was fixed by hand. |
| 6 Mark reviewed, save, identify the diffs | yes, with a wrong turn | Ctrl+S did not save the review state; a separate "Save workflow" did. |
| 7 Headless validation | yes | Same warnings as the editor, exit code 0. |

Protocol criteria: no invalid-source save, no disagreement between the editor and
`validate`, and no hidden source changes. One unexpected but valid change: a new key
was written to a second group block instead of the existing group.

## Findings

Major: no base-language text while translating; missing messages start in raw MF2;
review state has a separate save; closing loses unsaved review changes without a
warning; a stale approval can't be re-confirmed in the editor; the message list is
flat instead of a namespace tree. #225 lists all 24 findings, the structural causes
behind them, and the planned fixes.

## Next

Run the protocol with three participants once the major findings are fixed, and
record their results here.
