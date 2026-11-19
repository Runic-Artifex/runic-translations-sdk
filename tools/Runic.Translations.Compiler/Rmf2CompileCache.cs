using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

namespace Runic.Translations.Compiler;

/// <summary>
/// Everything a compile reads besides the message sources: runic.json and the options. Memos are valid only within one
/// link context (docs/plans/incremental-validation.md).
/// </summary>
internal sealed record Rmf2LinkContext(string ProjectPath, string ProjectHash, int MaximumDocumentBytes, int MaximumDepth,
    int MaximumKeysPerCatalog, int MaximumValueBytes, int MaximumPlaceholdersPerValue, int MaximumLocalesPerCatalog,
    bool RelaxedValidation, bool BaseLocaleOnly, bool PseudoLocales);

/// <summary>What one cached compile reused and what it computed again.</summary>
internal sealed record Rmf2CompileStatistics(int Files, int FilesRead, int Messages, int MessagesCompiled, int PairsChecked);

// Counts the memo misses of one cached compile.
internal sealed class Rmf2CompileCounters
{
    internal int MessagesCompiled;
    internal int PairsChecked;
}

/// <summary>
/// The reusable results of compiling one catalog again and again, as its sources change: a source unit per file (its
/// parse and lowering) and, per link context, the extracted messages with their memoized checks. A compile with the
/// cache has exactly the result of <see cref="TranslationCompiler.CompileProject"/> of the same sources and options.
/// </summary>
/// <remarks>
/// Thread-safe: compiles may overlap, and a cancelled compile leaves only completed memos. A unit is dropped when its path
/// leaves the source set or its bytes change; with it go its memos.
/// </remarks>
internal sealed class Rmf2CompileCache
{
    private const int MaximumContexts = 4;
    private readonly object _gate = new();
    private Dictionary<string, Rmf2SourceUnitV5> _units = new(StringComparer.Ordinal);
    private readonly List<Rmf2LinkContext> _contexts = new();

    /// <param name="options">The size limits the file units are read with; a compile with other limits reuses nothing.</param>
    internal Rmf2CompileCache(TranslationCompilerOptions? options = null) => Options = options ?? new TranslationCompilerOptions();

    internal TranslationCompilerOptions Options { get; }

    /// <summary>The number of file units held.</summary>
    internal int Count { get { lock (_gate) return _units.Count; } }

    /// <summary>
    /// The unit of one source, cached or new, for a host that reads a file before it compiles the catalog (the workspace's
    /// syntax), so the compile reuses the parse. Units of paths that the next compile lacks are dropped then.
    /// </summary>
    internal Rmf2SourceUnitV5 Unit(TranslationSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        lock (_gate)
            if (_units.TryGetValue(source.Path, out var cached) && cached.HasBytesOf(source)) return cached;
        var unit = Rmf2SourceUnitV5.Create(source, Options, cancellationToken);
        lock (_gate)
        {
            if (_units.TryGetValue(source.Path, out var cached) && cached.HasBytesOf(source)) return cached;
            // Copy on write: a compile may be reading the dictionary it installed.
            _units = new Dictionary<string, Rmf2SourceUnitV5>(_units, StringComparer.Ordinal) { [source.Path] = unit };
        }
        return unit;
    }

    /// <summary>Drops the unit of <paramref name="path"/>, as a host bounding its memory does.</summary>
    internal void Forget(string path)
    {
        lock (_gate)
            if (_units.ContainsKey(path))
            {
                var remaining = new Dictionary<string, Rmf2SourceUnitV5>(_units, StringComparer.Ordinal);
                remaining.Remove(path);
                _units = remaining;
            }
    }

    /// <summary>Compiles as <see cref="TranslationCompiler.CompileProject"/> does, reusing what the cache holds for unchanged sources.</summary>
    /// <param name="options">The options of this compile (such as a relaxed review compile); the cache's options when null.</param>
    /// <exception cref="OperationCanceledException">The token was canceled.</exception>
    internal Rmf2ProjectCompilation Compile(TranslationSource project, IEnumerable<TranslationSource> sources, out Rmf2CompileStatistics statistics,
        TranslationCompilerOptions? options = null, bool pseudoLocales = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(sources);
        options ??= Options;
        cancellationToken.ThrowIfCancellationRequested();
        TranslationSource[] materialized = TranslationCompiler.MaterializeSources(sources);
        if (!SameLimits(options, Options))
        {
            statistics = new(materialized.Length, materialized.Length, 0, 0, 0);
            return TranslationCompiler.CompileRmf2ProjectV5(project, materialized, options, pseudoLocales, cancellationToken);
        }

        // The unit of each source: the cached one while its path and bytes are the same, else a new one, read in parallel.
        var found = new Rmf2SourceUnitV5?[materialized.Length];
        lock (_gate)
            for (int index = 0; index < materialized.Length; index++)
                if (_units.TryGetValue(materialized[index].Path, out var unit) && unit.HasBytesOf(materialized[index])) found[index] = unit;
        int[] missing = Enumerable.Range(0, materialized.Length).Where(index => found[index] is null).ToArray();
        var created = CompilerParallel.Map(missing.Length, index => Rmf2SourceUnitV5.Create(materialized[missing[index]], Options, cancellationToken), cancellationToken);
        for (int index = 0; index < missing.Length; index++) found[missing[index]] = created[index];
        var lookup = new Dictionary<TranslationSource, Rmf2SourceUnitV5>(materialized.Length, ReferenceEqualityComparer.Instance);
        var current = new Dictionary<string, Rmf2SourceUnitV5>(materialized.Length, StringComparer.Ordinal);
        for (int index = 0; index < materialized.Length; index++)
        {
            lookup[materialized[index]] = found[index]!;
            current[materialized[index].Path] = found[index]!;
        }
        // The latest compile decides the source set: paths it lacks lose their units, and with them their memos.
        lock (_gate) _units = current;

        Rmf2LinkContext context = Context(new Rmf2LinkContext(project.Path, Convert.ToHexStringLower(SHA256.HashData(project.Bytes)),
            options.MaximumDocumentBytes, options.MaximumDepth, options.MaximumKeysPerCatalog, options.MaximumValueBytes,
            options.MaximumPlaceholdersPerValue, options.MaximumLocalesPerCatalog, options.RelaxedValidation, options.BaseLocaleOnly, pseudoLocales));
        var counters = new Rmf2CompileCounters();
        Rmf2ProjectCompilation result = TranslationCompiler.CompileRmf2ProjectV5Cached(project, materialized, lookup, options, pseudoLocales, context, counters, cancellationToken);
        statistics = new(materialized.Length, missing.Length,
            current.Values.Sum(unit => unit.Resource?.Nodes.Count(node => !node.IsGroup) ?? (unit.DirectMessage is null ? 0 : 1)),
            counters.MessagesCompiled, counters.PairsChecked);
        return result;
    }

    // One instance per distinct context, so memos compare contexts by reference; the oldest of more than four is forgotten.
    private Rmf2LinkContext Context(Rmf2LinkContext context)
    {
        lock (_gate)
        {
            int index = _contexts.IndexOf(context);
            if (index >= 0) return _contexts[index];
            if (_contexts.Count == MaximumContexts) _contexts.RemoveAt(0);
            _contexts.Add(context);
            return context;
        }
    }

    internal static bool SameLimits(TranslationCompilerOptions left, TranslationCompilerOptions right) =>
        ReferenceEquals(left, right) ||
        (left.MaximumDocumentBytes == right.MaximumDocumentBytes && left.MaximumDepth == right.MaximumDepth &&
         left.MaximumKeysPerCatalog == right.MaximumKeysPerCatalog && left.MaximumValueBytes == right.MaximumValueBytes &&
         left.MaximumPlaceholdersPerValue == right.MaximumPlaceholdersPerValue && left.MaximumLocalesPerCatalog == right.MaximumLocalesPerCatalog);
}
