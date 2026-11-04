using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Xml;
using Runic.Translations.Compiler;

namespace Runic.Translations.Tooling;

/// <summary>Deterministic, deliberately small XLIFF 2.1 interchange facade.</summary>
/// <remarks>
/// This facade exchanges the compiler's supported execution model. It is not a general XLIFF processor. Plain
/// messages, text with placeholders, are exchanged losslessly: every placeholder is an XLIFF <c>&lt;ph&gt;</c> whose
/// <c>equiv</c> is the exact MF2 expression. A message with selectors or declarations is one unit: its declarations
/// and <c>.match</c> are one protected <c>&lt;ph&gt;</c>, and every variant is a <c>&lt;pc&gt;</c> whose start code is
/// its keys, so a translator edits only the variant texts; import checks the declarations, the caller contract and
/// the target locale's plural categories. Messages with markup are exported for translator visibility, reported as
/// lossy and skipped on import, never flattened into a different message.
/// Import is tolerant of what translation management systems write: each unit is applied, skipped or refused on
/// its own.
/// </remarks>
public static class TranslationInterchange
{
    /// <summary>The closed Runic XLIFF interchange profile identifier.</summary>
    public const string XliffProfile = "runic.xliff21.closed-text";
    /// <summary>The closed Runic XLIFF interchange profile version that export writes. Import also reads version 2.</summary>
    public const int XliffProfileVersion = 3;
    internal const string XliffNamespace = "urn:oasis:names:tc:xliff:document:2.0";
    internal const string NeedsReviewSubState = "runic:needs-review";
    /// <summary>The XLIFF 2 Size and Length Restriction module, for <c>@maxLength</c>.</summary>
    internal const string SizeRestrictionNamespace = "urn:oasis:names:tc:xliff:sizerestriction:2.0";
    internal const string ReviewSchema = "runic.translations.interchange-review/2";
    internal const string FormerReviewSchema = "runic.translations.interchange-review/1";
    internal const int MaximumXliffBytes = 8 * 1024 * 1024;
    internal const int MaximumUnits = 50_000;
    internal const int MaximumTextLength = 65_536;
    /// <summary>The deepest element nesting an imported XLIFF document may use (the root element is level 0); deeper documents are refused with XLIFF21-LIMIT.</summary>
    internal const int MaximumXliffDepth = 64;
    private const int MaximumReviewEntries = 50_000;
    private const int MaximumReviewBytes = 8 * 1024 * 1024;

    /// <summary>Exports a successful RMF2 v5 project compilation as the closed Runic XLIFF 2.1 profile, one document per non-default locale.</summary>
    /// <remarks>
    /// An approved review entry whose source fingerprint no longer matches the unit's current source fingerprint
    /// is exported as needs-review and reported as <c>REVIEW-STALE</c>. Entries for keys or locales that are not
    /// exported are reported as <c>REVIEW-UNEXPORTED</c> and left out.
    /// </remarks>
    public static TranslationXliffExportResult ExportXliff21(
        Rmf2ProjectCompilation compilation,
        TranslationInterchangeReview? review = null) =>
        ExportXliff21(TranslationInterchangeProjectionAdapter.From(compilation), review);

    /// <summary>
    /// The per-unit source fingerprint of every canonical message: a hash of the key, the source text and the caller
    /// contract (input names and types). Review entries record it to tell when a translation predates a source change.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ComputeSourceFingerprints(Rmf2ProjectCompilation compilation) =>
        TranslationInterchangeProjectionAdapter.From(compilation).SourceFingerprints;

    internal static TranslationInterchangeProjection PreflightXliff21(
        Rmf2ProjectCompilation compilation) =>
        TranslationInterchangeProjectionAdapter.From(compilation);

    private static TranslationXliffExportResult ExportXliff21(
        TranslationInterchangeProjection projection,
        TranslationInterchangeReview? review)
    {
        ValidateReview(review, projection.CatalogId);
        var documents = new List<TranslationXliffDocument>();
        var losses = new List<TranslationInterchangeLoss>();
        var keys = new HashSet<string>(projection.CanonicalUnits.Select(static unit => unit.Key), StringComparer.Ordinal);
        var locales = new HashSet<string>(projection.Locales.Where(locale => locale.Tag != projection.SourceLocale).Select(static locale => locale.Tag), StringComparer.Ordinal);
        if (review is not null)
            foreach (TranslationInterchangeReviewEntry entry in review.Entries)
                if (!keys.Contains(entry.Key) || !locales.Contains(entry.Locale))
                    losses.Add(new("REVIEW-UNEXPORTED", "/" + entry.Key, "Review data for '" + entry.Key + "' in '" + entry.Locale + "' names a key or locale that is not exported; it was left out.", false));
        foreach (TranslationInterchangeLocale locale in projection.Locales.OrderBy(static item => item.Tag, StringComparer.Ordinal))
        {
            if (string.Equals(locale.Tag, projection.SourceLocale, StringComparison.Ordinal)) continue;
            byte[] bytes = Render(projection, locale, review, losses);
            documents.Add(new TranslationXliffDocument(projection.CatalogId, projection.SourceLocale, locale.Tag, bytes));
        }
        return new TranslationXliffExportResult(documents, new TranslationInterchangeReport(losses));
    }

    /// <summary>Imports a Runic XLIFF 2.0 or 2.1 document (profile 2 or 3, or a file a TMS wrote from one) into MF2 message documents.</summary>
    /// <remarks>
    /// Every unit is applied, skipped or refused on its own; see <see cref="TranslationXliffImportResult.Units"/>.
    /// <see cref="TranslationXliffImportResult.Messages"/> holds the applied units. Without the live compilation the
    /// placeholders are checked against the contract that the file records.
    /// </remarks>
    public static TranslationXliffImportResult ImportXliff21(ReadOnlyMemory<byte> source) =>
        TranslationXliffReader.Import(source, null, null);

    /// <summary>
    /// Imports an XLIFF document against the live catalog: the catalog, source locale and target locale must match,
    /// placeholders are checked against the current caller contract, and a unit whose source changed since export is
    /// applied as needs-review or refused, as <see cref="TranslationXliffImportOptions.SourceChangePolicy"/> selects.
    /// </summary>
    public static TranslationXliffImportResult ImportXliff21(
        ReadOnlyMemory<byte> source,
        Rmf2ProjectCompilation compilation,
        TranslationXliffImportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        return TranslationXliffReader.Import(source, TranslationInterchangeProjectionAdapter.From(compilation), options ?? new TranslationXliffImportOptions());
    }

    /// <summary>Renders review state as compact, stable <c>runic.translations.interchange-review/2</c> JSON suitable for source control, in a new array on every call.</summary>
    public static byte[] ExportReviewJson(TranslationInterchangeReview review)
    {
        ArgumentNullException.ThrowIfNull(review);
        ValidateReview(review, review.CatalogId);
        long reviewBytes = 128;
        foreach (TranslationInterchangeReviewEntry entry in review.Entries) { reviewBytes += Encoding.UTF8.GetByteCount(entry.Key) + Encoding.UTF8.GetByteCount(entry.Locale) + Encoding.UTF8.GetByteCount(TranslationReviewStates.ToWire(entry.State)) + (entry.Note is null ? 0 : Encoding.UTF8.GetByteCount(entry.Note)) + (entry.SourceFingerprint is null ? 0 : Encoding.UTF8.GetByteCount(entry.SourceFingerprint)) + 64; if (reviewBytes > MaximumReviewBytes) throw new TranslationInterchangeException("REVIEW-LIMIT", "The review representation exceeds the byte limit."); }
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject(); writer.WriteString("$schema", ReviewSchema); writer.WriteString("catalog", review.CatalogId);
            writer.WritePropertyName("entries"); writer.WriteStartArray();
            foreach (TranslationInterchangeReviewEntry entry in review.Entries.OrderBy(static item => item.Key, StringComparer.Ordinal).ThenBy(static item => item.Locale, StringComparer.Ordinal))
            {
                writer.WriteStartObject(); writer.WriteString("key", entry.Key); writer.WriteString("locale", entry.Locale); writer.WriteString("state", TranslationReviewStates.ToWire(entry.State));
                if (entry.Note is not null) writer.WriteString("note", entry.Note);
                if (entry.SourceFingerprint is not null) writer.WriteString("sourceFingerprint", entry.SourceFingerprint);
                writer.WriteEndObject();
            }
            writer.WriteEndArray(); writer.WriteEndObject();
        }
        stream.WriteByte((byte)'\n');
        if (stream.Length > MaximumReviewBytes) throw new TranslationInterchangeException("REVIEW-LIMIT", "The review representation exceeds the byte limit.");
        return stream.ToArray();
    }

    /// <summary>Reads the closed portable review representation, <c>runic.translations.interchange-review/2</c> or <c>/1</c>.</summary>
    /// <remarks>
    /// In <c>/1</c> an approved entry's fingerprint covered the whole catalog. It never matches a per-unit fingerprint,
    /// so such an approval is exported as needs-review until it is approved again.
    /// </remarks>
    public static TranslationInterchangeReview ImportReviewJson(ReadOnlyMemory<byte> source)
    {
        if (source.Length == 0) throw new TranslationInterchangeException("REVIEW-EMPTY", "A review representation is required.");
        if (source.Length > MaximumReviewBytes) throw new TranslationInterchangeException("REVIEW-LIMIT", "The review representation exceeds the byte limit.");
        try
        {
            using JsonDocument document = JsonDocument.Parse(source, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 16 });
            JsonElement root = document.RootElement;
            RequireObject(root, "REVIEW-ROOT"); RequireExact(root, "$schema", "catalog", "entries");
            if (RequiredString(root, "$schema", "REVIEW-ROOT") is not (ReviewSchema or FormerReviewSchema)) throw new TranslationInterchangeException("REVIEW-SCHEMA", "The review representation schema is not supported.");
            string catalog = RequiredString(root, "catalog", "REVIEW-ROOT");
            JsonElement values = Required(root, "entries", JsonValueKind.Array, "REVIEW-ROOT");
            var entries = new List<TranslationInterchangeReviewEntry>();
            foreach (JsonElement value in values.EnumerateArray())
            {
                RequireObject(value, "REVIEW-ENTRY"); RequireAllowed(value, "key", "locale", "state", "note", "sourceFingerprint");
                entries.Add(new TranslationInterchangeReviewEntry(RequiredString(value, "key", "REVIEW-ENTRY"), RequiredString(value, "locale", "REVIEW-ENTRY"), ReviewState(RequiredString(value, "state", "REVIEW-ENTRY"), "REVIEW-INVALID"), OptionalString(value, "note", "REVIEW-ENTRY"), OptionalString(value, "sourceFingerprint", "REVIEW-ENTRY")));
            }
            var review = new TranslationInterchangeReview(catalog, entries); ValidateReview(review, catalog); return review;
        }
        catch (TranslationInterchangeException) { throw; }
        catch (JsonException exception) { throw new TranslationInterchangeException("REVIEW-MALFORMED", "The review representation is malformed.", exception); }
    }

    private static byte[] Render(TranslationInterchangeProjection projection, TranslationInterchangeLocale targetLocale, TranslationInterchangeReview? review, List<TranslationInterchangeLoss> losses)
    {
        EnsureExportBounds(projection, targetLocale, review);
        var direct = targetLocale.DirectResources.ToDictionary(static resource => resource.Key, StringComparer.Ordinal);
        var reviewByKey = review?.Entries.Where(entry => string.Equals(entry.Locale, targetLocale.Tag, StringComparison.Ordinal)).ToDictionary(static entry => entry.Key, StringComparer.Ordinal) ?? new Dictionary<string, TranslationInterchangeReviewEntry>(StringComparer.Ordinal);
        using var stream = new MemoryStream();
        using (XmlWriter writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false, NewLineHandling = NewLineHandling.Entitize, OmitXmlDeclaration = false }))
        {
            writer.WriteStartDocument(); writer.WriteStartElement("xliff", XliffNamespace); writer.WriteAttributeString("version", "2.1"); writer.WriteAttributeString("srcLang", projection.SourceLocale); writer.WriteAttributeString("trgLang", targetLocale.Tag);
            bool sizeRestrictions = projection.CanonicalUnits.Any(static unit => unit.Metadata.MaxLength is not null && !unit.Structured);
            if (sizeRestrictions) writer.WriteAttributeString("xmlns", "slr", null, SizeRestrictionNamespace);
            writer.WriteStartElement("file", XliffNamespace); writer.WriteAttributeString("id", projection.CatalogId); writer.WriteAttributeString("original", projection.CatalogId + "." + targetLocale.Tag + ".xliff");
            // @maxLength counts Unicode code points after NFC, without placeholders: XLIFF's standard codepoints profile.
            if (sizeRestrictions)
            {
                writer.WriteStartElement("slr", "profiles", SizeRestrictionNamespace); writer.WriteAttributeString("generalProfile", "xliff:codepoints");
                writer.WriteStartElement("slr", "normalization", SizeRestrictionNamespace); writer.WriteAttributeString("general", "nfc"); writer.WriteEndElement();
                writer.WriteEndElement();
            }
            foreach (TranslationInterchangeSourceUnit source in projection.CanonicalUnits)
            {
                direct.TryGetValue(source.Key, out TranslationInterchangeTargetUnit? target);
                bool structured = source.Structured || target?.Structured == true;
                if (structured) losses.Add(new("XLIFF21-STRUCTURED-MESSAGE", "/" + source.Key, source.Document
                    ? "Document message (RMF2 document profile v1): its block structure and markup are not losslessly representable by the closed XLIFF text profile, and its import is skipped."
                    : "Markup, or a pattern that reads a value only its declarations define, is not losslessly representable by the closed XLIFF text profile; the unit is exported as not translatable and its import is skipped.", true));
                // A missing translation of a plural message whose locale selects other categories than the source
                // gets a target in state initial with the locale's variants, so a translator can fill in each one.
                TranslationInterchangeSelector? prefill = !structured && target is null && source.Selector is { } sourceSelector ? Prefill(sourceSelector, targetLocale.Tag) : null;
                TranslationInterchangeReviewEntry? entry = reviewByKey.TryGetValue(source.Key, out TranslationInterchangeReviewEntry? found) ? found : null;
                if (entry is { State: TranslationReviewState.Approved } && !string.Equals(entry.SourceFingerprint, source.SourceFingerprint, StringComparison.Ordinal))
                {
                    losses.Add(new("REVIEW-STALE", "/" + source.Key, "The approval of '" + source.Key + "' in '" + targetLocale.Tag + "' predates a source change; it is exported as needs-review.", false));
                    entry = new TranslationInterchangeReviewEntry(entry.Key, entry.Locale, TranslationReviewState.NeedsReview, entry.Note, entry.SourceFingerprint);
                }
                writer.WriteStartElement("unit", XliffNamespace); writer.WriteAttributeString("id", source.Key);
                if (structured) writer.WriteAttributeString("translate", "no");
                else if (source.Selector is null && source.Metadata.MaxLength is { } maxLength) writer.WriteAttributeString("slr", "sizeRestriction", SizeRestrictionNamespace, maxLength.ToString(System.Globalization.CultureInfo.InvariantCulture));
                WriteNotes(writer, projection, source, structured, entry);
                writer.WriteStartElement("segment", XliffNamespace); writer.WriteAttributeString("id", "1");
                // An existing translation without review data is "translated"; a missing one keeps XLIFF's default "initial".
                if (entry is not null)
                {
                    writer.WriteAttributeString("state", SegmentState(entry.State));
                    if (entry.State == TranslationReviewState.NeedsReview) writer.WriteAttributeString("subState", NeedsReviewSubState);
                }
                else if (target is not null) writer.WriteAttributeString("state", "translated");
                else if (prefill is not null) writer.WriteAttributeString("state", "initial");
                Dictionary<string, string> sourceIds = new(StringComparer.Ordinal);
                writer.WriteStartElement("source", XliffNamespace);
                if (structured) writer.WriteString(source.Text);
                else new InlineWriter(writer, null, sourceIds, source.Metadata.MaxLength).Write(source.Pieces, source.Selector);
                writer.WriteEndElement();
                if (target is not null || prefill is not null)
                {
                    writer.WriteStartElement("target", XliffNamespace);
                    if (structured) writer.WriteString(target!.Text);
                    else new InlineWriter(writer, sourceIds, null, source.Metadata.MaxLength).Write(target?.Pieces, target?.Selector ?? prefill);
                    writer.WriteEndElement();
                }
                writer.WriteEndElement(); writer.WriteEndElement();
            }
            writer.WriteEndElement(); writer.WriteEndElement(); writer.WriteEndDocument();
        }
        if (stream.Length > MaximumXliffBytes) throw new TranslationInterchangeException("XLIFF21-LIMIT", "The XLIFF export exceeds the byte limit.");
        return stream.ToArray();
    }

    // Source codes: the header is "h", variants "v1".."vn" and placeholders "1".."n" in order. A target code reuses the
    // id of an unused source code with the same content (expression, header or keys); any other gets its own id
    // ("th", "tv1".., "t1"..) and carries its content in equiv.
    private sealed class InlineWriter(XmlWriter writer, Dictionary<string, string>? sourceIds, Dictionary<string, string>? record, int? maxLength)
    {
        private readonly HashSet<string> _used = new(StringComparer.Ordinal);
        private int _placeholders, _variants, _targetPlaceholders, _targetVariants;

        internal void Write(IReadOnlyList<TranslationInterchangePiece>? pieces, TranslationInterchangeSelector? selector)
        {
            if (selector is null) { WritePieces(pieces ?? []); return; }
            string header = Id("h", "th", static id => id == "h", TranslationInterchangePattern.CanonicalHeader(selector.Header), TranslationInterchangePattern.CanonicalHeader, selector.Header);
            writer.WriteStartElement("ph", XliffNamespace);
            writer.WriteAttributeString("id", header);
            writer.WriteAttributeString("canCopy", "no");
            writer.WriteAttributeString("canDelete", "no");
            writer.WriteAttributeString("equiv", selector.Header);
            writer.WriteAttributeString("disp", selector.Display);
            writer.WriteEndElement();
            foreach (TranslationInterchangeVariant variant in selector.Variants)
            {
                string keys = variant.Keys ?? string.Empty;
                string id = Id("v" + (++_variants).ToString(System.Globalization.CultureInfo.InvariantCulture), "tv" + (_targetVariants + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    static id => id.StartsWith('v'), TranslationInterchangePattern.CanonicalKeys(keys)!, static value => TranslationInterchangePattern.CanonicalKeys(value)!, keys);
                if (id.StartsWith("tv", StringComparison.Ordinal)) _targetVariants++;
                writer.WriteStartElement("pc", XliffNamespace);
                writer.WriteAttributeString("id", id);
                writer.WriteAttributeString("canCopy", "no");
                writer.WriteAttributeString("canOverlap", "no");
                writer.WriteAttributeString("equivStart", (variant.Keys is null ? "" : variant.Keys + " ") + "{{");
                writer.WriteAttributeString("equivEnd", "}}");
                writer.WriteAttributeString("dispStart", variant.Keys ?? "{{");
                writer.WriteAttributeString("dispEnd", variant.Keys is null ? "}}" : "");
                if (maxLength is { } limit) writer.WriteAttributeString("slr", "sizeRestriction", SizeRestrictionNamespace, limit.ToString(System.Globalization.CultureInfo.InvariantCulture));
                WritePieces(variant.Pieces);
                writer.WriteEndElement();
            }
        }

        private void WritePieces(IReadOnlyList<TranslationInterchangePiece> pieces)
        {
            foreach (TranslationInterchangePiece piece in pieces)
            {
                if (piece.IsText) { writer.WriteString(piece.Text); continue; }
                string id = Id((++_placeholders).ToString(System.Globalization.CultureInfo.InvariantCulture), "t" + (_targetPlaceholders + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    static id => char.IsAsciiDigit(id[0]), piece.Expression!, static value => value, piece.Expression!);
                if (id.StartsWith('t')) _targetPlaceholders++;
                writer.WriteStartElement("ph", XliffNamespace);
                writer.WriteAttributeString("id", id);
                writer.WriteAttributeString("equiv", piece.Expression);
                writer.WriteAttributeString("disp", piece.Display);
                writer.WriteEndElement();
            }
        }

        // The source's own id (recorded with its content), or for a target the first unused source id of the same kind
        // and content, else a target id.
        private string Id(string sourceId, string targetId, Func<string, bool> kind, string content, Func<string, string> canonical, string recorded)
        {
            if (sourceIds is null)
            {
                record![sourceId] = recorded;
                _used.Add(sourceId);
                return sourceId;
            }
            string? match = sourceIds.Where(pair => kind(pair.Key) && !_used.Contains(pair.Key) && canonical(pair.Value) == content)
                .Select(static pair => pair.Key).OrderBy(static key => key.Length).ThenBy(static key => key, StringComparer.Ordinal).FirstOrDefault();
            string id = match ?? targetId;
            _used.Add(id);
            return id;
        }
    }

    // The variants a locale needs for a single plural or ordinal selector: keys of categories it never selects are
    // dropped, and each category it selects that has no key gets the catch-all's text. Null when nothing changes.
    private static TranslationInterchangeSelector? Prefill(TranslationInterchangeSelector source, string locale)
    {
        if (source.PluralFunction is null) return null;
        string[] categories = Rmf2AuthoringService.PluralCategories(locale, source.PluralFunction == "ordinal");
        TranslationInterchangeVariant? catchAll = source.Variants.FirstOrDefault(static variant => variant.Keys == "*");
        if (categories.Length == 0 || catchAll is null) return null;
        static bool IsCategory(string? keys) => keys is "zero" or "one" or "two" or "few" or "many" or "other";
        var variants = source.Variants.Where(variant => !IsCategory(variant.Keys) || variant.Keys == "other" || categories.Contains(variant.Keys)).ToList();
        int at = variants.IndexOf(catchAll);
        foreach (string category in categories)
            if (category != "other" && variants.All(variant => variant.Keys != category))
                variants.Insert(at++, new TranslationInterchangeVariant(category, catchAll.Pieces));
        return variants.Select(static variant => variant.Keys).SequenceEqual(source.Variants.Select(static variant => variant.Keys)) ? null : source with { Variants = variants };
    }

    private static void WriteNotes(XmlWriter writer, TranslationInterchangeProjection projection, TranslationInterchangeSourceUnit source, bool structured, TranslationInterchangeReviewEntry? review)
    {
        writer.WriteStartElement("notes", XliffNamespace);
        string? description = ReadableDescription(source.Metadata);
        if (description is not null) { writer.WriteStartElement("note", XliffNamespace); writer.WriteAttributeString("category", "description"); writer.WriteString(description); writer.WriteEndElement(); }
        writer.WriteStartElement("note", XliffNamespace); writer.WriteAttributeString("category", "runic:unit"); writer.WriteString(SerializeUnitNote(projection, source, structured)); writer.WriteEndElement();
        if (review is not null) { writer.WriteStartElement("note", XliffNamespace); writer.WriteAttributeString("category", "runic:review"); writer.WriteAttributeString("appliesTo", "target"); writer.WriteString(SerializeReviewNote(review, source.SourceFingerprint)); writer.WriteEndElement(); }
        writer.WriteEndElement();
    }

    // The note a translator reads in a TMS: the message's description, then one "@param $name - text" line per
    // documented placeholder. Import accepts it back and ignores it; the sources stay authoritative.
    private static string? ReadableDescription(TranslationInterchangeUnitMetadata metadata)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(metadata.Description)) lines.Add(metadata.Description);
        foreach (string parameter in metadata.Parameters) lines.Add("@param " + parameter);
        return lines.Count == 0 ? null : string.Join("\n", lines);
    }

    internal static string SegmentState(TranslationReviewState state) => state switch { TranslationReviewState.Draft => "initial", TranslationReviewState.Translated => "translated", TranslationReviewState.NeedsReview => "translated", TranslationReviewState.Approved => "final", _ => throw new TranslationInterchangeException("REVIEW-INVALID", "Unknown review state.") };

    private static string SerializePlaceholders(IReadOnlyList<TranslationInterchangePlaceholder> values)
    {
        using var stream = new MemoryStream(); using (var writer = new Utf8JsonWriter(stream)) { writer.WriteStartArray(); foreach (TranslationInterchangePlaceholder value in values.OrderBy(static item => item.Name, StringComparer.Ordinal)) { writer.WriteStartObject(); writer.WriteString("name", value.Name); writer.WriteString("type", value.Type); writer.WriteString("format", value.Format); writer.WriteEndObject(); } writer.WriteEndArray(); } return Convert.ToBase64String(stream.ToArray());
    }

    private static string SerializeUnitNote(TranslationInterchangeProjection projection, TranslationInterchangeSourceUnit source, bool structured)
    {
        TranslationInterchangeUnitMetadata metadata = source.Metadata;
        using var stream = new MemoryStream(); using (var writer = new Utf8JsonWriter(stream))
        { writer.WriteStartObject(); writer.WriteString("executionProfile", projection.ExecutionProfile); writer.WriteNumber("messageGrammarVersion", projection.MessageGrammarVersion); writer.WriteString("interchangeProfile", projection.InterchangeProfile); writer.WriteNumber("interchangeProfileVersion", projection.InterchangeProfileVersion); writer.WriteBoolean("structured", structured); if (metadata.Description is not null) writer.WriteString("description", metadata.Description); if (metadata.Since is not null) writer.WriteString("since", metadata.Since); if (metadata.Deprecated is not null) writer.WriteString("deprecated", metadata.Deprecated); writer.WriteString("tags", SerializeStrings(metadata.Tags)); writer.WriteString("placeholders", SerializePlaceholders(metadata.Placeholders)); writer.WriteString("sourceFingerprint", source.SourceFingerprint); writer.WriteEndObject(); }
        return Convert.ToBase64String(stream.ToArray());
    }

    // An approved entry is stamped with the unit's current source fingerprint; any other keeps the one it records.
    private static string SerializeReviewNote(TranslationInterchangeReviewEntry review, string unitFingerprint)
    {
        string? fingerprint = review.State == TranslationReviewState.Approved ? unitFingerprint : review.SourceFingerprint;
        using var stream = new MemoryStream(); using (var writer = new Utf8JsonWriter(stream)) { writer.WriteStartObject(); writer.WriteString("state", TranslationReviewStates.ToWire(review.State)); if (review.Note is not null) writer.WriteString("note", review.Note); if (fingerprint is not null) writer.WriteString("sourceFingerprint", fingerprint); writer.WriteEndObject(); } return Convert.ToBase64String(stream.ToArray());
    }

    internal static TranslationReviewState ReviewState(string value, string code) =>
        TranslationReviewStates.TryParse(value, out TranslationReviewState state) ? state : throw new TranslationInterchangeException(code, "Unknown review state '" + value + "'.");

    private static string SerializeStrings(IReadOnlyList<string> values)
    {
        using var stream = new MemoryStream(); using (var writer = new Utf8JsonWriter(stream)) { writer.WriteStartArray(); foreach (string value in values.OrderBy(static item => item, StringComparer.Ordinal)) writer.WriteStringValue(value); writer.WriteEndArray(); } return Convert.ToBase64String(stream.ToArray());
    }

    internal static void ValidateReview(TranslationInterchangeReview? review, string catalog) { if (review is null) return; if (!string.Equals(review.CatalogId, catalog, StringComparison.Ordinal) || review.Entries.Count > MaximumReviewEntries) throw new TranslationInterchangeException("REVIEW-INVALID", "The review representation has an invalid catalog or entry count."); var identities = new HashSet<string>(StringComparer.Ordinal); foreach (TranslationInterchangeReviewEntry entry in review.Entries) if (!IsKey(entry.Key) || string.IsNullOrEmpty(entry.Locale) || !Enum.IsDefined(entry.State) || entry.Note?.Length > 16_384 || entry.SourceFingerprint?.Length > 256 || !identities.Add(entry.Key + "\0" + entry.Locale)) throw new TranslationInterchangeException("REVIEW-INVALID", "The review representation contains an invalid or duplicate entry."); }
    private static void EnsureExportBounds(TranslationInterchangeProjection projection, TranslationInterchangeLocale locale, TranslationInterchangeReview? review) { if (projection.CanonicalUnits.Count > MaximumUnits) throw new TranslationInterchangeException("XLIFF21-LIMIT", "The catalog has too many resources for XLIFF export."); long bytes = 512; foreach (TranslationInterchangeSourceUnit source in projection.CanonicalUnits) { bytes += ExpandedBytes(source.Key) + ExpandedBytes(source.Text) * 3 + MetadataBytes(source.Metadata) + 1024; if (bytes > MaximumXliffBytes) throw new TranslationInterchangeException("XLIFF21-LIMIT", "The XLIFF export exceeds the byte limit."); } foreach (TranslationInterchangeTargetUnit target in locale.DirectResources) { bytes += ExpandedBytes(target.Text) * 3; if (bytes > MaximumXliffBytes) throw new TranslationInterchangeException("XLIFF21-LIMIT", "The XLIFF export exceeds the byte limit."); } if (review is not null) foreach (TranslationInterchangeReviewEntry entry in review.Entries) { bytes += ExpandedBytes(entry.Note) + ExpandedBytes(entry.SourceFingerprint) + 256; if (bytes > MaximumXliffBytes) throw new TranslationInterchangeException("XLIFF21-LIMIT", "The XLIFF export exceeds the byte limit."); } }
    private static long MetadataBytes(TranslationInterchangeUnitMetadata value) { long bytes = ExpandedBytes(value.Description) * 2 + ExpandedBytes(value.Since) + ExpandedBytes(value.Deprecated); foreach (string tag in value.Tags) bytes += ExpandedBytes(tag); foreach (string parameter in value.Parameters) bytes += ExpandedBytes(parameter); foreach (TranslationInterchangePlaceholder placeholder in value.Placeholders) bytes += ExpandedBytes(placeholder.Name) + ExpandedBytes(placeholder.Type) + ExpandedBytes(placeholder.Format) + 32; return checked(bytes * 2 + 512); }
    private static long ExpandedBytes(string? value) => value is null ? 0 : checked((long)Encoding.UTF8.GetByteCount(value) * 6 + 32);
    internal static bool IsKey(string value) => value.Length > 0 && (char.IsAsciiLetter(value[0]) || value[0] == '_') && value.Skip(1).All(static character => char.IsAsciiLetterOrDigit(character) || character == '_');
    internal static void RequireObject(JsonElement value, string code) { if (value.ValueKind != JsonValueKind.Object) throw new TranslationInterchangeException(code, "The JSON value must be an object."); }
    internal static JsonElement Required(JsonElement value, string name, JsonValueKind kind, string code) { if (!value.TryGetProperty(name, out JsonElement item) || item.ValueKind != kind) throw new TranslationInterchangeException(code, "A required JSON member is missing or malformed."); return item; }
    internal static string RequiredString(JsonElement value, string name, string code) => Required(value, name, JsonValueKind.String, code).GetString()!;
    internal static bool RequiredBoolean(JsonElement value, string name, string code) { if (!value.TryGetProperty(name, out JsonElement item) || item.ValueKind is not JsonValueKind.True and not JsonValueKind.False) throw new TranslationInterchangeException(code, "A required JSON member is missing or malformed."); return item.GetBoolean(); }
    internal static string? OptionalString(JsonElement value, string name, string code) { if (!value.TryGetProperty(name, out JsonElement item)) return null; if (item.ValueKind != JsonValueKind.String) throw new TranslationInterchangeException(code, "An optional JSON member is malformed."); return item.GetString(); }
    internal static void RequireExact(JsonElement value, params string[] names) { RequireAllowed(value, names); foreach (string name in names) if (!value.TryGetProperty(name, out _)) throw new TranslationInterchangeException("REVIEW-MEMBER", "A required JSON member is missing."); }
    internal static void RequireAllowed(JsonElement value, params string[] names) { var allowed = new HashSet<string>(names, StringComparer.Ordinal); var seen = new HashSet<string>(StringComparer.Ordinal); foreach (System.Text.Json.JsonProperty property in value.EnumerateObject()) if (!seen.Add(property.Name) || !allowed.Contains(property.Name)) throw new TranslationInterchangeException("REVIEW-MEMBER", "The review representation contains an unknown or duplicate member."); }
}

/// <summary>One canonical XLIFF export document.</summary>
public sealed class TranslationXliffDocument
{
    internal TranslationXliffDocument(string catalogId, string sourceLocale, string targetLocale, byte[] bytes) { CatalogId = catalogId; SourceLocale = sourceLocale; TargetLocale = targetLocale; Bytes = bytes; }
    public string CatalogId { get; }
    public string SourceLocale { get; }
    public string TargetLocale { get; }
    /// <summary>The UTF-8 XLIFF document. Read-only; copy it with <c>ToArray()</c> to change it.</summary>
    public ReadOnlyMemory<byte> Bytes { get; }
}

public sealed class TranslationXliffExportResult
{
    internal TranslationXliffExportResult(IReadOnlyList<TranslationXliffDocument> documents, TranslationInterchangeReport report) { Documents = documents; Report = report; }
    public IReadOnlyList<TranslationXliffDocument> Documents { get; }
    public TranslationInterchangeReport Report { get; }
}

public sealed class TranslationXliffImportResult
{
    internal TranslationXliffImportResult(string catalogId, string sourceLocale, string targetLocale, IReadOnlyList<TranslationMf2Document> messages, TranslationInterchangeReview review, TranslationInterchangeReport report, IReadOnlyList<TranslationXliffUnitResult> units, int profileVersion) { CatalogId = catalogId; SourceLocale = sourceLocale; TargetLocale = targetLocale; Messages = messages; Review = review; Report = report; Units = units; ProfileVersion = profileVersion; }
    public string CatalogId { get; }
    public string SourceLocale { get; }
    public string TargetLocale { get; }
    /// <summary>The MF2 message of every applied unit.</summary>
    public IReadOnlyList<TranslationMf2Document> Messages { get; }
    /// <summary>The review state of the applied units that state one: through a Runic review note, an XLIFF segment state, or a changed source.</summary>
    public TranslationInterchangeReview Review { get; }
    /// <summary>Notices that do not refuse a unit, such as an ignored translator note or a demoted approval.</summary>
    public TranslationInterchangeReport Report { get; }
    /// <summary>The outcome of every unit in document order: applied, skipped or refused, with its reason.</summary>
    public IReadOnlyList<TranslationXliffUnitResult> Units { get; }
    /// <summary>The Runic profile version that the document's units record, or <see cref="TranslationInterchange.XliffProfileVersion"/> when they record none.</summary>
    public int ProfileVersion { get; }
}

/// <summary>Options of an XLIFF import against the live catalog.</summary>
public sealed class TranslationXliffImportOptions
{
    /// <summary>What happens to a unit whose source changed since export. The default applies it as needs-review.</summary>
    public TranslationSourceChangePolicy SourceChangePolicy { get; init; } = TranslationSourceChangePolicy.NeedsReview;
}

/// <summary>How an import treats a translation of a source that changed after export.</summary>
public enum TranslationSourceChangePolicy
{
    /// <summary>Apply the translation and mark it needs-review. The default.</summary>
    NeedsReview = 0,
    /// <summary>Refuse the unit (<c>XLIFF21-SOURCE-CHANGED</c>); the rest of the document is still applied.</summary>
    Refuse = 1,
}

/// <summary>What an import did with one XLIFF unit.</summary>
public enum TranslationXliffUnitOutcome
{
    /// <summary>The translation is part of <see cref="TranslationXliffImportResult.Messages"/>.</summary>
    Applied = 0,
    /// <summary>Nothing to import: untranslated, empty, or a message the profile does not exchange.</summary>
    Skipped = 1,
    /// <summary>The unit is invalid, for example an unknown placeholder; the rest of the document is still applied.</summary>
    Refused = 2,
}

/// <summary>The outcome of one XLIFF unit.</summary>
public sealed class TranslationXliffUnitResult
{
    internal TranslationXliffUnitResult(string key, TranslationXliffUnitOutcome outcome, string? code, string? message, TranslationReviewState? state, string? sourceFingerprint, bool sourceChanged)
    { Key = key; Outcome = outcome; Code = code; Message = message; State = state; SourceFingerprint = sourceFingerprint; SourceChanged = sourceChanged; }
    /// <summary>The unit identifier, the message key.</summary>
    public string Key { get; }
    public TranslationXliffUnitOutcome Outcome { get; }
    /// <summary>Why the unit was skipped or refused, such as <c>XLIFF21-UNTRANSLATED</c>; <see langword="null"/> when applied.</summary>
    public string? Code { get; }
    public string? Message { get; }
    /// <summary>The review state of an applied unit; <c>translated</c> when the document states none.</summary>
    public TranslationReviewState? State { get; }
    /// <summary>The per-unit fingerprint of the source the translation was made for.</summary>
    public string? SourceFingerprint { get; }
    /// <summary>Whether the live source changed since the document was exported.</summary>
    public bool SourceChanged { get; }
}

/// <summary>One imported MF2 message addressed by its generated-call identifier.</summary>
public sealed class TranslationMf2Document
{
    internal TranslationMf2Document(string messageId, byte[] bytes) { MessageId = messageId; Bytes = bytes; }
    public string MessageId { get; }
    /// <summary>The UTF-8 MF2 message source. Read-only; copy it with <c>ToArray()</c> to change it.</summary>
    public ReadOnlyMemory<byte> Bytes { get; }
}

public sealed class TranslationInterchangeReport
{
    private readonly IReadOnlyList<TranslationInterchangeLoss> _losses;
    internal TranslationInterchangeReport(IEnumerable<TranslationInterchangeLoss> losses) => _losses = losses.OrderBy(static loss => loss.Location, StringComparer.Ordinal).ThenBy(static loss => loss.Code, StringComparer.Ordinal).ThenBy(static loss => loss.Message, StringComparer.Ordinal).ToArray();
    public IReadOnlyList<TranslationInterchangeLoss> Losses => _losses;
    public bool IsLossless => _losses.All(static loss => !loss.SemanticLoss);
}

public sealed class TranslationInterchangeLoss
{
    internal TranslationInterchangeLoss(string code, string location, string message, bool semanticLoss) { Code = code; Location = location; Message = message; SemanticLoss = semanticLoss; }
    public string Code { get; }
    public string Location { get; }
    public string Message { get; }
    public bool SemanticLoss { get; }
}

/// <summary>Git-friendly review state exchanged independently from translator content.</summary>
public sealed class TranslationInterchangeReview
{
    public TranslationInterchangeReview(string catalogId, IEnumerable<TranslationInterchangeReviewEntry> entries) { ArgumentException.ThrowIfNullOrWhiteSpace(catalogId); ArgumentNullException.ThrowIfNull(entries); CatalogId = catalogId; Entries = entries.ToArray(); }
    public string CatalogId { get; }
    public IReadOnlyList<TranslationInterchangeReviewEntry> Entries { get; }
}

/// <summary>The review state of one translation. The set is closed: it matches the four states of the
/// <c>runic.translations.interchange-review/2</c> file (<c>draft</c>, <c>translated</c>, <c>needs-review</c>,
/// <c>approved</c>) and the XLIFF segment states they map to.</summary>
public enum TranslationReviewState
{
    /// <summary><c>draft</c>: not translated or not ready (XLIFF <c>initial</c>). The default.</summary>
    Draft = 0,
    /// <summary><c>translated</c>: translated, not reviewed (XLIFF <c>translated</c>).</summary>
    Translated = 1,
    /// <summary><c>needs-review</c>: waiting for review (XLIFF <c>translated</c> with <c>subState="runic:needs-review"</c>).</summary>
    NeedsReview = 2,
    /// <summary><c>approved</c>: reviewed against the current source fingerprint (XLIFF <c>final</c>; <c>reviewed</c> is read as approved too).</summary>
    Approved = 3,
}

/// <summary>The wire spelling of <see cref="TranslationReviewState"/>.</summary>
internal static class TranslationReviewStates
{
    internal static string ToWire(TranslationReviewState state) => state switch
    {
        TranslationReviewState.Draft => "draft",
        TranslationReviewState.Translated => "translated",
        TranslationReviewState.NeedsReview => "needs-review",
        TranslationReviewState.Approved => "approved",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown review state."),
    };

    internal static bool TryParse(string? value, out TranslationReviewState state)
    {
        switch (value)
        {
            case "draft": state = TranslationReviewState.Draft; return true;
            case "translated": state = TranslationReviewState.Translated; return true;
            case "needs-review": state = TranslationReviewState.NeedsReview; return true;
            case "approved": state = TranslationReviewState.Approved; return true;
            default: state = default; return false;
        }
    }
}

public sealed class TranslationInterchangeReviewEntry
{
    public TranslationInterchangeReviewEntry(string key, string locale, TranslationReviewState state, string? note = null, string? sourceFingerprint = null)
    {
        if (!Enum.IsDefined(state)) throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown review state.");
        Key = key; Locale = locale; State = state; Note = note; SourceFingerprint = sourceFingerprint;
    }
    public string Key { get; }
    public string Locale { get; }
    public TranslationReviewState State { get; }
    public string? Note { get; }
    /// <summary>The per-unit fingerprint of the source this translation was made or approved for.</summary>
    public string? SourceFingerprint { get; }
}

/// <summary>Stable rejection ID for the closed interchange profile.</summary>
public sealed class TranslationInterchangeException : Exception
{
    internal TranslationInterchangeException(string code, string message, Exception? innerException = null) : base(message, innerException) => Code = code;
    public string Code { get; }
}
