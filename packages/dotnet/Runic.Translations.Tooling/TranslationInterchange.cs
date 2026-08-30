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
/// This facade exchanges the compiler's supported execution model. It is not a
/// general XLIFF processor and it does not parse MessageFormat syntax. Rich
/// messages are exported for translator visibility, but are reported as lossy
/// and are refused on import rather than flattened into a different message.
/// </remarks>
public static class TranslationInterchange
{
    private const string XliffNamespace = "urn:oasis:names:tc:xliff:document:2.0";
    private const int MaximumXliffBytes = 8 * 1024 * 1024;
    private const int MaximumUnits = 50_000;
    private const int MaximumTextLength = 65_536;
    private const int MaximumReviewEntries = 50_000;
    private const int MaximumReviewBytes = 8 * 1024 * 1024;

    /// <summary>Exports one compiler-valid catalog as one XLIFF document per non-default locale.</summary>
    public static TranslationXliffExportResult ExportXliff21(
        TranslationCompilation compilation,
        TranslationInterchangeReview? review = null)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        if (!compilation.Success) throw new TranslationInterchangeException("XLIFF21-COMPILATION", "XLIFF export requires a successful compiler result.");
        if (compilation.Catalogs.Count != 1) throw new TranslationInterchangeException("XLIFF21-CATALOG", "XLIFF export requires exactly one compiled catalog.");
        CompiledTextCatalog catalog = compilation.Catalogs[0];
        ValidateExportReview(review, catalog);
        var documents = new List<TranslationXliffDocument>();
        var losses = new List<TranslationInterchangeLoss>();
        if (catalog.Layers.Count != 1)
            throw new TranslationInterchangeException("XLIFF21-LAYERS", "XLIFF export requires exactly one source layer so its identity can be preserved.");
        foreach (CompiledTextLocale locale in catalog.Locales.OrderBy(static item => item.Tag, StringComparer.Ordinal))
        {
            if (string.Equals(locale.Tag, catalog.DefaultLocale, StringComparison.Ordinal)) continue;
            byte[] bytes = Render(catalog, catalog.Layers[0].Name, locale, review, losses);
            documents.Add(new TranslationXliffDocument(catalog.Id, catalog.DefaultLocale, locale.Tag, bytes));
        }
        return new TranslationXliffExportResult(documents, new TranslationInterchangeReport(losses));
    }

    /// <summary>Imports the closed Runic XLIFF 2.1 profile into a canonical v2 resource document.</summary>
    public static TranslationXliffImportResult ImportXliff21(ReadOnlyMemory<byte> source)
    {
        if (source.Length == 0) throw new TranslationInterchangeException("XLIFF21-EMPTY", "An XLIFF document is required.");
        if (source.Length > MaximumXliffBytes) throw new TranslationInterchangeException("XLIFF21-LIMIT", "The XLIFF document exceeds the byte limit.");
        try
        {
            using var stream = new MemoryStream(source.ToArray(), writable: false);
            using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = false,
                IgnoreProcessingInstructions = false, IgnoreWhitespace = false, MaxCharactersInDocument = MaximumXliffBytes,
            });
            return Read(reader);
        }
        catch (TranslationInterchangeException) { throw; }
        catch (XmlException exception) { throw new TranslationInterchangeException("XLIFF21-MALFORMED", "The XLIFF document is malformed.", exception); }
    }

    /// <summary>Renders review state as compact, stable JSON suitable for source control.</summary>
    public static byte[] ExportReviewJson(TranslationInterchangeReview review)
    {
        ArgumentNullException.ThrowIfNull(review);
        ValidateReview(review, review.CatalogId);
        long reviewBytes = 128;
        foreach (TranslationInterchangeReviewEntry entry in review.Entries) { reviewBytes += Encoding.UTF8.GetByteCount(entry.Key) + Encoding.UTF8.GetByteCount(entry.Locale) + Encoding.UTF8.GetByteCount(entry.State) + (entry.Note is null ? 0 : Encoding.UTF8.GetByteCount(entry.Note)) + (entry.SourceFingerprint is null ? 0 : Encoding.UTF8.GetByteCount(entry.SourceFingerprint)); if (reviewBytes > MaximumReviewBytes) throw new TranslationInterchangeException("REVIEW-LIMIT", "The review representation exceeds the byte limit."); }
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject(); writer.WriteString("$schema", "runic.translations.interchange-review/1"); writer.WriteString("catalog", review.CatalogId);
            writer.WritePropertyName("entries"); writer.WriteStartArray();
            foreach (TranslationInterchangeReviewEntry entry in review.Entries.OrderBy(static item => item.Key, StringComparer.Ordinal).ThenBy(static item => item.Locale, StringComparer.Ordinal))
            {
                writer.WriteStartObject(); writer.WriteString("key", entry.Key); writer.WriteString("locale", entry.Locale); writer.WriteString("state", entry.State);
                if (entry.Note is not null) writer.WriteString("note", entry.Note);
                if (entry.SourceFingerprint is not null) writer.WriteString("sourceFingerprint", entry.SourceFingerprint);
                writer.WriteEndObject();
            }
            writer.WriteEndArray(); writer.WriteEndObject();
        }
        if (stream.Length > MaximumReviewBytes) throw new TranslationInterchangeException("REVIEW-LIMIT", "The review representation exceeds the byte limit.");
        return stream.ToArray();
    }

    /// <summary>Reads the closed portable review representation.</summary>
    public static TranslationInterchangeReview ImportReviewJson(ReadOnlyMemory<byte> source)
    {
        if (source.Length == 0) throw new TranslationInterchangeException("REVIEW-EMPTY", "A review representation is required.");
        if (source.Length > MaximumReviewBytes) throw new TranslationInterchangeException("REVIEW-LIMIT", "The review representation exceeds the byte limit.");
        try
        {
            using JsonDocument document = JsonDocument.Parse(source, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 16 });
            JsonElement root = document.RootElement;
            RequireObject(root, "REVIEW-ROOT"); RequireExact(root, "$schema", "catalog", "entries");
            if (RequiredString(root, "$schema", "REVIEW-ROOT") != "runic.translations.interchange-review/1") throw new TranslationInterchangeException("REVIEW-SCHEMA", "The review representation schema is not supported.");
            string catalog = RequiredString(root, "catalog", "REVIEW-ROOT");
            JsonElement values = Required(root, "entries", JsonValueKind.Array, "REVIEW-ROOT");
            var entries = new List<TranslationInterchangeReviewEntry>();
            foreach (JsonElement value in values.EnumerateArray())
            {
                RequireObject(value, "REVIEW-ENTRY"); RequireAllowed(value, "key", "locale", "state", "note", "sourceFingerprint");
                entries.Add(new TranslationInterchangeReviewEntry(RequiredString(value, "key", "REVIEW-ENTRY"), RequiredString(value, "locale", "REVIEW-ENTRY"), RequiredString(value, "state", "REVIEW-ENTRY"), OptionalString(value, "note", "REVIEW-ENTRY"), OptionalString(value, "sourceFingerprint", "REVIEW-ENTRY")));
            }
            var review = new TranslationInterchangeReview(catalog, entries); ValidateReview(review, catalog); return review;
        }
        catch (TranslationInterchangeException) { throw; }
        catch (JsonException exception) { throw new TranslationInterchangeException("REVIEW-MALFORMED", "The review representation is malformed.", exception); }
    }

    private static byte[] Render(CompiledTextCatalog catalog, string layer, CompiledTextLocale targetLocale, TranslationInterchangeReview? review, List<TranslationInterchangeLoss> losses)
    {
        EnsureExportBounds(catalog, targetLocale, review);
        var direct = targetLocale.DirectResources.ToDictionary(static resource => resource.Key, StringComparer.Ordinal);
        var reviewByKey = review?.Entries.Where(entry => string.Equals(entry.Locale, targetLocale.Tag, StringComparison.Ordinal)).ToDictionary(static entry => entry.Key, StringComparer.Ordinal) ?? new Dictionary<string, TranslationInterchangeReviewEntry>(StringComparer.Ordinal);
        using var stream = new MemoryStream();
        using (XmlWriter writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false, NewLineHandling = NewLineHandling.None, OmitXmlDeclaration = false }))
        {
            writer.WriteStartDocument(); writer.WriteStartElement("xliff", XliffNamespace); writer.WriteAttributeString("version", "2.1"); writer.WriteAttributeString("srcLang", catalog.DefaultLocale); writer.WriteAttributeString("trgLang", targetLocale.Tag);
            writer.WriteStartElement("file", XliffNamespace); writer.WriteAttributeString("id", catalog.Id); writer.WriteAttributeString("original", catalog.Id + "." + targetLocale.Tag + "." + layer + ".xliff");
            foreach (CompiledTranslation source in catalog.CanonicalResources)
            {
                direct.TryGetValue(source.Key, out CompiledTranslation? target);
                bool structured = !source.IsTextInterchangeLossless || target?.IsTextInterchangeLossless == false;
                if (structured) losses.Add(new("XLIFF21-STRUCTURED-MESSAGE", "/" + source.Key, "Selectors, formatting, or markup are not losslessly representable by the closed XLIFF text profile.", true));
                writer.WriteStartElement("unit", XliffNamespace); writer.WriteAttributeString("id", source.Key);
                WriteNotes(writer, catalog.SchemaVersion, layer, source, target, structured, reviewByKey.TryGetValue(source.Key, out TranslationInterchangeReviewEntry? entry) ? entry : null);
                writer.WriteStartElement("segment", XliffNamespace); writer.WriteAttributeString("id", "1"); if (entry is not null) writer.WriteAttributeString("state", SegmentState(entry.State)); writer.WriteStartElement("source", XliffNamespace); writer.WriteString(source.Pattern); writer.WriteEndElement();
                if (target is not null) { writer.WriteStartElement("target", XliffNamespace); writer.WriteString(target.Pattern); writer.WriteEndElement(); }
                writer.WriteEndElement(); writer.WriteEndElement();
            }
            writer.WriteEndElement(); writer.WriteEndElement(); writer.WriteEndDocument();
        }
        if (stream.Length > MaximumXliffBytes) throw new TranslationInterchangeException("XLIFF21-LIMIT", "The XLIFF export exceeds the byte limit.");
        return stream.ToArray();
    }

    private static void WriteNotes(XmlWriter writer, int schemaVersion, string layer, CompiledTranslation source, CompiledTranslation? target, bool structured, TranslationInterchangeReviewEntry? review)
    {
        writer.WriteStartElement("notes", XliffNamespace);
        writer.WriteStartElement("note", XliffNamespace); writer.WriteAttributeString("category", "runic:unit"); writer.WriteString(SerializeUnitNote(schemaVersion, layer, source, target, structured)); writer.WriteEndElement();
        if (review is not null) { writer.WriteStartElement("note", XliffNamespace); writer.WriteAttributeString("category", "runic:review"); writer.WriteAttributeString("appliesTo", "target"); writer.WriteString(SerializeReviewNote(review)); writer.WriteEndElement(); }
        writer.WriteEndElement();
    }

    private static TranslationXliffImportResult Read(XmlReader reader)
    {
        reader.MoveToContent(); RequireElement(reader, "xliff", "XLIFF21-ROOT"); RequireAttributes(reader, "XLIFF21-ROOT", ["version", "srcLang", "trgLang"], []);
        if (reader.GetAttribute("version") != "2.1") throw new TranslationInterchangeException("XLIFF21-VERSION", "Only XLIFF version 2.1 is supported.");
        string sourceLocale = RequiredAttribute(reader, "srcLang", "XLIFF21-ROOT"); string targetLocale = RequiredAttribute(reader, "trgLang", "XLIFF21-ROOT");
        if (!reader.Read() || reader.MoveToContent() != XmlNodeType.Element) throw new TranslationInterchangeException("XLIFF21-FILE", "The XLIFF document must contain one file.");
        RequireElement(reader, "file", "XLIFF21-FILE"); RequireAttributes(reader, "XLIFF21-FILE", ["id"], ["original"]); string catalog = RequiredAttribute(reader, "id", "XLIFF21-FILE");
        var resources = new SortedDictionary<string, ImportedUnit>(StringComparer.Ordinal); var reviews = new List<TranslationInterchangeReviewEntry>();
        if (reader.IsEmptyElement) throw new TranslationInterchangeException("XLIFF21-FILE", "The XLIFF file cannot be empty.");
        reader.Read();
        while (reader.MoveToContent() == XmlNodeType.Element)
        {
            RequireElement(reader, "unit", "XLIFF21-UNIT"); RequireAttributes(reader, "XLIFF21-UNIT", ["id"], []); string key = RequiredAttribute(reader, "id", "XLIFF21-UNIT");
            if (!IsKey(key) || !resources.TryAdd(key, ReadUnit(reader, key, targetLocale, reviews))) throw new TranslationInterchangeException("XLIFF21-DUPLICATE-UNIT", "XLIFF unit identifiers must be unique Runic keys.");
            if (resources.Count > MaximumUnits) throw new TranslationInterchangeException("XLIFF21-LIMIT", "The XLIFF document has too many units.");
        }
        if (reader.NodeType != XmlNodeType.EndElement || reader.LocalName != "file") throw new TranslationInterchangeException("XLIFF21-FILE", "Unexpected content in XLIFF file."); reader.Read();
        if (reader.MoveToContent() != XmlNodeType.EndElement || reader.LocalName != "xliff") throw new TranslationInterchangeException("XLIFF21-ROOT", "The XLIFF document has trailing content."); reader.Read();
        while (reader.Read()) if (reader.NodeType is not XmlNodeType.Whitespace and not XmlNodeType.None) throw new TranslationInterchangeException("XLIFF21-ROOT", "The XLIFF document has trailing content.");
        if (resources.Count == 0) throw new TranslationInterchangeException("XLIFF21-FILE", "The XLIFF file cannot be empty.");
        string[] layers = resources.Values.Select(static value => value.Layer).Distinct(StringComparer.Ordinal).ToArray();
        int[] schemaVersions = resources.Values.Select(static value => value.SchemaVersion).Distinct().ToArray();
        if (layers.Length != 1) throw new TranslationInterchangeException("XLIFF21-LAYER", "Every imported XLIFF unit must declare the same source layer.");
        if (schemaVersions.Length != 1 || schemaVersions[0] is not (1 or 2)) throw new TranslationInterchangeException("XLIFF21-SCHEMA", "Every imported XLIFF unit must declare the same supported source schema version.");
        byte[] document = RenderResourceDocument(catalog, targetLocale, resources, layers[0], false, 2); string fingerprint = ValidateImportedContract(catalog, sourceLocale, targetLocale, layers[0], schemaVersions[0], resources, document); var review = new TranslationInterchangeReview(catalog, reviews); ValidateReview(review, catalog);
        foreach (TranslationInterchangeReviewEntry entry in review.Entries) if (entry.State == "approved" && !string.Equals(entry.SourceFingerprint, fingerprint, StringComparison.Ordinal)) throw new TranslationInterchangeException("REVIEW-FINGERPRINT", "Approved review data does not match the reconstructed source fingerprint.");
        return new TranslationXliffImportResult(catalog, sourceLocale, targetLocale, document, review, new TranslationInterchangeReport([]));
    }

    private static ImportedUnit ReadUnit(XmlReader reader, string key, string locale, List<TranslationInterchangeReviewEntry> reviews)
    {
        if (reader.IsEmptyElement) throw new TranslationInterchangeException("XLIFF21-SEGMENT", "A unit must contain one segment."); reader.Read();
        if (reader.MoveToContent() != XmlNodeType.Element || reader.LocalName != "notes") throw new TranslationInterchangeException("XLIFF21-METADATA", "A Runic unit must contain its metadata notes.");
        int reviewStart = reviews.Count;
        UnitNote metadata = ReadNotes(reader, key, locale, reviews);
        if (metadata.Structured) throw new TranslationInterchangeException("XLIFF21-STRUCTURED-IMPORT", "A structured Runic message cannot be imported from the XLIFF text profile.");
        if (reader.MoveToContent() != XmlNodeType.Element) throw new TranslationInterchangeException("XLIFF21-SEGMENT", "A unit must contain one segment.");
        RequireElement(reader, "segment", "XLIFF21-SEGMENT"); RequireAttributes(reader, "XLIFF21-SEGMENT", ["id"], ["state"]); if (reader.GetAttribute("id") != "1") throw new TranslationInterchangeException("XLIFF21-SEGMENT", "Only segment id '1' is supported."); string segmentState = reader.GetAttribute("state") ?? "initial"; if (segmentState is not ("initial" or "translated" or "reviewed" or "final")) throw new TranslationInterchangeException("XLIFF21-STATE", "The XLIFF segment state is invalid."); TranslationInterchangeReviewEntry? review = reviews.Count == reviewStart ? null : reviews[^1]; if ((review is null && segmentState != "initial") || (review is not null && SegmentState(review.State) != segmentState)) throw new TranslationInterchangeException("XLIFF21-STATE", "The XLIFF segment state and Runic review metadata disagree.");
        if (reader.IsEmptyElement) throw new TranslationInterchangeException("XLIFF21-SEGMENT", "A segment cannot be empty."); reader.Read(); if (reader.MoveToContent() != XmlNodeType.Element) throw new TranslationInterchangeException("XLIFF21-SOURCE", "A segment must contain source text.");
        RequireElement(reader, "source", "XLIFF21-SOURCE"); RequireAttributes(reader, "XLIFF21-SOURCE", [], []); string source = ReadText(reader, "source");
        if (reader.MoveToContent() != XmlNodeType.Element) throw new TranslationInterchangeException("XLIFF21-TARGET", "A segment must contain target text."); RequireElement(reader, "target", "XLIFF21-TARGET"); RequireAttributes(reader, "XLIFF21-TARGET", [], []); string target = ReadText(reader, "target");
        if (reader.MoveToContent() != XmlNodeType.EndElement || reader.LocalName != "segment") throw new TranslationInterchangeException("XLIFF21-SEGMENT", "A segment contains unsupported content."); reader.Read();
        if (reader.MoveToContent() != XmlNodeType.EndElement || reader.LocalName != "unit") throw new TranslationInterchangeException("XLIFF21-UNIT", "A unit contains unsupported content."); reader.Read();
        return new ImportedUnit(source, target, metadata.SchemaVersion, metadata.Layer, metadata.Description, metadata.Since, metadata.Deprecated, metadata.Tags, metadata.Placeholders);
    }

    private static string ReadText(XmlReader reader, string name)
    {
        if (reader.IsEmptyElement) { reader.Read(); return string.Empty; }
        reader.Read(); var text = new StringBuilder();
        while (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace or XmlNodeType.Whitespace) { text.Append(reader.Value); reader.Read(); }
        if (reader.NodeType != XmlNodeType.EndElement || reader.LocalName != name || text.Length > MaximumTextLength) throw new TranslationInterchangeException("XLIFF21-TEXT", "XLIFF text must be plain bounded text."); reader.Read(); return text.ToString();
    }

    private static byte[] RenderResourceDocument(string catalog, string locale, SortedDictionary<string, ImportedUnit> units, string layer, bool source, int schemaVersion)
    {
        var resources = new ResourceGroup();
        foreach ((string key, ImportedUnit unit) in units)
        {
            ResourceGroup group = resources;
            string[] parts = key.Split('.');
            for (int index = 0; index < parts.Length - 1; index++)
            {
                if (group.Leaves.ContainsKey(parts[index])) throw new TranslationInterchangeException("XLIFF21-KEY-PREFIX", "XLIFF unit keys cannot collide with a resource group prefix.");
                if (!group.Groups.TryGetValue(parts[index], out ResourceGroup? child)) { child = new ResourceGroup(); group.Groups.Add(parts[index], child); }
                group = child;
            }
            if (group.Groups.ContainsKey(parts[^1])) throw new TranslationInterchangeException("XLIFF21-KEY-PREFIX", "XLIFF unit keys cannot collide with a resource group prefix.");
            group.Leaves.Add(parts[^1], unit);
        }
        using var stream = new MemoryStream(); using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject(); writer.WriteNumber("schemaVersion", schemaVersion); writer.WriteString("catalog", catalog); writer.WriteString("locale", locale); writer.WriteString("layer", layer); writer.WritePropertyName("resources"); WriteGroup(writer, resources, source); writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static void WriteGroup(Utf8JsonWriter writer, ResourceGroup group, bool source)
    {
        writer.WriteStartObject();
        foreach (string name in group.Groups.Keys.OrderBy(static value => value, StringComparer.Ordinal)) { writer.WritePropertyName(name); WriteGroup(writer, group.Groups[name], source); }
        foreach (string name in group.Leaves.Keys.OrderBy(static value => value, StringComparer.Ordinal)) { writer.WritePropertyName(name); WriteLeaf(writer, group.Leaves[name], source); }
        writer.WriteEndObject();
    }

    private static void WriteLeaf(Utf8JsonWriter writer, ImportedUnit unit, bool source)
    {
        writer.WriteStartObject(); writer.WriteString("$value", source ? unit.SourcePattern : unit.Pattern); if (unit.Description is not null) writer.WriteString("$description", unit.Description); if (unit.Since is not null) writer.WriteString("$since", unit.Since); if (unit.Deprecated is not null) writer.WriteString("$deprecated", unit.Deprecated); if (unit.Tags.Length > 0) { writer.WritePropertyName("$tags"); writer.WriteStartArray(); foreach (string tag in unit.Tags.OrderBy(static tag => tag, StringComparer.Ordinal)) writer.WriteStringValue(tag); writer.WriteEndArray(); } if (unit.Placeholders.Count > 0) { writer.WritePropertyName("$placeholders"); writer.WriteStartObject(); foreach (ImportedPlaceholder placeholder in unit.Placeholders.OrderBy(static value => value.Name, StringComparer.Ordinal)) { writer.WritePropertyName(placeholder.Name); writer.WriteStartObject(); writer.WriteString("type", placeholder.Type); writer.WriteString("format", placeholder.Format); writer.WriteEndObject(); } writer.WriteEndObject(); } writer.WriteEndObject();
    }

    private static string ValidateImportedContract(string catalog, string sourceLocale, string targetLocale, string layer, int schemaVersion, SortedDictionary<string, ImportedUnit> units, byte[] targetDocument)
    {
        byte[] sourceDocument = RenderResourceDocument(catalog, sourceLocale, units, layer, true, schemaVersion);
        byte[] validationTarget = RenderResourceDocument(catalog, targetLocale, units, layer, false, schemaVersion);
        string manifest = "{\"schemaVersion\":" + schemaVersion + ",\"catalog\":" + Quote(catalog) + ",\"code\":{\"namespace\":\"Runic.Interchange\",\"className\":\"Text\"},\"defaultLocale\":" + Quote(sourceLocale) + ",\"locales\":[{\"tag\":" + Quote(sourceLocale) + "},{\"tag\":" + Quote(targetLocale) + ",\"fallback\":" + Quote(sourceLocale) + "}],\"layers\":[{\"name\":" + Quote(layer) + ",\"priority\":0}]}";
        TranslationCompilation compilation = TranslationCompiler.Compile([new TranslationSource("interchange.catalog.json", Encoding.UTF8.GetBytes(manifest))], [new TranslationSource("interchange.source.json", sourceDocument), new TranslationSource("interchange.target.json", validationTarget)]);
        if (!compilation.Success || compilation.Catalogs.Count != 1) throw new TranslationInterchangeException("XLIFF21-CONTRACT", "XLIFF target text or placeholder metadata does not satisfy the compiler contract.");
        return compilation.Catalogs[0].Fingerprint;
    }

    private static string SerializePlaceholders(IReadOnlyList<CompiledTextPlaceholder> values)
    {
        using var stream = new MemoryStream(); using (var writer = new Utf8JsonWriter(stream)) { writer.WriteStartArray(); foreach (CompiledTextPlaceholder value in values.OrderBy(static item => item.Name, StringComparer.Ordinal)) { writer.WriteStartObject(); writer.WriteString("name", value.Name); writer.WriteString("type", TypeName(value.Type)); writer.WriteString("format", value.Format); writer.WriteEndObject(); } writer.WriteEndArray(); } return Convert.ToBase64String(stream.ToArray());
    }

    private static string SerializeUnitNote(int schemaVersion, string layer, CompiledTranslation source, CompiledTranslation? target, bool structured)
    {
        using var stream = new MemoryStream(); using (var writer = new Utf8JsonWriter(stream))
        { string? description = target?.Description ?? source.Description; string? since = target?.Since ?? source.Since; string? deprecated = target?.DeprecatedReason ?? source.DeprecatedReason; writer.WriteStartObject(); writer.WriteNumber("schemaVersion", schemaVersion); writer.WriteString("layer", layer); writer.WriteBoolean("structured", structured); if (description is not null) writer.WriteString("description", description); if (since is not null) writer.WriteString("since", since); if (deprecated is not null) writer.WriteString("deprecated", deprecated); writer.WriteString("tags", SerializeStrings(target?.Tags ?? source.Tags)); writer.WriteString("placeholders", SerializePlaceholders(source.Placeholders)); writer.WriteEndObject(); }
        return Convert.ToBase64String(stream.ToArray());
    }

    private static string SerializeReviewNote(TranslationInterchangeReviewEntry review)
    {
        using var stream = new MemoryStream(); using (var writer = new Utf8JsonWriter(stream)) { writer.WriteStartObject(); writer.WriteString("state", review.State); if (review.Note is not null) writer.WriteString("note", review.Note); if (review.SourceFingerprint is not null) writer.WriteString("sourceFingerprint", review.SourceFingerprint); writer.WriteEndObject(); } return Convert.ToBase64String(stream.ToArray());
    }

    private static UnitNote ReadNotes(XmlReader reader, string key, string locale, List<TranslationInterchangeReviewEntry> reviews)
    {
        RequireElement(reader, "notes", "XLIFF21-METADATA"); RequireAttributes(reader, "XLIFF21-METADATA", [], []); if (reader.IsEmptyElement) throw new TranslationInterchangeException("XLIFF21-METADATA", "Runic unit metadata is required."); reader.Read(); UnitNote? metadata = null; bool reviewSeen = false;
        while (reader.MoveToContent() == XmlNodeType.Element)
        {
            RequireElement(reader, "note", "XLIFF21-METADATA"); RequireAttributes(reader, "XLIFF21-METADATA", ["category"], ["appliesTo"]); string category = RequiredAttribute(reader, "category", "XLIFF21-METADATA"); string? appliesTo = reader.GetAttribute("appliesTo"); string value = ReadText(reader, "note");
            if (category == "runic:unit" && appliesTo is null) { if (metadata is not null) throw new TranslationInterchangeException("XLIFF21-METADATA", "Duplicate Runic unit metadata."); metadata = ParseUnitNote(value); }
            else if (category == "runic:review" && appliesTo == "target") { if (reviewSeen) throw new TranslationInterchangeException("XLIFF21-REVIEW", "Duplicate Runic review metadata."); reviews.Add(ParseReviewNote(value, key, locale)); reviewSeen = true; }
            else throw new TranslationInterchangeException("XLIFF21-METADATA", "The XLIFF profile contains unsupported notes.");
        }
        if (reader.NodeType != XmlNodeType.EndElement || reader.LocalName != "notes" || metadata is null) throw new TranslationInterchangeException("XLIFF21-METADATA", "Runic unit metadata is required."); reader.Read(); return metadata;
    }

    private static UnitNote ParseUnitNote(string encoded)
    {
        try { using JsonDocument document = JsonDocument.Parse(Convert.FromBase64String(encoded)); JsonElement root = document.RootElement; RequireObject(root, "XLIFF21-METADATA"); RequireAllowed(root, "schemaVersion", "layer", "structured", "description", "since", "deprecated", "tags", "placeholders"); int schemaVersion = Required(root, "schemaVersion", JsonValueKind.Number, "XLIFF21-METADATA").GetInt32(); string layer = RequiredString(root, "layer", "XLIFF21-METADATA"); bool structured = RequiredBoolean(root, "structured", "XLIFF21-METADATA"); return new UnitNote(schemaVersion, layer, structured, OptionalString(root, "description", "XLIFF21-METADATA"), OptionalString(root, "since", "XLIFF21-METADATA"), OptionalString(root, "deprecated", "XLIFF21-METADATA"), ParseStrings(RequiredString(root, "tags", "XLIFF21-METADATA")), ParsePlaceholders(RequiredString(root, "placeholders", "XLIFF21-METADATA"))); } catch (FormatException exception) { throw new TranslationInterchangeException("XLIFF21-METADATA", "Runic unit metadata is invalid.", exception); } catch (JsonException exception) { throw new TranslationInterchangeException("XLIFF21-METADATA", "Runic unit metadata is invalid.", exception); }
    }

    private static TranslationInterchangeReviewEntry ParseReviewNote(string encoded, string key, string locale)
    {
        try { using JsonDocument document = JsonDocument.Parse(Convert.FromBase64String(encoded)); JsonElement root = document.RootElement; RequireObject(root, "XLIFF21-REVIEW"); RequireAllowed(root, "state", "note", "sourceFingerprint"); return new TranslationInterchangeReviewEntry(key, locale, RequiredString(root, "state", "XLIFF21-REVIEW"), OptionalString(root, "note", "XLIFF21-REVIEW"), OptionalString(root, "sourceFingerprint", "XLIFF21-REVIEW")); } catch (FormatException exception) { throw new TranslationInterchangeException("XLIFF21-REVIEW", "Runic review metadata is invalid.", exception); } catch (JsonException exception) { throw new TranslationInterchangeException("XLIFF21-REVIEW", "Runic review metadata is invalid.", exception); }
    }

    private static string SegmentState(string state) => state switch { "draft" => "initial", "translated" => "translated", "needs-review" => "reviewed", "approved" => "final", _ => throw new TranslationInterchangeException("REVIEW-INVALID", "Unknown review state.") };

    private static string SerializeStrings(IReadOnlyList<string> values)
    {
        using var stream = new MemoryStream(); using (var writer = new Utf8JsonWriter(stream)) { writer.WriteStartArray(); foreach (string value in values.OrderBy(static item => item, StringComparer.Ordinal)) writer.WriteStringValue(value); writer.WriteEndArray(); } return Convert.ToBase64String(stream.ToArray());
    }

    private static string[] ParseStrings(string? encoded)
    {
        if (encoded is null) return []; try { byte[] bytes = Convert.FromBase64String(encoded); using JsonDocument document = JsonDocument.Parse(bytes); if (document.RootElement.ValueKind != JsonValueKind.Array) throw new TranslationInterchangeException("XLIFF21-TAGS", "Runic tag metadata is invalid."); string[] result = document.RootElement.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString()! : throw new TranslationInterchangeException("XLIFF21-TAGS", "Runic tag metadata is invalid.")).ToArray(); if (result.Length > 64 || result.Distinct(StringComparer.Ordinal).Count() != result.Length) throw new TranslationInterchangeException("XLIFF21-TAGS", "Runic tag metadata is invalid."); return result; } catch (FormatException exception) { throw new TranslationInterchangeException("XLIFF21-TAGS", "Runic tag metadata is invalid.", exception); } catch (JsonException exception) { throw new TranslationInterchangeException("XLIFF21-TAGS", "Runic tag metadata is invalid.", exception); }
    }

    private static List<ImportedPlaceholder> ParsePlaceholders(string? encoded)
    {
        if (encoded is null) return []; try { byte[] bytes = Convert.FromBase64String(encoded); using JsonDocument document = JsonDocument.Parse(bytes); if (document.RootElement.ValueKind != JsonValueKind.Array) throw new TranslationInterchangeException("XLIFF21-PLACEHOLDERS", "Runic placeholder metadata is invalid."); var result = new List<ImportedPlaceholder>(); foreach (JsonElement item in document.RootElement.EnumerateArray()) { RequireObject(item, "XLIFF21-PLACEHOLDERS"); RequireExact(item, "name", "type", "format"); result.Add(new ImportedPlaceholder(RequiredString(item, "name", "XLIFF21-PLACEHOLDERS"), RequiredString(item, "type", "XLIFF21-PLACEHOLDERS"), RequiredString(item, "format", "XLIFF21-PLACEHOLDERS"))); } if (result.Count > 32 || result.Select(static item => item.Name).Distinct(StringComparer.Ordinal).Count() != result.Count) throw new TranslationInterchangeException("XLIFF21-PLACEHOLDERS", "Runic placeholder metadata is invalid."); return result; } catch (FormatException exception) { throw new TranslationInterchangeException("XLIFF21-PLACEHOLDERS", "Runic placeholder metadata is invalid.", exception); } catch (JsonException exception) { throw new TranslationInterchangeException("XLIFF21-PLACEHOLDERS", "Runic placeholder metadata is invalid.", exception); }
    }

    private static string TypeName(TranslationArgumentType type) => type switch { TranslationArgumentType.Int => "int", TranslationArgumentType.Number => "number", TranslationArgumentType.Boolean => "bool", TranslationArgumentType.Date => "date", TranslationArgumentType.Time => "time", TranslationArgumentType.DateTime => "datetime", TranslationArgumentType.Guid => "guid", _ => "string" };
    private static string Quote(string value) { using var stream = new MemoryStream(); using (var writer = new Utf8JsonWriter(stream)) writer.WriteStringValue(value); return Encoding.UTF8.GetString(stream.ToArray()); }
    private static void RequireElement(XmlReader reader, string name, string code) { if (reader.NamespaceURI != XliffNamespace || reader.LocalName != name) throw new TranslationInterchangeException(code, "The XLIFF profile contains an unsupported element."); }
    private static string RequiredAttribute(XmlReader reader, string name, string code) => reader.GetAttribute(name) ?? throw new TranslationInterchangeException(code, "The XLIFF profile is missing a required attribute.");
    private static void RequireAttributes(XmlReader reader, string code, IReadOnlyCollection<string> required, IReadOnlyCollection<string> optional) { var seen = new HashSet<string>(StringComparer.Ordinal); if (reader.HasAttributes) for (int index = 0; index < reader.AttributeCount; index++) { reader.MoveToAttribute(index); if (reader.Prefix == "xmlns" || reader.NamespaceURI == "http://www.w3.org/2000/xmlns/") continue; bool accepted = reader.NamespaceURI.Length == 0 && (required.Contains(reader.LocalName, StringComparer.Ordinal) || optional.Contains(reader.LocalName, StringComparer.Ordinal)); if (!accepted || !seen.Add(reader.LocalName)) throw new TranslationInterchangeException(code, "The XLIFF profile contains an unknown or duplicate attribute."); } reader.MoveToElement(); foreach (string name in required) if (reader.GetAttribute(name) is null) throw new TranslationInterchangeException(code, "The XLIFF profile is missing a required attribute."); }
    private static void ValidateReview(TranslationInterchangeReview? review, string catalog) { if (review is null) return; if (!string.Equals(review.CatalogId, catalog, StringComparison.Ordinal) || review.Entries.Count > MaximumReviewEntries) throw new TranslationInterchangeException("REVIEW-INVALID", "The review representation has an invalid catalog or entry count."); var identities = new HashSet<string>(StringComparer.Ordinal); foreach (TranslationInterchangeReviewEntry entry in review.Entries) if (!IsKey(entry.Key) || string.IsNullOrEmpty(entry.Locale) || !IsReviewState(entry.State) || entry.Note?.Length > 16_384 || entry.SourceFingerprint?.Length > 256 || !identities.Add(entry.Key + "\0" + entry.Locale)) throw new TranslationInterchangeException("REVIEW-INVALID", "The review representation contains an invalid or duplicate entry."); }
    private static void ValidateExportReview(TranslationInterchangeReview? review, CompiledTextCatalog catalog) { ValidateReview(review, catalog.Id); if (review is null) return; var keys = new HashSet<string>(catalog.CanonicalResources.Select(static value => value.Key), StringComparer.Ordinal); var locales = new HashSet<string>(catalog.Locales.Where(locale => locale.Tag != catalog.DefaultLocale).Select(static value => value.Tag), StringComparer.Ordinal); foreach (TranslationInterchangeReviewEntry entry in review.Entries) { if (!keys.Contains(entry.Key) || !locales.Contains(entry.Locale)) throw new TranslationInterchangeException("REVIEW-UNEXPORTED", "Review data references a key or locale that is not exported."); if (entry.State == "approved" && !string.Equals(entry.SourceFingerprint, catalog.Fingerprint, StringComparison.Ordinal)) throw new TranslationInterchangeException("REVIEW-FINGERPRINT", "Approved review data must match the compiled source fingerprint."); } }
    private static void EnsureExportBounds(CompiledTextCatalog catalog, CompiledTextLocale locale, TranslationInterchangeReview? review) { if (catalog.CanonicalResources.Count > MaximumUnits) throw new TranslationInterchangeException("XLIFF21-LIMIT", "The catalog has too many resources for XLIFF export."); long bytes = 512; foreach (CompiledTranslation source in catalog.CanonicalResources) { bytes += ExpandedBytes(source.Key) + ExpandedBytes(source.Pattern) + MetadataBytes(source) + 1024; if (bytes > MaximumXliffBytes) throw new TranslationInterchangeException("XLIFF21-LIMIT", "The XLIFF export exceeds the byte limit."); } foreach (CompiledTranslation target in locale.DirectResources) { bytes += ExpandedBytes(target.Pattern) + MetadataBytes(target); if (bytes > MaximumXliffBytes) throw new TranslationInterchangeException("XLIFF21-LIMIT", "The XLIFF export exceeds the byte limit."); } if (review is not null) foreach (TranslationInterchangeReviewEntry entry in review.Entries) { bytes += ExpandedBytes(entry.Note) + ExpandedBytes(entry.SourceFingerprint) + 256; if (bytes > MaximumXliffBytes) throw new TranslationInterchangeException("XLIFF21-LIMIT", "The XLIFF export exceeds the byte limit."); } }
    private static long MetadataBytes(CompiledTranslation value) { long bytes = ExpandedBytes(value.Description) + ExpandedBytes(value.Since) + ExpandedBytes(value.DeprecatedReason); foreach (string tag in value.Tags) bytes += ExpandedBytes(tag); foreach (CompiledTextPlaceholder placeholder in value.Placeholders) bytes += ExpandedBytes(placeholder.Name) + ExpandedBytes(placeholder.Format) + 32; return checked(bytes * 2 + 256); }
    private static long ExpandedBytes(string? value) => value is null ? 0 : checked((long)Encoding.UTF8.GetByteCount(value) * 6 + 32);
    private static bool IsReviewState(string value) => value is "draft" or "translated" or "needs-review" or "approved";
    private static bool IsKey(string value) => value.Split('.').All(static part => part.Length > 0 && (char.IsAsciiLetter(part[0]) || part[0] == '_') && part.Skip(1).All(static character => char.IsAsciiLetterOrDigit(character) || character == '_'));
    private static void RequireObject(JsonElement value, string code) { if (value.ValueKind != JsonValueKind.Object) throw new TranslationInterchangeException(code, "The JSON value must be an object."); }
    private static JsonElement Required(JsonElement value, string name, JsonValueKind kind, string code) { if (!value.TryGetProperty(name, out JsonElement item) || item.ValueKind != kind) throw new TranslationInterchangeException(code, "A required JSON member is missing or malformed."); return item; }
    private static string RequiredString(JsonElement value, string name, string code) => Required(value, name, JsonValueKind.String, code).GetString()!;
    private static bool RequiredBoolean(JsonElement value, string name, string code) { if (!value.TryGetProperty(name, out JsonElement item) || item.ValueKind is not JsonValueKind.True and not JsonValueKind.False) throw new TranslationInterchangeException(code, "A required JSON member is missing or malformed."); return item.GetBoolean(); }
    private static string? OptionalString(JsonElement value, string name, string code) { if (!value.TryGetProperty(name, out JsonElement item)) return null; if (item.ValueKind != JsonValueKind.String) throw new TranslationInterchangeException(code, "An optional JSON member is malformed."); return item.GetString(); }
    private static void RequireExact(JsonElement value, params string[] names) { RequireAllowed(value, names); foreach (string name in names) if (!value.TryGetProperty(name, out _)) throw new TranslationInterchangeException("REVIEW-MEMBER", "A required JSON member is missing."); }
    private static void RequireAllowed(JsonElement value, params string[] names) { var allowed = new HashSet<string>(names, StringComparer.Ordinal); var seen = new HashSet<string>(StringComparer.Ordinal); foreach (JsonProperty property in value.EnumerateObject()) if (!seen.Add(property.Name) || !allowed.Contains(property.Name)) throw new TranslationInterchangeException("REVIEW-MEMBER", "The review representation contains an unknown or duplicate member."); }
    private sealed record ImportedUnit(string SourcePattern, string Pattern, int SchemaVersion, string Layer, string? Description, string? Since, string? Deprecated, string[] Tags, IReadOnlyList<ImportedPlaceholder> Placeholders);
    private sealed record UnitNote(int SchemaVersion, string Layer, bool Structured, string? Description, string? Since, string? Deprecated, string[] Tags, IReadOnlyList<ImportedPlaceholder> Placeholders);
    private sealed record ImportedPlaceholder(string Name, string Type, string Format);
    private sealed class ResourceGroup
    {
        internal Dictionary<string, ResourceGroup> Groups { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, ImportedUnit> Leaves { get; } = new(StringComparer.Ordinal);
    }
}

/// <summary>One canonical XLIFF export document.</summary>
public sealed class TranslationXliffDocument
{
    internal TranslationXliffDocument(string catalogId, string sourceLocale, string targetLocale, byte[] bytes) { CatalogId = catalogId; SourceLocale = sourceLocale; TargetLocale = targetLocale; Bytes = bytes; }
    public string CatalogId { get; }
    public string SourceLocale { get; }
    public string TargetLocale { get; }
    public byte[] Bytes { get; }
}

public sealed class TranslationXliffExportResult
{
    internal TranslationXliffExportResult(IReadOnlyList<TranslationXliffDocument> documents, TranslationInterchangeReport report) { Documents = documents; Report = report; }
    public IReadOnlyList<TranslationXliffDocument> Documents { get; }
    public TranslationInterchangeReport Report { get; }
}

public sealed class TranslationXliffImportResult
{
    internal TranslationXliffImportResult(string catalogId, string sourceLocale, string targetLocale, byte[] resourceDocumentBytes, TranslationInterchangeReview review, TranslationInterchangeReport report) { CatalogId = catalogId; SourceLocale = sourceLocale; TargetLocale = targetLocale; ResourceDocumentBytes = resourceDocumentBytes; Review = review; Report = report; }
    public string CatalogId { get; }
    public string SourceLocale { get; }
    public string TargetLocale { get; }
    public byte[] ResourceDocumentBytes { get; }
    public TranslationInterchangeReview Review { get; }
    public TranslationInterchangeReport Report { get; }
}

public sealed class TranslationInterchangeReport
{
    private readonly IReadOnlyList<TranslationInterchangeLoss> _losses;
    internal TranslationInterchangeReport(IEnumerable<TranslationInterchangeLoss> losses) => _losses = losses.OrderBy(static loss => loss.Location, StringComparer.Ordinal).ThenBy(static loss => loss.Code, StringComparer.Ordinal).ToArray();
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

public sealed class TranslationInterchangeReviewEntry
{
    public TranslationInterchangeReviewEntry(string key, string locale, string state, string? note = null, string? sourceFingerprint = null) { Key = key; Locale = locale; State = state; Note = note; SourceFingerprint = sourceFingerprint; }
    public string Key { get; }
    public string Locale { get; }
    public string State { get; }
    public string? Note { get; }
    public string? SourceFingerprint { get; }
}

/// <summary>Stable rejection ID for the closed interchange profile.</summary>
public sealed class TranslationInterchangeException : Exception
{
    internal TranslationInterchangeException(string code, string message, Exception? innerException = null) : base(message, innerException) => Code = code;
    public string Code { get; }
}
