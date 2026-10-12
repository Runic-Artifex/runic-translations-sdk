using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TUnit.Core;

namespace Runic.Translations.CommandLine.Tests;

internal sealed class ReadmeSnippetTests
{
    // The two types a reader's own project generates: the catalog of their translations and the command catalog.
    // Their shape is what the generators emit (ITranslationProvider CreateProvider() and CommandCatalog Create()).
    private const string GeneratedTypes = """
        public static class AppTextCatalog
        {
            public static global::Runic.Translations.ITranslationProvider CreateProvider() => throw new global::System.NotSupportedException();
        }

        namespace Runic.CommandLine.Generated
        {
            public static class GeneratedCommandCatalog
            {
                public static global::Runic.CommandLine.CommandCatalog Create(global::System.Action<global::Runic.CommandLine.CommandCatalogBuilder>? configure = null) => throw new global::System.NotSupportedException();
            }
        }
        """;

    [Test, DisplayName("the adapter README snippet compiles as printed")]
    public void AdapterReadmeSnippetCompiles()
    {
        string readme = File.ReadAllText(Path.Combine(RepositoryRoot(), "packages", "dotnet", "Runic.Translations.CommandLine", "README.md"));
        string snippet = FirstCSharpBlock(readme);
        Assert.True(snippet.Contains("TranslationCommandTextResolver", StringComparison.Ordinal), "The first README snippet no longer shows the adapter.");

        CSharpCompilation compilation = CSharpCompilation.Create("ReadmeSnippet",
            [CSharpSyntaxTree.ParseText(snippet + "\n"), CSharpSyntaxTree.ParseText(GeneratedTypes)],
            References(),
            new CSharpCompilationOptions(OutputKind.ConsoleApplication, nullableContextOptions: NullableContextOptions.Enable));
        string[] errors = compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(static diagnostic => diagnostic.ToString())
            .ToArray();
        Assert.True(errors.Length == 0, "The README snippet does not compile as printed:\n" + string.Join('\n', errors));
    }

    [Test, DisplayName("the snippet check rejects a snippet without its usings")]
    public void SnippetCheckSeesMissingUsings()
    {
        const string withoutUsings = "using Runic.CommandLine;\nusing Runic.Translations;\nusing Runic.Translations.CommandLine;\n\n" +
            "ITranslationSnapshot snapshot = await AppTextCatalog.CreateProvider().GetSnapshotAsync(\"de\");\n" +
            "return await new CommandApp(GeneratedCommandCatalog.Create()) { Culture = CultureInfo.GetCultureInfo(\"de\"), TextResolver = new TranslationCommandTextResolver(snapshot) }.RunAsync(args);\n";
        CSharpCompilation compilation = CSharpCompilation.Create("MissingUsings",
            [CSharpSyntaxTree.ParseText(withoutUsings), CSharpSyntaxTree.ParseText(GeneratedTypes)],
            References(),
            new CSharpCompilationOptions(OutputKind.ConsoleApplication));
        string[] codes = compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Select(static diagnostic => diagnostic.Id).ToArray();
        Assert.True(codes.Contains("CS0103"), "Expected CS0103 for the missing usings, got: " + string.Join(", ", codes));
    }

    private static string FirstCSharpBlock(string markdown)
    {
        const string fence = "```csharp\n";
        string text = markdown.Replace("\r\n", "\n", StringComparison.Ordinal);
        int start = text.IndexOf(fence, StringComparison.Ordinal);
        Assert.True(start >= 0, "The README has no csharp snippet.");
        start += fence.Length;
        int end = text.IndexOf("\n```", start, StringComparison.Ordinal);
        Assert.True(end >= 0, "The README snippet is not closed.");
        return text[start..end];
    }

    private static IEnumerable<MetadataReference> References()
    {
        string trusted = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("Trusted platform assemblies are unavailable.");
        // The trusted platform assemblies include the copied Runic.CommandLine, Runic.Translations and adapter assemblies.
        return trusted.Split(Path.PathSeparator).Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path));
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Runic.Translations.slnx"))) return directory.FullName;
        throw new InvalidOperationException("Runic.Translations.slnx was not found above " + AppContext.BaseDirectory);
    }
}
