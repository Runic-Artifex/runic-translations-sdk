using TUnit.Core;
using TUnit.Core.Helpers;

// Cases that write files create and delete their own uniquely named directory under the temporary directory; the
// other cases plan edits in memory. The suite changes no environment variables, current directory, culture or ports
// and starts no processes. Run at most one case per core; the timed editor-state scale case runs on its own.
[assembly: ParallelLimiter<ProcessorCountParallelLimit>]
