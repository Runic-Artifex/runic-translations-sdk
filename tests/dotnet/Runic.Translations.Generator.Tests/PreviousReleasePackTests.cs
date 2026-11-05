using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Runic.Translations.Compiler;
using Runic.Translations.Compiler.Generation;
using TUnit.Core;

namespace Runic.Translations.Generator.Tests;

// generate --contract packs for a release built with 0.8.0, loaded by that release's own .NET loader: the C# the published
// 0.8.0 tool generated (tests/fixtures/translations/generated-0.8.0) compiled against the published Runic.Translations 0.8.0
// (a PackageDownload), in a collectible AssemblyLoadContext beside the current runtime. Rmf2ContractPackTests loads the same
// packs with the fixture's 0.8.0 web runtime.
internal sealed class PreviousReleasePackTests
{
    private const string Probe = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Runic.Translations;

        public static class PreviousReleaseProbe
        {
            // The German text of a pack through GridTextCatalog.CreateExternalManagerAsync, or "rejected: <reason>".
            public static async Task<string> FormatAsync(byte[] pack)
            {
                try
                {
                    ITranslationManager manager = await Acme.Grid.Translations.GridTextCatalog.CreateExternalManagerAsync(new Source(pack), "de");
                    ITranslationSnapshot snapshot = manager.Current;
                    string Format(string name, params TextArgument[] arguments) =>
                        snapshot.TryGetKey(name, out TranslationKey key) ? snapshot.Format(key, arguments) : "missing " + name;
                    return string.Join("|", Format("grid_empty"), Format("grid_rows", new TextArgument("count", 1L)),
                        Format("grid_total", new TextArgument("amount", 1234.5m)), Format("grid_updated", new TextArgument("when", new DateOnly(2026, 11, 5))));
                }
                catch (TranslationPackException exception)
                {
                    return "rejected: " + TranslationPackFailure.GetReason(exception);
                }
            }

            private sealed class Source(byte[] pack) : IExternalTranslationSource
            {
                public ValueTask<ExternalTranslationPack?> LoadAsync(string catalog, string locale, CancellationToken cancellationToken) =>
                    ValueTask.FromResult<ExternalTranslationPack?>(locale == "de" ? new ExternalTranslationPack(pack) : null);
            }
        }
        """;

    [Test, DisplayName("contract packs load in the .NET loader of a real 0.8.0 release")]
    public async Task PreviousReleaseDotNetLoader()
    {
        string fixture = RepositoryFile("tests", "fixtures", "translations", "generated-0.8.0");
        string runtime = typeof(PreviousReleasePackTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(static item => item.Key == "RunicTranslations080").Value!;
        Assert.True(File.Exists(runtime), "Runic.Translations 0.8.0 was not downloaded: " + runtime);

        var shipped = Rmf2ShippedContractV5.Parse(File.ReadAllBytes(Path.Combine(fixture, "output", "acme-grid.en.locale-v5.json")));
        Rmf2ProjectV5 current = Current(fixture);
        var skipped = new List<Rmf2ContractPackSkipV5>();
        byte[] pack = Rmf2ContractPackV5.Render(current, "de", shipped, skipped)!.GetUtf8Bytes();
        Assert.Equal("grid_new content-locale-changed|grid_updated unsupported-function",
            string.Join('|', skipped.Select(static item => item.Key + " " + item.Reason).Order(StringComparer.Ordinal)), "skipped messages");
        // The same pack with the message that uses the medium date style 0.9 added.
        JsonObject forced = JsonNode.Parse(pack)!.AsObject();
        forced["messages"]!.AsObject()["grid_updated"] = JsonNode.Parse(Rmf2LocaleArtifactV5.Render(current, "de").GetUtf8Bytes())!["messages"]!["grid_updated"]!.DeepClone();

        IEnumerable<MetadataReference> references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Where(static path => !Path.GetFileName(path).StartsWith("Runic.", StringComparison.Ordinal))
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(runtime));
        SyntaxTree[] sources = [.. Directory.GetFiles(Path.Combine(fixture, "output"), "*.g.cs").Order(StringComparer.Ordinal)
            .Select(static path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path)), CSharpSyntaxTree.ParseText(Probe)];
        CSharpCompilation compilation = CSharpCompilation.Create("PreviousReleaseProbe", sources, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        using var stream = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics.Where(static item => item.Severity == DiagnosticSeverity.Error)));
        stream.Position = 0;

        var context = new PreviousReleaseContext(runtime);
        try
        {
            Assembly probe = context.LoadFromStream(stream);
            Assert.Equal("0.8.0.0", context.LoadFromAssemblyName(new AssemblyName("Runic.Translations")).GetName().Version!.ToString(), "the 0.8.0 runtime");
            MethodInfo format = probe.GetType("PreviousReleaseProbe", throwOnError: true)!.GetMethod("FormatAsync")!;
            string text = await (Task<string>)format.Invoke(null, [pack])!;
            // The pack's text, and the release's own German date for the message the pack leaves out.
            string[] parts = text.Split('|');
            Assert.Equal("Leer|1 Zeile|Summe 1.234,5", string.Join('|', parts.Take(3)), text);
            Assert.True(parts.Length == 4 && parts[3].StartsWith("Aktualisiert am ", StringComparison.Ordinal), text);
            // With that message the 0.8.0 loader rejects the whole pack.
            string rejected = await (Task<string>)format.Invoke(null, [Encoding.UTF8.GetBytes(forced.ToJsonString())])!;
            Assert.True(rejected.StartsWith("rejected: ", StringComparison.Ordinal), rejected);
        }
        finally
        {
            context.Unload();
        }
    }

    // The fixture's catalog one release later, as in Rmf2ContractPackTests: German grid_empty fixed, grid_new newly translated,
    // grid_updated in the medium date style.
    private static Rmf2ProjectV5 Current(string fixture)
    {
        string Source(string locale) => File.ReadAllText(Path.Combine(fixture, "source", locale + ".rmf2")).ReplaceLineEndings("\n")
            .Replace("style=long", "style=medium", StringComparison.Ordinal);
        Rmf2ProjectCompilation result = TranslationCompiler.CompileRmf2ProjectV5(
            new TranslationSource("translations/runic.json", File.ReadAllBytes(Path.Combine(fixture, "source", "runic.json"))),
            [
                new TranslationSource("translations/en.rmf2", Encoding.UTF8.GetBytes(Source("en"))),
                new TranslationSource("translations/de.rmf2", Encoding.UTF8.GetBytes(
                    Source("de").Replace("grid_empty = Keine Zeilen", "grid_empty = Leer", StringComparison.Ordinal) + "grid_new = Neue Spalte\n")),
            ]);
        Assert.True(result.Success && result.Project is not null, string.Join("; ", result.Diagnostics.Select(static item => item.Message)));
        return result.Project!;
    }

    private static string RepositoryFile(params string[] parts)
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Runic.Translations.slnx"))) return Path.Combine([directory.FullName, .. parts]);
        throw new InvalidOperationException("The repository root was not found above " + AppContext.BaseDirectory + ".");
    }

    // Resolves Runic.Translations to the 0.8.0 assembly; everything else comes from the default context.
    private sealed class PreviousReleaseContext(string runtime) : AssemblyLoadContext("Runic.Translations 0.8.0", isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName) =>
            assemblyName.Name == "Runic.Translations" ? LoadFromAssemblyPath(runtime) : null;
    }
}
