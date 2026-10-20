using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Runic.Translations.Tool;

internal static class SupportApplication
{
    private const string EnvelopeSchema = "runic.support-envelope/1";
    private const string EditorSchema = "runic.translations.editor-diagnostics/1";
    private const int MaximumSourceBytes = 2 * 1024 * 1024;
    private const int MaximumEnvelopeBytes = 64 * 1024;
    private static readonly string[] OmittedSources =
    [
        "automatic-capture", "network", "workspace-roots", "relative-paths",
        "source-text", "translation-text", "review-text", "sessions-cookies-tokens",
    ];

    internal static async Task<SupportCommandResult> ExecuteAsync(
        SupportOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        string mode = options.Mode.ToLowerInvariant();
        return mode switch
        {
            "preview" => Preview(options),
            "collect" => await CollectAsync(options, cancellationToken).ConfigureAwait(false),
            "remove" => await RemoveAsync(options, cancellationToken).ConfigureAwait(false),
            _ => throw new SupportUsageException("RTRSUP001", "Use support preview, collect, or remove."),
        };
    }

    private static SupportCommandResult Preview(SupportOptions options)
    {
        SupportEnvelope envelope = CreateEnvelope(options);
        return new SupportCommandResult(
            "preview",
            null,
            Digest(envelope),
            envelope.Collectors.Select(static collector => collector.Id).ToArray(),
            envelope.Omissions.Select(static omission => omission.Source).ToArray(),
            0);
    }

    private static async Task<SupportCommandResult> CollectAsync(SupportOptions options, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Output))
            throw new SupportUsageException("RTRSUP002", "Collect requires --destination <path>.");
        string output = Path.GetFullPath(options.Output);
        if (File.Exists(output)) throw new SupportUsageException("RTRSUP003", "The support-envelope output already exists.");
        SupportEnvelope envelope = CreateEnvelope(options);
        byte[] bytes = CanonicalBytes(envelope);
        if (bytes.Length > MaximumEnvelopeBytes) throw new SupportUsageException("RTRSUP004", "The support envelope exceeded its size limit.");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await using (FileStream stream = new(output, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        return new SupportCommandResult(
            "collect", output, Digest(bytes), envelope.Collectors.Select(static collector => collector.Id).ToArray(),
            envelope.Omissions.Select(static omission => omission.Source).ToArray(), 0);
    }

    private static async Task<SupportCommandResult> RemoveAsync(SupportOptions options, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(options.EditorDiagnostics))
            throw new SupportUsageException("RTRSUP005", "Remove does not accept a collector source.");
        if (string.IsNullOrWhiteSpace(options.Output))
            throw new SupportUsageException("RTRSUP006", "Remove requires --destination <path>.");
        string output = Path.GetFullPath(options.Output);
        if (!File.Exists(output)) throw new SupportUsageException("RTRSUP007", "The support-envelope output does not exist.");
        byte[] bytes = await File.ReadAllBytesAsync(output, cancellationToken).ConfigureAwait(false);
        ValidateEnvelope(bytes);
        string digest = Digest(bytes);
        File.Delete(output);
        if (File.Exists(output)) throw new IOException("The support-envelope output could not be removed.");
        return new SupportCommandResult("remove", output, digest, [], [], 0);
    }

    private static SupportEnvelope CreateEnvelope(SupportOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.EditorDiagnostics))
            throw new SupportUsageException("RTRSUP008", "Select the Editor collector explicitly with --editor-diagnostics <zip>.");
        if (!string.Equals(Path.GetExtension(options.EditorDiagnostics), ".zip", StringComparison.OrdinalIgnoreCase))
            throw new SupportUsageException("RTRSUP009", "The Editor collector must be a diagnostic ZIP.");
        EditorDiagnosticSummary editor = ReadEditorSummary(Path.GetFullPath(options.EditorDiagnostics));
        return new SupportEnvelope(
            EnvelopeSchema,
            [new SupportCollector("runic.translations.editor-diagnostics", editor.Schema, editor.Application, editor.Workspace)],
            OmittedSources.Select(static source => new SupportOmission(source, "not-collected-by-local-support-envelope")).ToArray());
    }

    private static EditorDiagnosticSummary ReadEditorSummary(string path)
    {
        FileInfo source = new(path);
        if (!source.Exists || source.Length > MaximumSourceBytes)
            throw new SupportUsageException("RTRSUP010", "The Editor diagnostic source is missing or exceeds its size limit.");
        try
        {
            using ZipArchive archive = ZipFile.OpenRead(path);
            if (archive.Entries.Count is < 1 or > 3) throw new SupportUsageException("RTRSUP011", "The Editor diagnostic ZIP has an invalid entry count.");
            ZipArchiveEntry? entry = archive.GetEntry("diagnostics.json");
            if (entry is null || entry.Length > MaximumEnvelopeBytes) throw new SupportUsageException("RTRSUP012", "The Editor diagnostic ZIP does not contain a bounded diagnostics.json entry.");
            using Stream stream = entry.Open();
            using JsonDocument document = JsonDocument.Parse(stream);
            return ParseEditorSummary(document.RootElement);
        }
        catch (InvalidDataException exception)
        {
            throw new SupportUsageException("RTRSUP013", "The Editor diagnostic source is not a valid diagnostic ZIP.", exception);
        }
        catch (JsonException exception)
        {
            throw new SupportUsageException("RTRSUP014", "The Editor diagnostic source is not a valid diagnostic summary.", exception);
        }
    }

    private static EditorDiagnosticSummary ParseEditorSummary(JsonElement root)
    {
        RequireObject(root, "diagnostics");
        RequireProperties(root, "schema", "generatedAt", "application", "workspace");
        string schema = RequiredString(root, "schema");
        if (schema != EditorSchema) throw new SupportUsageException("RTRSUP015", "The selected collector uses an unsupported diagnostic schema.");
        JsonElement application = Required(root, "application");
        RequireObject(application, "application");
        RequireProperties(application, "product", "version", "updateChannel", "commit", "runtime", "runtimeIdentifier", "operatingSystem", "architecture");
        JsonElement workspace = Required(root, "workspace");
        RequireObject(workspace, "workspace");
        RequireProperties(workspace, "catalogId", "schemaVersion", "localeCount", "documentCount", "messageCount", "compilerSuccess", "reviewStateAvailable", "pendingTransaction", "pendingTransactionPathCount", "diagnostics");
        JsonElement diagnostics = Required(workspace, "diagnostics");
        if (diagnostics.ValueKind != JsonValueKind.Array || diagnostics.GetArrayLength() > 256)
            throw new SupportUsageException("RTRSUP016", "The Editor diagnostic groups are invalid or exceed their limit.");
        EditorDiagnosticGroup[] groups = diagnostics.EnumerateArray().Select(ParseGroup).OrderBy(static group => group.Id, StringComparer.Ordinal).ThenBy(static group => group.Severity, StringComparer.Ordinal).ToArray();
        return new EditorDiagnosticSummary(
            schema,
            new SupportApplicationIdentity(
                SafeString(RequiredString(application, "product")), SafeString(RequiredString(application, "version")),
                SafeString(RequiredString(application, "updateChannel")), OptionalSafeString(application, "commit"),
                SafeString(RequiredString(application, "runtime")), SafeString(RequiredString(application, "runtimeIdentifier")),
                SafeString(RequiredString(application, "operatingSystem")), SafeString(RequiredString(application, "architecture"))),
            new SupportWorkspaceSummary(
                OptionalSafeString(workspace, "catalogId"), OptionalInt(workspace, "schemaVersion"), RequiredInt(workspace, "localeCount"), RequiredInt(workspace, "documentCount"),
                RequiredInt(workspace, "messageCount"), RequiredBool(workspace, "compilerSuccess"), RequiredBool(workspace, "reviewStateAvailable"),
                RequiredBool(workspace, "pendingTransaction"), RequiredInt(workspace, "pendingTransactionPathCount"), groups));
    }

    private static EditorDiagnosticGroup ParseGroup(JsonElement value)
    {
        RequireObject(value, "diagnostic group"); RequireProperties(value, "id", "severity", "count");
        return new EditorDiagnosticGroup(SafeString(RequiredString(value, "id")), SafeString(RequiredString(value, "severity")), RequiredInt(value, "count"));
    }

    private static void ValidateEnvelope(byte[] bytes)
    {
        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement root = document.RootElement; RequireObject(root, "support envelope"); RequireProperties(root, "Schema", "Collectors", "Omissions");
        if (RequiredString(root, "Schema") != EnvelopeSchema) throw new SupportUsageException("RTRSUP017", "The output is not a support envelope created by this command.");
    }

    private static byte[] CanonicalBytes(SupportEnvelope envelope) => JsonSerializer.SerializeToUtf8Bytes(envelope, SupportJsonContext.Default.SupportEnvelope);
    private static string Digest(SupportEnvelope envelope) => Digest(CanonicalBytes(envelope));
    private static string Digest(byte[] bytes) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static JsonElement Required(JsonElement value, string property) => value.TryGetProperty(property, out JsonElement result) ? result : throw new SupportUsageException("RTRSUP018", $"The diagnostic summary omitted '{property}'.");
    private static string RequiredString(JsonElement value, string property) => Required(value, property).ValueKind == JsonValueKind.String ? Required(value, property).GetString() ?? throw new SupportUsageException("RTRSUP019", $"The diagnostic summary has an invalid '{property}'.") : throw new SupportUsageException("RTRSUP019", $"The diagnostic summary has an invalid '{property}'.");
    private static string? OptionalSafeString(JsonElement value, string property) => Required(value, property).ValueKind == JsonValueKind.Null ? null : SafeString(RequiredString(value, property));
    private static int RequiredInt(JsonElement value, string property) => Required(value, property).TryGetInt32(out int result) && result >= 0 ? result : throw new SupportUsageException("RTRSUP020", $"The diagnostic summary has an invalid '{property}'.");
    private static int? OptionalInt(JsonElement value, string property) => Required(value, property).ValueKind == JsonValueKind.Null ? null : RequiredInt(value, property);
    private static bool RequiredBool(JsonElement value, string property) => Required(value, property).ValueKind is JsonValueKind.True or JsonValueKind.False ? Required(value, property).GetBoolean() : throw new SupportUsageException("RTRSUP021", $"The diagnostic summary has an invalid '{property}'.");
    private static void RequireObject(JsonElement value, string name) { if (value.ValueKind != JsonValueKind.Object) throw new SupportUsageException("RTRSUP022", $"The {name} must be an object."); }
    private static void RequireProperties(JsonElement value, params string[] names) { HashSet<string> actual = value.EnumerateObject().Select(static property => property.Name).ToHashSet(StringComparer.Ordinal); if (!actual.SetEquals(names)) throw new SupportUsageException("RTRSUP023", "The diagnostic summary contains unsupported fields."); }
    private static string SafeString(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains('/') || value.Contains('\\') || value.Contains("..", StringComparison.Ordinal) || value.Contains("token", StringComparison.OrdinalIgnoreCase) || value.Contains("cookie", StringComparison.OrdinalIgnoreCase) || value.Contains("session", StringComparison.OrdinalIgnoreCase))
            throw new SupportUsageException("RTRSUP024", "The selected diagnostic summary contains a path or sensitive text.");
        return value;
    }
}

internal sealed record SupportOptions(string Mode, string? EditorDiagnostics, string? Output);
internal sealed class SupportUsageException(string code, string message, Exception? innerException = null) : Exception(message, innerException) { internal string Code { get; } = code; }
internal sealed record SupportEnvelope(string Schema, IReadOnlyList<SupportCollector> Collectors, IReadOnlyList<SupportOmission> Omissions);
internal sealed record SupportCollector(string Id, string Schema, SupportApplicationIdentity Application, SupportWorkspaceSummary Workspace);
internal sealed record SupportOmission(string Source, string Reason);
internal sealed record SupportApplicationIdentity(string Product, string Version, string UpdateChannel, string? Commit, string Runtime, string RuntimeIdentifier, string OperatingSystem, string Architecture);
internal sealed record SupportWorkspaceSummary(string? CatalogId, int? SchemaVersion, int LocaleCount, int DocumentCount, int MessageCount, bool CompilerSuccess, bool ReviewStateAvailable, bool PendingTransaction, int PendingTransactionPathCount, IReadOnlyList<EditorDiagnosticGroup> Diagnostics);
internal sealed record EditorDiagnosticGroup(string Id, string Severity, int Count);
internal sealed record EditorDiagnosticSummary(string Schema, SupportApplicationIdentity Application, SupportWorkspaceSummary Workspace);

[JsonSerializable(typeof(SupportEnvelope))]
[JsonSerializable(typeof(SupportCollector))]
[JsonSerializable(typeof(SupportOmission))]
[JsonSerializable(typeof(SupportApplicationIdentity))]
[JsonSerializable(typeof(SupportWorkspaceSummary))]
[JsonSerializable(typeof(EditorDiagnosticGroup))]
[JsonSerializable(typeof(IReadOnlyList<SupportCollector>))]
[JsonSerializable(typeof(IReadOnlyList<SupportOmission>))]
[JsonSerializable(typeof(IReadOnlyList<EditorDiagnosticGroup>))]
internal sealed partial class SupportJsonContext : JsonSerializerContext;
