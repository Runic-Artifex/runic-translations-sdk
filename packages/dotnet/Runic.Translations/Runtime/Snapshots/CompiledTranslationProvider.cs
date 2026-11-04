using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Runic.Translations;

/// <summary>Creates a fully composed snapshot for one resolved declared locale.</summary>
/// <remarks>
/// Applications may implement this interface. A minor release adds members to it only with a default implementation, which
/// may throw <see cref="NotSupportedException"/> or report that the feature is not supported.
/// </remarks>
public interface ITranslationSnapshotFactory
{
    /// <summary>Creates and validates one immutable snapshot.</summary>
    ValueTask<ITranslationSnapshot> CreateSnapshotAsync(
        CompiledTranslationCatalog catalog,
        string canonicalLocale,
        ITextValueFormatter valueFormatter,
        CancellationToken cancellationToken);
}

/// <summary>
/// Drops memoized snapshots so the next request recomposes from sources. Implemented by providers
/// that cache successful compositions; <see cref="TranslationManager"/> uses it to refresh the
/// active locale without weakening caching for ordinary transitions.
/// </summary>
internal interface ITranslationSnapshotCacheInvalidator
{
    /// <summary>Drops any memoized snapshot held for the canonical locale.</summary>
    void InvalidateSnapshot(string canonicalLocale);
}

/// <summary>
/// Resolves requested locales and coalesces concurrent snapshot creation per canonical locale.
/// </summary>
public sealed class CompiledTranslationProvider : ITranslationProvider, ITranslationSnapshotCacheInvalidator
{
    private readonly CompiledTranslationCatalog _catalog;
    private readonly ITextValueFormatter _valueFormatter;
    private readonly ITranslationSnapshotFactory _snapshotFactory;
    private readonly ConcurrentDictionary<string, LoadEntry> _snapshots =
        new(StringComparer.Ordinal);

    /// <summary>Creates a provider over immutable generated catalog data.</summary>
    public CompiledTranslationProvider(
        CompiledTranslationCatalog catalog,
        ITextValueFormatter? valueFormatter = null,
        ITranslationSnapshotFactory? snapshotFactory = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _valueFormatter = valueFormatter ?? DefaultTextValueFormatter.Shared;
        _snapshotFactory = snapshotFactory ?? DefaultSnapshotFactory.Instance;
    }

    /// <inheritdoc />
    public ValueTask<ITranslationSnapshot> GetSnapshotAsync(
        string requestedLocale,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string canonicalLocale = _catalog.ResolveRequestedLocale(requestedLocale);
        // A regional request such as de-CH for a de snapshot formats numbers and dates in de-CH; the cached snapshot is shared.
        string? regional = requestedLocale.Length != 0 && LocaleTag.TryCanonicalize(requestedLocale, out string canonicalRequest) &&
            !string.Equals(canonicalRequest, canonicalLocale, StringComparison.Ordinal) ? canonicalRequest : null;

        while (true)
        {
            var candidate = new LoadEntry(this, canonicalLocale);
            LoadEntry entry = _snapshots.GetOrAdd(canonicalLocale, candidate);
            if (!ReferenceEquals(candidate, entry))
            {
                candidate.Dispose();
            }

            if (entry.TryAddWaiter())
            {
                Task<ITranslationSnapshot> task = entry.Task;
                Task<ITranslationSnapshot> waiting = WaitForCallerAsync(entry, task, cancellationToken);
                return new ValueTask<ITranslationSnapshot>(regional is null ? waiting : WithRequestedLocaleAsync(waiting, regional));
            }

            RemoveIfCurrent(canonicalLocale, entry);
        }
    }

    private async Task<ITranslationSnapshot> CreateSnapshotAsync(
        LoadEntry entry,
        string canonicalLocale,
        CancellationToken cancellationToken)
    {
        try
        {
            ITranslationSnapshot snapshot = await _snapshotFactory.CreateSnapshotAsync(
                _catalog,
                canonicalLocale,
                _valueFormatter,
                cancellationToken).ConfigureAwait(false);

            if (snapshot is null)
            {
                throw new InvalidOperationException("The snapshot factory returned null.");
            }

            if (!string.Equals(snapshot.Catalog, _catalog.Catalog, StringComparison.Ordinal) ||
                !string.Equals(snapshot.Locale, canonicalLocale, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The snapshot factory returned a snapshot for a different catalog or locale.");
            }

            return snapshot;
        }
        catch
        {
            RemoveIfCurrent(canonicalLocale, entry);
            throw;
        }
    }

    // The cached snapshot serves every request that resolves to its locale; a regional request gets its view.
    private static async Task<ITranslationSnapshot> WithRequestedLocaleAsync(Task<ITranslationSnapshot> waiting, string requestedLocale) =>
        await waiting.ConfigureAwait(false) is CompiledTranslationSnapshot compiled ? compiled.ForRequestedLocale(requestedLocale) : await waiting.ConfigureAwait(false);

    private static async Task<ITranslationSnapshot> WaitForCallerAsync(
        LoadEntry entry,
        Task<ITranslationSnapshot> task,
        CancellationToken cancellationToken)
    {
        try
        {
            return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            entry.ReleaseWaiter(task);
        }
    }

    private void RemoveIfCurrent(string canonicalLocale, LoadEntry entry)
    {
        if (_snapshots.TryGetValue(canonicalLocale, out LoadEntry? current) && ReferenceEquals(current, entry))
        {
            _snapshots.TryRemove(canonicalLocale, out _);
        }
    }

    /// <inheritdoc />
    void ITranslationSnapshotCacheInvalidator.InvalidateSnapshot(string canonicalLocale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalLocale);
        _snapshots.TryRemove(canonicalLocale, out _);
    }

    private sealed class LoadEntry : IDisposable
    {
        private readonly Lazy<Task<ITranslationSnapshot>> _task;
        private readonly CancellationTokenSource _loadCancellation = new();
        private readonly CompiledTranslationProvider _owner;
        private readonly string _canonicalLocale;
        private readonly object _gate = new();
        private bool _abandoned;
        private int _waiters;

        internal LoadEntry(
            CompiledTranslationProvider owner,
            string canonicalLocale)
        {
            _owner = owner;
            _canonicalLocale = canonicalLocale;
            _task = new Lazy<Task<ITranslationSnapshot>>(
                () => ObserveCompletion(owner.CreateSnapshotAsync(
                    this,
                    canonicalLocale,
                    _loadCancellation.Token)),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        internal Task<ITranslationSnapshot> Task => _task.Value;

        internal bool TryAddWaiter()
        {
            lock (_gate)
            {
                if (_abandoned)
                {
                    return false;
                }

                _waiters++;
                return true;
            }
        }

        internal void ReleaseWaiter(Task<ITranslationSnapshot> task)
        {
            bool abandon = false;
            lock (_gate)
            {
                _waiters--;
                if (_waiters == 0 && !task.IsCompleted)
                {
                    _abandoned = true;
                    abandon = true;
                }
            }

            if (abandon)
            {
                _owner.RemoveIfCurrent(_canonicalLocale, this);
                _loadCancellation.Cancel();
            }
        }

        public void Dispose() => _loadCancellation.Dispose();

        private Task<ITranslationSnapshot> ObserveCompletion(Task<ITranslationSnapshot> task)
        {
            _ = task.ContinueWith(
                static (_, state) => ((LoadEntry)state!).Dispose(),
                this,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return task;
        }
    }

    private sealed class DefaultSnapshotFactory : ITranslationSnapshotFactory
    {
        internal static DefaultSnapshotFactory Instance { get; } = new();

        public ValueTask<ITranslationSnapshot> CreateSnapshotAsync(
            CompiledTranslationCatalog catalog,
            string canonicalLocale,
            ITextValueFormatter valueFormatter,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ITranslationSnapshot snapshot = new CompiledTranslationSnapshot(
                catalog,
                canonicalLocale,
                valueFormatter);
            return ValueTask.FromResult(snapshot);
        }
    }
}
