using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Runic.Translations.Compiler.Generation;
using Runic.Translations.Generated;

namespace Runic.Translations.Compiler.Tests;

// specs/translations/corpus/compiled: the locale artifacts the compiler produces for the corpora, with the pack contract each
// one is read against, so that a runtime without the C# compiler runs the corpora (D080, R7 G15). The corpus runners read
// these committed files for their pack routes; CompiledCorpusTests fails when they are stale, and `bun eng/generate-corpus.mjs`
// rewrites them. Each corpus test class composes its own groups (CompiledGroups), so the committed files and the test's
// linked routes come from the same projects.
internal static class CompiledCorpus
{
    internal static string Root => RepositoryPaths.Resolve("specs", "translations", "corpus", "compiled");

    // One locale artifact of a compiled project. Corpus is the corpus file the group serves, relative to the corpus directory.
    // CaseIds maps a message corpus's case ids, in order, to the keys vector0, vector1, … of the project.
    internal sealed record Group(string Id, string Corpus, Rmf2ProjectV5 Project, string Locale, IReadOnlyList<string>? CaseIds = null);

    private static readonly JavaScriptEncoder Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

    internal static IEnumerable<Group> Groups()
    {
        Rmf2ProjectV5 oracle = Rmf2V1CorpusTests.Compile();
        foreach (Rmf2LocaleV5 locale in oracle.Locales) yield return new Group("rmf2-v1/" + locale.Tag, "rmf2-v1/index.json", oracle, locale.Tag);
        Rmf2ProjectV5 document = Rmf2DocumentCorpusTests.Compile();
        foreach (Rmf2LocaleV5 locale in document.Locales) yield return new Group("rmf2-document-v1/" + locale.Tag, "rmf2-document-v1/index.json", document, locale.Tag);
        Rmf2ProjectV5 fixture = Rmf2EsmV5Tests.Fixture();
        foreach (Rmf2LocaleV5 locale in fixture.Locales) yield return new Group("v5-project/" + locale.Tag, "v5-project/expected-contract.json", fixture, locale.Tag);
        yield return new Group("format-data-invalid/en", "rmf2-format-data-invalid.json", Rmf2FormatDataValidationTests.Project(), "en");
        foreach (Group group in TransportV2ConformanceTests.CompiledGroups()) yield return group;
        foreach (Group group in Rmf2ListConformanceTests.CompiledGroups()) yield return group;
        foreach (Group group in Rmf2GroupingConformanceTests.CompiledGroups()) yield return group;
        foreach (Group group in Rmf2DatePercentConformanceTests.CompiledGroups()) yield return group;
        foreach (Group group in Rmf2BidiConformanceTests.CompiledGroups()) yield return group;
        foreach (Group group in Rmf2CurrencyUnitConformanceTests.CompiledGroups()) yield return group;
    }

    // Every file of the compiled directory, by path relative to it.
    internal static SortedDictionary<string, byte[]> Export()
    {
        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        var groups = new List<(Group Group, string Sha256)>();
        foreach (Group group in Groups())
        {
            byte[] artifact = Rmf2LocaleArtifactV5.Render(group.Project, group.Locale).GetUtf8Bytes();
            files.Add(group.Id + ".json", artifact);
            files.Add(group.Id + ".contract.json", Contract(group.Project, group.Locale));
            groups.Add((group, "sha256:" + Convert.ToHexStringLower(SHA256.HashData(artifact))));
        }
        files.Add("index.json", Index(groups));
        return files;
    }

    private static byte[] Index(IReadOnlyList<(Group Group, string Sha256)> groups) => Write(writer =>
    {
        writer.WriteStartObject();
        writer.WriteNumber("formatVersion", 1);
        writer.WriteString("generator", "bun eng/generate-corpus.mjs");
        writer.WriteStartArray("groups");
        foreach ((Group group, string sha256) in groups)
        {
            writer.WriteStartObject();
            writer.WriteString("id", group.Id);
            writer.WriteString("corpus", group.Corpus);
            writer.WriteString("artifact", group.Id + ".json");
            writer.WriteString("contract", group.Id + ".contract.json");
            writer.WriteString("sha256", sha256);
            if (group.CaseIds is { } ids)
            {
                writer.WriteStartObject("cases");
                for (int index = 0; index < ids.Count; index++) writer.WriteString(ids[index], "vector" + index);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    });

    // The pack contract of one locale, as the generated code of the project holds it (Rmf2V1CorpusTests.PackContract): the
    // canonical messages by id, then the locale-only messages that this locale resolves, numbered after the canonical ones in
    // ordinal key order. markupContract is the project's markup contract JSON, byte for byte.
    internal static byte[] Contract(Rmf2ProjectV5 project, string locale)
    {
        HashSet<string> resolved = project.Locales.Single(item => item.Tag == locale).ResolvedResources.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
        Rmf2MessageContractV5[] canonical = project.CanonicalMessages.OrderBy(item => item.Id).ToArray();
        Rmf2MessageContractV5[] extras = project.ExtraMessages.OrderBy(item => item.Key, StringComparer.Ordinal).ToArray();
        var messages = canonical.Select(item => (Contract: item, Id: item.Id, Canonical: true))
            .Concat(extras.Select((item, index) => (Contract: item, Id: canonical.Length + index, Canonical: false)))
            .Where(item => item.Canonical || resolved.Contains(item.Contract.Key));
        return Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("catalog", project.Id);
            writer.WriteString("locale", locale);
            writer.WriteString("contractFingerprint", project.CallerFingerprint);
            writer.WriteString("bidiIsolation", project.BidiIsolation.ToString().ToLowerInvariant());
            writer.WriteStartArray("locales");
            foreach (Rmf2LocaleV5 item in project.Locales)
            {
                writer.WriteStartObject();
                writer.WriteString("tag", item.Tag);
                if (item.FallbackTag is null) writer.WriteNull("fallback"); else writer.WriteString("fallback", item.FallbackTag);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("messages");
            foreach ((Rmf2MessageContractV5 message, int id, bool isCanonical) in messages)
            {
                writer.WriteStartObject();
                writer.WriteNumber("id", id);
                writer.WriteString("key", message.Key);
                writer.WriteBoolean("canonical", isCanonical);
                writer.WriteStartArray("inputs");
                foreach (Rmf2InputV5 input in message.Inputs)
                {
                    writer.WriteStartObject();
                    writer.WriteString("name", input.Name);
                    writer.WriteString("type", input.Type);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WritePropertyName("markupContract");
            writer.WriteRawValue(project.MarkupContract, skipInputValidation: false);
            writer.WriteEndObject();
        });
    }

    private static byte[] Write(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = Encoder, NewLine = "\n" })) write(writer);
        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    // Reading the committed files.

    private static readonly Dictionary<string, JsonObject> IndexCache = new(StringComparer.Ordinal);

    private static JsonObject IndexEntry(string group)
    {
        lock (IndexCache)
        {
            if (IndexCache.Count == 0)
                foreach (JsonNode? item in JsonNode.Parse(File.ReadAllBytes(Path.Combine(Root, "index.json")))!["groups"]!.AsArray())
                    IndexCache.Add(item!["id"]!.GetValue<string>(), item.AsObject());
            return IndexCache.TryGetValue(group, out JsonObject? entry) ? entry : throw new InvalidOperationException("The compiled corpus has no group " + group + "; run bun eng/generate-corpus.mjs.");
        }
    }

    internal static IReadOnlyList<string> GroupIds()
    {
        _ = IndexEntry("rmf2-v1/en");
        lock (IndexCache) return IndexCache.Keys.ToArray();
    }

    internal static byte[] Artifact(string group) => File.ReadAllBytes(Path.Combine(Root, IndexEntry(group)["artifact"]!.GetValue<string>()));

    internal static JsonDocument ContractDocument(string group) => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Root, IndexEntry(group)["contract"]!.GetValue<string>())));

    // The key of a message corpus case in its group.
    internal static string Key(string group, string caseId) =>
        IndexEntry(group)["cases"]?[caseId]?.GetValue<string>() ?? throw new InvalidOperationException("The compiled group " + group + " has no case " + caseId + ".");

    internal static TranslationPackContract PackContract(string group)
    {
        using JsonDocument document = ContractDocument(group);
        return PackContract(document.RootElement);
    }

    internal static TranslationPackContract PackContract(JsonElement contract)
    {
        string catalog = contract.GetProperty("catalog").GetString()!;
        return TranslationPackContract.CreateRmf2V5(catalog, contract.GetProperty("locale").GetString()!, contract.GetProperty("contractFingerprint").GetString()!,
            contract.GetProperty("messages").EnumerateArray().Select(message => TranslationPackMessageContract.FromRmf2Inputs(
                new TranslationKey(catalog, message.GetProperty("id").GetInt32(), message.GetProperty("key").GetString()!),
                message.GetProperty("inputs").EnumerateArray().Select(input => new CompiledRmf2Input(input.GetProperty("name").GetString()!,
                    Rmf2V1CorpusTests.Type(input.GetProperty("type").GetString()!))).ToArray())).ToArray(),
            contract.GetProperty("markupContract").GetRawText());
    }

    // The committed artifact verified as an exact pack against its committed contract.
    internal static VerifiedExternalTranslationPack Load(string group) =>
        TranslationPackLoader.VerifyAsync(new ExternalTranslationPack(Artifact(group)), PackContract(group)).AsTask().GetAwaiter().GetResult();

    // A pack recipe of a corpus case applied to a base artifact: "patch", an RFC 6902 JSON Patch whose values may use the
    // corpus macros, applied to the parsed artifact, which is then written as compact JSON in member order; or "textEdit",
    // {"find": f, "replace": r}, which replaces the first occurrence of f in the artifact's text, for a pack that is not
    // a JSON value with unique members.
    internal static byte[] Apply(byte[] artifact, JsonNode recipe)
    {
        if (recipe["textEdit"] is JsonObject edit)
        {
            string text = Encoding.UTF8.GetString(artifact), find = edit["find"]!.GetValue<string>();
            int at = text.IndexOf(find, StringComparison.Ordinal);
            if (at < 0) throw new InvalidOperationException("The text edit finds nothing: " + find);
            return Encoding.UTF8.GetBytes(string.Concat(text.AsSpan(0, at), edit["replace"]!.GetValue<string>(), text.AsSpan(at + find.Length)));
        }
        JsonNode? root = JsonPatch.Apply(JsonNode.Parse(artifact), recipe["patch"]?.AsArray() ?? throw new InvalidOperationException("The case has no patch or textEdit."));
        return Encoding.UTF8.GetBytes(root?.ToJsonString() ?? "null");
    }

    internal static byte[] Apply(byte[] artifact, JsonElement recipe) => Apply(artifact, JsonNode.Parse(recipe.GetRawText())!);
}

// The corpus macros, which keep large limit cases readable: {"$text": c, "length": n} is a string of n copies of c,
// {"$items": v, "count": n} an array of n copies of v, and {"$arguments": v, "count": n, "prefix": p} an object with the
// members p0 to p(n-1), each a copy of v. They expand wherever a corpus value may use them (README, "Macros").
internal static class CorpusMacros
{
    internal static JsonNode? Expand(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject macro when macro.ContainsKey("$text"):
                return (JsonNode)string.Concat(Enumerable.Repeat(macro["$text"]!.GetValue<string>(), macro["length"]!.GetValue<int>()));
            case JsonObject macro when macro.ContainsKey("$items"):
                return new JsonArray(Enumerable.Range(0, macro["count"]!.GetValue<int>()).Select(_ => Expand(macro["$items"])).ToArray());
            case JsonObject macro when macro.ContainsKey("$arguments"):
                return new JsonObject(Enumerable.Range(0, macro["count"]!.GetValue<int>())
                    .Select(index => KeyValuePair.Create(macro["prefix"]!.GetValue<string>() + index, Expand(macro["$arguments"]))));
            case JsonObject value:
                return new JsonObject(value.Select(item => KeyValuePair.Create(item.Key, Expand(item.Value))));
            case JsonArray array:
                return new JsonArray(array.Select(Expand).ToArray());
            default:
                return node?.DeepClone();
        }
    }
}

// RFC 6902 JSON Patch over System.Text.Json.Nodes, with RFC 6901 pointers. Values are macro-expanded first. "add" on an
// existing object member replaces it in place, as RFC 6902 requires; a new member goes last.
internal static class JsonPatch
{
    internal static JsonNode? Apply(JsonNode? document, JsonArray operations)
    {
        foreach (JsonNode? item in operations)
        {
            JsonObject operation = item!.AsObject();
            string op = operation["op"]!.GetValue<string>(), path = operation["path"]!.GetValue<string>();
            JsonNode? Value() => operation.ContainsKey("value") ? CorpusMacros.Expand(operation["value"]) : throw new InvalidOperationException(op + " " + path + " has no value.");
            string From() => operation["from"]?.GetValue<string>() ?? throw new InvalidOperationException(op + " " + path + " has no from.");
            switch (op)
            {
                case "add": document = Add(document, path, Value()); break;
                case "remove": Remove(document, path); break;
                case "replace": _ = Get(document, path); document = Set(document, path, Value()); break;
                case "move":
                {
                    string from = From();
                    if (path.StartsWith(from + "/", StringComparison.Ordinal)) throw new InvalidOperationException("move into its own child: " + path);
                    JsonNode? value = Get(document, from);
                    Remove(document, from);
                    document = Add(document, path, value);
                    break;
                }
                case "copy": document = Add(document, path, Get(document, From())?.DeepClone()); break;
                case "test":
                    if (!JsonNode.DeepEquals(Get(document, path), Value())) throw new InvalidOperationException("test failed at " + path + ": " + (Get(document, path)?.ToJsonString() ?? "null"));
                    break;
                default: throw new InvalidOperationException("Unknown JSON Patch operation " + op);
            }
        }
        return document;
    }

    private static string[] Tokens(string pointer)
    {
        if (pointer.Length == 0) return [];
        if (pointer[0] != '/') throw new InvalidOperationException("Invalid JSON pointer " + pointer);
        return pointer[1..].Split('/').Select(token => token.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal)).ToArray();
    }

    private static int Index(JsonArray array, string token, bool allowEnd)
    {
        if (allowEnd && token == "-") return array.Count;
        if (token.Length == 0 || (token.Length > 1 && token[0] == '0') || !token.All(char.IsAsciiDigit) || !int.TryParse(token, out int index))
            throw new InvalidOperationException("Invalid array index " + token);
        if (index > array.Count || (!allowEnd && index == array.Count)) throw new InvalidOperationException("Array index out of range: " + token);
        return index;
    }

    private static JsonNode? Get(JsonNode? document, string pointer)
    {
        JsonNode? node = document;
        foreach (string token in Tokens(pointer))
            node = node switch
            {
                JsonObject value => value.TryGetPropertyValue(token, out JsonNode? member) ? member : throw new InvalidOperationException("No member at " + pointer),
                JsonArray value => value[Index(value, token, allowEnd: false)],
                _ => throw new InvalidOperationException("No value at " + pointer),
            };
        return node;
    }

    private static (JsonNode Parent, string Token) Parent(JsonNode? document, string pointer)
    {
        string[] tokens = Tokens(pointer);
        string parent = string.Concat(tokens[..^1].Select(token => "/" + token.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal)));
        return (Get(document, parent) ?? throw new InvalidOperationException("No container at " + pointer), tokens[^1]);
    }

    private static JsonNode? Add(JsonNode? document, string pointer, JsonNode? value)
    {
        if (pointer.Length == 0) return value;
        (JsonNode parent, string token) = Parent(document, pointer);
        switch (parent)
        {
            case JsonObject container: container[token] = value; break;
            case JsonArray container: container.Insert(Index(container, token, allowEnd: true), value); break;
            default: throw new InvalidOperationException("No container at " + pointer);
        }
        return document;
    }

    private static JsonNode? Set(JsonNode? document, string pointer, JsonNode? value)
    {
        if (pointer.Length == 0) return value;
        (JsonNode parent, string token) = Parent(document, pointer);
        switch (parent)
        {
            case JsonObject container: container[token] = value; break;
            case JsonArray container: container[Index(container, token, allowEnd: false)] = value; break;
            default: throw new InvalidOperationException("No container at " + pointer);
        }
        return document;
    }

    private static void Remove(JsonNode? document, string pointer)
    {
        if (pointer.Length == 0) throw new InvalidOperationException("Cannot remove the document.");
        (JsonNode parent, string token) = Parent(document, pointer);
        switch (parent)
        {
            case JsonObject container when container.Remove(token): break;
            case JsonArray container: container.RemoveAt(Index(container, token, allowEnd: false)); break;
            default: throw new InvalidOperationException("Nothing to remove at " + pointer);
        }
    }
}
