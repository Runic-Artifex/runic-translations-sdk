using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace WebUIToolkit.TextResources;

/// <summary>Creates a fully composed snapshot for one resolved declared locale.</summary>
public interface ITextResourceSnapshotFactory
{
    /// <summary>Creates and validates one immutable snapshot.</summary>
    ValueTask<ITextResourceSnapshot> CreateSnapshotAsync(
        CompiledTextResourceCatalog catalog,
        string canonicalLocale,
        ITextValueFormatter valueFormatter,
        CancellationToken cancellationToken);
}

/// <summary>
/// Resolves requested locales and coalesces concurrent snapshot creation per canonical locale.
/// </summary>
public sealed class CompiledTextResourceProvider : ITextResourceProvider
{
    private readonly CompiledTextResourceCatalog _catalog;
    private readonly ITextValueFormatter _valueFormatter;
    private readonly ITextResourceSnapshotFactory _snapshotFactory;
    private readonly ConcurrentDictionary<string, LoadEntry> _snapshots =
        new(StringComparer.Ordinal);

    /// <summary>Creates a provider over immutable generated catalog data.</summary>
    public CompiledTextResourceProvider(
        CompiledTextResourceCatalog catalog,
        ITextValueFormatter? valueFormatter = null,
        ITextResourceSnapshotFactory? snapshotFactory = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _valueFormatter = valueFormatter ?? DefaultTextValueFormatter.Shared;
        _snapshotFactory = snapshotFactory ?? DefaultSnapshotFactory.Instance;
    }

    /// <inheritdoc />
    public ValueTask<ITextResourceSnapshot> GetSnapshotAsync(
        string requestedLocale,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string canonicalLocale = _catalog.ResolveRequestedLocale(requestedLocale);

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
                Task<ITextResourceSnapshot> task = entry.Task;
                return new ValueTask<ITextResourceSnapshot>(WaitForCallerAsync(entry, task, cancellationToken));
            }

            RemoveIfCurrent(canonicalLocale, entry);
        }
    }

    private async Task<ITextResourceSnapshot> CreateSnapshotAsync(
        LoadEntry entry,
        string canonicalLocale,
        CancellationToken cancellationToken)
    {
        try
        {
            ITextResourceSnapshot snapshot = await _snapshotFactory.CreateSnapshotAsync(
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

    private static async Task<ITextResourceSnapshot> WaitForCallerAsync(
        LoadEntry entry,
        Task<ITextResourceSnapshot> task,
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

    private sealed class LoadEntry : IDisposable
    {
        private readonly Lazy<Task<ITextResourceSnapshot>> _task;
        private readonly CancellationTokenSource _loadCancellation = new();
        private readonly CompiledTextResourceProvider _owner;
        private readonly string _canonicalLocale;
        private readonly object _gate = new();
        private bool _abandoned;
        private int _waiters;

        internal LoadEntry(
            CompiledTextResourceProvider owner,
            string canonicalLocale)
        {
            _owner = owner;
            _canonicalLocale = canonicalLocale;
            _task = new Lazy<Task<ITextResourceSnapshot>>(
                () => ObserveCompletion(owner.CreateSnapshotAsync(
                    this,
                    canonicalLocale,
                    _loadCancellation.Token)),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        internal Task<ITextResourceSnapshot> Task => _task.Value;

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

        internal void ReleaseWaiter(Task<ITextResourceSnapshot> task)
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

        private Task<ITextResourceSnapshot> ObserveCompletion(Task<ITextResourceSnapshot> task)
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

    private sealed class DefaultSnapshotFactory : ITextResourceSnapshotFactory
    {
        internal static DefaultSnapshotFactory Instance { get; } = new();

        public ValueTask<ITextResourceSnapshot> CreateSnapshotAsync(
            CompiledTextResourceCatalog catalog,
            string canonicalLocale,
            ITextValueFormatter valueFormatter,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ITextResourceSnapshot snapshot = new CompiledTextResourceSnapshot(
                catalog,
                canonicalLocale,
                valueFormatter);
            return ValueTask.FromResult(snapshot);
        }
    }
}
