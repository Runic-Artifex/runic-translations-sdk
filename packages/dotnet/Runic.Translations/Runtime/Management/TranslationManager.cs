using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace Runic.Translations;

/// <summary>
/// Atomically activates immutable locale snapshots supplied by an
/// <see cref="ITranslationProvider"/>.
/// </summary>
/// <remarks>
/// Concurrent requests for the same locale share one provider operation. A
/// caller's cancellation only cancels that caller's wait; the provider
/// operation is cancelled when no callers remain interested in it.
/// </remarks>
[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The transition semaphore never exposes its wait handle, and disposing it would race active callers.")]
public sealed class TranslationManager : ITranslationManager
{
    private readonly object _pendingGate = new();
    private readonly Dictionary<string, PendingSwitch> _pendingSwitches =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ITranslationProvider _provider;
    private readonly string _catalog;
    private readonly SemaphoreSlim _transitionGate = new(1, 1);
    private PendingRefresh? _pendingRefresh;
    private ITranslationSnapshot _current;

    /// <summary>Creates a manager with an already validated initial snapshot.</summary>
    /// <param name="provider">The provider used to build replacement snapshots.</param>
    /// <param name="initialSnapshot">The snapshot that is immediately available as <see cref="Current"/>.</param>
    public TranslationManager(
        ITranslationProvider provider,
        ITranslationSnapshot initialSnapshot)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(initialSnapshot);

        ValidateInitialSnapshot(initialSnapshot);

        _provider = provider;
        _catalog = initialSnapshot.Catalog;
        _current = initialSnapshot;
    }

    /// <inheritdoc />
    public string CurrentLocale => Current.Locale;

    /// <inheritdoc />
    public ITranslationSnapshot Current => Volatile.Read(ref _current);

    /// <inheritdoc />
    public event EventHandler<TranslationLocaleChangedEventArgs>? LocaleChanged;

    /// <inheritdoc />
    public ValueTask SetLocaleAsync(
        string locale,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);
        cancellationToken.ThrowIfCancellationRequested();

        if (IsCurrentLocale(locale))
        {
            return ValueTask.CompletedTask;
        }

        PendingSwitch pending;
        lock (_pendingGate)
        {
            if (IsCurrentLocale(locale))
            {
                return ValueTask.CompletedTask;
            }

            if (!_pendingSwitches.TryGetValue(locale, out pending!))
            {
                pending = new PendingSwitch(locale);
                pending.AddWaiter();
                _pendingSwitches.Add(locale, pending);
                pending.Operation = ActivateAsync(pending);
            }
            else
            {
                pending.AddWaiter();
            }
        }

        return new ValueTask(WaitForTransitionAsync(pending, cancellationToken));
    }

    /// <inheritdoc />
    public ValueTask RefreshAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        PendingRefresh pending;
        lock (_pendingGate)
        {
            if (_pendingRefresh is null)
            {
                pending = new PendingRefresh();
                pending.AddWaiter();
                _pendingRefresh = pending;
                pending.Operation = RefreshCurrentAsync(pending);
            }
            else
            {
                pending = _pendingRefresh;
                pending.AddWaiter();
            }
        }

        return new ValueTask(WaitForTransitionAsync(pending, cancellationToken));
    }

    private async Task RefreshCurrentAsync(PendingRefresh pending)
    {
        try
        {
            await _transitionGate.WaitAsync(pending.CancellationToken).ConfigureAwait(false);
            try
            {
                string refreshedLocale = Current.Locale;
                if (_provider is ITranslationSnapshotCacheInvalidator invalidatable)
                {
                    invalidatable.InvalidateSnapshot(refreshedLocale);
                }

                ITranslationSnapshot? replacement = await _provider
                    .GetSnapshotAsync(refreshedLocale, pending.CancellationToken)
                    .ConfigureAwait(false);

                pending.CancellationToken.ThrowIfCancellationRequested();
                ValidateReplacementSnapshot(replacement);
                if (!string.Equals(
                    replacement.Locale,
                    refreshedLocale,
                    StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "The translation provider returned a snapshot for a different locale during refresh.");
                }

                lock (_pendingGate)
                {
                    if (!pending.TryCommit())
                    {
                        throw new OperationCanceledException(
                            "All callers canceled the refresh before it committed.",
                            pending.CancellationToken);
                    }

                    Interlocked.Exchange(ref _current, replacement);
                }
            }
            finally
            {
                _transitionGate.Release();
            }
        }
        finally
        {
            CompletePendingRefresh(pending);
        }
    }

    private bool IsCurrentLocale(string requestedLocale) =>
        string.Equals(Current.Locale, requestedLocale, StringComparison.OrdinalIgnoreCase);

    private async Task ActivateAsync(PendingSwitch pending)
    {
        try
        {
            TranslationLocaleChangedEventArgs? notification = null;
            await _transitionGate.WaitAsync(pending.CancellationToken).ConfigureAwait(false);
            try
            {
                if (IsCurrentLocale(pending.Locale))
                {
                    return;
                }

                ITranslationSnapshot? replacement = await _provider
                    .GetSnapshotAsync(pending.Locale, pending.CancellationToken)
                    .ConfigureAwait(false);

                pending.CancellationToken.ThrowIfCancellationRequested();
                ValidateReplacementSnapshot(replacement);

                lock (_pendingGate)
                {
                    if (!pending.TryCommit())
                    {
                        throw new OperationCanceledException(
                            "All callers canceled the locale transition before it committed.",
                            pending.CancellationToken);
                    }

                    ITranslationSnapshot current = Current;
                    if (!string.Equals(
                        current.Locale,
                        replacement.Locale,
                        StringComparison.Ordinal))
                    {
                        ITranslationSnapshot previous =
                            Interlocked.Exchange(ref _current, replacement);
                        notification = new TranslationLocaleChangedEventArgs(previous, replacement);
                    }
                }
            }
            finally
            {
                _transitionGate.Release();
            }

            if (notification is not null)
            {
                NotifyLocaleChanged(notification);
            }
        }
        finally
        {
            CompletePendingSwitch(pending);
        }
    }

    private async Task WaitForTransitionAsync(
        IPendingTransition pending,
        CancellationToken cancellationToken)
    {
        int released = 0;
        TaskCompletionSource cancellation =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        void ReleaseInterest()
        {
            if (Volatile.Read(ref released) == 0 && TryCancelWaiter(pending))
            {
                Interlocked.Exchange(ref released, 1);
                cancellation.TrySetCanceled(cancellationToken);
            }
        }

        using CancellationTokenRegistration cancellationRegistration =
            cancellationToken.Register(ReleaseInterest);
        try
        {
            Task completed = await Task
                .WhenAny(pending.Operation, cancellation.Task)
                .ConfigureAwait(false);
            await completed.ConfigureAwait(false);
        }
        finally
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
            {
                ReleaseWaiter(pending);
            }
        }
    }

    private bool TryCancelWaiter(IPendingTransition pending)
    {
        PendingSwitchRelease release;
        lock (_pendingGate)
        {
            if (pending.Committed)
            {
                return false;
            }

            release = pending.ReleaseWaiter();
            if (release == PendingSwitchRelease.Cancel &&
                pending is PendingSwitch abandoned &&
                _pendingSwitches.TryGetValue(abandoned.Locale, out PendingSwitch? registered) &&
                ReferenceEquals(registered, abandoned))
            {
                _pendingSwitches.Remove(abandoned.Locale);
            }
        }

        HandleWaiterRelease(pending, release);
        return true;
    }

    private void CompletePendingRefresh(PendingRefresh pending)
    {
        PendingSwitchRelease release;
        lock (_pendingGate)
        {
            if (ReferenceEquals(_pendingRefresh, pending))
            {
                _pendingRefresh = null;
            }

            release = pending.Complete();
        }

        if (release == PendingSwitchRelease.Dispose)
        {
            pending.Dispose();
        }
    }

    private void CompletePendingSwitch(PendingSwitch pending)
    {
        PendingSwitchRelease release;
        lock (_pendingGate)
        {
            if (_pendingSwitches.TryGetValue(pending.Locale, out PendingSwitch? registered) &&
                ReferenceEquals(registered, pending))
            {
                _pendingSwitches.Remove(pending.Locale);
            }

            release = pending.Complete();
        }

        if (release == PendingSwitchRelease.Dispose)
        {
            pending.Dispose();
        }
    }

    private void ReleaseWaiter(IPendingTransition pending)
    {
        PendingSwitchRelease release;
        lock (_pendingGate)
        {
            release = pending.ReleaseWaiter();
        }

        HandleWaiterRelease(pending, release);
    }

    private void HandleWaiterRelease(
        IPendingTransition pending,
        PendingSwitchRelease release)
    {
        if (release == PendingSwitchRelease.Cancel)
        {
            _ = Task.Factory.StartNew(
                () => CancelPending(pending),
                CancellationToken.None,
                TaskCreationOptions.DenyChildAttach,
                TaskScheduler.Default);
        }
        else if (release == PendingSwitchRelease.Dispose)
        {
            pending.Dispose();
        }
    }

    private void NotifyLocaleChanged(TranslationLocaleChangedEventArgs notification)
    {
        EventHandler<TranslationLocaleChangedEventArgs>? handlers = LocaleChanged;
        if (handlers is null)
        {
            return;
        }

        foreach (Delegate registeredHandler in handlers.GetInvocationList())
        {
            try
            {
                ((EventHandler<TranslationLocaleChangedEventArgs>)registeredHandler)(this, notification);
            }
            catch (Exception)
            {
                // Locale publication is complete. Subscriber failures cannot roll it back
                // and must not fault callers that successfully committed the transition.
            }
        }
    }

    private void CancelPending(IPendingTransition pending)
    {
        try
        {
            pending.Cancel();
        }
        finally
        {
            PendingSwitchRelease release;
            lock (_pendingGate)
            {
                release = pending.CancellationFinished();
            }

            if (release == PendingSwitchRelease.Dispose)
            {
                pending.Dispose();
            }
        }
    }

    private static void ValidateInitialSnapshot(ITranslationSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot.Catalog))
        {
            throw new ArgumentException(
                "The initial snapshot must have a catalog identifier.",
                nameof(snapshot));
        }

        if (!LocaleTag.TryCanonicalize(snapshot.Locale, out string canonicalLocale) ||
            !string.Equals(snapshot.Locale, canonicalLocale, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The initial snapshot must have a canonical locale.",
                nameof(snapshot));
        }
    }

    private void ValidateReplacementSnapshot(
        [NotNull] ITranslationSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            throw new InvalidOperationException("The translation provider returned a null snapshot.");
        }

        if (!string.Equals(snapshot.Catalog, _catalog, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The translation provider returned a snapshot for a different catalog.");
        }

        if (!LocaleTag.TryCanonicalize(snapshot.Locale, out string canonicalLocale) ||
            !string.Equals(snapshot.Locale, canonicalLocale, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The translation provider returned a snapshot without a canonical locale.");
        }
    }

    private interface IPendingTransition : IDisposable
    {
        CancellationToken CancellationToken { get; }

        bool Committed { get; }

        Task Operation { get; set; }

        void AddWaiter();

        bool TryCommit();

        PendingSwitchRelease Complete();

        void Cancel();

        PendingSwitchRelease ReleaseWaiter();

        PendingSwitchRelease CancellationFinished();
    }

    private sealed class PendingSwitch : IPendingTransition
    {
        private readonly CancellationTokenSource _cancellation = new();
        private bool _cancellationInProgress;
        private bool _committed;
        private bool _completed;
        private bool _disposed;
        private int _waiters;

        internal PendingSwitch(string locale) => Locale = locale;

        public CancellationToken CancellationToken => _cancellation.Token;

        public bool Committed => _committed;

        internal string Locale { get; }

        public Task Operation { get; set; } = null!;

        public void AddWaiter() => _waiters++;

        public bool TryCommit()
        {
            if (_waiters == 0)
            {
                return false;
            }

            _committed = true;
            return true;
        }

        public PendingSwitchRelease Complete()
        {
            _completed = true;
            return TryClaimDisposal();
        }

        public PendingSwitchRelease ReleaseWaiter()
        {
            _waiters--;
            if (_waiters == 0 && !_completed)
            {
                _cancellationInProgress = true;
                return PendingSwitchRelease.Cancel;
            }

            return TryClaimDisposal();
        }

        public void Cancel()
        {
            _cancellation.Cancel();
        }

        public PendingSwitchRelease CancellationFinished()
        {
            _cancellationInProgress = false;
            return TryClaimDisposal();
        }

        public void Dispose() => _cancellation.Dispose();

        private PendingSwitchRelease TryClaimDisposal()
        {
            if (!_disposed && _completed && _waiters == 0 && !_cancellationInProgress)
            {
                _disposed = true;
                return PendingSwitchRelease.Dispose;
            }

            return PendingSwitchRelease.None;
        }
    }

    private sealed class PendingRefresh : IPendingTransition
    {
        private readonly CancellationTokenSource _cancellation = new();
        private bool _cancellationInProgress;
        private bool _committed;
        private bool _completed;
        private bool _disposed;
        private int _waiters;

        public CancellationToken CancellationToken => _cancellation.Token;

        public bool Committed => _committed;

        public Task Operation { get; set; } = null!;

        public void AddWaiter() => _waiters++;

        public bool TryCommit()
        {
            if (_waiters == 0)
            {
                return false;
            }

            _committed = true;
            return true;
        }

        public PendingSwitchRelease Complete()
        {
            _completed = true;
            return TryClaimDisposal();
        }

        public PendingSwitchRelease ReleaseWaiter()
        {
            _waiters--;
            if (_waiters == 0 && !_completed)
            {
                _cancellationInProgress = true;
                return PendingSwitchRelease.Cancel;
            }

            return TryClaimDisposal();
        }

        public void Cancel()
        {
            _cancellation.Cancel();
        }

        public PendingSwitchRelease CancellationFinished()
        {
            _cancellationInProgress = false;
            return TryClaimDisposal();
        }

        public void Dispose() => _cancellation.Dispose();

        private PendingSwitchRelease TryClaimDisposal()
        {
            if (!_disposed && _completed && _waiters == 0 && !_cancellationInProgress)
            {
                _disposed = true;
                return PendingSwitchRelease.Dispose;
            }

            return PendingSwitchRelease.None;
        }
    }

    private enum PendingSwitchRelease
    {
        None,
        Cancel,
        Dispose,
    }
}
