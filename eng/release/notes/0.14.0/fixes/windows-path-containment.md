- On Windows, a project with several `sourceRoots` attributes each resource file to its own source root again. The
  language server's hover, completion and rename keys, its refactorings and the authoring edits used the first root's
  namespace for files in a sibling root, and did not reject files outside every root. Every containment check of the
  tools now handles both separators, other drives and shares, and the platform's case rules.
- On Windows, `runic-translations` export, import and message edits print a path outside the working directory in full with
  `/`, as on Linux and macOS, instead of a relative `..\` path.
