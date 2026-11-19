using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Runic.Translations.Compiler;

namespace Runic.Translations.Authoring;

/// <summary>
/// One validation of a catalog: the compilation and its diagnostics, immutable and safe to read from any thread.
/// </summary>
internal sealed class TranslationValidationSnapshot
{
    private readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<TranslationDiagnostic>>> _byPath;

    internal TranslationValidationSnapshot(long revision, Rmf2ProjectCompilation compilation, IReadOnlyList<string> changedPaths, Rmf2CompileStatistics statistics)
    {
        Revision = revision;
        Compilation = compilation;
        ChangedPaths = changedPaths;
        Statistics = statistics;
        _byPath = new(() => compilation.Diagnostics.GroupBy(diagnostic => diagnostic.Location.Path, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<TranslationDiagnostic>)group.ToArray(), StringComparer.Ordinal), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>The number of this validation in its session, from 1.</summary>
    public long Revision { get; }

    /// <summary>The compilation: equal to <see cref="TranslationCompiler.CompileProject"/> of the same sources, diagnostics in the same order.</summary>
    public Rmf2ProjectCompilation Compilation { get; }

    public IReadOnlyList<TranslationDiagnostic> Diagnostics => Compilation.Diagnostics;

    /// <summary>The diagnostics by the path of the file they are located in.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<TranslationDiagnostic>> DiagnosticsByPath => _byPath.Value;

    /// <summary>The sources added, removed or changed since the session's previous validation (every source in the first), in ordinal order.</summary>
    public IReadOnlyList<string> ChangedPaths { get; }

    /// <summary>What this validation read and compiled again, and what it reused.</summary>
    public Rmf2CompileStatistics Statistics { get; }
}

/// <summary>
/// Validates one catalog again and again as its sources change, rechecking only what a change affects: the changed
/// files, the messages they define and the cross-file checks of those messages' keys (docs/plans/incremental-validation.md).
/// Every result equals a full compile of the same sources.
/// </summary>
/// <remarks>
/// Thread-safe. A host that validates on a worker and cancels outdated validations passes their token: a cancelled
/// validation throws <see cref="OperationCanceledException"/> and leaves the session valid for the next.
/// </remarks>
internal sealed class TranslationValidationSession
{
    private readonly object _gate = new();
    private Dictionary<string, TranslationSource> _previous = new(StringComparer.Ordinal);
    private long _revision;

    public TranslationValidationSession(TranslationCompilerOptions? options = null) => Cache = new Rmf2CompileCache(options);

    internal Rmf2CompileCache Cache { get; }

    /// <summary>The options the session validates with by default; their size limits decide what it can reuse.</summary>
    public TranslationCompilerOptions Options => Cache.Options;

    /// <summary>Validates <paramref name="project"/> with <paramref name="sources"/>, reusing the work of earlier validations.</summary>
    /// <exception cref="OperationCanceledException">The token was canceled.</exception>
    public TranslationValidationSnapshot Validate(TranslationSource project, IEnumerable<TranslationSource> sources, CancellationToken cancellationToken = default) =>
        Validate(project, sources, null, false, cancellationToken);

    /// <param name="options">Options of this validation, such as the relaxed review compile of <c>TranslationReviewFile.ForReview</c>; the session's when null.</param>
    /// <param name="pseudoLocales">Whether to generate the pseudo-locales runic.json declares.</param>
    public TranslationValidationSnapshot Validate(TranslationSource project, IEnumerable<TranslationSource> sources, TranslationCompilerOptions? options,
        bool pseudoLocales, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(sources);
        TranslationSource[] all = sources as TranslationSource[] ?? sources.ToArray();
        Rmf2ProjectCompilation compilation = Cache.Compile(project, all, out Rmf2CompileStatistics statistics, options, pseudoLocales, cancellationToken);
        lock (_gate)
        {
            var current = new Dictionary<string, TranslationSource>(all.Length, StringComparer.Ordinal);
            foreach (TranslationSource source in all) current[source.Path] = source;
            var changed = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var pair in current)
                if (!_previous.TryGetValue(pair.Key, out var before) || !(ReferenceEquals(before, pair.Value) || before.Bytes.AsSpan().SequenceEqual(pair.Value.Bytes))) changed.Add(pair.Key);
            foreach (string path in _previous.Keys)
                if (!current.ContainsKey(path)) changed.Add(path);
            _previous = current;
            return new TranslationValidationSnapshot(++_revision, compilation, changed.ToArray(), statistics);
        }
    }
}
