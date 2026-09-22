using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

namespace Runic.Translations.Tooling;

/// <summary>Read-only structural verdict for one translation interchange artifact.</summary>
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
    /// <summary>Whether integrity metadata such as review state is present.</summary>
    public bool HasIntegrityMetadata { get; }
    /// <summary>The contract fingerprint when present.</summary>
    public string? ContractFingerprint { get; }
    /// <summary>The inspected message count.</summary>
    public int MessageCount { get; }
    /// <summary>The inspected XLIFF resource count.</summary>
    public int ResourceCount { get; }
    /// <summary>The inspected structured MF2 message count.</summary>
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
        if (Kind == "xliff-2.1" || Kind == "xliff")
        {
            text.Append("resources: ").Append(ResourceCount).Append('\n');
            text.Append("structuredMessages: ").Append(StructuredMessageCount).Append('\n');
            text.Append("units: ").Append(UnitCount).Append('\n');
            text.Append("reviewEntries: ").Append(ReviewEntryCount).Append('\n');
        }
        text.Append("integrityMetadata: ").Append(HasIntegrityMetadata ? "present" : "absent").Append('\n');
        text.Append("bytes: ").Append(ByteLength.ToString(CultureInfo.InvariantCulture)).Append('\n');
        if (Findings.Count == 0) text.Append("findings: none\n");
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

/// <summary>Deterministic read-only inspection of closed XLIFF 2.1 interchange documents.</summary>
public static class ArtifactInspector
{
    /// <summary>The shared document byte bound used by sibling commands.</summary>
    public const int DefaultMaximumBytes = 8 * 1024 * 1024;

    /// <summary>Inspects XLIFF bytes and reports structure without executing payloads.</summary>
    public static ArtifactInspection Inspect(ReadOnlyMemory<byte> content, int maximumBytes = DefaultMaximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        if (content.Length == 0)
            return Unknown(content.Length, "INSPECT-UNSUPPORTED-KIND", "The artifact is empty.");
        if (content.Length > maximumBytes)
            return Unknown(content.Length, "XLIFF21-LIMIT", "The XLIFF document exceeds the configured document limit.");
        return Probe(content);
    }

    private static ArtifactInspection Probe(ReadOnlyMemory<byte> content)
    {
        ReadOnlySpan<byte> span = content.Span;
        int start = SkipPrologue(span);
        if (start >= span.Length || span[start] != (byte)'<')
            return Unknown(content.Length, "INSPECT-UNSUPPORTED-KIND", "Only closed XLIFF 2.1 interchange documents are supported.");

        string? version;
        try
        {
            using var stream = new MemoryStream(content.ToArray(), writable: false);
            using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            reader.MoveToContent();
            version = reader.GetAttribute("version");
        }
        catch (XmlException)
        {
            return Unknown(content.Length, "INSPECT-MALFORMED", "The XML document is malformed.", "xliff");
        }

        string kind = version == "2.1" ? "xliff-2.1" : "xliff";
        try
        {
            TranslationXliffImportResult import = TranslationInterchange.ImportXliff21(content);
            ArtifactInspectionFinding[] findings = import.Report.Losses
                .Select(static loss => new ArtifactInspectionFinding(loss.Code, loss.Location + ": " + loss.Message))
                .OrderBy(static finding => finding.Code, StringComparer.Ordinal)
                .ThenBy(static finding => finding.Message, StringComparer.Ordinal)
                .ToArray();
            return new ArtifactInspection(kind, null, import.CatalogId, import.TargetLocale, null, content.Length,
                import.Review.Entries.Count > 0, null, 0, import.Messages.Count, 0, import.Messages.Count,
                import.Review.Entries.Count, findings);
        }
        catch (TranslationInterchangeException exception)
        {
            return new ArtifactInspection(kind, null, null, null, null, content.Length, false, null, 0, 0, 0, 0, 0,
                [new ArtifactInspectionFinding(exception.Code, exception.Message)]);
        }
    }

    private static ArtifactInspection Unknown(long byteLength, string code, string message, string kind = "unknown") =>
        new(kind, null, null, null, null, byteLength, false, null, 0, 0, 0, 0, 0,
            [new ArtifactInspectionFinding(code, message)]);

    private static int SkipPrologue(ReadOnlySpan<byte> content)
    {
        int index = 0;
        if (content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF) index = 3;
        while (index < content.Length && (content[index] == (byte)' ' || content[index] == (byte)'\t' || content[index] == (byte)'\r' || content[index] == (byte)'\n')) index++;
        return index;
    }
}
