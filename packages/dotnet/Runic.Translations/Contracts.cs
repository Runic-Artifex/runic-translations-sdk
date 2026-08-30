using System;
using System.Threading;
using System.Threading.Tasks;

namespace Runic.Translations;

/// <summary>An immutable, thread-safe locale snapshot.</summary>
public interface ITranslationSnapshot
{
    /// <summary>The stable catalog identifier.</summary>
    string Catalog { get; }
    /// <summary>The canonical locale tag.</summary>
    string Locale { get; }
    /// <summary>Attempts to resolve a resource pattern.</summary>
    bool TryGet(TranslationKey key, out string pattern);
    /// <summary>Resolves a resource pattern under the catalog missing-key policy.</summary>
    string Get(TranslationKey key);
    /// <summary>Formats a resolved resource using closed typed arguments.</summary>
    string Format(TranslationKey key, ReadOnlySpan<TextArgument> arguments);
    /// <summary>Formats safe structured localized content without interpreting it as HTML.</summary>
    LocalizedTextContent FormatContent(TranslationKey key, ReadOnlySpan<TextArgument> arguments) =>
        throw new NotSupportedException("This snapshot does not support structured localized content.");
}

/// <summary>Creates immutable snapshots for requested locales.</summary>
public interface ITranslationProvider
{
    /// <summary>Gets a fully validated snapshot.</summary>
    ValueTask<ITranslationSnapshot> GetSnapshotAsync(string requestedLocale, CancellationToken cancellationToken = default);
}

/// <summary>Owns the currently active immutable locale snapshot.</summary>
public interface ITranslationManager
{
    /// <summary>The canonical locale of <see cref="Current"/>.</summary>
    string CurrentLocale { get; }
    /// <summary>The active snapshot.</summary>
    ITranslationSnapshot Current { get; }
    /// <summary>Raised exactly once after a successful atomic locale swap.</summary>
    event EventHandler<TranslationLocaleChangedEventArgs>? LocaleChanged;
    /// <summary>Builds and atomically activates a locale snapshot.</summary>
    /// <remarks>
    /// Requesting the active locale completes without composing a snapshot; use
    /// <see cref="RefreshAsync"/> to reload replacement bytes for the active locale.
    /// </remarks>
    ValueTask SetLocaleAsync(string locale, CancellationToken cancellationToken = default);
    /// <summary>
    /// Rebuilds and atomically republishes the active locale's snapshot from its sources as if the
    /// locale were being entered fresh, without changing locales.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Refresh recomposes whatever locale is current once an internal transition lock is acquired,
    /// so concurrent callers coalesce into one composition and a refresh racing pending
    /// <see cref="SetLocaleAsync"/> work resolves deterministically: whichever acquires the lock
    /// first runs first, and a refresh that runs after a committed switch recomposes the newly
    /// active locale. <see cref="LocaleChanged"/> is never raised by a refresh because the active
    /// locale is unchanged.
    /// </para>
    /// <para>
    /// The replacement publishes only after the same validation gates as
    /// <see cref="SetLocaleAsync"/>. Any failure, including cancellation, preserves
    /// <see cref="Current"/> exactly and surfaces the provider's exception; incompatible external
    /// packs surface the established normalized <c>RTR0023</c> rejection identity.
    /// </para>
    /// </remarks>
    ValueTask RefreshAsync(CancellationToken cancellationToken = default);
}

/// <summary>Formats a closed typed argument for a resource locale.</summary>
public interface ITextValueFormatter
{
    /// <summary>Formats <paramref name="value"/> without interpreting arbitrary consumer format strings.</summary>
    string Format(in TextArgument value, string resourceLocale);
}

/// <summary>Optionally supplies caller-owned external pack bytes.</summary>
public interface IExternalTranslationSource
{
    /// <summary>Loads a pack, or returns <see langword="null"/> when none is available.</summary>
    ValueTask<ExternalTranslationPack?> LoadAsync(string catalog, string locale, CancellationToken cancellationToken);
}

/// <summary>Describes a successful atomic locale transition.</summary>
public sealed class TranslationLocaleChangedEventArgs : EventArgs
{
    /// <summary>Creates transition event data.</summary>
    public TranslationLocaleChangedEventArgs(ITranslationSnapshot oldSnapshot, ITranslationSnapshot newSnapshot)
    {
        OldSnapshot = oldSnapshot ?? throw new ArgumentNullException(nameof(oldSnapshot));
        NewSnapshot = newSnapshot ?? throw new ArgumentNullException(nameof(newSnapshot));
    }

    /// <summary>The previously active snapshot.</summary>
    public ITranslationSnapshot OldSnapshot { get; }
    /// <summary>The newly active snapshot.</summary>
    public ITranslationSnapshot NewSnapshot { get; }
    /// <summary>The previous canonical locale.</summary>
    public string OldLocale => OldSnapshot.Locale;
    /// <summary>The new canonical locale.</summary>
    public string NewLocale => NewSnapshot.Locale;
}

/// <summary>Opaque caller-owned bytes for an external locale pack.</summary>
public sealed class ExternalTranslationPack
{
    /// <summary>Creates a pack from immutable byte memory.</summary>
    public ExternalTranslationPack(ReadOnlyMemory<byte> content) => Content = content;

    /// <summary>The encoded external-pack document.</summary>
    public ReadOnlyMemory<byte> Content { get; }
}
