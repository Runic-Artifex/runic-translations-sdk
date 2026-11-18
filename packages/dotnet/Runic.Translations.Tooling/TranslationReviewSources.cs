using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Runic.Translations.Tooling;

/// <summary>One recorded base text: the MF2 source of message <paramref name="Key"/> whose per-unit fingerprint is <paramref name="Fingerprint"/>.</summary>
internal sealed record TranslationReviewSource(string Key, string Fingerprint, string Text);

/// <summary>
/// The base texts that review states were recorded for, in <c>&lt;catalog&gt;.review-sources.json</c> beside the review
/// file (<c>runic.translations.review-sources/1</c>). The review file keeps only each entry's source fingerprint, and its
/// published readers (Tooling 0.14 and earlier) refuse unknown members and schemas, so the text lives in this companion
/// file: older tools neither read nor write it and keep working with the review file as before.
/// <para>
/// The file is content-addressed: one text per message key and fingerprint, shared by every locale whose entry has that
/// fingerprint. A fingerprint hashes the key, the source text and the caller contract, so a text never goes out of date;
/// a review file another tool wrote without this file simply has entries without a recorded text. Writers add the text
/// of each entry they record and drop texts no entry of the review file refers to any more. Readers ignore members they
/// do not know, so the format can grow without a new version, and treat an unreadable file as empty.
/// </para>
/// </summary>
internal static class TranslationReviewSources
{
    internal const string Schema = "runic.translations.review-sources/1";
    internal const int MaximumBytes = 8 * 1024 * 1024;
    /// <summary>The longest text that is recorded, in UTF-16 code units; a longer base message is not recorded.</summary>
    internal const int MaximumTextLength = 65_536;

    /// <summary>
    /// The companion file of the review file <paramref name="reviewPath"/>: <c>app.review.json</c> → <c>app.review-sources.json</c>;
    /// any other name gets <c>-sources.json</c> in place of its <c>.json</c> extension.
    /// </summary>
    internal static string PathFor(string reviewPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(reviewPath);
        string directory = Path.GetDirectoryName(reviewPath) ?? string.Empty;
        string name = Path.GetFileName(reviewPath);
        string stem = name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? name[..^".json".Length] : name;
        return Path.Combine(directory, stem + "-sources.json");
    }

    /// <summary>The recorded texts by <see cref="TranslationReviewFile.Identity"/> of key and fingerprint; empty when the file is missing or unreadable.</summary>
    internal static IReadOnlyDictionary<string, string> Read(string reviewPath, string catalog) =>
        Parse(ReadBytes(PathFor(reviewPath)), catalog);

    /// <summary>The text <paramref name="fingerprint"/> of <paramref name="key"/> was computed from, when it was recorded.</summary>
    internal static string? Text(IReadOnlyDictionary<string, string> sources, string key, string? fingerprint) =>
        fingerprint is not null && sources.TryGetValue(TranslationReviewFile.Identity(key, fingerprint), out string? text) ? text : null;

    /// <summary>
    /// Adds <paramref name="added"/> to the companion file of <paramref name="reviewPath"/> and drops the texts that
    /// neither an entry of the review file on disk nor <paramref name="added"/> refers to. A writer calls it before it
    /// writes the review file, so a reader that sees the new entry also sees its text. Concurrent writers are merged
    /// (compare-and-swap, as the review file is written); a file that cannot be read is replaced.
    /// </summary>
    internal static void Record(string reviewPath, string catalog, IEnumerable<TranslationReviewSource> added)
    {
        ArgumentNullException.ThrowIfNull(added);
        TranslationReviewSource[] additions = added.Where(IsRecordable).ToArray();
        if (additions.Length == 0) return;
        string path = PathFor(reviewPath);
        for (int attempt = 1; ; attempt++)
        {
            byte[]? before = ReadBytes(path);
            TranslationInterchangeReview? review;
            try { review = TranslationReviewFile.Read(reviewPath, catalog); }
            catch (TranslationInterchangeException) { review = null; }
            byte[]? after = Merge(before, catalog, review, additions);
            if (after is null || (before is not null && before.AsSpan().SequenceEqual(after))) return;
            try
            {
                TranslationReviewFile.WriteRendered(path, after, before);
                return;
            }
            catch (TranslationInterchangeException exception) when (exception.Code == TranslationReviewFile.ChangedCode)
            {
                // Another writer replaced the file in between: merge again, a few times, then leave the texts out.
                if (attempt >= 5) return;
            }
        }
    }

    /// <summary>
    /// The companion file after adding <paramref name="added"/> to <paramref name="existing"/>, keeping only the texts an
    /// entry of <paramref name="review"/> (when known) or <paramref name="added"/> refers to; null when nothing is to be
    /// written (no file and nothing to record, or the result would exceed 8 MiB).
    /// </summary>
    internal static byte[]? Merge(byte[]? existing, string catalog, TranslationInterchangeReview? review, IEnumerable<TranslationReviewSource> added)
    {
        var texts = new SortedDictionary<(string Key, string Fingerprint), string>(Comparer<(string Key, string Fingerprint)>.Create(static (left, right) =>
        {
            int key = string.CompareOrdinal(left.Key, right.Key);
            return key != 0 ? key : string.CompareOrdinal(left.Fingerprint, right.Fingerprint);
        }));
        foreach ((string key, string fingerprint, string text) in Entries(existing, catalog)) texts[(key, fingerprint)] = text;
        var kept = new HashSet<string>(StringComparer.Ordinal);
        foreach (TranslationReviewSource source in added.Where(IsRecordable))
        {
            texts[(source.Key, source.Fingerprint)] = source.Text;
            kept.Add(TranslationReviewFile.Identity(source.Key, source.Fingerprint));
        }
        if (review is not null)
        {
            foreach (TranslationInterchangeReviewEntry entry in review.Entries)
                if (entry.SourceFingerprint is { } fingerprint) kept.Add(TranslationReviewFile.Identity(entry.Key, fingerprint));
            foreach ((string Key, string Fingerprint) identity in texts.Keys.ToArray())
                if (!kept.Contains(TranslationReviewFile.Identity(identity.Key, identity.Fingerprint))) texts.Remove(identity);
        }
        if (existing is null && texts.Count == 0) return null;
        byte[] bytes = Render(catalog, texts.Select(static item => new TranslationReviewSource(item.Key.Key, item.Key.Fingerprint, item.Value)));
        return bytes.Length > MaximumBytes ? null : bytes;
    }

    /// <summary>Renders the file: indented, sorted by key and fingerprint, text unescaped where JSON allows it, one trailing newline.</summary>
    internal static byte[] Render(string catalog, IEnumerable<TranslationReviewSource> sources)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteString("$schema", Schema);
            writer.WriteString("catalog", catalog);
            writer.WriteStartArray("sources");
            foreach (TranslationReviewSource source in sources.OrderBy(static item => item.Key, StringComparer.Ordinal).ThenBy(static item => item.Fingerprint, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("key", source.Key);
                writer.WriteString("sourceFingerprint", source.Fingerprint);
                writer.WriteString("source", source.Text);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    /// <summary>The texts of <paramref name="bytes"/> by key and fingerprint; empty when the bytes are missing, malformed or of another catalog.</summary>
    internal static IReadOnlyDictionary<string, string> Parse(byte[]? bytes, string catalog)
    {
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string key, string fingerprint, string text) in Entries(bytes, catalog)) texts[TranslationReviewFile.Identity(key, fingerprint)] = text;
        return texts;
    }

    private static List<(string Key, string Fingerprint, string Text)> Entries(byte[]? bytes, string catalog)
    {
        if (bytes is null || bytes.Length == 0 || bytes.Length > MaximumBytes) return [];
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("$schema", out JsonElement schema) || schema.ValueKind != JsonValueKind.String || schema.GetString() != Schema ||
                !root.TryGetProperty("catalog", out JsonElement owner) || owner.ValueKind != JsonValueKind.String || owner.GetString() != catalog ||
                !root.TryGetProperty("sources", out JsonElement sources) || sources.ValueKind != JsonValueKind.Array) return [];
            var entries = new List<(string, string, string)>();
            foreach (JsonElement item in sources.EnumerateArray())
            {
                // An entry this version cannot use is skipped; unknown members are ignored.
                if (item.ValueKind != JsonValueKind.Object || String(item, "key") is not { } key || String(item, "sourceFingerprint") is not { } fingerprint ||
                    String(item, "source") is not { } text || !IsRecordable(new TranslationReviewSource(key, fingerprint, text))) continue;
                entries.Add((key, fingerprint, text));
            }
            return entries;
        }
        catch (JsonException) { return []; }

        static string? String(JsonElement item, string name) =>
            item.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static bool IsRecordable(TranslationReviewSource source) =>
        !string.IsNullOrEmpty(source.Key) && !string.IsNullOrEmpty(source.Fingerprint) && source.Fingerprint.Length <= 256 &&
        source.Text is not null && source.Text.Length <= MaximumTextLength;

    private static byte[]? ReadBytes(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > MaximumBytes) return null;
            return File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return null; }
    }
}
