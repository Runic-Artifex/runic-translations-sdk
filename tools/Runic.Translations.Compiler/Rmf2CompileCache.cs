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

/// <summary>
/// What a session keeps from one compile of a link context for the next: each locale's key order, the contract
/// exports and the last result. Each part is an immutable object replaced as a whole, so overlapping compiles are safe.
/// </summary>
internal sealed class Rmf2LinkState
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string[]> _orders = new(StringComparer.OrdinalIgnoreCase);
    internal string[]? KeyOrder(string locale) => _orders.TryGetValue(locale, out string[]? order) ? order : null;
    internal void SetKeyOrder(string locale, string[] order) => _orders[locale] = order;
    internal Rmf2CallerContractMemo? CallerContract;
    internal Rmf2MarkupExportMemo? MarkupExport;
    internal Rmf2ResultMemo? Last;
    internal Rmf2ReadableReferencesMemo? ReadableReferences;
}
internal sealed record Rmf2ReadableReferencesMemo(Rmf2MessageContractV5[] Contracts, List<(string Key, string Problem)> Problems);
internal sealed record Rmf2CallerContractMemo(string Catalog, Rmf2MessageContractV5[] Contracts, string[] Markup, string Json);
internal sealed record Rmf2MarkupExportMemo(Rmf2MessageContractV5[] Contracts, string[] Used, Rmf2LocaleV5[] Locales, string Json);
internal sealed record Rmf2ResultMemo(Rmf2SourceUnitV5[] Units, Rmf2ProjectCompilation Result);

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
    private readonly List<(Rmf2LinkContext Context, Rmf2LinkState State)> _contexts = new();

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

        var (context, state) = Context(new Rmf2LinkContext(project.Path, Convert.ToHexStringLower(SHA256.HashData(project.Bytes)),
            options.MaximumDocumentBytes, options.MaximumDepth, options.MaximumKeysPerCatalog, options.MaximumValueBytes,
            options.MaximumPlaceholdersPerValue, options.MaximumLocalesPerCatalog, options.RelaxedValidation, options.BaseLocaleOnly, pseudoLocales));
        // Nothing changed since the last compile of this context: its result is this one's.
        var units = Array.ConvertAll(found, unit => unit!);
        if (state.Last is { } last && last.Units.AsSpan().SequenceEqual(units))
        {
            statistics = new(materialized.Length, 0, last.Units.Sum(MessageCount), 0, 0);
            return last.Result;
        }
        var counters = new Rmf2CompileCounters();
        Rmf2ProjectCompilation result = TranslationCompiler.CompileRmf2ProjectV5Cached(project, materialized, lookup, options, pseudoLocales, context, state, counters, cancellationToken);
        state.Last = new(units, result);
        statistics = new(materialized.Length, missing.Length,
            current.Values.Sum(MessageCount),
            counters.MessagesCompiled, counters.PairsChecked);
        return result;
    }

    // One instance per distinct context, so memos compare contexts by reference, with its state; the oldest of more than
    // four is forgotten.
    private (Rmf2LinkContext Context, Rmf2LinkState State) Context(Rmf2LinkContext context)
    {
        lock (_gate)
        {
            int index = _contexts.FindIndex(item => item.Context.Equals(context));
            if (index >= 0) return _contexts[index];
            if (_contexts.Count == MaximumContexts) _contexts.RemoveAt(0);
            _contexts.Add((context, new Rmf2LinkState()));
            return _contexts[^1];
        }
    }

    private static int MessageCount(Rmf2SourceUnitV5 unit) => unit.MessageCount;

    internal static bool SameLimits(TranslationCompilerOptions left, TranslationCompilerOptions right) =>
        ReferenceEquals(left, right) ||
        (left.MaximumDocumentBytes == right.MaximumDocumentBytes && left.MaximumDepth == right.MaximumDepth &&
         left.MaximumKeysPerCatalog == right.MaximumKeysPerCatalog && left.MaximumValueBytes == right.MaximumValueBytes &&
         left.MaximumPlaceholdersPerValue == right.MaximumPlaceholdersPerValue && left.MaximumLocalesPerCatalog == right.MaximumLocalesPerCatalog);
}
