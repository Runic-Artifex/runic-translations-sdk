using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Xml;

namespace Runic.Translations.Tooling;

/// <summary>Read-only structural verdict for one translation artifact.</summary>
/// <remarks>
/// Inspection never loads or executes message payloads; it reports identity,
/// counts, bounds, and normalized findings only.
/// </remarks>
public sealed class ArtifactInspection
{
    internal ArtifactInspection(
        string kind,
        int? formatVersion,
        string? catalog,
        string? locale,
        string? layer,
        long byteLength,
        bool hasIntegrityMetadata,
        string? contractFingerprint,
        int messageCount,
        int resourceCount,
        int structuredMessageCount,
        int unitCount,
        int reviewEntryCount,
        IReadOnlyList<ArtifactInspectionFinding> findings)
    {
        Kind = kind;
        FormatVersion = formatVersion;
        Catalog = catalog;
        Locale = locale;
        Layer = layer;
        ByteLength = byteLength;
        HasIntegrityMetadata = hasIntegrityMetadata;
        ContractFingerprint = contractFingerprint;
        MessageCount = messageCount;
        ResourceCount = resourceCount;
        StructuredMessageCount = structuredMessageCount;
        UnitCount = unitCount;
        ReviewEntryCount = reviewEntryCount;
        Findings = findings;
    }

    /// <summary>The detected artifact kind.</summary>
    public string Kind { get; }
    /// <summary>The declared artifact or schema version when present.</summary>
    public int? FormatVersion { get; }
    /// <summary>The catalog identifier when present.</summary>
    public string? Catalog { get; }
    /// <summary>The locale tag when present.</summary>
    public string? Locale { get; }
    /// <summary>The source layer name when present.</summary>
    public string? Layer { get; }
    /// <summary>The inspected byte length.</summary>
    public long ByteLength { get; }
    /// <summary>Whether integrity metadata such as a contract fingerprint is present.</summary>
    public bool HasIntegrityMetadata { get; }
    /// <summary>The contract fingerprint when present.</summary>
    public string? ContractFingerprint { get; }
    /// <summary>The locale-pack message count.</summary>
    public int MessageCount { get; }
    /// <summary>The resource leaf count for catalog and resource documents.</summary>
    public int ResourceCount { get; }
    /// <summary>The structured MF2 message count for catalog and resource documents.</summary>
    public int StructuredMessageCount { get; }
    /// <summary>The XLIFF unit count.</summary>
    public int UnitCount { get; }
    /// <summary>The XLIFF review entry count.</summary>
    public int ReviewEntryCount { get; }
    /// <summary>Normalized findings in stable code order; empty when the artifact is clean.</summary>
    public IReadOnlyList<ArtifactInspectionFinding> Findings { get; }

    /// <summary>Renders the deterministic human report without timestamps or paths.</summary>
    public string ToReport()
    {
        var text = new StringBuilder();
        text.Append("kind: ").Append(Kind).Append('\n');
        AppendIf(text, "formatVersion", FormatVersion is null ? null : FormatVersion.Value.ToString(CultureInfo.InvariantCulture));
        AppendIf(text, "catalog", Catalog);
        AppendIf(text, "locale", Locale);
        AppendIf(text, "layer", Layer);
        AppendIf(text, "contractFingerprint", ContractFingerprint);
        if (Kind.StartsWith("locale-pack", StringComparison.Ordinal)) text.Append("messages: ").Append(MessageCount).Append('\n');
        if (Kind.StartsWith("resources-json", StringComparison.Ordinal) || Kind == "xliff-2.1" || Kind == "xliff")
        {
            text.Append("resources: ").Append(ResourceCount).Append('\n');
            text.Append("structuredMessages: ").Append(StructuredMessageCount).Append('\n');
        }
        if (Kind.StartsWith("xliff", StringComparison.Ordinal))
        {
            text.Append("units: ").Append(UnitCount).Append('\n');
            text.Append("reviewEntries: ").Append(ReviewEntryCount).Append('\n');
        }
        text.Append("integrityMetadata: ").Append(HasIntegrityMetadata ? "present" : "absent").Append('\n');
        text.Append("bytes: ").Append(ByteLength.ToString(CultureInfo.InvariantCulture)).Append('\n');
        if (Findings.Count == 0)
        {
            text.Append("findings: none\n");
        }
        else
        {
            text.Append("findings:\n");
            foreach (ArtifactInspectionFinding finding in Findings)
                text.Append(finding.Code).Append(": ").Append(finding.Message).Append('\n');
        }
        return text.ToString();
    }

    private static void AppendIf(StringBuilder text, string name, string? value)
    {
        if (value is not null) text.Append(name).Append(": ").Append(value).Append('\n');
    }
}

/// <summary>One normalized inspection finding with a stable location-free code.</summary>
public sealed class ArtifactInspectionFinding
{
    internal ArtifactInspectionFinding(string code, string message) { Code = code; Message = message; }
    /// <summary>The stable machine-readable rejection or finding ID.</summary>
    public string Code { get; }
    /// <summary>The human-readable detail.</summary>
    public string Message { get; }
}

/// <summary>Deterministic read-only inspection of compiled and exchanged translation artifacts.</summary>
/// <remarks>
/// Locale-pack-v2 structure rules mirror the runtime decoder twin emitted by
/// <c>EsmOutputRenderer</c>; rejections reuse the same stable RTR0023 reason IDs.
/// XLIFF findings come from running the closed interchange profile itself.
/// </remarks>
public static class ArtifactInspector
{
    /// <summary>The shared document byte bound used by sibling commands.</summary>
    public const int DefaultMaximumBytes = 8 * 1024 * 1024;

    private const int MaximumDepth = 64;
    private const int MaximumPackMessages = 50_000;
    private const int MaximumMessageInputs = 32;
    private const int MaximumMessageSelectors = 16;
    private const int MaximumMessageVariants = 256;

    /// <summary>Inspects artifact bytes and reports structure without executing payloads.</summary>
    public static ArtifactInspection Inspect(ReadOnlyMemory<byte> content, int maximumBytes = DefaultMaximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        return content.Length switch
        {
            0 => Unknown(content.Length, new ArtifactInspectionFinding("INSPECT-UNSUPPORTED-KIND", "The artifact is empty.")),
            _ => Probe(content, maximumBytes),
        };
    }

    private static ArtifactInspection Probe(ReadOnlyMemory<byte> content, int maximumBytes)
    {
        ReadOnlySpan<byte> span = content.Span;
        int start = SkipPrologue(span);
        if (start < span.Length && (span[start] == (byte)'{' || span[start] == (byte)'['))
        {
            bool packFamily = HasRootMember(span, "artifactVersion"u8);
            if (packFamily && content.Length > maximumBytes)
                return new ArtifactInspection("locale-pack-v2", null, null, null, null, content.Length, false, null, 0, 0, 0, 0, 0,
                    [new ArtifactInspectionFinding("RTR0023/limit-exceeded", "The external pack exceeds the configured document limit.")]);
            if (!packFamily && HasRootMember(span, "schemaVersion"u8) && content.Length > maximumBytes)
                return new ArtifactInspection("resources-json", null, null, null, null, content.Length, false, null, 0, 0, 0, 0, 0,
                    [new ArtifactInspectionFinding("RTR0022", $"Document exceeds the configured byte limit of {maximumBytes} bytes.")]);
            return packFamily ? InspectLocalePackV2(content) : InspectResourcesJson(content);
        }
        if (start < span.Length && span[start] == (byte)'<') return InspectXliff(content);
        return Unknown(content.Length, new ArtifactInspectionFinding("INSPECT-UNSUPPORTED-KIND", "The artifact kind is unsupported."));
    }

    private static ArtifactInspection Unknown(long byteLength, ArtifactInspectionFinding finding) =>
        new("unknown", null, null, null, null, byteLength, false, null, 0, 0, 0, 0, 0, [finding]);

    private static ArtifactInspection InspectLocalePackV2(ReadOnlyMemory<byte> content)
    {
        var findings = new List<ArtifactInspectionFinding>();
        // Pre-scan mirrors TranslationPackV2Loader.WithinMaximumDepth and its ESM twin
        // withinJsonDepth so over-deep packs classify as limit-exceeded, not malformed.
        if (!WithinMaximumDepth(content.Span, MaximumDepth))
        {
            findings.Add(PackFinding(TranslationPackReason.LimitExceeded, "The external pack exceeds the configured depth limit."));
            return new ArtifactInspection("locale-pack-v2", null, null, null, null, content.Length, false, null, 0, 0, 0, 0, 0, findings);
        }
        JsonDocument? document;
        try
        {
            document = JsonDocument.Parse(content, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = MaximumDepth,
            });
        }
        catch (JsonException exception)
        {
            findings.Add(PackFinding(TranslationPackReason.Malformed,
                "The external pack is incomplete or contains malformed JSON near byte " + exception.BytePositionInLine + "."));
            return new ArtifactInspection("locale-pack-v2", null, null, null, null, content.Length, false, null, 0, 0, 0, 0, 0, findings);
        }

        using (document)
        {
            int? formatVersion = null;
            string? catalog = null;
            string? locale = null;
            string? fingerprint = null;
            int messageCount = 0;
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                findings.Add(PackFinding(TranslationPackReason.Malformed, "The external pack root must be an object."));
                return new ArtifactInspection("locale-pack-v2", null, null, null, null, content.Length, false, null, 0, 0, 0, 0, 0, findings);
            }

            Dictionary<string, JsonElement> root = ReadMembers(document.RootElement, findings,
                ["artifactVersion", "messageGrammarVersion", "catalog", "locale", "contractFingerprint", "messages"]);
            if (root.Count != 0)
            {
                if (TryInteger(root["artifactVersion"], out int artifactVersion))
                {
                    formatVersion = artifactVersion;
                    if (artifactVersion != 2)
                        findings.Add(PackFinding(TranslationPackReason.ArtifactVersionMismatch, "The external pack artifact version is unsupported."));
                }
                else findings.Add(PackFinding(TranslationPackReason.Malformed, "'artifactVersion' must be an integer."));
                if (TryInteger(root["messageGrammarVersion"], out int grammarVersion))
                {
                    if (grammarVersion != 2)
                        findings.Add(PackFinding(TranslationPackReason.MessageGrammarVersionMismatch, "The external pack message grammar version is unsupported."));
                }
                else findings.Add(PackFinding(TranslationPackReason.Malformed, "'messageGrammarVersion' must be an integer."));
                catalog = TryString(root["catalog"]);
                if (catalog is null || !IsCatalog(catalog)) findings.Add(PackFinding(TranslationPackReason.Malformed, "The external pack catalog identifier is invalid."));
                locale = TryString(root["locale"]);
                if (locale is null || !IsLocaleTag(locale)) findings.Add(PackFinding(TranslationPackReason.Malformed, "The external pack locale is invalid."));
                fingerprint = TryString(root["contractFingerprint"]);
                if (fingerprint is null || !IsFingerprint(fingerprint)) findings.Add(PackFinding(TranslationPackReason.Malformed, "The external pack contract fingerprint is invalid."));
                if (root["messages"].ValueKind != JsonValueKind.Object)
                    findings.Add(PackFinding(TranslationPackReason.Malformed, "The external pack messages value must be an object."));
                else messageCount = CountMessages(root["messages"], findings);
            }

            // Rejection IDs mirror TranslationPackFailure.RejectionIdPrefix plus the stable
            // reason names shared by the .NET decoder and its generated ESM twin.
            return new ArtifactInspection("locale-pack-v2", formatVersion, catalog, locale, null, content.Length,
                fingerprint is not null && IsFingerprint(fingerprint), fingerprint, messageCount, 0, 0, 0, 0,
                findings.OrderBy(static finding => finding.Code, StringComparer.Ordinal)
                    .ThenBy(static finding => finding.Message, StringComparer.Ordinal).ToArray());
        }
    }

    private static int CountMessages(JsonElement messages, List<ArtifactInspectionFinding> findings)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        int count = 0;
        foreach (JsonProperty property in messages.EnumerateObject())
        {
            if (!keys.Add(property.Name))
            {
                findings.Add(PackFinding(TranslationPackReason.Malformed, "The external pack contains duplicate message key '" + property.Name + "'."));
                continue;
            }
            if (++count > MaximumPackMessages)
            {
                findings.Add(PackFinding(TranslationPackReason.LimitExceeded, "The external pack exceeds the configured message limit."));
                return count - 1;
            }
            if (!IsResourceKey(property.Name))
            {
                findings.Add(PackFinding(TranslationPackReason.Malformed, "The external pack contains an invalid message key."));
                continue;
            }

            Dictionary<string, JsonElement> fields = ReadMembers(property.Value, findings, ["astVersion", "inputs", "selectors", "variants"]);
            if (fields.Count == 0) continue;
            if (!TryInteger(fields["astVersion"], out int astVersion) || astVersion != 2)
                findings.Add(PackFinding(TranslationPackReason.Malformed, "Message '" + property.Name + "' has an unsupported AST version."));
            if (fields["inputs"].ValueKind != JsonValueKind.Object)
                findings.Add(PackFinding(TranslationPackReason.Malformed, "A message input contract must be an object."));
            else
            {
                int inputs = 0;
                foreach (JsonProperty _ in fields["inputs"].EnumerateObject()) inputs++;
                if (inputs > MaximumMessageInputs)
                    findings.Add(PackFinding(TranslationPackReason.LimitExceeded, "A message exceeds the configured input limit."));
            }
            if (fields["selectors"].ValueKind != JsonValueKind.Array)
                findings.Add(PackFinding(TranslationPackReason.Malformed, "A message selector list is invalid."));
            else if (fields["selectors"].GetArrayLength() > MaximumMessageSelectors)
                findings.Add(PackFinding(TranslationPackReason.LimitExceeded, "A message exceeds the normalized selector limit."));
            if (fields["variants"].ValueKind != JsonValueKind.Array)
                findings.Add(PackFinding(TranslationPackReason.Malformed, "A message variant list is invalid."));
            else if (fields["variants"].GetArrayLength() is < 1)
                findings.Add(PackFinding(TranslationPackReason.Malformed, "A message variant list is invalid."));
            else if (fields["variants"].GetArrayLength() > MaximumMessageVariants)
                findings.Add(PackFinding(TranslationPackReason.LimitExceeded, "A message exceeds the normalized variant limit."));
        }
        return count;
    }

    private static ArtifactInspection InspectResourcesJson(ReadOnlyMemory<byte> content)
    {
        var findings = new List<ArtifactInspectionFinding>();
        JsonDocument? document;
        try
        {
            document = JsonDocument.Parse(content, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = MaximumDepth + 1,
            });
        }
        catch (JsonException exception)
        {
            findings.Add(new ArtifactInspectionFinding("INSPECT-MALFORMED",
                "The JSON document is incomplete or malformed near byte " + exception.BytePositionInLine + "."));
            return new ArtifactInspection("resources-json", null, null, null, null, content.Length, false, null, 0, 0, 0, 0, 0, findings);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                findings.Add(new ArtifactInspectionFinding("INSPECT-MALFORMED", "The JSON document root must be an object."));
                return new ArtifactInspection("resources-json", null, null, null, null, content.Length, false, null, 0, 0, 0, 0, 0, findings);
            }

            string kind = TryIntegerMember(document.RootElement, "schemaVersion", out int schemaVersion) && schemaVersion == 3
                ? "resources-json-v3"
                : "resources-json";
            int? formatVersion = TryIntegerMember(document.RootElement, "schemaVersion", out schemaVersion) ? schemaVersion : null;
            string? catalog = TryStringMember(document.RootElement, "catalog");
            string? locale = TryStringMember(document.RootElement, "locale");
            string? layer = TryStringMember(document.RootElement, "layer");
            int resources = 0;
            int structured = 0;
            if (document.RootElement.TryGetProperty("resources", out JsonElement group) && group.ValueKind == JsonValueKind.Object)
                CountGroup(group, findings, 0, ref resources, ref structured);
            return new ArtifactInspection(kind, formatVersion, catalog, locale, layer, content.Length, false, null, 0, resources, structured, 0, 0,
                findings.OrderBy(static finding => finding.Code, StringComparer.Ordinal)
                    .ThenBy(static finding => finding.Message, StringComparer.Ordinal).ToArray());
        }
    }

    private static void CountGroup(JsonElement group, List<ArtifactInspectionFinding> findings, int depth, ref int resources, ref int structured)
    {
        if (depth > MaximumDepth)
        {
            findings.Add(new ArtifactInspectionFinding("INSPECT-LIMIT", "The resource tree exceeds the configured depth limit."));
            return;
        }
        foreach (JsonProperty property in group.EnumerateObject())
        {
            JsonElement value = property.Value;
            if (value.ValueKind == JsonValueKind.String) { resources++; continue; }
            if (value.ValueKind != JsonValueKind.Object) { resources++; continue; }
            if (value.TryGetProperty("$value", out JsonElement leaf))
            {
                resources++;
                if (leaf.ValueKind == JsonValueKind.Object && leaf.TryGetProperty("mf2", out _)) structured++;
                continue;
            }
            CountGroup(value, findings, depth + 1, ref resources, ref structured);
        }
    }

    private static ArtifactInspection InspectXliff(ReadOnlyMemory<byte> content)
    {
        var findings = new List<ArtifactInspectionFinding>();
        string? version = null;
        try
        {
            using var stream = new MemoryStream(content.ToArray(), writable: false);
            using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            reader.MoveToContent();
            version = reader.GetAttribute("version");
        }
        catch (XmlException)
        {
            return new ArtifactInspection("xliff", null, null, null, null, content.Length, false, null, 0, 0, 0, 0, 0,
                [new ArtifactInspectionFinding("INSPECT-MALFORMED", "The XML document is malformed.")]);
        }

        string kind = version == "2.1" ? "xliff-2.1" : "xliff";
        string catalog;
        string sourceLocale;
        string targetLocale;
        int units;
        int reviewEntries;
        try
        {
            TranslationXliffImportResult import = TranslationInterchange.ImportXliff21(content);
            catalog = import.CatalogId;
            sourceLocale = import.SourceLocale;
            targetLocale = import.TargetLocale;
            (units, reviewEntries) = CountImportedUnits(import.ResourceDocumentBytes, import.Review.Entries.Count);
            if (import.Report.Losses.Count > 0)
                findings.AddRange(import.Report.Losses.Select(loss => new ArtifactInspectionFinding(loss.Code, loss.Location + ": " + loss.Message)));
        }
        catch (TranslationInterchangeException exception)
        {
            // The closed interchange profile owns these normalized codes, including
            // unsupported-metadata reports such as XLIFF21-METADATA and XLIFF21-VERSION.
            findings.Add(new ArtifactInspectionFinding(exception.Code, exception.Message));
            return new ArtifactInspection(kind, null, null, null, null, content.Length, false, null, 0, 0, 0, 0, 0,
                findings.OrderBy(static finding => finding.Code, StringComparer.Ordinal)
                    .ThenBy(static finding => finding.Message, StringComparer.Ordinal).ToArray());
        }

        return new ArtifactInspection(kind, null, catalog, targetLocale, null, content.Length, reviewEntries > 0, null, 0, units, 0, units, reviewEntries, findings);
    }

    private static (int Units, int ReviewEntries) CountImportedUnits(byte[] resourceDocumentBytes, int reviewEntries)
    {
        using JsonDocument document = JsonDocument.Parse(resourceDocumentBytes);
        int resources = 0;
        int structured = 0;
        if (document.RootElement.TryGetProperty("resources", out JsonElement group) && group.ValueKind == JsonValueKind.Object)
            CountGroup(group, [], 0, ref resources, ref structured);
        return (resources, reviewEntries);
    }

    private static Dictionary<string, JsonElement> ReadMembers(JsonElement value, List<ArtifactInspectionFinding> findings, string[] expected)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var allowed = new HashSet<string>(expected, StringComparer.Ordinal);
        foreach (JsonProperty property in value.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
                findings.Add(PackFinding(TranslationPackReason.UnknownMember, "The external pack contains unknown property '" + property.Name + "'."));
            else if (!result.TryAdd(property.Name, property.Value))
                findings.Add(PackFinding(TranslationPackReason.Malformed, "The external pack contains duplicate property '" + property.Name + "'."));
        }
        foreach (string name in expected)
            if (!result.ContainsKey(name))
                findings.Add(PackFinding(TranslationPackReason.Malformed, "The external pack is missing required property '" + name + "'."));
        return result;
    }

    private static ArtifactInspectionFinding PackFinding(TranslationPackReason reason, string message) =>
        new("RTR0023/" + ReasonName(reason), message);

    // Stable names shared with TranslationPackFailure.RejectionName in the runtime assembly.
    private enum TranslationPackReason
    {
        Malformed,
        LimitExceeded,
        UnknownMember,
        ArtifactVersionMismatch,
        MessageGrammarVersionMismatch,
    }

    private static string ReasonName(TranslationPackReason reason) => reason switch
    {
        TranslationPackReason.ArtifactVersionMismatch => "artifact-version-mismatch",
        TranslationPackReason.MessageGrammarVersionMismatch => "message-grammar-version-mismatch",
        TranslationPackReason.LimitExceeded => "limit-exceeded",
        TranslationPackReason.UnknownMember => "unknown-member",
        _ => "malformed",
    };

    private static bool HasRootMember(ReadOnlySpan<byte> content, ReadOnlySpan<byte> name)
    {
        try
        {
            var reader = new Utf8JsonReader(content, new JsonReaderOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return false;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject) return false;
                if (reader.TokenType != JsonTokenType.PropertyName) return false;
                bool match = reader.ValueTextEquals(name);
                if (!reader.Read()) return false;
                if (match) return true;
                reader.Skip();
            }
            return false;
        }
        catch (JsonException)
        {
            // Malformed probe bytes stay normalized: the routed inspector re-parses
            // and owns the stable INSPECT-MALFORMED or RTR0023/malformed finding.
            return false;
        }
    }

    private static bool WithinMaximumDepth(ReadOnlySpan<byte> content, int maximumDepth)
    {
        int depth = 0;
        bool quoted = false;
        bool escaped = false;
        for (int index = 0; index < content.Length; index++)
        {
            byte value = content[index];
            if (quoted)
            {
                if (escaped) escaped = false;
                else if (value == (byte)'\\') escaped = true;
                else if (value == (byte)'\"') quoted = false;
            }
            else if (value == (byte)'\"') quoted = true;
            else if (value is (byte)'{' or (byte)'[')
            {
                if (++depth > maximumDepth) return false;
            }
            else if (value is (byte)'}' or (byte)']') depth--;
        }
        return true;
    }

    private static int SkipPrologue(ReadOnlySpan<byte> content)
    {
        int index = 0;
        if (content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF) index = 3;
        while (index < content.Length && (content[index] == (byte)' ' || content[index] == (byte)'\t' || content[index] == (byte)'\r' || content[index] == (byte)'\n')) index++;
        return index;
    }

    private static bool TryInteger(JsonElement value, out int result)
    {
        result = 0;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out result);
    }

    private static string? TryString(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool TryIntegerMember(JsonElement value, string name, out int result)
    {
        result = 0;
        return value.TryGetProperty(name, out JsonElement member) && TryInteger(member, out result);
    }

    private static string? TryStringMember(JsonElement value, string name) =>
        value.TryGetProperty(name, out JsonElement member) ? TryString(member) : null;

    private static bool IsCatalog(string value)
    {
        if (value.Length == 0 || value[0] < 'a' || value[0] > 'z') return false;
        for (int index = 1; index < value.Length; index++)
        {
            char character = value[index];
            if ((character < 'a' || character > 'z') && (character < '0' || character > '9') && character != '.' && character != '-') return false;
        }
        return true;
    }

    private static bool IsLocaleTag(string value)
    {
        // Structural check against the published locale-artifact-v2 pattern:
        // ^[A-Za-z]{2,8}(?:-[A-Za-z0-9]{1,8})*$
        string[] parts = value.Split('-');
        if (parts[0].Length is < 2 or > 8 || !IsAsciiLetters(parts[0])) return false;
        for (int index = 1; index < parts.Length; index++)
            if (parts[index].Length is < 1 or > 8 || !parts[index].All(char.IsAsciiLetterOrDigit)) return false;
        return true;
    }

    private static bool IsFingerprint(string value)
    {
        if (value.Length != 71 || !value.StartsWith("sha256:", StringComparison.Ordinal)) return false;
        for (int index = 7; index < value.Length; index++)
            if (value[index] is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')) return false;
        return true;
    }

    private static bool IsResourceKey(string value)
    {
        int segmentStart = 0;
        for (int index = 0; index <= value.Length; index++)
        {
            if (index != value.Length && value[index] != '.') continue;
            int length = index - segmentStart;
            if (length == 0) return false;
            string segment = value.Substring(segmentStart, length);
            if (!char.IsAsciiLetter(segment[0]) && segment[0] != '_') return false;
            for (int offset = 1; offset < segment.Length; offset++)
                if (!char.IsAsciiLetterOrDigit(segment[offset]) && segment[offset] != '_') return false;
            segmentStart = index + 1;
        }
        return true;
    }

    private static bool IsAsciiLetters(string value)
    {
        foreach (char character in value) if (!char.IsAsciiLetter(character)) return false;
        return true;
    }
}
