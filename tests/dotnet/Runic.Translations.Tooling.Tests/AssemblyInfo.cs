using TUnit.Core;
using TUnit.Core.Helpers;

// Every case compiles its own in-memory sources and exports, imports or inspects in-memory documents. The suite
// changes no process state: no environment variables, current directory, culture, ports, files or child
// processes. Run at most one case per core.
[assembly: ParallelLimiter<ProcessorCountParallelLimit>]
