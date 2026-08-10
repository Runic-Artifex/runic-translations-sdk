using System;
using System.Threading;
using System.Threading.Tasks;

namespace RunicTranslations;

/// <summary>An immutable, thread-safe locale snapshot.</summary>
public interface ITextResourceSnapshot
{
    /// <summary>The stable catalog identifier.</summary>
    string Catalog { get; }
    /// <summary>The canonical locale tag.</summary>
    string Locale { get; }
    /// <summary>Attempts to resolve a resource pattern.</summary>
    bool TryGet(TextResourceKey key, out string pattern);
    /// <summary>Resolves a resource pattern under the catalog missing-key policy.</summary>
    string Get(TextResourceKey key);
    /// <summary>Formats a resolved resource using closed typed arguments.</summary>
    string Format(TextResourceKey key, ReadOnlySpan<TextArgument> arguments);
    /// <summary>Formats safe structured localized content without interpreting it as HTML.</summary>
    LocalizedTextContent FormatContent(TextResourceKey key, ReadOnlySpan<TextArgument> arguments) =>
        throw new NotSupportedException("This snapshot does not support structured localized content.");
}

/// <summary>Creates immutable snapshots for requested locales.</summary>
public interface ITextResourceProvider
{
    /// <summary>Gets a fully validated snapshot.</summary>
    ValueTask<ITextResourceSnapshot> GetSnapshotAsync(string requestedLocale, CancellationToken cancellationToken = default);
}

/// <summary>Owns the currently active immutable locale snapshot.</summary>
public interface ITextResourceManager
{
    /// <summary>The canonical locale of <see cref="Current"/>.</summary>
    string CurrentLocale { get; }
    /// <summary>The active snapshot.</summary>
    ITextResourceSnapshot Current { get; }
    /// <summary>Raised exactly once after a successful atomic locale swap.</summary>
    event EventHandler<TextResourceLocaleChangedEventArgs>? LocaleChanged;
    /// <summary>Builds and atomically activates a locale snapshot.</summary>
    ValueTask SetLocaleAsync(string locale, CancellationToken cancellationToken = default);
}

/// <summary>Formats a closed typed argument for a resource locale.</summary>
public interface ITextValueFormatter
{
    /// <summary>Formats <paramref name="value"/> without interpreting arbitrary consumer format strings.</summary>
    string Format(in TextArgument value, string resourceLocale);
}

/// <summary>Optionally supplies caller-owned external pack bytes.</summary>
public interface IExternalTextResourceSource
{
    /// <summary>Loads a pack, or returns <see langword="null"/> when none is available.</summary>
    ValueTask<ExternalTextResourcePack?> LoadAsync(string catalog, string locale, CancellationToken cancellationToken);
}

/// <summary>Describes a successful atomic locale transition.</summary>
public sealed class TextResourceLocaleChangedEventArgs : EventArgs
{
    /// <summary>Creates transition event data.</summary>
    public TextResourceLocaleChangedEventArgs(ITextResourceSnapshot oldSnapshot, ITextResourceSnapshot newSnapshot)
    {
        OldSnapshot = oldSnapshot ?? throw new ArgumentNullException(nameof(oldSnapshot));
        NewSnapshot = newSnapshot ?? throw new ArgumentNullException(nameof(newSnapshot));
    }

    /// <summary>The previously active snapshot.</summary>
    public ITextResourceSnapshot OldSnapshot { get; }
    /// <summary>The newly active snapshot.</summary>
    public ITextResourceSnapshot NewSnapshot { get; }
    /// <summary>The previous canonical locale.</summary>
    public string OldLocale => OldSnapshot.Locale;
    /// <summary>The new canonical locale.</summary>
    public string NewLocale => NewSnapshot.Locale;
}

/// <summary>Opaque caller-owned bytes for an external locale pack.</summary>
public sealed class ExternalTextResourcePack
{
    /// <summary>Creates a pack from immutable byte memory.</summary>
    public ExternalTextResourcePack(ReadOnlyMemory<byte> content) => Content = content;

    /// <summary>The encoded external-pack document.</summary>
    public ReadOnlyMemory<byte> Content { get; }
}
