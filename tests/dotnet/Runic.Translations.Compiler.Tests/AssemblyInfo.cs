using TUnit.Core;
using TUnit.Core.Helpers;

// Cases compile, render and validate in memory and read shared repository files only. Cases that write files create
// and delete their own uniquely named directory under the temporary directory. The decimal canonicalization case sets
// CultureInfo.CurrentCulture, which flows with its own execution context only, and restores it. The suite changes no
// environment variables, current directory or ports. Cases that start bun, for example to run generated ESM or tsc,
// are marked [NotInParallel] and run one at a time on their own. Run the other cases at most one per core.
[assembly: ParallelLimiter<ProcessorCountParallelLimit>]
