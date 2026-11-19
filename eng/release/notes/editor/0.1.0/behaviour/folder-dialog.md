- The Translations Editor's **Browse** dialog for a workspace folder now belongs to the editor window, through the
  window's Runic Desktop file dialogs: the XDG portal's FileChooser parented to the window on Linux, the system folder
  dialog modal for the window on Windows, and a sheet on the window on macOS. In an installed browser (`--browser`) the
  same dialogs open without an owner, and `serve`, which has no window, shows them without one. The `zenity` fallback
  on Linux and the PowerShell and AppleScript dialogs on Windows and macOS are gone: where no dialog can open, for
  example on Linux without a FileChooser portal, the editor asks for the workspace path instead.
