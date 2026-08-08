using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace RunicTextResources.Authoring;

public static partial class TextResourceEditorStateStore
{
    public const string Schema = "runic.textresources.editor-state/1";
    public const int MaximumBytes = 8 * 1024 * 1024;
    public const int MaximumEntries = 50_000;
    public const int MaximumTerms = 2_000;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly string[] States = ["draft", "translated", "needs-review", "approved"];

    public static TextResourceEditorStateLoadResult Load(string root, string catalogId)
    {
        string path = RelativePath(catalogId);
        string fullPath = Resolve(root, path, forWrite: false);
        TextResourceEditorState empty = new(catalogId, [], []);
        if (!File.Exists(fullPath)) return new TextResourceEditorStateLoadResult(path, null, empty, null);
        try
        {
            byte[] bytes = ReadBounded(fullPath);
            string revision = Revision(bytes);
            using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            JsonElement value = document.RootElement;
            RequireObject(value, "The editor state root must be an object.");
            RequireExactMembers(value, "$schema", "catalog", "messages", "terminology");
            if (RequiredString(value, "$schema") != Schema)
                throw new TextResourceEditorStateException($"Unsupported editor-state schema. Expected '{Schema}'.");
            if (!string.Equals(RequiredString(value, "catalog"), catalogId, StringComparison.Ordinal))
                throw new TextResourceEditorStateException("The editor-state catalog does not match the selected catalog.");

            JsonElement messages = Required(value, "messages", JsonValueKind.Object);
            RequireUniqueMembers(messages);
            var entries = new List<TextResourceEditorStateEntry>();
            foreach (JsonProperty key in messages.EnumerateObject())
            {
                RequireName(key.Name, "message key");
                RequireObject(key.Value, "Each message state must be an object.");
                RequireUniqueMembers(key.Value);
                foreach (JsonProperty locale in key.Value.EnumerateObject())
                {
                    RequireName(locale.Name, "locale");
                    RequireObject(locale.Value, "Each locale state must be an object.");
                    RequireAllowedMembers(locale.Value, "state", "note", "sourceFingerprint", "samples");
                    string state = RequiredString(locale.Value, "state");
                    if (!States.Contains(state, StringComparer.Ordinal))
                        throw new TextResourceEditorStateException($"Unknown review state '{state}'.");
                    string? note = OptionalString(locale.Value, "note", 16_384);
                    string? sourceFingerprint = OptionalString(locale.Value, "sourceFingerprint", 256);
                    var samples = new SortedDictionary<string, string>(StringComparer.Ordinal);
                    if (locale.Value.TryGetProperty("samples", out JsonElement sampleValue))
                    {
                        RequireObject(sampleValue, "samples must be an object.");
                        RequireUniqueMembers(sampleValue);
                        foreach (JsonProperty sample in sampleValue.EnumerateObject())
                        {
                            RequireName(sample.Name, "sample");
                            if (sample.Value.ValueKind != JsonValueKind.String)
                                throw new TextResourceEditorStateException("Sample values must be strings.");
                            string text = sample.Value.GetString()!;
                            if (text.Length > 16_384) throw new TextResourceEditorStateException("A sample value is too large.");
                            samples.Add(sample.Name, text);
                            if (samples.Count > 32) throw new TextResourceEditorStateException("An editor-state entry contains too many samples.");
                        }
                    }
                    entries.Add(new TextResourceEditorStateEntry(key.Name, locale.Name, state, note, sourceFingerprint, samples));
                    if (entries.Count > MaximumEntries) throw new TextResourceEditorStateException("The editor state contains too many message entries.");
                }
            }

            JsonElement terminology = Required(value, "terminology", JsonValueKind.Array);
            var terms = new List<TextResourceTerminologyEntry>();
            foreach (JsonElement term in terminology.EnumerateArray())
            {
                RequireObject(term, "Each terminology entry must be an object.");
                RequireAllowedMembers(term, "source", "preferred", "locale", "note");
                string source = RequiredString(term, "source");
                string preferred = RequiredString(term, "preferred");
                if (source.Length > 4_096 || preferred.Length > 4_096)
                    throw new TextResourceEditorStateException("A terminology value is too large.");
                terms.Add(new TextResourceTerminologyEntry(
                    source, preferred, OptionalString(term, "locale", 128), OptionalString(term, "note", 16_384)));
                if (terms.Count > MaximumTerms) throw new TextResourceEditorStateException("The editor state contains too many terminology entries.");
            }
            entries.Sort(static (left, right) => {
                int key = StringComparer.Ordinal.Compare(left.Key, right.Key);
                return key != 0 ? key : StringComparer.Ordinal.Compare(left.Locale, right.Locale);
            });
            terms.Sort(static (left, right) => {
                int source = StringComparer.Ordinal.Compare(left.Source, right.Source);
                return source != 0 ? source : StringComparer.Ordinal.Compare(left.Locale, right.Locale);
            });
            return new TextResourceEditorStateLoadResult(path, revision, new TextResourceEditorState(catalogId, entries, terms), null);
        }
        catch (Exception exception) when (exception is JsonException or DecoderFallbackException or TextResourceEditorStateException or IOException)
        {
            string? revision = null;
            try { revision = Revision(File.ReadAllBytes(fullPath)); } catch (IOException) { }
            return new TextResourceEditorStateLoadResult(path, revision, empty, exception.Message);
        }
    }

    public static TextResourceEditorStateLoadResult Save(
        string root,
        TextResourceEditorState state,
        string? expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidateState(state);
        string path = RelativePath(state.CatalogId);
        string fullPath = Resolve(root, path, forWrite: true);
        byte[]? current = File.Exists(fullPath) ? ReadBounded(fullPath) : null;
        string? revision = current is null ? null : Revision(current);
        if (!string.Equals(revision, expectedRevision, StringComparison.Ordinal))
            throw new TextResourceEditorStateException("The editor-state sidecar changed on disk. Reload before saving review data.");

        byte[] bytes = StrictUtf8.GetBytes(Render(state));
        if (bytes.Length > MaximumBytes) throw new TextResourceEditorStateException("The editor state exceeds the size limit.");
        string directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        RejectLink(directory);
        string temporary = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, fullPath, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        return new TextResourceEditorStateLoadResult(path, Revision(bytes), state, null);
    }

    private static string Render(TextResourceEditorState state)
    {
        var messages = new JsonObject();
        foreach (TextResourceEditorStateEntry entry in state.Entries.OrderBy(static item => item.Key, StringComparer.Ordinal)
            .ThenBy(static item => item.Locale, StringComparer.Ordinal))
        {
            if (messages[entry.Key] is not JsonObject locales)
            {
                locales = new JsonObject();
                messages.Add(entry.Key, locales);
            }
            var value = new JsonObject { ["state"] = entry.State };
            if (entry.Note is not null) value.Add("note", entry.Note);
            if (entry.SourceFingerprint is not null) value.Add("sourceFingerprint", entry.SourceFingerprint);
            if (entry.Samples.Count > 0)
            {
                var samples = new JsonObject();
                foreach (KeyValuePair<string, string> sample in entry.Samples.OrderBy(static item => item.Key, StringComparer.Ordinal))
                    samples.Add(sample.Key, sample.Value);
                value.Add("samples", samples);
            }
            locales.Add(entry.Locale, value);
        }
        var terminology = new JsonArray();
        foreach (TextResourceTerminologyEntry term in state.Terminology.OrderBy(static item => item.Source, StringComparer.Ordinal)
            .ThenBy(static item => item.Locale, StringComparer.Ordinal))
        {
            var value = new JsonObject { ["source"] = term.Source, ["preferred"] = term.Preferred };
            if (term.Locale is not null) value.Add("locale", term.Locale);
            if (term.Note is not null) value.Add("note", term.Note);
            terminology.Add(value);
        }
        var root = new JsonObject {
            ["$schema"] = Schema,
            ["catalog"] = state.CatalogId,
            ["messages"] = messages,
            ["terminology"] = terminology,
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
    }

    private static void ValidateState(TextResourceEditorState state)
    {
        _ = RelativePath(state.CatalogId);
        if (state.Entries.Count > MaximumEntries || state.Terminology.Count > MaximumTerms)
            throw new TextResourceEditorStateException("The editor state exceeds its entry limit.");
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (TextResourceEditorStateEntry entry in state.Entries)
        {
            RequireName(entry.Key, "message key");
            RequireName(entry.Locale, "locale");
            if (!States.Contains(entry.State, StringComparer.Ordinal))
                throw new TextResourceEditorStateException($"Unknown review state '{entry.State}'.");
            if (!identities.Add(entry.Key + "\0" + entry.Locale))
                throw new TextResourceEditorStateException("Duplicate message/locale editor-state entry.");
            if (entry.Note?.Length > 16_384 || entry.SourceFingerprint?.Length > 256 || entry.Samples.Count > 32 ||
                entry.Samples.Any(static sample => sample.Key.Length > 128 || sample.Value.Length > 16_384))
                throw new TextResourceEditorStateException("An editor-state entry exceeds its bounded fields.");
        }
        foreach (TextResourceTerminologyEntry term in state.Terminology)
            if (term.Source.Length == 0 || term.Source.Length > 4_096 || term.Preferred.Length == 0 || term.Preferred.Length > 4_096 ||
                term.Locale?.Length > 128 || term.Note?.Length > 16_384)
                throw new TextResourceEditorStateException("A terminology entry is invalid.");
    }

    private static string RelativePath(string catalogId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogId);
        if (!CatalogId().IsMatch(catalogId)) throw new TextResourceEditorStateException("The editor-state catalog ID is invalid.");
        return $".runic-textresources/{catalogId}.editor-state.json";
    }

    private static string Resolve(string root, string relativePath, bool forWrite)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        string boundaryRoot = Path.GetFullPath(root);
        string boundary = boundaryRoot.EndsWith(Path.DirectorySeparatorChar) ? boundaryRoot : boundaryRoot + Path.DirectorySeparatorChar;
        string fullPath = Path.GetFullPath(relativePath.Replace('/', Path.DirectorySeparatorChar), boundaryRoot);
        if (!fullPath.StartsWith(boundary, StringComparison.Ordinal))
            throw new TextResourceEditorStateException("The editor-state path escapes the workspace.");
        RejectLink(boundaryRoot);
        string? parent = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent)) RejectLink(parent);
        if (!forWrite && File.Exists(fullPath)) RejectLink(fullPath);
        return fullPath;
    }

    private static void RejectLink(string path)
    {
        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new TextResourceEditorStateException("Editor-state paths cannot traverse links or reparse points.");
    }

    private static byte[] ReadBounded(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > MaximumBytes) throw new TextResourceEditorStateException("The editor-state sidecar exceeds the size limit.");
        return File.ReadAllBytes(path);
    }

    private static string Revision(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static void RequireObject(JsonElement value, string message) { if (value.ValueKind != JsonValueKind.Object) throw new TextResourceEditorStateException(message); }
    private static JsonElement Required(JsonElement value, string name, JsonValueKind kind) {
        if (!value.TryGetProperty(name, out JsonElement property) || property.ValueKind != kind) throw new TextResourceEditorStateException($"'{name}' is required and must be {kind}.");
        return property;
    }
    private static string RequiredString(JsonElement value, string name) => Required(value, name, JsonValueKind.String).GetString()!;
    private static string? OptionalString(JsonElement value, string name, int maximum) {
        if (!value.TryGetProperty(name, out JsonElement property)) return null;
        if (property.ValueKind != JsonValueKind.String) throw new TextResourceEditorStateException($"'{name}' must be a string.");
        string result = property.GetString()!;
        if (result.Length > maximum) throw new TextResourceEditorStateException($"'{name}' is too large.");
        return result;
    }
    private static void RequireName(string value, string kind) {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 512 || value.Contains('\0', StringComparison.Ordinal)) throw new TextResourceEditorStateException($"Invalid {kind}.");
    }
    private static void RequireExactMembers(JsonElement value, params string[] names) {
        RequireAllowedMembers(value, names);
        foreach (string name in names) if (!value.TryGetProperty(name, out _)) throw new TextResourceEditorStateException($"'{name}' is required.");
    }
    private static void RequireAllowedMembers(JsonElement value, params string[] names) {
        var allowed = new HashSet<string>(names, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in value.EnumerateObject()) {
            if (!seen.Add(property.Name)) throw new TextResourceEditorStateException($"Duplicate editor-state member '{property.Name}'.");
            if (!allowed.Contains(property.Name)) throw new TextResourceEditorStateException($"Unknown editor-state member '{property.Name}'.");
        }
    }
    private static void RequireUniqueMembers(JsonElement value) {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in value.EnumerateObject())
            if (!seen.Add(property.Name)) throw new TextResourceEditorStateException($"Duplicate editor-state member '{property.Name}'.");
    }

    [GeneratedRegex("^[a-z][a-z0-9.-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex CatalogId();
}
