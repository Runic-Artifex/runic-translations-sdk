- The Translations Editor publishes for Windows and macOS from any build machine. `dotnet publish -r win-x64` or
  `-r osx-arm64` on Linux failed with CS0234: the Views bootstrap build clears the runtime identifier, so it chose the
  build machine's platform package, which the restore for the target runtime does not contain. The bootstrap now compiles
  without a platform package, and the Windows and macOS builds compile cleanly with warnings as errors.
- A published Translations Editor no longer carries its smoke test and the Runic SDK's window test host
  (`Runic.Application.Testing`, `Microsoft.Extensions.TimeProvider.Testing`): a framework-dependent linux-x64 publish is
  735,577 bytes smaller and the NativeAOT executable 410,704 bytes. Verification builds (`bun run build`,
  `bun run verify-editor-packed`) compile the smoke test with `-p:RunicEditorSmokeTest=true` and run the same scenarios;
  in other builds `--smoke-test` fails with `REDIT0001`.
