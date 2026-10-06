using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace Runic.Translations.Generator.Tests;

internal static partial class GeneratorDiagnosticsTests
{
    private const string Project = """
        {
          "schemaVersion": 1,
          "catalog": "app",
          "code": { "namespace": "Example.Localization", "className": "AppText" },
          "baseLocale": "en",
          "locales": [ "en", "de" ],
          "validation": { "extraLocaleKeys": "warning" }
        }
        """;

    internal static void Register(TestRunner runner)
    {
        runner.Add("every compiler diagnostic ID has a tracked descriptor, a specific title and a reference entry", DescriptorsCoverCompilerDiagnostics);
        runner.Add("reported diagnostics use the static descriptor and keep the compiler's severity", ReportsUseStaticDescriptors);
    }

    private static void DescriptorsCoverCompilerDiagnostics()
    {
        string root = RepositoryRoot();
        var used = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string directory in new[] { "tools/Runic.Translations.Compiler", "packages/dotnet/Runic.Translations.Generator" })
            foreach (string file in Directory.EnumerateFiles(Path.Combine(root, directory), "*.cs", SearchOption.AllDirectories))
                foreach (Match match in DiagnosticIdLiteral().Matches(File.ReadAllText(file)))
                    used.Add(match.Groups[1].Value);
        Assert.True(used.Count > 20, "The compiler diagnostic scan found too few IDs.");

        var descriptors = TranslationsDiagnostics.All.ToDictionary(descriptor => descriptor.Id, StringComparer.Ordinal);
        string[] missing = used.Where(id => !descriptors.ContainsKey(id)).ToArray();
        Assert.True(missing.Length == 0, "Compiler diagnostics without a descriptor: " + string.Join(", ", missing));

        string reference = File.ReadAllText(Path.Combine(root, "docs", "guides", "translations", "diagnostics.md"));
        string shipped = File.ReadAllText(Path.Combine(root, "packages", "dotnet", "Runic.Translations.Generator", "AnalyzerReleases.Shipped.md"));
        Assert.True(reference.StartsWith("# Diagnostics\n", StringComparison.Ordinal), "The diagnostics reference must keep its #diagnostics anchor.");
        var titles = new HashSet<string>(StringComparer.Ordinal);
        foreach (DiagnosticDescriptor descriptor in TranslationsDiagnostics.All)
        {
            string title = descriptor.Title.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Assert.True(titles.Add(title), $"{descriptor.Id} reuses the title '{title}'.");
            Assert.True(title != "Text resource compilation" && !title.EndsWith('.'), $"{descriptor.Id} has a generic or punctuated title.");
            Assert.Equal("https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/diagnostics.md#" + descriptor.Id.ToLowerInvariant(),
                descriptor.HelpLinkUri, descriptor.Id + " help link");
            Assert.True(reference.Contains("\n## " + descriptor.Id + "\n", StringComparison.Ordinal), $"The diagnostics reference has no '## {descriptor.Id}' entry.");
            Assert.True(reference.Contains("\n## " + descriptor.Id + "\n\n" + title + ".", StringComparison.Ordinal), $"The {descriptor.Id} reference entry does not start with its title.");
            Assert.True(shipped.Contains("\n" + descriptor.Id + " | " + TranslationsDiagnostics.Category + " | ", StringComparison.Ordinal), $"{descriptor.Id} is not release-tracked.");
        }
    }

    private static void ReportsUseStaticDescriptors()
    {
        GeneratorRun missing = GeneratorTestHost.Run(RuntimeReferenceMode.Missing, ProjectInput(), new TestInput("C:/repo/translations/en.rmf2", "Rmf2", "title = Shop\n"));
        Assert.True(ReferenceEquals(TranslationsDiagnostics.RuntimeAbi, missing.SingleResult.Diagnostics.Single().Descriptor), "RTR0024 does not use its static descriptor.");

        GeneratorRun extra = GeneratorTestHost.Run(ProjectInput(),
            new TestInput("C:/repo/translations/en.rmf2", "Rmf2", "title = Shop\n"),
            new TestInput("C:/repo/translations/de.rmf2", "Rmf2", "title = Laden\nextra = Nur hier\n"));
        Diagnostic warning = extra.SingleResult.Diagnostics.Single();
        Assert.True(ReferenceEquals(TranslationsDiagnostics.ExtraLocaleKey, warning.Descriptor), "RTR0011 does not use its static descriptor.");
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity, "policy severity");
        Assert.Equal("translations/de.rmf2", warning.Location.GetLineSpan().Path, "diagnostic path");
        Assert.Equal(1, warning.Location.GetLineSpan().StartLinePosition.Line, "diagnostic line");
        Assert.Equal(4, extra.SingleResult.GeneratedSources.Length, "generated sources despite a warning");
    }

    private static TestInput ProjectInput() => new("C:/repo/translations/runic.json", "Project", Project);

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "Runic.Translations.slnx"))) return current.FullName;
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    [GeneratedRegex("\"(RTR[0-9]{4})\"")]
    private static partial Regex DiagnosticIdLiteral();
}
