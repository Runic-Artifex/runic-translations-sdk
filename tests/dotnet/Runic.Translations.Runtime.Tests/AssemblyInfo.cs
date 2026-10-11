using TUnit.Core;
using TUnit.Core.Helpers;

// Every case builds its own catalogs, snapshots, managers and providers, and the suite reads shared files only.
// It changes no process state: no environment variables, current directory, culture or ports. The one case that
// installs a synchronization context does so on its own thread. Run at most one case per core.
[assembly: ParallelLimiter<ProcessorCountParallelLimit>]
