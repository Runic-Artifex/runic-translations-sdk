## The Translations Editor's list view: palette commands, unsaved marks, and large catalogs ([#225](https://github.com/Runic-Artifex/runic-translations-sdk/issues/225))

The command palette switches between the workbench and the list (**Switch to list view**, **Switch to workbench**),
in English and German. A row of the list whose message has unsaved work, text that differs from the file on disk or a
changed review state, shows the window title's unsaved mark (•) before its path, named "Unsaved changes" for screen
readers; Save and editing the text back to the saved text clear it. The list renders only the rows in view and
measures rows of different heights, so a catalog of 20 000 messages opens in the list in milliseconds instead of
seconds; keyboard navigation, editing row after row with Tab, filters and the remembered view work as before, and
screen readers still hear each row's position in the whole list.
