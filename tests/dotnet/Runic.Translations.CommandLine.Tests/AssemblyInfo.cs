using TUnit.Core;
using TUnit.Core.Helpers;

// Every case builds its own command application, in-memory console, resolver, manager or snapshot and passes the
// culture explicitly. The suite changes no process state: no environment variables, current directory, culture,
// ports, files or child processes. The built-in catalog snapshots are cached in a concurrent dictionary. Run at
// most one case per core.
[assembly: ParallelLimiter<ProcessorCountParallelLimit>]
