using TUnit.Core;

// Cases start child processes: dotnet builds and restores of temporary consumer projects, the CLI, serve mode and the
// language server, each with a fixed response timeout. Run them one at a time so that load from other cases cannot
// push them past those timeouts.
[assembly: NotInParallel]
