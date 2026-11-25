using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using Microsoft.Build.Framework;

namespace Runic.Translations.Build;

/// <summary>
/// Writes the CLDR subset of an application that sets <c>RunicTranslationsCldrSubset</c>: the records of the referenced
/// Runic.Translations' <c>GeneratedLocaleData.bin</c> (number formats, weekdays, day periods, list patterns and relative
/// time) whose locale has the language of a catalog locale or a language that CLDR links to it, as the ESM output selects
/// a catalog's CLDR data.
/// </summary>
/// <remarks>
/// The locales are those of each runic.json (<c>baseLocale</c>, <c>locales</c> and <c>pseudoLocales</c>; without
/// <c>locales</c>, the locales its sources name), of the packed catalogs of referenced packages, and the
/// <c>RunicTranslationsCldrLocale</c> items. The data comes from the referenced runtime assembly, so it matches the
/// runtime's tables. The output is written only when it changes.
/// </remarks>
public sealed class CldrLocaleSubset : ITask
{
    private const string ResourceName = "Runic.Translations.GeneratedLocaleData.bin";

    /// <summary>The application's runic.json files.</summary>
    public ITaskItem[] Projects { get; set; } = Array.Empty<ITaskItem>();
    /// <summary>The translation sources of the projects, whose locales count when a runic.json has no <c>locales</c>.</summary>
    public ITaskItem[] Sources { get; set; } = Array.Empty<ITaskItem>();
    /// <summary>The packed catalogs of referenced packages (<c>RunicTranslationsPackedCatalog</c>: their runic.json).</summary>
    public ITaskItem[] PackedCatalogs { get; set; } = Array.Empty<ITaskItem>();
    /// <summary>Further locales (<c>RunicTranslationsCldrLocale</c>), such as those an overlay adds at run time.</summary>
    public ITaskItem[] Locales { get; set; } = Array.Empty<ITaskItem>();
    /// <summary>The resolved references; the subset reads the data of the one named Runic.Translations.</summary>
    [Required]
    public ITaskItem[] References { get; set; } = Array.Empty<ITaskItem>();
    /// <summary>The deflated subset to write.</summary>
    [Required]
    public string OutputFile { get; set; } = "";
    /// <summary>The CLDR languages the subset covers, sorted.</summary>
    [Output]
    public string[] Languages { get; set; } = Array.Empty<string>();
    public IBuildEngine BuildEngine { get; set; } = null!;
    public ITaskHost HostObject { get; set; } = null!;

    public bool Execute()
    {
        try
        {
            var tags = new SortedSet<string>(StringComparer.Ordinal);
            foreach (ITaskItem project in Projects) ReadProject(project.ItemSpec, tags, includeSources: true);
            foreach (ITaskItem packed in PackedCatalogs) ReadProject(packed.ItemSpec, tags, includeSources: false);
            foreach (ITaskItem locale in Locales) tags.Add(locale.ItemSpec);
            HashSet<string> languages = CatalogLanguages(tags.Select(Normalize).Where(tag => tag.Length != 0));
            if (languages.Count == 0)
                throw new InvalidOperationException("RunicTranslationsCldrSubset found no catalog locale: declare a RunicTranslationProject or RunicTranslationsCldrLocale items.");
            Languages = languages.OrderBy(language => language, StringComparer.Ordinal).ToArray();

            ITaskItem runtime = References.FirstOrDefault(reference => string.Equals(Path.GetFileNameWithoutExtension(reference.ItemSpec), "Runic.Translations", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("RunicTranslationsCldrSubset needs a reference to Runic.Translations.");
            byte[] subset = Subset(Inflate(ReadResource(runtime.ItemSpec)), languages, out int records);
            byte[] compressed = Deflate(subset);
            string output = Path.GetFullPath(OutputFile);
            if (!File.Exists(output) || !File.ReadAllBytes(output).AsSpan().SequenceEqual(compressed))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                File.WriteAllBytes(output, compressed);
            }
            BuildEngine.LogMessageEvent(new BuildMessageEventArgs(
                $"CLDR subset: {records} records, {compressed.Length} bytes, for the languages {string.Join(", ", Languages)}.", "", nameof(CldrLocaleSubset), MessageImportance.Low));
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or BadImageFormatException or InvalidDataException)
        {
            BuildEngine.LogErrorEvent(new BuildErrorEventArgs("translations", "RTR0108", "", 0, 0, 0, 0, exception.Message, "", nameof(CldrLocaleSubset)));
            return false;
        }
    }

    // baseLocale, locales (strings, or objects with a tag) and pseudoLocales; without locales, the locale each source names:
    // the stem of a grouped .rmf2 file and the directories of a direct .mf2 source (an extra tag only adds data).
    private void ReadProject(string path, SortedSet<string> tags, bool includeSources)
    {
        string config = Path.GetFullPath(path);
        if (new FileInfo(config).Length > 8 * 1024 * 1024) throw new IOException("Translation project exceeds byte limit.");
        using var json = JsonDocument.Parse(File.ReadAllBytes(config), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        JsonElement root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("runic.json must contain a JSON object.");
        if (root.TryGetProperty("baseLocale", out JsonElement baseLocale) && baseLocale.ValueKind == JsonValueKind.String) tags.Add(baseLocale.GetString()!);
        bool declared = false;
        foreach (string member in new[] { "locales", "pseudoLocales" })
        {
            if (!root.TryGetProperty(member, out JsonElement list) || list.ValueKind != JsonValueKind.Array) continue;
            declared |= member == "locales";
            foreach (JsonElement item in list.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String) tags.Add(item.GetString()!);
                else if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("tag", out JsonElement tag) && tag.ValueKind == JsonValueKind.String) tags.Add(tag.GetString()!);
            }
        }
        if (declared || !includeSources) return;
        string directory = Path.GetDirectoryName(config)!;
        foreach (ITaskItem source in Sources)
        {
            string file = Path.GetFullPath(source.ItemSpec);
            if (Runic.Translations.Compiler.TranslationCatalogFormats.IsResourceSource(file)) tags.Add(Path.GetFileNameWithoutExtension(file));
            else if (string.Equals(Path.GetExtension(file), ".mf2", StringComparison.OrdinalIgnoreCase))
                foreach (string segment in Path.GetRelativePath(directory, Path.GetDirectoryName(file)!).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                    tags.Add(segment);
        }
    }

    // A BCP 47 tag in canonical case (en_US → en-US, ZH-hant → zh-Hant), or "" when it cannot be one. Extensions are ignored.
    internal static string Normalize(string tag)
    {
        string[] parts = tag.Trim().Replace('_', '-').Split('-');
        if (parts[0].Length is < 2 or > 8 || !parts[0].All(char.IsAsciiLetter)) return "";
        var result = new List<string> { parts[0].ToLowerInvariant() };
        for (int index = 1; index < parts.Length; index++)
        {
            string part = parts[index];
            if (part.Length == 1 || !part.All(char.IsAsciiLetterOrDigit)) break;
            result.Add(part.Length == 4 && char.IsAsciiLetter(part[0]) ? char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()
                : part.Length == 2 ? part.ToUpperInvariant() : part.ToLowerInvariant());
        }
        return string.Join("-", result);
    }

    /// <summary>
    /// The CLDR languages a catalog's data covers: its locales' languages and the languages their CLDR parents link to
    /// (<c>nb</c> and <c>nn</c> with <c>no</c>, <c>hi</c> with <c>en</c> through <c>hi-Latn</c>), as the compiler's
    /// TranslationCapabilityRegistry.CatalogLanguages selects them for the ESM output.
    /// </summary>
    internal static HashSet<string> CatalogLanguages(IEnumerable<string> locales)
    {
        var languages = new HashSet<string>(locales.Select(locale => CldrLocales.Language(CldrLocales.Parse(locale).Language)), StringComparer.Ordinal);
        for (bool changed = true; changed;)
        {
            changed = false;
            for (int index = 0; index < GeneratedLocaleTables.ParentKeys.Length; index++)
            {
                string child = GeneratedLocaleTables.ParentKeys[index], parent = GeneratedLocaleTables.ParentValues[index];
                if (parent == "root") continue;
                string childLanguage = CldrLocales.Parse(child).Language, parentLanguage = CldrLocales.Parse(parent).Language;
                if (languages.Contains(childLanguage)) changed |= languages.Add(parentLanguage);
                if (languages.Contains(parentLanguage) && child == childLanguage) changed |= languages.Add(childLanguage);
            }
        }
        return languages;
    }

    /// <summary>
    /// The resource JSON (<c>{section: {id: record}}</c>) with the records of <paramref name="languages"/> only, and CLDR
    /// root's (<c>list:root</c>, <c>relativeTime:root</c>), whose language-neutral patterns a locale outside the subset uses.
    /// </summary>
    internal static byte[] Subset(byte[] json, HashSet<string> languages, out int records)
    {
        using var document = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        records = 0;
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (JsonProperty section in document.RootElement.EnumerateObject())
            {
                writer.WriteStartObject(section.Name);
                foreach (JsonProperty record in section.Value.EnumerateObject())
                {
                    if (record.Name != "root" && !languages.Contains(CldrLocales.Parse(record.Name).Language)) continue;
                    record.WriteTo(writer);
                    records++;
                }
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    // The manifest resource of an assembly, read as metadata: the task does not load the assembly.
    private static byte[] ReadResource(string assembly)
    {
        using var stream = File.OpenRead(assembly);
        using var pe = new PEReader(stream);
        MetadataReader metadata = pe.GetMetadataReader();
        foreach (ManifestResourceHandle handle in metadata.ManifestResources)
        {
            ManifestResource resource = metadata.GetManifestResource(handle);
            if (!resource.Implementation.IsNil || !metadata.StringComparer.Equals(resource.Name, ResourceName)) continue;
            PEMemoryBlock block = pe.GetSectionData(pe.PEHeaders.CorHeader!.ResourcesDirectory.RelativeVirtualAddress);
            BlobReader reader = block.GetReader((int)resource.Offset, block.Length - (int)resource.Offset);
            int length = reader.ReadInt32();
            return reader.ReadBytes(length);
        }
        throw new InvalidOperationException($"'{assembly}' has no {ResourceName}: RunicTranslationsCldrSubset needs Runic.Translations 0.14 or later.");
    }

    private static byte[] Inflate(byte[] compressed)
    {
        using var inflater = new DeflateStream(new MemoryStream(compressed), CompressionMode.Decompress);
        using var buffer = new MemoryStream();
        inflater.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static byte[] Deflate(byte[] data)
    {
        using var buffer = new MemoryStream();
        using (var deflater = new DeflateStream(buffer, CompressionLevel.SmallestSize)) deflater.Write(data);
        return buffer.ToArray();
    }
}
