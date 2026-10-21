using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Runic.Translations.Compiler.Generation;
using TUnit.Core;

namespace Runic.Translations.Compiler.Tests;

// The generated C# is the contract between the compiler and the runtime: names in Runic.Translations.Generated,
// GeneratedCatalogSupport factories and the IReadableMessages surface. A deliberate change regenerates the snapshots
// with RUNIC_UPDATE_SNAPSHOTS=1 and the diff is reviewed in the pull request.
internal sealed class GeneratedCSharpSnapshotTests
{
    private const string Directory = "tests/fixtures/translations/generated-csharp";

    [Test, DisplayName("generated C# matches the reviewed snapshot")]
    public void GeneratedCSharpMatchesSnapshot()
    {
        var compilation = TranslationCompiler.CompileProject(
            Source("translations/runic.json", "{\"schemaVersion\":1,\"catalog\":\"snapshot\",\"code\":{\"namespace\":\"Snapshot\",\"className\":\"SnapshotText\"},\"baseLocale\":\"en\",\"locales\":[\"en\",\"de\"]}"),
            [
                Source("translations/en.rmf2", "greeting = Hello {$name}\nitems =\n  .input {$count :integer}\n  .match $count\n  one {{One item}}\n  * {{{$count} items}}\nhelp = Read the {#link}guide{/link}.\n"),
                Source("translations/de.rmf2", "greeting = Hallo {$name}\nitems =\n  .input {$count :integer}\n  .match $count\n  one {{Ein Artikel}}\n  * {{{$count} Artikel}}\nhelp = Lies die {#link}Anleitung{/link}.\n"),
            ]);
        if (!compilation.Success || compilation.Project is not { } project)
            throw new InvalidOperationException("The snapshot project did not compile: " + string.Join("; ", compilation.Diagnostics.Select(d => d.Id + " " + d.Message)));
        TranslationGeneratedOutput[] outputs =
        [
            TranslationOutputRenderer.RenderRmf2V5CSharpKeys(project),
            TranslationOutputRenderer.RenderRmf2V5CSharpAccessors(project),
            TranslationOutputRenderer.RenderRmf2V5CSharpCatalogData(project),
            TranslationOutputRenderer.RenderRmf2V5CSharpRegistration(project),
            TranslationOutputRenderer.RenderRmf2V5CSharpReadable(project),
        ];
        string root = RepositoryPaths.Resolve(Directory.Split('/'));
        bool update = Environment.GetEnvironmentVariable("RUNIC_UPDATE_SNAPSHOTS") == "1";
        var mismatches = new List<string>();
        foreach (TranslationGeneratedOutput output in outputs)
        {
            string path = Path.Combine(root, Path.GetFileName(output.RelativePath));
            byte[] actual = output.GetUtf8Bytes();
            if (update)
            {
                System.IO.Directory.CreateDirectory(root);
                File.WriteAllBytes(path, actual);
            }
            else if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(actual))
                mismatches.Add(Path.GetFileName(path));
        }
        if (mismatches.Count != 0)
            throw new InvalidOperationException("Generated C# differs from the snapshot in " + Directory + ": " + string.Join(", ", mismatches) +
                ". Review the change and regenerate with RUNIC_UPDATE_SNAPSHOTS=1.");
        string all = string.Concat(outputs.Select(output => Encoding.UTF8.GetString(output.GetUtf8Bytes())));
        foreach (string expected in new[] { "global::Runic.Translations.Generated.GeneratedCatalogSupport.", "global::Runic.Translations.Generated.TranslationsCompatibility.", ": global::Runic.Translations.IReadableMessages" })
            if (!all.Contains(expected, StringComparison.Ordinal)) throw new InvalidOperationException("Generated C# lacks " + expected);
        foreach (string retired in new[] { "FromRmf2Inputs", "CreateDeferred(", "ReadRmf2Values", "CreateRmf2(", "CreateRmf2V5", "global::Runic.Translations.CompiledRmf2", "global::Runic.Translations.TranslationsCompatibility" })
            if (all.Contains(retired, StringComparison.Ordinal)) throw new InvalidOperationException("Generated C# still uses " + retired);
    }

    private static TranslationSource Source(string path, string text) => new(path, Encoding.UTF8.GetBytes(text));
}
