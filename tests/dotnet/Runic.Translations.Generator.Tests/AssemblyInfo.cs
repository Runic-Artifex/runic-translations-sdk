using TUnit.Core;
using TUnit.Core.Helpers;

// Every case builds its own compilations and generator driver, and generated assemblies load from bytes into
// separate load contexts, so cases share no mutable state. The work is CPU-bound: run at most one case per core.
[assembly: ParallelLimiter<ProcessorCountParallelLimit>]
