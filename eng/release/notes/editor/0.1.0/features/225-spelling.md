## Spell checking in each text's language

([#225](https://github.com/Runic-Artifex/runic-translations-sdk/issues/225), D074.10, D086.2)

The editor checks spelling itself, in the language of the text being edited, with a Hunspell checker per language from
runic-sdk's `Runic.Spelling` (0.24.0-preview.1). The webview's own checker is off, since it checked a German
translation with the system's English dictionary. Misspelled words get a wavy underline in the pattern box, in a
variant's message source and in the composer's message source. Placeholders, markup and MF2 syntax are never marked.
With the caret in a marked word, the editor shows corrections and **Add to dictionary**. Added words go to a word list
that all Runic applications of the user share.

- **English** (`en_US`, `en_GB`) ships with the editor, and the system's Hunspell dictionaries are used.
- **The system language's dictionary downloads on its own** when a project has that language and no dictionary is
  installed, and the composer says so while it runs. **Editor settings → Download my language's dictionary
  automatically** turns this off.
- **Any other language asks first:** the composer names the dictionary's size and downloads it only after
  **Download**. A failed download installs nothing and offers **Try again**.
- Dictionaries come only from `Runic.Spelling`'s pinned catalog: files at a fixed GitHub commit, each checked by size and
  SHA-256 before it is installed in the folder that all Runic applications share, with its license beside it. These
  downloads are the editor's only network requests
  ([Diagnostics and privacy](https://github.com/Runic-Artifex/runic-translations-sdk/blob/editor-v0.1.0/apps/translations-editor/docs/editor-distribution.md#diagnostics-and-privacy)).
- **Editor settings → Check spelling** turns the marks off.
