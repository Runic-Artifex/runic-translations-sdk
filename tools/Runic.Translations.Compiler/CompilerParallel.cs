using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace Runic.Translations.Compiler;

/// <summary>
/// Data-parallel loops for the per-file and per-message work of a compile: each index computes a value from its own
/// input only, and the caller consumes the results in index order, so the output is the same as a sequential loop.
/// </summary>
internal static class CompilerParallel
{
    // Below this many items the work runs inline: small compiles (tests, the source generator's single files, a
    // keystroke's relink) gain nothing from scheduling.
    internal const int Threshold = 32;

    /// <summary>Computes <paramref name="compute"/> for each index, in parallel when there are enough of them.</summary>
    /// <exception cref="OperationCanceledException">The token was canceled.</exception>
    internal static T[] Map<T>(int count, Func<int, T> compute, CancellationToken cancellationToken)
    {
        var results = new T[count];
        if (count < Threshold || Environment.ProcessorCount == 1)
        {
            for (int index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                results[index] = compute(index);
            }
            return results;
        }
        try
        {
            Parallel.For(0, count, new ParallelOptions { CancellationToken = cancellationToken }, index => results[index] = compute(index));
        }
        catch (AggregateException aggregate)
        {
            // The first failure as a sequential loop would have thrown it; a cancellation stays a cancellation.
            cancellationToken.ThrowIfCancellationRequested();
            Exception first = aggregate.Flatten().InnerExceptions[0];
            foreach (Exception inner in aggregate.Flatten().InnerExceptions)
                if (inner is OperationCanceledException) { first = inner; break; }
            ExceptionDispatchInfo.Capture(first).Throw();
        }
        return results;
    }
}
