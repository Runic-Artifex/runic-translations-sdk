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
/// The latest <see cref="SetLocaleAsync"/> call wins. A call supersedes every earlier call that has not published yet:
/// the earlier call's preparation is canceled, its result is discarded and it completes without error. A slow switch,
/// such as one that downloads an overlay pack, therefore never delays a later one, and a request for the locale already
/// published cancels a switch that is still in flight.
/// </para>
/// <para>
/// A manager's <see cref="ITranslationManager.SetLocaleAsync"/> switches the whole context, and its
/// <see cref="ITranslationManager.RefreshAsync"/> recomposes only its own catalog. Managers raise their events as a
/// standalone manager does: <see cref="ITranslationManager.LocaleChanged"/> when their catalog's locale or the requested tag
/// changed (<c>de</c> to <c>de-CH</c> keeps the text but changes how numbers and dates format), then
/// <see cref="ITranslationManager.SnapshotPublished"/>. After every manager, the context raises <see cref="LocaleChanged"/>.
/// Events are raised in publication order and never while the context holds a lock. When publications overlap, only the
/// latest state is raised, and its event data starts from the state subscribers last saw, so a subscriber never ends on a
/// superseded locale. Handlers run on the thread that completed a switch; when another thread is still raising events
/// for an earlier switch, that thread raises the later ones too. A handler's exception does not affect the caller.
/// </para>
/// </remarks>
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "The refresh semaphore never exposes its wait handle, and disposing it would race active callers.")]
public sealed class TranslationContext : ITranslationContext
{
    private readonly TranslationContextEntry[] _entries;
    private readonly Dictionary<string, int> _indexById;
    private readonly ContextManager[] _managers;
    private readonly ReadOnlyCollection<string> _supportedLocales;

    // Guards _state writes, _switchSequence and _pendingSwitch. Never held while user code runs.
    private readonly Lock _sync = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private State _state;
    private long _switchSequence;
    private SwitchRequest? _pendingSwitch;

    // Event delivery: one thread at a time raises the events of the latest published state.
    private int _notifying;
    private long _notifiedVersion;
    private string _notifiedRequestedLocale;

    private TranslationContext(TranslationContextEntry[] entries, State initial)
    {
        _entries = entries;
        _indexById = new Dictionary<string, int>(entries.Length, StringComparer.Ordinal);
        _managers = new ContextManager[entries.Length];
        for (int index = 0; index < entries.Length; index++)
        {
            _indexById.Add(entries[index].Registration.CatalogId, index);
            _managers[index] = new ContextManager(this, index, initial.Snapshots[index]);
        }

        _supportedLocales = Array.AsReadOnly(IntersectSupportedLocales(entries));
        _state = initial;
        TranslationHotReload.Track(this);
        _notifiedVersion = initial.Version;
        _notifiedRequestedLocale = initial.RequestedLocale;
    }

    /// <summary>Starts a context: register catalogs and overlays, then build it.</summary>
    public static TranslationContextBuilder CreateBuilder() => new();

    /// <inheritdoc />
    public string RequestedLocale => Volatile.Read(ref _state).RequestedLocale;

    /// <inheritdoc />
    public IReadOnlyList<TranslationCatalogStatus> Catalogs => Volatile.Read(ref _state).Statuses;

    /// <inheritdoc />
    public IReadOnlyList<string> SupportedLocales => _supportedLocales;

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
    /// <remarks>
    /// The latest call wins: an earlier call that has not published yet is superseded, its preparation canceled and its
    /// result discarded, and it completes without error. A call for the locale that is already published returns at once
    /// (after superseding any switch in flight).
    /// </remarks>
    public async ValueTask SetLocaleAsync(string locale, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(locale);
        cancellationToken.ThrowIfCancellationRequested();
        string requested = LocaleRequestResolver.Canonicalize(locale, nameof(locale));

        SwitchRequest? superseded;
        SwitchRequest? request = null;
        lock (_sync)
        {
            superseded = _pendingSwitch;
            _pendingSwitch = null;
            long sequence = ++_switchSequence;
            // Decided against the published state only after every earlier request is superseded, so an earlier switch
            // that is still in flight cannot publish over this request.
            if (!string.Equals(_state.RequestedLocale, requested, StringComparison.Ordinal))
                _pendingSwitch = request = new SwitchRequest(sequence, cancellationToken);
        }

        // Canceling runs the token's callbacks, which may be user code, so it happens outside the lock.
        superseded?.Supersede();
        if (request is null) return;

        try
        {
            ITranslationSnapshot[] snapshots;
            try
            {
                snapshots = await PrepareAsync(_entries, requested, null, request.Token).ConfigureAwait(false);
            }
            catch (Exception) when (request.IsSuperseded && !cancellationToken.IsCancellationRequested)
            {
                // A later request replaced this one; whatever the preparation did is discarded.
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            State next;
            lock (_sync)
            {
                if (request.Sequence != _switchSequence) return;
                _pendingSwitch = null;
                next = Publish(requested, snapshots, _state.Generations);
            }

            TranslationsEventSource.ReportResolutions(next.Statuses);
            NotifyPublished();
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_pendingSwitch, request)) _pendingSwitch = null;
            }

            request.Dispose();
        }
    }

    /// <inheritdoc />
    public ValueTask RefreshAsync(CancellationToken cancellationToken = default) => RefreshAsync(null, cancellationToken);

    /// <summary>Whether the context has a catalog with one of these IDs (Hot Reload).</summary>
    internal bool ContainsAnyCatalog(IReadOnlyCollection<string> catalogIds)
    {
        foreach (string id in catalogIds)
        {
            if (_indexById.ContainsKey(id)) return true;
        }

        return false;
    }

    /// <inheritdoc />
    public TranslationReferenceResolution Resolve(TranslationReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (!_indexById.TryGetValue(reference.Catalog, out int index)) return TranslationReferenceResolver.CatalogMismatch(reference);
        return TranslationReferenceResolver.Resolve(Volatile.Read(ref _state).Snapshots[index], _entries[index].Registration.ContractFingerprint, reference);
    }

    // Refreshes run one at a time. A locale switch that publishes while a refresh prepares supersedes the refresh: the
    // switch composed its snapshots after the refresh invalidated the caches, so the refresh result is discarded.
    private async ValueTask RefreshAsync(int? only, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            State start = Volatile.Read(ref _state);
            for (int index = 0; index < _entries.Length; index++)
            {
                if ((only is null || only == index) && _entries[index].Provider is ITranslationSnapshotCacheInvalidator invalidator)
                    invalidator.InvalidateSnapshot(start.Snapshots[index].Locale);
            }

            ITranslationSnapshot[] snapshots = await PrepareAsync(_entries, start.RequestedLocale, only is null ? null : start.Snapshots,
                cancellationToken, only).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            for (int index = 0; index < snapshots.Length; index++)
            {
                if (!string.Equals(snapshots[index].Locale, start.Snapshots[index].Locale, StringComparison.Ordinal))
                    throw new InvalidOperationException("The translation provider returned a snapshot for a different locale during refresh.");
            }

            lock (_sync)
            {
                if (!ReferenceEquals(_state, start)) return;
                long[] generations = (long[])start.Generations.Clone();
                for (int index = 0; index < generations.Length; index++)
                    if (only is null || only == index) generations[index]++;
                Publish(start.RequestedLocale, snapshots, generations);
            }
        }
        finally
        {
            _refreshGate.Release();
        }

        NotifyPublished();
    }

    internal static async ValueTask<TranslationContext> CreateAsync(TranslationContextEntry[] entries, string requestedLocale, CancellationToken cancellationToken)
    {
        ITranslationSnapshot[] snapshots = await PrepareAsync(entries, requestedLocale, null, cancellationToken).ConfigureAwait(false);
        State initial = CreateState(entries, 0, requestedLocale, snapshots, new long[entries.Length]);
        TranslationsEventSource.ReportResolutions(initial.Statuses);
        return new TranslationContext(entries, initial);
    }

    // Called under _sync.
    private State Publish(string requestedLocale, ITranslationSnapshot[] snapshots, long[] generations)
    {
        State next = CreateState(_entries, _state.Version + 1, requestedLocale, snapshots, generations);
        Volatile.Write(ref _state, next);
        return next;
    }

    // Requests every catalog's snapshot at once and waits for all of them, so a failure never leaves work running
    // that a later switch could race. The first failure in registration order is rethrown.
    private static async ValueTask<ITranslationSnapshot[]> PrepareAsync(TranslationContextEntry[] entries, string requestedLocale,
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

    private static State CreateState(TranslationContextEntry[] entries, long version, string requestedLocale, ITranslationSnapshot[] snapshots,
        long[] generations)
    {
        var statuses = new TranslationCatalogStatus[entries.Length];
        for (int index = 0; index < entries.Length; index++)
        {
            TranslationOverlayDiagnostic[] diagnostics = snapshots[index] is CompiledTranslationSnapshot compiled ? compiled.OverlayDiagnostics : [];
            statuses[index] = new TranslationCatalogStatus(snapshots[index], entries[index].SupportedLocales, diagnostics);
        }

        return new State(version, requestedLocale, snapshots, generations, Array.AsReadOnly(statuses));
    }

    // The locales every catalog with known locales declares (or has added by an overlay), in ordinal order.
    private static string[] IntersectSupportedLocales(TranslationContextEntry[] entries)
    {
        HashSet<string>? common = null;
        foreach (TranslationContextEntry entry in entries)
        {
            if (entry.SupportedLocales.Length == 0) continue;
            if (common is null) common = new HashSet<string>(entry.SupportedLocales, StringComparer.Ordinal);
            else common.IntersectWith(entry.SupportedLocales);
        }

        if (common is null) return [];
        string[] result = [.. common];
        Array.Sort(result, StringComparer.Ordinal);
        return result;
    }

    // Raises the events of the latest published state. A thread that finds another thread raising events leaves the
    // work to it: that thread loops until it has raised the latest state, so events follow publication order without
    // blocking, also when a handler switches the locale again.
    private void NotifyPublished()
    {
        while (true)
        {
            if (Interlocked.CompareExchange(ref _notifying, 1, 0) != 0) return;
            try
            {
                State target;
                while ((target = Volatile.Read(ref _state)).Version != Interlocked.Read(ref _notifiedVersion))
                {
                    if (Raise(target)) Interlocked.Exchange(ref _notifiedVersion, target.Version);
                }
            }
            finally
            {
                Volatile.Write(ref _notifying, 0);
            }

            // A publication between the last check and the release found the flag set; deliver it now.
            if (Volatile.Read(ref _state).Version == Interlocked.Read(ref _notifiedVersion)) return;
        }
    }

    // Raises the events from the state subscribers last saw to target. Stops, returning false, as soon as a newer state is
    // published, so a stale event is never raised after a newer one.
    private bool Raise(State target)
    {
        for (int index = 0; index < _managers.Length; index++)
        {
            if (!ReferenceEquals(Volatile.Read(ref _state), target)) return false;
            _managers[index].Raise(target.Snapshots[index], target.Generations[index]);
        }

        if (!ReferenceEquals(Volatile.Read(ref _state), target)) return false;
        if (!string.Equals(_notifiedRequestedLocale, target.RequestedLocale, StringComparison.Ordinal))
        {
            string old = _notifiedRequestedLocale;
            _notifiedRequestedLocale = target.RequestedLocale;
            Notify(LocaleChanged, new TranslationContextLocaleChangedEventArgs(old, target.RequestedLocale, target.Statuses), this);
        }

        return true;
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

    // Version grows with every publication; Generations[i] grows when catalog i is refreshed.
    private sealed record State(long Version, string RequestedLocale, ITranslationSnapshot[] Snapshots, long[] Generations,
        ReadOnlyCollection<TranslationCatalogStatus> Statuses);

    // A pending SetLocaleAsync call: canceled by the caller's token or when a later call supersedes it.
    private sealed class SwitchRequest : IDisposable
    {
        private readonly CancellationTokenSource _superseded = new();
        private readonly CancellationTokenSource? _linked;

        internal SwitchRequest(long sequence, CancellationToken cancellationToken)
        {
            Sequence = sequence;
            _linked = cancellationToken.CanBeCanceled ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _superseded.Token) : null;
            Token = _linked?.Token ?? _superseded.Token;
        }

        internal long Sequence { get; }

        internal CancellationToken Token { get; }

        internal bool IsSuperseded => _superseded.IsCancellationRequested;

        internal void Supersede()
        {
            try
            {
                _superseded.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The request completed meanwhile.
            }
            catch (AggregateException)
            {
                // A provider's cancellation callback failed; the superseded switch is discarded either way.
            }
        }

        public void Dispose()
        {
            _linked?.Dispose();
            _superseded.Dispose();
        }
    }

    // A catalog's manager: its snapshot is the catalog's entry in the context's state.
    private sealed class ContextManager : ITranslationManager
    {
        private readonly TranslationContext _context;
        private readonly int _index;
        // The snapshot and refresh generation this manager's subscribers last saw; touched only by the raising thread.
        private ITranslationSnapshot _raisedSnapshot;
        private long _raisedGeneration;

        internal ContextManager(TranslationContext context, int index, ITranslationSnapshot initial)
        {
            _context = context;
            _index = index;
            _raisedSnapshot = initial;
        }

        public string CurrentLocale => Current.Locale;

        public ITranslationSnapshot Current => Volatile.Read(ref _context._state).Snapshots[_index];

        public event EventHandler<TranslationLocaleChangedEventArgs>? LocaleChanged;

        public event EventHandler<TranslationSnapshotPublishedEventArgs>? SnapshotPublished;

        public ValueTask SetLocaleAsync(string locale, CancellationToken cancellationToken = default) =>
            _context.SetLocaleAsync(locale, cancellationToken);

        public ValueTask RefreshAsync(CancellationToken cancellationToken = default) => _context.RefreshAsync(_index, cancellationToken);

        internal void Raise(ITranslationSnapshot next, long generation)
        {
            ITranslationSnapshot previous = _raisedSnapshot;
            bool refreshed = generation != _raisedGeneration;
            _raisedSnapshot = next;
            _raisedGeneration = generation;
            // As a standalone manager: the requested tag is part of the locale, since de-CH over a de snapshot keeps the
            // text but formats numbers and dates for Switzerland.
            if (!string.Equals(previous.Locale, next.Locale, StringComparison.Ordinal) ||
                !string.Equals(previous.RequestedLocale, next.RequestedLocale, StringComparison.Ordinal))
            {
                Notify(LocaleChanged, new TranslationLocaleChangedEventArgs(previous, next), this);
                Notify(SnapshotPublished, new TranslationSnapshotPublishedEventArgs(next, TranslationSnapshotPublishReason.LocaleChanged), this);
            }
            else if (refreshed)
            {
                Notify(SnapshotPublished, new TranslationSnapshotPublishedEventArgs(next, TranslationSnapshotPublishReason.Refresh), this);
            }
        }
    }
}

/// <summary>A registered catalog of a context: its registration, its (possibly overlaid) provider and its locales.</summary>
internal sealed record TranslationContextEntry(TranslationCatalogRegistration Registration, ITranslationProvider Provider, string[] SupportedLocales);
