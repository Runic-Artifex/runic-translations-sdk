using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using CompilerModel = Runic.Translations.Compiler;

namespace Runic.Translations.Compiler.Tests;

internal static class CompilerTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("compiler merges layers and resolves fallback per key", MergeAndFallback);
        runner.Add("compiler preserves version 1 pattern contracts", PatternContracts);
        runner.Add("compiler normalizes source paths and retains exact locations", SourcePathsAndLocations);
        runner.Add("compiler IR and fingerprints are deterministic across order and cultures", Determinism);
        runner.Add("compiler rejects duplicate normalized source paths deterministically", DuplicateNormalizedSourcePaths);
        runner.Add("compiler rejects duplicate generated root identities deterministically", DuplicateGeneratedRootIdentities);
        runner.Add("compiler rejects portable case-insensitive hint stem collisions", CaseInsensitiveHintStemCollisions);
        runner.Add("compiler rejects Windows device generated filename stems", WindowsDeviceGeneratedFilenameStems);
        runner.Add("compiler enforces configured hostile-input limits", ConfiguredLimits);
        runner.Add("compiler observes cancellation without diagnostics", Cancellation);
        runner.Add("compiler rejects synthesized generated identifier collisions", GeneratedIdentifierCollisions);
        runner.Add("compiler rejects divergent contracts for allowed extra keys", AllowedExtraContractParity);
    }

    private static void MergeAndFallback()
    {
        CompilerModel.TranslationCompilation compilation = CompileCase("valid", "merge");
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
        CompilerModel.TranslationCompilation compilation = CompileCase("valid", "patterns");
        Assert.True(compilation.Success, DiagnosticsText(compilation.Diagnostics));
        CompilerModel.CompiledTextCatalog catalog = Assert.Single(compilation.Catalogs);
        CompilerModel.CompiledTranslation resource = Find(catalog.CanonicalResources, "All");

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
        Assert.Equal(CompilerModel.TranslationArgumentType.Guid, FindPlaceholder(resource, "id").Type);

        CompilerModel.CompiledTranslation escaped = Find(catalog.CanonicalResources, "Escapes");
        Assert.Equal("Literal {{open}} and repeated text", escaped.Pattern);
        Assert.Equal(0, escaped.Placeholders.Count);
    }

    private static void SourcePathsAndLocations()
    {
        const string manifest = "{\n  \"schemaVersion\": 1,\n  \"catalog\": \"app\",\n  \"code\": { \"namespace\": \"Tests\", \"className\": \"Text\" },\n  \"defaultLocale\": \"en\",\n  \"locales\": [{ \"tag\": \"en\" }],\n  \"layers\": [{ \"name\": \"base\", \"priority\": 0 }]\n}";
        const string malformed = "{\n  \"schemaVersion\": 1,\n  \"catalog\": \"app\",\n  \"locale\": \"en\",\n  \"layer\": \"base\",\n  \"resources\": {\n    \"Bad\": \"unmatched {\"\n  }\n}";

        CompilerModel.TranslationCompilation compilation = CompilerModel.TranslationCompiler.Compile(
            [Source(".\\fixtures\\manifest.json", manifest)],
            [Source("./fixtures\\bad.json", malformed)]);
        CompilerModel.TranslationDiagnostic diagnostic = Assert.Single(compilation.Diagnostics);
        Assert.Equal("RTR0014", diagnostic.Id);
        Assert.Equal(CompilerModel.TranslationDiagnosticSeverity.Error, diagnostic.Severity);
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
            CompilerModel.TranslationCompilation first = CompileCase("valid", "determinism-a", reverseDocuments: false);

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
            CompilerModel.TranslationCompilation reordered = CompileCase("valid", "determinism-a", reverseDocuments: true);
            CompilerModel.TranslationCompilation repartitioned = CompileCase("valid", "determinism-b", reverseDocuments: true);

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
        CompilerModel.TranslationCompilerOptions limits = new(
            maximumDocumentBytes: 12,
            maximumDepth: 2,
            maximumKeysPerCatalog: 1,
            maximumValueBytes: 4,
            maximumPlaceholdersPerValue: 1,
            maximumLocalesPerCatalog: 1);
        CompilerModel.TranslationCompilation compilation = CompilerModel.TranslationCompiler.Compile(
            [Source("too-large.json", "{\"schemaVersion\":1}")],
            Array.Empty<CompilerModel.TranslationSource>(),
            limits);
        CompilerModel.TranslationDiagnostic diagnostic = Assert.Single(compilation.Diagnostics);
        Assert.Equal("RTR0022", diagnostic.Id);
        Assert.Equal("too-large.json", diagnostic.Location.Path);
        Assert.True(!compilation.Success, "Limit diagnostics must fail compilation.");
        Assert.True(diagnostic.Id != "RTR0099", "Malformed or oversized consumer input must never become RTR0099.");
    }

    private static void DuplicateNormalizedSourcePaths()
    {
        const string manifest = """
            {
              "schemaVersion": 1,
              "catalog": "duplicates",
              "code": { "namespace": "Tests", "className": "DuplicateText" },
              "defaultLocale": "en",
              "locales": [{ "tag": "en" }],
              "layers": [{ "name": "base", "priority": 0 }]
            }
            """;
        CompilerModel.TranslationSource[] forward =
        [
            Source(@".\same\b.json", "not JSON"),
            Source("same/a.json", "{}"),
            Source(@".\same\a.json", "[1, 2, 3]"),
            Source("same/b.json", "{\"schemaVersion\":1}"),
        ];
        CompilerModel.TranslationSource[] reversed =
        [
            forward[3],
            forward[2],
            forward[1],
            forward[0],
        ];

        CompilerModel.TranslationCompilation first = CompilerModel.TranslationCompiler.Compile(
            [Source("manifest.json", manifest)], forward);
        CompilerModel.TranslationCompilation second = CompilerModel.TranslationCompiler.Compile(
            [Source("manifest.json", manifest)], reversed);

        Assert.Equal(DiagnosticSnapshot(first.Diagnostics), DiagnosticSnapshot(second.Diagnostics));
        Assert.Equal(2, first.Diagnostics.Count, DiagnosticsText(first.Diagnostics));
        Assert.Equal("same/a.json", first.Diagnostics[0].Location.Path);
        Assert.Equal("same/b.json", first.Diagnostics[1].Location.Path);
        foreach (CompilerModel.TranslationDiagnostic diagnostic in first.Diagnostics)
        {
            Assert.Equal("RTR0002", diagnostic.Id);
            Assert.Equal(0, diagnostic.Location.StartByte);
            Assert.Equal(0, diagnostic.Location.LengthBytes);
            Assert.Equal(1, diagnostic.Location.Line);
            Assert.Equal(1, diagnostic.Location.Column);
        }
    }

    private static string DiagnosticSnapshot(IReadOnlyList<CompilerModel.TranslationDiagnostic> diagnostics)
    {
        StringBuilder builder = new();
        foreach (CompilerModel.TranslationDiagnostic diagnostic in diagnostics)
        {
            builder.Append(diagnostic.Id).Append('|').Append(diagnostic.Severity).Append('|')
                .Append(diagnostic.Message).Append('|').Append(diagnostic.Location.Path).Append('|')
                .Append(diagnostic.Location.StartByte).Append('|').Append(diagnostic.Location.LengthBytes).Append('|')
                .Append(diagnostic.Location.Line).Append('|').Append(diagnostic.Location.Column).Append('|')
                .Append(diagnostic.Location.EndLine).Append('|').Append(diagnostic.Location.EndColumn).Append('\n');
        }

        return builder.ToString();
    }

    private static void DuplicateGeneratedRootIdentities()
    {
        const string firstManifest = """
            {
              "schemaVersion": 1,
              "catalog": "alpha",
              "code": { "namespace": "Tests.Shared", "className": "SharedText" },
              "defaultLocale": "en",
              "locales": [{ "tag": "en" }],
              "layers": [{ "name": "base", "priority": 0 }]
            }
            """;
        const string secondManifest = """
            {
              "schemaVersion": 1,
              "catalog": "beta",
              "code": { "namespace": "Tests.Shared", "className": "SharedText" },
              "defaultLocale": "en",
              "locales": [{ "tag": "en" }],
              "layers": [{ "name": "base", "priority": 0 }]
            }
            """;
        const string firstDocument = """
            { "schemaVersion": 1, "catalog": "alpha", "locale": "en", "layer": "base", "resources": { "Value": "Alpha" } }
            """;
        const string secondDocument = """
            { "schemaVersion": 1, "catalog": "beta", "locale": "en", "layer": "base", "resources": { "Value": "Beta" } }
            """;

        CompilerModel.TranslationCompilation first = CompilerModel.TranslationCompiler.Compile(
            [Source("catalogs/a.manifest.json", firstManifest), Source("catalogs/z.manifest.json", secondManifest)],
            [Source("catalogs/a.texts.json", firstDocument), Source("catalogs/z.texts.json", secondDocument)]);
        CompilerModel.TranslationCompilation reversed = CompilerModel.TranslationCompiler.Compile(
            [Source("catalogs/z.manifest.json", secondManifest), Source("catalogs/a.manifest.json", firstManifest)],
            [Source("catalogs/z.texts.json", secondDocument), Source("catalogs/a.texts.json", firstDocument)]);

        Assert.Equal(DiagnosticSnapshot(first.Diagnostics), DiagnosticSnapshot(reversed.Diagnostics));
        CompilerModel.TranslationDiagnostic diagnostic = Assert.Single(first.Diagnostics);
        Assert.Equal("RTR0018", diagnostic.Id);
        Assert.Equal("catalogs/z.manifest.json", diagnostic.Location.Path);
        int expectedStart = secondManifest.IndexOf("\"SharedText\"", StringComparison.Ordinal);
        Assert.Equal(expectedStart, diagnostic.Location.StartByte);
        Assert.Equal("\"SharedText\"".Length, diagnostic.Location.LengthBytes);
        Assert.True(diagnostic.Message.Contains("catalogs/a.manifest.json", StringComparison.Ordinal),
            "The collision diagnostic must identify the first manifest deterministically.");
        Assert.Equal(
            "Generated type 'Tests.Shared.SharedText' for catalog 'beta' collides with catalog 'alpha' declared in 'catalogs/a.manifest.json'.",
            diagnostic.Message);
    }

    private static void CaseInsensitiveHintStemCollisions()
    {
        const string firstManifest = """
            {
              "schemaVersion": 1,
              "catalog": "alpha-hint",
              "code": { "namespace": "Tests.One", "className": "Foo" },
              "defaultLocale": "en",
              "locales": [{ "tag": "en" }],
              "layers": [{ "name": "base", "priority": 0 }]
            }
            """;
        const string secondManifest = """
            {
              "schemaVersion": 1,
              "catalog": "beta-hint",
              "code": { "namespace": "Tests.Two", "className": "foo" },
              "defaultLocale": "en",
              "locales": [{ "tag": "en" }],
              "layers": [{ "name": "base", "priority": 0 }]
            }
            """;
        const string firstDocument = """
            { "schemaVersion": 1, "catalog": "alpha-hint", "locale": "en", "layer": "base", "resources": { "Value": "Alpha" } }
            """;
        const string secondDocument = """
            { "schemaVersion": 1, "catalog": "beta-hint", "locale": "en", "layer": "base", "resources": { "Value": "Beta" } }
            """;

        CompilerModel.TranslationCompilation first = CompilerModel.TranslationCompiler.Compile(
            [Source("hints/a.manifest.json", firstManifest), Source("hints/z.manifest.json", secondManifest)],
            [Source("hints/a.texts.json", firstDocument), Source("hints/z.texts.json", secondDocument)]);
        CompilerModel.TranslationCompilation reversed = CompilerModel.TranslationCompiler.Compile(
            [Source("hints/z.manifest.json", secondManifest), Source("hints/a.manifest.json", firstManifest)],
            [Source("hints/z.texts.json", secondDocument), Source("hints/a.texts.json", firstDocument)]);

        Assert.Equal(DiagnosticSnapshot(first.Diagnostics), DiagnosticSnapshot(reversed.Diagnostics));
        CompilerModel.TranslationDiagnostic diagnostic = Assert.Single(first.Diagnostics);
        Assert.Equal("RTR0018", diagnostic.Id);
        Assert.Equal("hints/z.manifest.json", diagnostic.Location.Path);
        Assert.Equal(secondManifest.IndexOf("\"foo\"", StringComparison.Ordinal), diagnostic.Location.StartByte);
        Assert.Equal("\"foo\"".Length, diagnostic.Location.LengthBytes);
        Assert.Equal(
            "Generated hint stem 'foo' for catalog 'beta-hint' collides case-insensitively with stem 'Foo' for catalog 'alpha-hint' declared in 'hints/a.manifest.json'.",
            diagnostic.Message);
    }

    private static void WindowsDeviceGeneratedFilenameStems()
    {
        const string catalogManifest = """
            {
              "schemaVersion": 1,
              "catalog": "con",
              "code": { "namespace": "Tests", "className": "AppText" },
              "defaultLocale": "en",
              "locales": [{ "tag": "en" }],
              "layers": [{ "name": "base", "priority": 0 }]
            }
            """;
        const string catalogDocument = """
            { "schemaVersion": 1, "catalog": "con", "locale": "en", "layer": "base", "resources": { "Value": "Text" } }
            """;
        const string classManifest = """
            {
              "schemaVersion": 1,
              "catalog": "app",
              "code": { "namespace": "Tests", "className": "CON" },
              "defaultLocale": "en",
              "locales": [{ "tag": "en" }],
              "layers": [{ "name": "base", "priority": 0 }]
            }
            """;
        const string classDocument = """
            { "schemaVersion": 1, "catalog": "app", "locale": "en", "layer": "base", "resources": { "Value": "Text" } }
            """;

        AssertDeviceStem(
            "device-catalog.manifest.json",
            catalogManifest,
            catalogDocument,
            "\"con\"");
        AssertDeviceStem(
            "device-catalog-uppercase.manifest.json",
            catalogManifest.Replace("\"con\"", "\"CON\"", StringComparison.Ordinal),
            catalogDocument.Replace("\"con\"", "\"CON\"", StringComparison.Ordinal),
            "\"CON\"");
        AssertDeviceStem(
            "device-class.manifest.json",
            classManifest,
            classDocument,
            "\"CON\"");
        AssertDeviceStem(
            "device-class-lowercase.manifest.json",
            classManifest.Replace("\"CON\"", "\"con\"", StringComparison.Ordinal),
            classDocument,
            "\"con\"");

        static void AssertDeviceStem(string path, string manifest, string document, string expectedToken)
        {
            CompilerModel.TranslationCompilation compilation = CompilerModel.TranslationCompiler.Compile(
                [Source(path, manifest)],
                [Source(path.Replace("manifest", "texts", StringComparison.Ordinal), document)]);
            CompilerModel.TranslationDiagnostic diagnostic = Assert.Single(compilation.Diagnostics);
            Assert.Equal("RTR0018", diagnostic.Id);
            Assert.Equal(path, diagnostic.Location.Path);
            Assert.Equal(manifest.IndexOf(expectedToken, StringComparison.Ordinal), diagnostic.Location.StartByte);
            Assert.Equal(expectedToken.Length, diagnostic.Location.LengthBytes);
        }
    }

    private static void Cancellation()
    {
        using CancellationTokenSource source = new();
        source.Cancel();
        bool canceled = false;
        try
        {
            CompilerModel.TranslationCompiler.Compile(
                [Source("manifest.json", "{\"schemaVersion\":1}")],
                Array.Empty<CompilerModel.TranslationSource>(),
                source.Token);
        }
        catch (OperationCanceledException exception) when (exception.CancellationToken == source.Token)
        {
            canceled = true;
        }

        Assert.True(canceled, "A canceled compilation must throw OperationCanceledException with the caller's token.");
    }

    private static void GeneratedIdentifierCollisions()
    {
        const string manifest = """
            {
              "schemaVersion": 1,
              "catalog": "collision",
              "code": { "namespace": "Tests", "className": "AppText" },
              "defaultLocale": "en",
              "locales": [{ "tag": "en" }],
              "layers": [{ "name": "base", "priority": 0 }]
            }
            """;
        const string document = """
            {
              "schemaVersion": 1,
              "catalog": "collision",
              "locale": "en",
              "layer": "base",
              "resources": {
                "Foo": { "Bar": "nested" },
                "FooGroup": "sibling",
                "AppTextKeys": { "Value": "keys" }
              }
            }
            """;

        CompilerModel.TranslationCompilation compilation = CompilerModel.TranslationCompiler.Compile(
            [Source("collision.manifest.json", manifest)],
            [Source("collision.en.json", document)]);
        Assert.Equal(2, CountDiagnostics(compilation, "RTR0018"), DiagnosticsText(compilation.Diagnostics));
    }

    private static void AllowedExtraContractParity()
    {
        const string manifest = """
            {
              "schemaVersion": 1,
              "catalog": "extras",
              "code": { "namespace": "Tests", "className": "ExtraText" },
              "defaultLocale": "en",
              "locales": [
                { "tag": "en" },
                { "tag": "de", "fallback": "en" },
                { "tag": "fr", "fallback": "en" }
              ],
              "layers": [{ "name": "base", "priority": 0 }],
              "validation": { "translationCompleteness": "allow", "extraLocaleKeys": "allow" }
            }
            """;
        const string english = """
            { "schemaVersion": 1, "catalog": "extras", "locale": "en", "layer": "base", "resources": { "Base": "Base" } }
            """;
        const string german = """
            { "schemaVersion": 1, "catalog": "extras", "locale": "de", "layer": "base", "resources": { "Extra": { "$value": "{value}", "$placeholders": { "value": { "type": "int" } } } } }
            """;
        const string french = """
            { "schemaVersion": 1, "catalog": "extras", "locale": "fr", "layer": "base", "resources": { "Extra": { "$value": "{value}", "$placeholders": { "value": { "type": "string" } } } } }
            """;

        CompilerModel.TranslationCompilation compilation = CompilerModel.TranslationCompiler.Compile(
            [Source("extras.manifest.json", manifest)],
            [Source("extras.en.json", english), Source("extras.de.json", german), Source("extras.fr.json", french)]);
        Assert.Equal(1, CountDiagnostics(compilation, "RTR0016"), DiagnosticsText(compilation.Diagnostics));
        Assert.True(!compilation.Success, "Divergent allowed-extra contracts must fail before generation.");
    }

    private static int CountDiagnostics(CompilerModel.TranslationCompilation compilation, string id)
    {
        int count = 0;
        foreach (CompilerModel.TranslationDiagnostic diagnostic in compilation.Diagnostics)
        {
            if (string.Equals(diagnostic.Id, id, StringComparison.Ordinal)) count++;
        }

        return count;
    }

    internal static CompilerModel.TranslationCompilation CompileCase(
        string category,
        string caseName,
        bool reverseDocuments = false)
    {
        string directory = RepositoryPaths.Resolve("spec", "corpus", category, caseName);
        string manifestPath = Path.Combine(directory, "manifest.json");
        List<CompilerModel.TranslationSource> manifests = new();
        if (File.Exists(manifestPath))
        {
            manifests.Add(ReadSource(manifestPath));
        }

        string[] documentPaths = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly);
        Array.Sort(documentPaths, StringComparer.Ordinal);
        List<CompilerModel.TranslationSource> documents = new();
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

        return CompilerModel.TranslationCompiler.Compile(manifests, documents);
    }

    internal static CompilerModel.TranslationSource ReadSource(string absolutePath)
    {
        string path = Path.GetRelativePath(
            RepositoryPaths.Resolve("spec", "corpus"),
            absolutePath).Replace('\\', '/');
        return new CompilerModel.TranslationSource(path, File.ReadAllBytes(absolutePath));
    }

    internal static CompilerModel.TranslationSource Source(string path, string json) =>
        new(path, Encoding.UTF8.GetBytes(json));

    internal static string DiagnosticsText(IReadOnlyList<CompilerModel.TranslationDiagnostic> diagnostics)
    {
        StringBuilder builder = new();
        foreach (CompilerModel.TranslationDiagnostic diagnostic in diagnostics)
        {
            builder.Append(diagnostic.Id).Append(' ').Append(diagnostic.Severity).Append(' ')
                .Append(diagnostic.Location).Append(' ').AppendLine(diagnostic.Message);
        }

        return builder.ToString();
    }

    private static CompilerModel.CompiledTranslation Find(
        IReadOnlyList<CompilerModel.CompiledTranslation> resources,
        string key)
    {
        foreach (CompilerModel.CompiledTranslation resource in resources)
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
        CompilerModel.CompiledTranslation resource,
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

    private static string[] Keys(IReadOnlyList<CompilerModel.CompiledTranslation> resources)
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
