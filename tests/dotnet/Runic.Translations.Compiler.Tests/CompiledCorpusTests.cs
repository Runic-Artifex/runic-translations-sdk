using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using TUnit.Core;

namespace Runic.Translations.Compiler.Tests;

// The committed compiled corpus (specs/translations/corpus/compiled) is what the compiler produces now, and every committed
// artifact loads as an exact pack against its committed contract. `bun eng/generate-corpus.mjs` runs Fresh with
// --test-parameter update-compiled-corpus=true to rewrite the directory, and without it as the --check.
[Category("rmf2-semantic-v5")]
internal sealed class CompiledCorpusTests
{
    [Test, NotInParallel, DisplayName("the committed compiled corpus matches the compiler")]
    public void Fresh()
    {
        SortedDictionary<string, byte[]> expected = CompiledCorpus.Export();
        string root = CompiledCorpus.Root;
        bool update = TestContext.Parameters.TryGetValue("update-compiled-corpus", out var value) && value.Contains("true");
        string[] committed = Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/')).Order(StringComparer.Ordinal).ToArray()
            : [];
        var stale = new List<string>();
        foreach ((string path, byte[] bytes) in expected)
        {
            string file = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(file) && File.ReadAllBytes(file).AsSpan().SequenceEqual(bytes)) continue;
            stale.Add((File.Exists(file) ? "changed: " : "missing: ") + path);
            if (update)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllBytes(file, bytes);
            }
        }
        foreach (string path in committed.Where(path => !expected.ContainsKey(path)))
        {
            stale.Add("not generated: " + path);
            if (update) File.Delete(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
        }
        if (!update)
            Assert.True(stale.Count == 0, "The compiled corpus is stale; run `bun eng/generate-corpus.mjs` and commit the result.\n" + string.Join("\n", stale));
    }

    [Test, DisplayName("every committed compiled artifact loads as an exact pack against its committed contract and digest")]
    public void Loads()
    {
        using JsonDocument index = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(CompiledCorpus.Root, "index.json")));
        Assert.Equal(1, index.RootElement.GetProperty("formatVersion").GetInt32());
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement group in index.RootElement.GetProperty("groups").EnumerateArray())
        {
            string id = group.GetProperty("id").GetString()!;
            Assert.True(ids.Add(id), "Duplicate compiled group " + id);
            Assert.True(File.Exists(Path.Combine(RepositoryPaths.Resolve("specs", "translations", "corpus"), group.GetProperty("corpus").GetString()!)), id + ": corpus file");
            byte[] artifact = CompiledCorpus.Artifact(id);
            Assert.Equal(group.GetProperty("sha256").GetString(), "sha256:" + Convert.ToHexStringLower(SHA256.HashData(artifact)), id + ": digest");
            VerifiedExternalTranslationPack pack = CompiledCorpus.Load(id);
            using JsonDocument contract = CompiledCorpus.ContractDocument(id);
            Assert.Equal(contract.RootElement.GetProperty("messages").GetArrayLength(), pack.Messages.Count, id + ": messages");
            if (group.TryGetProperty("cases", out JsonElement cases))
                foreach (System.Text.Json.JsonProperty item in cases.EnumerateObject())
                    Assert.True(pack.Messages.Any(message => message.Key.Name == item.Value.GetString()), id + ": " + item.Name + " has no message");
        }
    }
}
