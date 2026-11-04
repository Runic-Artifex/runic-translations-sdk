using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace Runic.Translations;

/// <summary>
/// Owns one requested locale for several catalogs and publishes their snapshots together.
/// </summary>
/// <remarks>
/// <para>
/// Create it with <see cref="CreateBuilder"/>. Every catalog keeps its own <see cref="ITranslationManager"/>
/// (<see cref="GetManager"/>), so existing adapters keep working; its <see cref="ITranslationManager.Current"/> reads
/// the context's state, which <see cref="SetLocaleAsync"/> replaces in one step. A reader never sees one catalog in the new
/// locale and another in the old one.
/// </para>
/// <para>
/// A manager's <see cref="ITranslationManager.SetLocaleAsync"/> switches the whole context, and its
/// <see cref="ITranslationManager.RefreshAsync"/> recomposes only its own catalog. Managers raise their events as a
/// standalone manager does: <see cref="ITranslationManager.LocaleChanged"/> when their catalog's locale changed, then
/// <see cref="ITranslationManager.SnapshotPublished"/>. After every manager, the context raises <see cref="LocaleChanged"/>.
/// Handlers run on the thread that completed the switch, never while the context holds its transition lock; a handler's
/// exception does not affect the caller.
/// </para>
/// </remarks>
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "The transition semaphore never exposes its wait handle, and disposing it would race active callers.")]
public sealed class TranslationContext : ITranslationContext
{
    private readonly Entry[] _entries;
    private readonly Dictionary<string, int> _indexById;
    private readonly ContextManager[] _managers;
    private readonly SemaphoreSlim _transitionGate = new(1, 1);
    private State _state;

    private TranslationContext(Entry[] entries, State initial)
    {
        _entries = entries;
        _indexById = new Dictionary<string, int>(entries.Length, StringComparer.Ordinal);
        _managers = new ContextManager[entries.Length];
        for (int index = 0; index < entries.Length; index++)
        {
            _indexById.Add(entries[index].Registration.CatalogId, index);
            _managers[index] = new ContextManager(this, index);
        }

        _state = initial;
    }

    /// <summary>Starts a context: register catalogs and overlays, then build it.</summary>
    public static TranslationContextBuilder CreateBuilder() => new();

    /// <inheritdoc />
    public string RequestedLocale => Volatile.Read(ref _state).RequestedLocale;

    /// <inheritdoc />
    public IReadOnlyList<TranslationCatalogStatus> Catalogs => Volatile.Read(ref _state).Statuses;

    /// <inheritdoc />
    public event EventHandler<TranslationContextLocaleChangedEventArgs>? LocaleChanged;

    /// <inheritdoc />
    public ITranslationManager GetManager(string catalogId)
    {
        ArgumentNullException.ThrowIfNull(catalogId);
        return TryGetManager(catalogId, out ITranslationManager? manager)
            ? manager
            : throw new KeyNotFoundException($"Catalog '{catalogId}' is not registered with this translation context.");
    }

    /// <inheritdoc />
    public bool TryGetManager(string catalogId, [NotNullWhen(true)] out ITranslationManager? manager)
    {
        ArgumentNullException.ThrowIfNull(catalogId);
        if (_indexById.TryGetValue(catalogId, out int index))
        {
            manager = _managers[index];
            return true;
        }

        manager = null;
        return false;
    }

    /// <inheritdoc />
    public async ValueTask SetLocaleAsync(string locale, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(locale);
        cancellationToken.ThrowIfCancellationRequested();
        string requested = LocaleRequestResolver.Canonicalize(locale, nameof(locale));
        if (string.Equals(RequestedLocale, requested, StringComparison.Ordinal)) return;

        State previous, next;
        await _transitionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            previous = _state;
            if (string.Equals(previous.RequestedLocale, requested, StringComparison.Ordinal)) return;
            ITranslationSnapshot[] snapshots = await PrepareAsync(_entries, requested, null, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            next = CreateState(_entries, requested, snapshots);
            Volatile.Write(ref _state, next);
        }
        finally
        {
            _transitionGate.Release();
        }

        for (int index = 0; index < _managers.Length; index++)
            _managers[index].NotifySwitch(previous.Snapshots[index], next.Snapshots[index]);
        Notify(LocaleChanged, new TranslationContextLocaleChangedEventArgs(previous.RequestedLocale, next.RequestedLocale, next.Statuses), this);
    }

    /// <inheritdoc />
    public ValueTask RefreshAsync(CancellationToken cancellationToken = default) => RefreshAsync(null, cancellationToken);

    /// <inheritdoc />
    public TranslationReferenceResolution Resolve(TranslationReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (!_indexById.TryGetValue(reference.Catalog, out int index)) return TranslationReferenceResolver.CatalogMismatch(reference);
        return TranslationReferenceResolver.Resolve(Volatile.Read(ref _state).Snapshots[index], _entries[index].Registration.ContractFingerprint, reference);
    }

    private async ValueTask RefreshAsync(int? only, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        State previous, next;
        await _transitionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            previous = _state;
            for (int index = 0; index < _entries.Length; index++)
            {
                if ((only is null || only == index) && _entries[index].Provider is ITranslationSnapshotCacheInvalidator invalidator)
                    invalidator.InvalidateSnapshot(previous.Snapshots[index].Locale);
            }

            ITranslationSnapshot[] snapshots = await PrepareAsync(_entries, previous.RequestedLocale, only is null ? null : previous.Snapshots,
                cancellationToken, only).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            for (int index = 0; index < snapshots.Length; index++)
            {
                if (!string.Equals(snapshots[index].Locale, previous.Snapshots[index].Locale, StringComparison.Ordinal))
                    throw new InvalidOperationException("The translation provider returned a snapshot for a different locale during refresh.");
            }

            next = CreateState(_entries, previous.RequestedLocale, snapshots);
            Volatile.Write(ref _state, next);
        }
        finally
        {
            _transitionGate.Release();
        }

        for (int index = 0; index < _managers.Length; index++)
            if (only is null || only == index) _managers[index].NotifyRefresh(next.Snapshots[index]);
    }

    internal static async ValueTask<TranslationContext> CreateAsync(Entry[] entries, string requestedLocale, CancellationToken cancellationToken)
    {
        ITranslationSnapshot[] snapshots = await PrepareAsync(entries, requestedLocale, null, cancellationToken).ConfigureAwait(false);
        return new TranslationContext(entries, CreateState(entries, requestedLocale, snapshots));
    }

    // Requests every catalog's snapshot at once and waits for all of them, so a failure never leaves work running
    // that a later switch could race. The first failure in registration order is rethrown.
    private static async ValueTask<ITranslationSnapshot[]> PrepareAsync(Entry[] entries, string requestedLocale,
        ITranslationSnapshot[]? keep, CancellationToken cancellationToken, int? only = null)
    {
        var tasks = new Task<ITranslationSnapshot>[entries.Length];
        for (int index = 0; index < entries.Length; index++)
        {
            tasks[index] = keep is not null && only != index
                ? Task.FromResult(keep[index])
                : Request(entries[index].Provider, requestedLocale, cancellationToken);
        }

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (Exception) when (Array.Exists(tasks, static task => !task.IsCompletedSuccessfully))
        {
            // Rethrown below, in registration order.
        }

        var snapshots = new ITranslationSnapshot[entries.Length];
        for (int index = 0; index < entries.Length; index++)
        {
            ITranslationSnapshot snapshot = await tasks[index].ConfigureAwait(false);
            if (snapshot is null) throw new InvalidOperationException("The translation provider returned a null snapshot.");
            if (!string.Equals(snapshot.Catalog, entries[index].Registration.CatalogId, StringComparison.Ordinal))
                throw new InvalidOperationException($"The provider of catalog '{entries[index].Registration.CatalogId}' returned a snapshot for catalog '{snapshot.Catalog}'.");
            snapshots[index] = snapshot;
        }

        return snapshots;

        static Task<ITranslationSnapshot> Request(ITranslationProvider provider, string locale, CancellationToken cancellationToken)
        {
            try
            {
                return provider.GetSnapshotAsync(locale, cancellationToken).AsTask();
            }
            catch (Exception exception)
            {
                return Task.FromException<ITranslationSnapshot>(exception);
            }
        }
    }

    private static State CreateState(Entry[] entries, string requestedLocale, ITranslationSnapshot[] snapshots)
    {
        var statuses = new TranslationCatalogStatus[entries.Length];
        for (int index = 0; index < entries.Length; index++)
        {
            TranslationOverlayDiagnostic[] diagnostics = snapshots[index] is CompiledTranslationSnapshot compiled ? compiled.OverlayDiagnostics : [];
            statuses[index] = new TranslationCatalogStatus(snapshots[index], entries[index].SupportedLocales, diagnostics);
        }

        return new State(requestedLocale, snapshots, Array.AsReadOnly(statuses));
    }

    private static void Notify<T>(EventHandler<T>? handlers, T arguments, object? sender = null)
    {
        if (handlers is null) return;
        foreach (Delegate handler in handlers.GetInvocationList())
        {
            try
            {
                ((EventHandler<T>)handler)(sender, arguments);
            }
            catch (Exception)
            {
                // Publication is complete; subscriber failures cannot roll it back.
            }
        }
    }

    internal sealed record Entry(TranslationCatalogRegistration Registration, ITranslationProvider Provider, string[] SupportedLocales);

    private sealed record State(string RequestedLocale, ITranslationSnapshot[] Snapshots, ReadOnlyCollection<TranslationCatalogStatus> Statuses);

    // A catalog's manager: its snapshot is the catalog's entry in the context's state.
    private sealed class ContextManager : ITranslationManager
    {
        private readonly TranslationContext _context;
        private readonly int _index;

        internal ContextManager(TranslationContext context, int index)
        {
            _context = context;
            _index = index;
        }

        public string CurrentLocale => Current.Locale;

        public ITranslationSnapshot Current => Volatile.Read(ref _context._state).Snapshots[_index];

        public event EventHandler<TranslationLocaleChangedEventArgs>? LocaleChanged;

        public event EventHandler<TranslationSnapshotPublishedEventArgs>? SnapshotPublished;

        public ValueTask SetLocaleAsync(string locale, CancellationToken cancellationToken = default) =>
            _context.SetLocaleAsync(locale, cancellationToken);

        public ValueTask RefreshAsync(CancellationToken cancellationToken = default) => _context.RefreshAsync(_index, cancellationToken);

        internal void NotifySwitch(ITranslationSnapshot previous, ITranslationSnapshot next)
        {
            // As a standalone manager: a request that resolves to the same locale changes no text and raises nothing.
            if (string.Equals(previous.Locale, next.Locale, StringComparison.Ordinal)) return;
            Notify(LocaleChanged, new TranslationLocaleChangedEventArgs(previous, next), this);
            Notify(SnapshotPublished, new TranslationSnapshotPublishedEventArgs(next, TranslationSnapshotPublishReason.LocaleChanged), this);
        }

        internal void NotifyRefresh(ITranslationSnapshot next) =>
            Notify(SnapshotPublished, new TranslationSnapshotPublishedEventArgs(next, TranslationSnapshotPublishReason.Refresh), this);
    }
}
