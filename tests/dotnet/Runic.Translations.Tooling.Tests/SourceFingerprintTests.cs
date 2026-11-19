using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Runic.Translations.Compiler;
using TUnit.Core;

namespace Runic.Translations.Tooling.Tests;

// The source fingerprints of review data (TranslationInterchange.ComputeSourceFingerprints) are computed without projecting
// the translations and kept per message across the compiles of a validation session; they equal the fingerprints of the
// full interchange projection.
internal sealed class SourceFingerprintTests
{
    private static readonly string[] Projects = ["rmf2-document-v1", "rmf2-v1", "v5-project", "xliff-v3/project", "migration/project"];

    [Test, DisplayName("Source fingerprints equal the interchange projection's on the corpus projects")]
    public void EqualTheProjection()
    {
        int compared = 0;
        foreach (string name in Projects)
        {
            (TranslationSource project, TranslationSource[] sources) = Read(name);
            Rmf2ProjectCompilation compilation = TranslationCompiler.CompileRmf2ProjectV5(project, sources);
            if (!compilation.Success || compilation.Project is null) continue;
            RequireSame(compilation, name);
            Require(ReferenceEquals(TranslationInterchange.ComputeSourceFingerprints(compilation), TranslationInterchange.ComputeSourceFingerprints(compilation)),
                "A compilation's fingerprints are computed once.");
            compared++;
        }
        Require(compared >= 3, $"Only {compared} corpus projects compiled.");
    }

    [Test, DisplayName("Source fingerprints stay equal to the projection's across the edits of a validation session")]
    public void EqualAcrossSessionEdits()
    {
        (TranslationSource project, TranslationSource[] sources) = Read("xliff-v3/project");
        var cache = new Rmf2CompileCache();
        Rmf2ProjectCompilation first = Compile(cache, project, sources, "first compile");
        IReadOnlyDictionary<string, string> before = TranslationInterchange.ComputeSourceFingerprints(first);

        // The same bytes in new sources, a translation edit, a base edit and a new base message.
        Rmf2ProjectCompilation same = Compile(cache, project, sources.Select(Copy).ToArray(), "same bytes");
        Require(Equal(before, TranslationInterchange.ComputeSourceFingerprints(same)), "The fingerprints of an unchanged catalog are the same.");
        TranslationSource[] translated = sources.Select(source => source.Path.EndsWith("de.rmf2", StringComparison.Ordinal)
            ? Replace(source, "\nfarewell = ", "\nfarewell = Bis gleich! ") : source).ToArray();
        Rmf2ProjectCompilation translation = Compile(cache, project, translated, "translation edit");
        Require(Equal(before, TranslationInterchange.ComputeSourceFingerprints(translation)), "A translation edit keeps every source fingerprint.");
        TranslationSource[] edited = translated.Select(source => source.Path.EndsWith("en.rmf2", StringComparison.Ordinal)
            ? Replace(source, "farewell = See you soon", "farewell = See you later") : source).ToArray();
        Rmf2ProjectCompilation baseEdit = Compile(cache, project, edited, "base edit");
        IReadOnlyDictionary<string, string> after = TranslationInterchange.ComputeSourceFingerprints(baseEdit);
        Require(before.Where(pair => pair.Key != "farewell").All(pair => after[pair.Key] == pair.Value) && after["farewell"] != before["farewell"],
            "A base edit changes only the edited message's fingerprint.");
        TranslationSource[] added = edited.Select(source => source.Path.EndsWith("en.rmf2", StringComparison.Ordinal)
            ? Replace(source, "farewell = See you later", "farewell = See you later\nwelcome_back = Welcome back") : source).ToArray();
        Rmf2ProjectCompilation addition = Compile(cache, project, added, "new message");
        Require(TranslationInterchange.ComputeSourceFingerprints(addition).ContainsKey("welcome_back"), "A new message has a fingerprint.");
    }

    private static Rmf2ProjectCompilation Compile(Rmf2CompileCache cache, TranslationSource project, TranslationSource[] sources, string step)
    {
        Rmf2ProjectCompilation compilation = cache.Compile(project, sources, out _);
        Require(compilation.Success && compilation.Project is not null, $"The {step} compiles: " +
            string.Join("; ", compilation.Diagnostics.Select(diagnostic => diagnostic.Id + " " + diagnostic.Message)));
        // The projection of a fresh full compile of the same sources: the reference the session's fingerprints must match.
        RequireSame(compilation, step);
        Rmf2ProjectCompilation full = TranslationCompiler.CompileRmf2ProjectV5(project, sources);
        Require(Equal(TranslationInterchangeProjectionAdapter.From(full).SourceFingerprints, TranslationInterchange.ComputeSourceFingerprints(compilation)),
            $"The session's fingerprints after the {step} equal a full compile's.");
        return compilation;
    }

    private static void RequireSame(Rmf2ProjectCompilation compilation, string name) =>
        Require(Equal(TranslationInterchangeProjectionAdapter.From(compilation).SourceFingerprints, TranslationInterchange.ComputeSourceFingerprints(compilation)),
            $"The source fingerprints of {name} differ from the projection's.");

    private static bool Equal(IReadOnlyDictionary<string, string> expected, IReadOnlyDictionary<string, string> actual) =>
        expected.Count == actual.Count && expected.All(pair => actual.TryGetValue(pair.Key, out string? value) && value == pair.Value);

    private static TranslationSource Copy(TranslationSource source) => new(source.Path, source.GetUtf8Bytes());

    private static TranslationSource Replace(TranslationSource source, string from, string to)
    {
        string text = Encoding.UTF8.GetString(source.GetUtf8Bytes());
        Require(text.Contains(from, StringComparison.Ordinal), $"'{from}' is in {source.Path}.");
        return new(source.Path, Encoding.UTF8.GetBytes(text.Replace(from, to, StringComparison.Ordinal)));
    }

    // runic.json and every .rmf2/.mf2 file of a corpus project, with paths relative to its directory.
    private static (TranslationSource Project, TranslationSource[] Sources) Read(string name)
    {
        string directory = Path.Combine(XliffProfileV3Tests.RepositoryRoot(), "specs", "translations", "corpus", name);
        TranslationSource[] sources = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(static path => path.EndsWith(".rmf2", StringComparison.Ordinal) || path.EndsWith(".mf2", StringComparison.Ordinal))
            .Select(path => new TranslationSource(Path.GetRelativePath(directory, path).Replace('\\', '/'), File.ReadAllBytes(path)))
            .OrderBy(static source => source.Path, StringComparer.Ordinal).ToArray();
        return (new TranslationSource("runic.json", File.ReadAllBytes(Path.Combine(directory, "runic.json"))), sources);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
