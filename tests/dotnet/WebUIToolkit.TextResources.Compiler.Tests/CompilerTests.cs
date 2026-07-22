using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using CompilerModel = WebUIToolkit.TextResources.Compiler;

namespace WebUIToolkit.TextResources.Compiler.Tests;

internal static class CompilerTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("compiler merges layers and resolves fallback per key", MergeAndFallback);
        runner.Add("compiler preserves version 1 pattern contracts", PatternContracts);
        runner.Add("compiler normalizes source paths and retains exact locations", SourcePathsAndLocations);
        runner.Add("compiler IR and fingerprints are deterministic across order and cultures", Determinism);
        runner.Add("compiler enforces configured hostile-input limits", ConfiguredLimits);
        runner.Add("compiler observes cancellation without diagnostics", Cancellation);
    }

    private static void MergeAndFallback()
    {
        CompilerModel.TextResourceCompilation compilation = CompileCase("valid", "merge");
        Assert.True(compilation.Success, DiagnosticsText(compilation.Diagnostics));
        CompilerModel.CompiledTextCatalog catalog = Assert.Single(compilation.Catalogs);

        Assert.Equal("merge", catalog.Id);
        Assert.Equal("en", catalog.DefaultLocale);
        Assert.Equal(2, catalog.Layers.Count);
        Assert.Equal("base", catalog.Layers[0].Name);
        Assert.Equal(-10, catalog.Layers[0].Priority);
        Assert.Equal("application", catalog.Layers[1].Name);
        Assert.Equal(100, catalog.Layers[1].Priority);

        AssertSequence(["Alpha", "Common.Cancel", "Common.Save", "Zulu"], Keys(catalog.CanonicalResources));
        Assert.Equal("Save now", Find(catalog.CanonicalResources, "Common.Save").Pattern);
        Assert.True(Find(catalog.CanonicalResources, "Common.Save").Description is not null,
            "A higher layer must replace the entire leaf, including its metadata.");

        CompilerModel.CompiledTextLocale englishGb = FindLocale(catalog, "en-GB");
        AssertSequence(["Common.Cancel"], Keys(englishGb.DirectResources));
        AssertSequence(["Alpha", "Common.Cancel", "Common.Save", "Zulu"], Keys(englishGb.ResolvedResources));
        Assert.Equal("Save now", Find(englishGb.ResolvedResources, "Common.Save").Pattern);

        CompilerModel.CompiledTextLocale frenchCanada = FindLocale(catalog, "fr-CA");
        Assert.Equal(0, frenchCanada.DirectResources.Count);
        AssertSequence(["Alpha", "Common.Cancel", "Common.Save", "Zulu"], Keys(frenchCanada.ResolvedResources));
    }

    private static void PatternContracts()
    {
        CompilerModel.TextResourceCompilation compilation = CompileCase("valid", "patterns");
        Assert.True(compilation.Success, DiagnosticsText(compilation.Diagnostics));
        CompilerModel.CompiledTextCatalog catalog = Assert.Single(compilation.Catalogs);
        CompilerModel.CompiledTextResource resource = Find(catalog.CanonicalResources, "All");

        Assert.Equal("{text}|{count}|{amount}|{enabled}|{day}|{clock}|{instant}|{id}|{text}", resource.Pattern);
        Assert.Equal("1.0", resource.Since);
        Assert.Equal("Use a product-specific message.", resource.DeprecatedReason);
        AssertSequence(["all-types", "conformance"], resource.Tags);
        AssertSequence(
            ["amount", "clock", "count", "day", "enabled", "id", "instant", "text"],
            PlaceholderNames(resource.Placeholders));
        Assert.Equal("percent4", FindPlaceholder(resource, "amount").Format);
        Assert.Equal("grouped", FindPlaceholder(resource, "count").Format);
        Assert.Equal("n", FindPlaceholder(resource, "id").Format);
        Assert.Equal(CompilerModel.TextResourceArgumentType.Guid, FindPlaceholder(resource, "id").Type);

        CompilerModel.CompiledTextResource escaped = Find(catalog.CanonicalResources, "Escapes");
        Assert.Equal("Literal {{open}} and repeated text", escaped.Pattern);
        Assert.Equal(0, escaped.Placeholders.Count);
    }

    private static void SourcePathsAndLocations()
    {
        const string manifest = "{\n  \"schemaVersion\": 1,\n  \"catalog\": \"app\",\n  \"code\": { \"namespace\": \"Tests\", \"className\": \"Text\" },\n  \"defaultLocale\": \"en\",\n  \"locales\": [{ \"tag\": \"en\" }],\n  \"layers\": [{ \"name\": \"base\", \"priority\": 0 }]\n}";
        const string malformed = "{\n  \"schemaVersion\": 1,\n  \"catalog\": \"app\",\n  \"locale\": \"en\",\n  \"layer\": \"base\",\n  \"resources\": {\n    \"Bad\": \"unmatched {\"\n  }\n}";

        CompilerModel.TextResourceCompilation compilation = CompilerModel.TextResourceCompiler.Compile(
            [Source(".\\fixtures\\manifest.json", manifest)],
            [Source("./fixtures\\bad.json", malformed)]);
        CompilerModel.TextResourceDiagnostic diagnostic = Assert.Single(compilation.Diagnostics);
        Assert.Equal("WUTTEXT0014", diagnostic.Id);
        Assert.Equal(CompilerModel.TextResourceDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("fixtures/bad.json", diagnostic.Location.Path);
        Assert.Equal(7, diagnostic.Location.Line);
        Assert.Equal(12, diagnostic.Location.Column);
        Assert.Equal(13, diagnostic.Location.LengthBytes);
        Assert.True(diagnostic.Location.EndColumn > diagnostic.Location.Column,
            "The malformed pattern location must be non-empty and end-exclusive.");
    }

    private static void Determinism()
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("tr-TR");
            CompilerModel.TextResourceCompilation first = CompileCase("valid", "determinism-a", reverseDocuments: false);

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
            CompilerModel.TextResourceCompilation reordered = CompileCase("valid", "determinism-a", reverseDocuments: true);
            CompilerModel.TextResourceCompilation repartitioned = CompileCase("valid", "determinism-b", reverseDocuments: true);

            Assert.True(first.Success, DiagnosticsText(first.Diagnostics));
            Assert.True(reordered.Success, DiagnosticsText(reordered.Diagnostics));
            Assert.True(repartitioned.Success, DiagnosticsText(repartitioned.Diagnostics));
            CompilerModel.CompiledTextCatalog firstCatalog = Assert.Single(first.Catalogs);
            CompilerModel.CompiledTextCatalog reorderedCatalog = Assert.Single(reordered.Catalogs);
            CompilerModel.CompiledTextCatalog repartitionedCatalog = Assert.Single(repartitioned.Catalogs);
            Assert.Equal(firstCatalog.Fingerprint, reorderedCatalog.Fingerprint);
            Assert.Equal(firstCatalog.Fingerprint, repartitionedCatalog.Fingerprint);
            AssertSequence(Keys(firstCatalog.CanonicalResources), Keys(reorderedCatalog.CanonicalResources));
            AssertSequence(Keys(firstCatalog.CanonicalResources), Keys(repartitionedCatalog.CanonicalResources));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    private static void ConfiguredLimits()
    {
        CompilerModel.TextResourceCompilerOptions limits = new(
            maximumDocumentBytes: 12,
            maximumDepth: 2,
            maximumKeysPerCatalog: 1,
            maximumValueBytes: 4,
            maximumPlaceholdersPerValue: 1,
            maximumLocalesPerCatalog: 1);
        CompilerModel.TextResourceCompilation compilation = CompilerModel.TextResourceCompiler.Compile(
            [Source("too-large.json", "{\"schemaVersion\":1}")],
            Array.Empty<CompilerModel.TextResourceSource>(),
            limits);
        CompilerModel.TextResourceDiagnostic diagnostic = Assert.Single(compilation.Diagnostics);
        Assert.Equal("WUTTEXT0022", diagnostic.Id);
        Assert.Equal("too-large.json", diagnostic.Location.Path);
        Assert.True(!compilation.Success, "Limit diagnostics must fail compilation.");
        Assert.True(diagnostic.Id != "WUTTEXT0099", "Malformed or oversized consumer input must never become WUTTEXT0099.");
    }

    private static void Cancellation()
    {
        using CancellationTokenSource source = new();
        source.Cancel();
        bool canceled = false;
        try
        {
            CompilerModel.TextResourceCompiler.Compile(
                [Source("manifest.json", "{\"schemaVersion\":1}")],
                Array.Empty<CompilerModel.TextResourceSource>(),
                source.Token);
        }
        catch (OperationCanceledException exception) when (exception.CancellationToken == source.Token)
        {
            canceled = true;
        }

        Assert.True(canceled, "A canceled compilation must throw OperationCanceledException with the caller's token.");
    }

    internal static CompilerModel.TextResourceCompilation CompileCase(
        string category,
        string caseName,
        bool reverseDocuments = false)
    {
        string directory = RepositoryPaths.Resolve("spec", "text-resources", "corpus", category, caseName);
        string manifestPath = Path.Combine(directory, "manifest.json");
        List<CompilerModel.TextResourceSource> manifests = new();
        if (File.Exists(manifestPath))
        {
            manifests.Add(ReadSource(manifestPath));
        }

        string[] documentPaths = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly);
        Array.Sort(documentPaths, StringComparer.Ordinal);
        List<CompilerModel.TextResourceSource> documents = new();
        foreach (string path in documentPaths)
        {
            if (!string.Equals(path, manifestPath, StringComparison.OrdinalIgnoreCase))
            {
                documents.Add(ReadSource(path));
            }
        }

        if (reverseDocuments)
        {
            documents.Reverse();
        }

        return CompilerModel.TextResourceCompiler.Compile(manifests, documents);
    }

    internal static CompilerModel.TextResourceSource ReadSource(string absolutePath)
    {
        string path = Path.GetRelativePath(
            RepositoryPaths.Resolve("spec", "text-resources", "corpus"),
            absolutePath).Replace('\\', '/');
        return new CompilerModel.TextResourceSource(path, File.ReadAllBytes(absolutePath));
    }

    internal static CompilerModel.TextResourceSource Source(string path, string json) =>
        new(path, Encoding.UTF8.GetBytes(json));

    internal static string DiagnosticsText(IReadOnlyList<CompilerModel.TextResourceDiagnostic> diagnostics)
    {
        StringBuilder builder = new();
        foreach (CompilerModel.TextResourceDiagnostic diagnostic in diagnostics)
        {
            builder.Append(diagnostic.Id).Append(' ').Append(diagnostic.Severity).Append(' ')
                .Append(diagnostic.Location).Append(' ').AppendLine(diagnostic.Message);
        }

        return builder.ToString();
    }

    private static CompilerModel.CompiledTextResource Find(
        IReadOnlyList<CompilerModel.CompiledTextResource> resources,
        string key)
    {
        foreach (CompilerModel.CompiledTextResource resource in resources)
        {
            if (string.Equals(resource.Key, key, StringComparison.Ordinal))
            {
                return resource;
            }
        }

        throw new InvalidOperationException($"Resource '{key}' was not found.");
    }

    private static CompilerModel.CompiledTextLocale FindLocale(CompilerModel.CompiledTextCatalog catalog, string tag)
    {
        foreach (CompilerModel.CompiledTextLocale locale in catalog.Locales)
        {
            if (string.Equals(locale.Tag, tag, StringComparison.Ordinal))
            {
                return locale;
            }
        }

        throw new InvalidOperationException($"Locale '{tag}' was not found.");
    }

    private static CompilerModel.CompiledTextPlaceholder FindPlaceholder(
        CompilerModel.CompiledTextResource resource,
        string name)
    {
        foreach (CompilerModel.CompiledTextPlaceholder placeholder in resource.Placeholders)
        {
            if (string.Equals(placeholder.Name, name, StringComparison.Ordinal))
            {
                return placeholder;
            }
        }

        throw new InvalidOperationException($"Placeholder '{name}' was not found.");
    }

    private static string[] Keys(IReadOnlyList<CompilerModel.CompiledTextResource> resources)
    {
        string[] keys = new string[resources.Count];
        for (int i = 0; i < resources.Count; i++)
        {
            keys[i] = resources[i].Key;
        }

        return keys;
    }

    private static string[] PlaceholderNames(IReadOnlyList<CompilerModel.CompiledTextPlaceholder> placeholders)
    {
        string[] names = new string[placeholders.Count];
        for (int i = 0; i < placeholders.Count; i++)
        {
            names[i] = placeholders[i].Name;
        }

        return names;
    }

    private static void AssertSequence(string[] expected, IReadOnlyList<string> actual)
    {
        Assert.Equal(expected.Length, actual.Count, "Sequence lengths differ.");
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], actual[i], $"Sequences differ at index {i}.");
        }
    }
}
