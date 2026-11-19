## A side-by-side list for bulk translation in the Translations Editor ([#225](https://github.com/Runic-Artifex/runic-translations-sdk/issues/225))

The editor's toolbar switches between the workbench and a list (D074.1). The list shows the selected language's
messages as rows in the order of the message tree, under their namespaces, with the dotted message path, the source
text, the translation and its state (a hollow square for missing, a filled one for done, a diagonal for outdated). It
follows the sidebar's search and filter, and the selected message stays selected when you switch.

A message whose text is one sentence with placeholders is edited in its row with the same placeholder chips as the
workbench; a missing translation starts empty beside its source text. Up and Down move between rows, Enter edits,
Escape stops editing and Tab goes on to the next row. A message with plural forms, other variants or more structure
shows a summary and opens in the workbench. Edits share the workbench's one save, undo, recovery of unsaved work and
close question, and the density setting applies to the rows. The source language and one-language projects list the
path, the text and the developers' note. The editor remembers the view you chose.
