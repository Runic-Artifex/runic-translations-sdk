- **The editor's application ID is now `eu.runicartifex.TranslationsEditor`** (it was `eu.runic-artifex.TranslationsEditor`),
  because runic-sdk's packages accept only letters and digits in each part of the ID. It is the GTK application ID,
  the desktop entry's and icon's name and the macOS bundle identifier. If you installed the desktop entry with
  `desktop-entry install` from an earlier build, remove it: run `desktop-entry uninstall` with that build, or delete
  `~/.local/share/applications/eu.runic-artifex.TranslationsEditor.desktop` and
  `~/.local/share/icons/hicolor/*/apps/eu.runic-artifex.TranslationsEditor.png`. The editor's preferences, recent
  projects and drafts are not stored under the ID and stay where they are.
