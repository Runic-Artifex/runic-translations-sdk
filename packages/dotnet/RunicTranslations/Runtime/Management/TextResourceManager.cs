using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace RunicTranslations;

/// <summary>
/// Atomically activates immutable locale snapshots supplied by an
/// <see cref="ITextResourceProvider"/>.
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
public sealed class TextResourceManager : ITextResourceManager
{
    private readonly object _pendingGate = new();
    private readonly Dictionary<string, PendingSwitch> _pendingSwitches =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ITextResourceProvider _provider;
    private readonly string _catalog;
    private readonly SemaphoreSlim _transitionGate = new(1, 1);
    private ITextResourceSnapshot _current;

    /// <summary>Creates a manager with an already validated initial snapshot.</summary>
    /// <param name="provider">The provider used to build replacement snapshots.</param>
    /// <param name="initialSnapshot">The snapshot that is immediately available as <see cref="Current"/>.</param>
    public TextResourceManager(
        ITextResourceProvider provider,
        ITextResourceSnapshot initialSnapshot)
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
    public ITextResourceSnapshot Current => Volatile.Read(ref _current);

    /// <inheritdoc />
    public event EventHandler<TextResourceLocaleChangedEventArgs>? LocaleChanged;

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

        return new ValueTask(WaitForSwitchAsync(pending, cancellationToken));
    }

    private bool IsCurrentLocale(string requestedLocale) =>
        string.Equals(Current.Locale, requestedLocale, StringComparison.OrdinalIgnoreCase);

    private async Task ActivateAsync(PendingSwitch pending)
    {
        try
        {
            TextResourceLocaleChangedEventArgs? notification = null;
            await _transitionGate.WaitAsync(pending.CancellationToken).ConfigureAwait(false);
            try
            {
                if (IsCurrentLocale(pending.Locale))
                {
                    return;
                }

                ITextResourceSnapshot? replacement = await _provider
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

                    ITextResourceSnapshot current = Current;
                    if (!string.Equals(
                        current.Locale,
                        replacement.Locale,
                        StringComparison.Ordinal))
                    {
                        ITextResourceSnapshot previous =
                            Interlocked.Exchange(ref _current, replacement);
                        notification = new TextResourceLocaleChangedEventArgs(previous, replacement);
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

    private async Task WaitForSwitchAsync(
        PendingSwitch pending,
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

    private bool TryCancelWaiter(PendingSwitch pending)
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
                _pendingSwitches.TryGetValue(pending.Locale, out PendingSwitch? registered) &&
                ReferenceEquals(registered, pending))
            {
                _pendingSwitches.Remove(pending.Locale);
            }
        }

        HandleWaiterRelease(pending, release);
        return true;
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

    private void ReleaseWaiter(PendingSwitch pending)
    {
        PendingSwitchRelease release;
        lock (_pendingGate)
        {
            release = pending.ReleaseWaiter();
        }

        HandleWaiterRelease(pending, release);
    }

    private void HandleWaiterRelease(
        PendingSwitch pending,
        PendingSwitchRelease release)
    {
        if (release == PendingSwitchRelease.Cancel)
        {
            _ = Task.Factory.StartNew(
                () => CancelPendingSwitch(pending),
                CancellationToken.None,
                TaskCreationOptions.DenyChildAttach,
                TaskScheduler.Default);
        }
        else if (release == PendingSwitchRelease.Dispose)
        {
            pending.Dispose();
        }
    }

    private void NotifyLocaleChanged(TextResourceLocaleChangedEventArgs notification)
    {
        EventHandler<TextResourceLocaleChangedEventArgs>? handlers = LocaleChanged;
        if (handlers is null)
        {
            return;
        }

        foreach (Delegate registeredHandler in handlers.GetInvocationList())
        {
            try
            {
                ((EventHandler<TextResourceLocaleChangedEventArgs>)registeredHandler)(this, notification);
            }
            catch (Exception)
            {
                // Locale publication is complete. Subscriber failures cannot roll it back
                // and must not fault callers that successfully committed the transition.
            }
        }
    }

    private void CancelPendingSwitch(PendingSwitch pending)
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

    private static void ValidateInitialSnapshot(ITextResourceSnapshot snapshot)
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
        [NotNull] ITextResourceSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            throw new InvalidOperationException("The text resource provider returned a null snapshot.");
        }

        if (!string.Equals(snapshot.Catalog, _catalog, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The text resource provider returned a snapshot for a different catalog.");
        }

        if (!LocaleTag.TryCanonicalize(snapshot.Locale, out string canonicalLocale) ||
            !string.Equals(snapshot.Locale, canonicalLocale, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The text resource provider returned a snapshot without a canonical locale.");
        }
    }

    private sealed class PendingSwitch : IDisposable
    {
        private readonly CancellationTokenSource _cancellation = new();
        private bool _cancellationInProgress;
        private bool _committed;
        private bool _completed;
        private bool _disposed;
        private int _waiters;

        internal PendingSwitch(string locale) => Locale = locale;

        internal CancellationToken CancellationToken => _cancellation.Token;

        internal bool Committed => _committed;

        internal string Locale { get; }

        internal Task Operation { get; set; } = null!;

        internal void AddWaiter() => _waiters++;

        internal bool TryCommit()
        {
            if (_waiters == 0)
            {
                return false;
            }

            _committed = true;
            return true;
        }

        internal PendingSwitchRelease Complete()
        {
            _completed = true;
            return TryClaimDisposal();
        }

        internal PendingSwitchRelease ReleaseWaiter()
        {
            _waiters--;
            if (_waiters == 0 && !_completed)
            {
                _cancellationInProgress = true;
                return PendingSwitchRelease.Cancel;
            }

            return TryClaimDisposal();
        }

        internal void Cancel()
        {
            _cancellation.Cancel();
        }

        internal PendingSwitchRelease CancellationFinished()
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
