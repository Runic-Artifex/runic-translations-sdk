using System.Text;
using System.Text.Json;

namespace Runic.Translations.Editor;

/// <summary>
/// Owns the editor's small per-user state independently of a WebView/browser
/// profile. A complete replacement is written through a sibling temporary file
/// and published atomically, so an interrupted write leaves the preceding
/// valid snapshot available on the next launch.
/// </summary>
internal static class EditorLocalStateStore
{
    private const string Schema = "runic.translations.editor-local-state/1";
    private const int MaximumEntries = 64;
    private const int MaximumKeyLength = 512;
    private const int MaximumValueLength = 2 * 1024 * 1024;
    private const int MaximumBytes = 8 * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly HashSet<string> ExactKeys = new(StringComparer.Ordinal)
    {
        "runic-translations.theme-mode",
        "runic-translations.theme-palette",
        "runic-translations.pseudo-localization",
        "runic-translations.ui-direction",
        "runic-translations:recent:1",
        "runic.sidebar.languages",
        "runic.sidebar.messages",
        "runic.sidebar.languages-share",
    };

    internal static EditorLocalStateSnapshot Load()
    {
        string path = StatePath();
        if (!File.Exists(path)) return new EditorLocalStateSnapshot([], false);
        try
        {
            byte[] bytes = ReadBounded(path);
            using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("$schema", out JsonElement schema) ||
                schema.GetString() != Schema || !root.TryGetProperty("entries", out JsonElement entries) ||
                entries.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("The local editor-state record is not a supported snapshot.");
            var members = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty member in root.EnumerateObject())
            {
                if (!members.Add(member.Name) || (member.Name is not "$schema" and not "entries"))
                    throw new InvalidDataException("The local editor-state record has unsupported members.");
            }
            var values = new List<EditorLocalStateEntry>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement entry in entries.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("key", out JsonElement key) ||
                    !entry.TryGetProperty("value", out JsonElement value) || key.ValueKind != JsonValueKind.String ||
                    value.ValueKind != JsonValueKind.String)
                    throw new InvalidDataException("A local editor-state entry is invalid.");
                string stateKey = key.GetString()!;
                string stateValue = value.GetString()!;
                ValidateEntry(stateKey, stateValue);
                if (!keys.Add(stateKey)) throw new InvalidDataException("The local editor-state record contains duplicate keys.");
                values.Add(new EditorLocalStateEntry(stateKey, stateValue));
                if (values.Count > MaximumEntries) throw new InvalidDataException("The local editor-state record has too many entries.");
            }
            return new EditorLocalStateSnapshot(values.OrderBy(static entry => entry.Key, StringComparer.Ordinal).ToArray(), false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or DecoderFallbackException or InvalidDataException)
        {
            Quarantine(path);
            return new EditorLocalStateSnapshot([], true);
        }
    }

    internal static EditorLocalStateSnapshot Save(IReadOnlyList<EditorLocalStateEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count > MaximumEntries) throw new ArgumentException("The local editor-state record has too many entries.", nameof(entries));
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (EditorLocalStateEntry entry in entries)
        {
            ValidateEntry(entry.Key, entry.Value);
            if (!values.TryAdd(entry.Key, entry.Value))
                throw new ArgumentException("The local editor-state record contains duplicate keys.", nameof(entries));
        }
        byte[] bytes = Serialize(values);
        if (bytes.Length > MaximumBytes) throw new ArgumentException("The local editor-state record exceeds its size limit.", nameof(entries));
        string path = StatePath();
        string directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        return new EditorLocalStateSnapshot(values
            .OrderBy(static entry => entry.Key, StringComparer.Ordinal)
            .Select(static entry => new EditorLocalStateEntry(entry.Key, entry.Value))
            .ToArray(), false);
    }

    internal static EditorLocalStateClearResult Clear()
    {
        EditorLocalStateSnapshot current = Load();
        string path = StatePath();
        if (File.Exists(path)) File.Delete(path);
        return new EditorLocalStateClearResult(current.Entries.Count, current.Recovered);
    }

    private static byte[] Serialize(IReadOnlyDictionary<string, string> values)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("$schema", Schema);
            writer.WritePropertyName("entries");
            writer.WriteStartArray();
            foreach (KeyValuePair<string, string> entry in values.OrderBy(static entry => entry.Key, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("key", entry.Key);
                writer.WriteString("value", entry.Value);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static void ValidateEntry(string key, string value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        if (key.Length is 0 or > MaximumKeyLength || !IsAllowedKey(key))
            throw new ArgumentException("The local editor-state key is not owned by this editor.", nameof(key));
        if (value.Length > MaximumValueLength || StrictUtf8.GetByteCount(value) > MaximumValueLength)
            throw new ArgumentException("A local editor-state value is too large.", nameof(value));
    }

    private static bool IsAllowedKey(string key) => ExactKeys.Contains(key) ||
        key.StartsWith("runic-translations:drafts:1:", StringComparison.Ordinal);

    private static byte[] ReadBounded(string path)
    {
        FileInfo info = new(path);
        if (info.Length > MaximumBytes) throw new InvalidDataException("The local editor-state record exceeds its size limit.");
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("The local editor-state record exceeds its size limit.");
        _ = StrictUtf8.GetString(bytes);
        return bytes;
    }

    private static void Quarantine(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            string backup = path + ".corrupt-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
            File.Move(path, backup, false);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string StatePath()
    {
        string root;
        if (OperatingSystem.IsWindows())
        {
            root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }
        else if (OperatingSystem.IsMacOS())
        {
            root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support");
        }
        else
        {
            root = Environment.GetEnvironmentVariable("XDG_STATE_HOME") ??
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state");
        }
        if (string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("The per-user application-data directory is unavailable.");
        return Path.Combine(root, "RunicArtifex", "Runic.Translations.Editor", "editor-state-v1.json");
    }
}
