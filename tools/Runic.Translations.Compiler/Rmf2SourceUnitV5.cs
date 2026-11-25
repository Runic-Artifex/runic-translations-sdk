using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace Runic.Translations.Compiler;

/// <summary>
/// Per-source compilation work that depends only on one source file: its RMF2 or MF2 parse and the
/// semantic lowering of each message it contains. A project link that receives units reuses this
/// work for every source whose bytes did not change, so an edit recompiles only the edited file
/// before relinking the catalog.
/// </summary>
/// <remarks>
/// Lowering is memoized lazily per message text and canonical caller contract, because a
/// translated message is lowered against the base-locale inputs of the same key. A unit is
/// immutable apart from that thread-safe memo, so hosts can keep it across links (the source
/// generator's incremental cache, the tool's serve mode) and drop it when the file changes.
/// </remarks>
internal sealed class Rmf2SourceUnitV5
{
    // Lock-free reads: the messages of one file are lowered in parallel.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(string Message, string Contract), Rmf2SemanticResultV5> _lowered = new();
    private int _memoCount;
    private readonly object _gate = new();
    private readonly int _memoLimit;
    private int _loweringCount;

    private Rmf2SourceUnitV5(TranslationSource source, TranslationCompilerOptions options,
        Rmf2ResourceDocument? resource, string? directMessage, Mf2SyntaxDocument? directSyntax)
    {
        Source = source;
        Options = options;
        Resource = resource;
        DirectMessage = directMessage;
        DirectSyntax = directSyntax;
        _memoLimit = Math.Max(64, 4 * (resource?.Nodes.Count ?? 1));
    }

    internal TranslationSource Source { get; }
    internal TranslationCompilerOptions Options { get; }

    /// <summary>The grouped RMF2 outline, or null for direct MF2 and unsupported sources.</summary>
    internal Rmf2ResourceDocument? Resource { get; }

    /// <summary>The decoded direct MF2 message, or null when the source is not direct MF2 or is not valid UTF-8.</summary>
    internal string? DirectMessage { get; }
    internal Mf2SyntaxDocument? DirectSyntax { get; }

    /// <summary>How many messages this unit has lowered, as opposed to served from its memo.</summary>
    internal int LoweringCount => Volatile.Read(ref _loweringCount);

    internal static Rmf2SourceUnitV5 Create(TranslationSource source, TranslationCompilerOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        if (TranslationCatalogFormats.IsResourceSource(source.Path))
            return new(source, options, Rmf2ResourceReader.Read(source, options, cancellationToken), null, null);
        if (!source.Path.EndsWith(".mf2", StringComparison.OrdinalIgnoreCase))
            return new(source, options, null, null, null);
        string? message;
        try { message = StrictJsonParser.StrictUtf8.GetString(source.Bytes); }
        catch (DecoderFallbackException) { message = null; }
        return new(source, options, null, message, message is null ? null : Mf2SyntaxReader.Read(source, options, cancellationToken));
    }

    /// <summary>Releases the token streams of the unit's messages (<see cref="Mf2SyntaxDocument.ReleaseTokens"/>).</summary>
    internal void ReleaseTokens()
    {
        if (Resource is { } resource)
            foreach (var node in resource.Nodes) node.MessageSyntax?.ReleaseTokens();
        DirectSyntax?.ReleaseTokens();
    }

    private int _messageCount = -1;

    /// <summary>The number of messages in the source (not groups).</summary>
    internal int MessageCount
    {
        get
        {
            if (_messageCount < 0) _messageCount = Resource?.Nodes.Count(node => !node.IsGroup) ?? (DirectMessage is null ? 0 : 1);
            return _messageCount;
        }
    }

    /// <summary>Whether this unit was built from exactly these bytes at this path, with any options.</summary>
    internal bool HasBytesOf(TranslationSource source) =>
        string.Equals(source.Path, Source.Path, StringComparison.Ordinal) &&
        (ReferenceEquals(source, Source) || source.Bytes.AsSpan().SequenceEqual(Source.Bytes));

    // The nodes a cached compile extracted from this unit, per link context (Rmf2CompileCache); at most four contexts.
    private readonly List<(object Context, object Extraction)> _extractions = new();

    internal object? Extraction(object context)
    {
        lock (_gate)
            foreach (var item in _extractions)
                if (ReferenceEquals(item.Context, context)) return item.Extraction;
        return null;
    }

    internal void SetExtraction(object context, object extraction)
    {
        lock (_gate)
        {
            _extractions.RemoveAll(item => ReferenceEquals(item.Context, context));
            if (_extractions.Count == 4) _extractions.RemoveAt(0);
            _extractions.Add((context, extraction));
        }
    }

    /// <summary>Whether this unit was built from exactly these bytes, at this path, with these options.</summary>
    internal bool Matches(TranslationSource source, TranslationCompilerOptions options) =>
        ReferenceEquals(options, Options) &&
        string.Equals(source.Path, Source.Path, StringComparison.Ordinal) &&
        source.Bytes.AsSpan().SequenceEqual(Source.Bytes);

    internal Rmf2SemanticResultV5 Lower(string message, IReadOnlyList<Rmf2InputV5>? callerInputs,
        CancellationToken cancellationToken) => Lower(message, null, callerInputs, cancellationToken);

    /// <param name="syntax">The message's MF2 syntax as this unit's reader read it, or null to read it here.</param>
    internal Rmf2SemanticResultV5 Lower(string message, Mf2SyntaxDocument? syntax, IReadOnlyList<Rmf2InputV5>? callerInputs,
        CancellationToken cancellationToken)
    {
        var key = (message, callerInputs is null ? string.Empty : Signature(callerInputs));
        if (_lowered.TryGetValue(key, out Rmf2SemanticResultV5? cached)) return cached;

        Interlocked.Increment(ref _loweringCount);
        syntax ??= Mf2SyntaxReader.Read(TranslationSource.Owned(Source.Path, Encoding.UTF8.GetBytes(message)), Options, cancellationToken);
        Rmf2SemanticResultV5 result = Rmf2SemanticCompilerV5.Lower(syntax, callerInputs, Options, cancellationToken);
        // A translated message is normally lowered against one caller contract at a time. Keep the
        // memo bounded when the base-locale inputs change repeatedly during one unit's lifetime.
        if (Interlocked.Increment(ref _memoCount) > _memoLimit)
        {
            lock (_gate)
            {
                if (Volatile.Read(ref _memoCount) > _memoLimit) { _lowered.Clear(); Volatile.Write(ref _memoCount, 0); }
            }
        }
        _lowered[key] = result;
        return result;
    }

    private static string Signature(IReadOnlyList<Rmf2InputV5> inputs)
    {
        var builder = new StringBuilder("\u0001");
        foreach (Rmf2InputV5 input in inputs)
            builder.Append(input.Name).Append('\u001f').Append(input.Type).Append('\u001e');
        return builder.ToString();
    }
}
