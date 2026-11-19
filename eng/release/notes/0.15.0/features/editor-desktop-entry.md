## The Translations Editor's icon on Linux desktops

`runic-translations-editor desktop-entry install` writes the editor's desktop entry and icon for the current user:
`$XDG_DATA_HOME/applications/eu.runic-artifex.TranslationsEditor.desktop` and the icon in the user's hicolor theme
(`$XDG_DATA_HOME` is `~/.local/share` by default). The entry is named after the editor's app ID, and its
`StartupWMClass` and icon name are that ID, so Wayland compositors and X11 desktops show the editor's window with its
icon and list the editor with the applications instead of a generic icon. `desktop-entry uninstall` removes exactly
those files; an entry of the same name that the editor did not write, such as a package's, is neither replaced nor
removed. No launch writes into the home directory. On macOS the editor sets its Dock icon from its packaged icon.
