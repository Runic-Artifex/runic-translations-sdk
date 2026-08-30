using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using CompilerModel = Runic.Translations.Compiler;

namespace Runic.Translations.Compiler.Tests;

internal static class CorpusTests
{
    private static readonly CorpusIndex Index = ReadIndex();

    public static void Register(TestRunner runner)
    {
        runner.Add("corpus declares versioned source and location contracts", CorpusContract);
        foreach (CorpusCase testCase in Index.Cases)
        {
            CorpusCase captured = testCase;
            runner.Add("corpus " + captured.Id, () => ExecuteCase(captured));
        }

        runner.Add("corpus fingerprints agree across partition groups", FingerprintGroups);
        runner.Add("invalid-source exact-location quality is at least 95 percent", LocationQuality);
        runner.Add("strict parser rejects comments NaN Infinity and invalid UTF-8 without internal failures", AdditionalHostileJson);
    }

    private static void CorpusContract()
    {
        Assert.Equal(1, Index.FormatVersion);
        Assert.Equal("Runic.Translations.Conformance", Index.Identity);
        Assert.Equal(1, Index.CatalogSchemaVersion);
        Assert.Equal(1, Index.ResourceSchemaVersion);
        Assert.Equal(1, Index.MessageGrammarVersion);
        Assert.Equal(1, Index.LineBase);
        Assert.Equal(1, Index.ColumnBase);
        Assert.True(Index.StartInclusive, "Corpus locations must be start-inclusive.");
        Assert.True(Index.EndExclusive, "Corpus locations must be end-exclusive.");
        Assert.Equal("UTF-16", Index.ColumnUnits);
        AssertSequence(["RTR0020"], Index.ExcludedDiagnostics);
        Assert.True(Index.Cases.Count >= 30, "Wave A must contain a substantial language-neutral corpus.");
    }

    private static void ExecuteCase(CorpusCase testCase)
    {
        CompilerModel.TranslationCompilation compilation = Compile(testCase, reverseDocuments: false);
        Assert.Equal(testCase.Valid, compilation.Success,
            "Unexpected success state.\n" + CompilerTests.DiagnosticsText(compilation.Diagnostics));
        AssertDiagnostics(testCase.ExpectedDiagnostics, compilation.Diagnostics, testCase.Id);

        if (!testCase.Valid || compilation.Catalogs.Count == 0)
        {
            return;
        }

        CompilerModel.CompiledTextCatalog catalog = Assert.Single(compilation.Catalogs);
        if (testCase.Catalog is not null)
        {
            Assert.Equal(testCase.Catalog, catalog.Id);
        }

        if (testCase.DefaultLocale is not null)
        {
            Assert.Equal(testCase.DefaultLocale, catalog.DefaultLocale);
        }

        if (testCase.Locales is not null)
        {
            AssertSequence(testCase.Locales, LocaleTags(catalog.Locales));
        }

        if (testCase.Layers is not null)
        {
            AssertSequence(testCase.Layers, LayerNames(catalog.Layers));
        }

        if (testCase.OrderedKeys is not null)
        {
            AssertSequence(testCase.OrderedKeys, ResourceKeys(catalog.CanonicalResources));
        }

        foreach (KeyValuePair<string, string> pair in testCase.EffectiveValues)
        {
            (string locale, string key) = SplitLocaleKey(pair.Key);
            CompilerModel.CompiledTextLocale compiledLocale = FindLocale(catalog, locale);
            Assert.Equal(pair.Value, FindResource(compiledLocale.DirectResources, key).Pattern, pair.Key);
        }

        foreach (KeyValuePair<string, string> pair in testCase.FallbackValues)
        {
            (string locale, string key) = SplitLocaleKey(pair.Key);
            CompilerModel.CompiledTextLocale compiledLocale = FindLocale(catalog, locale);
            Assert.Equal(pair.Value, FindResource(compiledLocale.ResolvedResources, key).Pattern, pair.Key);
        }

        if (testCase.PlaceholderOrder is not null)
        {
            CompilerModel.CompiledTranslation resource = FindResource(catalog.CanonicalResources, "All");
            string[] names = new string[resource.Placeholders.Count];
            for (int i = 0; i < names.Length; i++)
            {
                names[i] = resource.Placeholders[i].Name;
            }

            AssertSequence(testCase.PlaceholderOrder, names);
        }

        if (testCase.FingerprintHexLength is int expectedLength)
        {
            const string algorithmPrefix = "sha256:";
            Assert.True(catalog.Fingerprint.StartsWith(algorithmPrefix, StringComparison.Ordinal),
                "Fingerprints must identify the SHA-256 algorithm.");
            ReadOnlySpan<char> hexadecimal = catalog.Fingerprint.AsSpan(algorithmPrefix.Length);
            Assert.Equal(expectedLength, hexadecimal.Length);
            foreach (char character in hexadecimal)
            {
                Assert.True((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f'),
                    "Fingerprints must be lowercase hexadecimal SHA-256 values.");
            }
        }
    }

    private static void FingerprintGroups()
    {
        Dictionary<string, string> fingerprints = new(StringComparer.Ordinal);
        foreach (CorpusCase testCase in Index.Cases)
        {
            if (testCase.FingerprintGroup is null)
            {
                continue;
            }

            CompilerModel.TranslationCompilation normal = Compile(testCase, reverseDocuments: false);
            CompilerModel.TranslationCompilation reversed = Compile(testCase, reverseDocuments: true);
            Assert.True(normal.Success, CompilerTests.DiagnosticsText(normal.Diagnostics));
            Assert.True(reversed.Success, CompilerTests.DiagnosticsText(reversed.Diagnostics));
            string fingerprint = Assert.Single(normal.Catalogs).Fingerprint;
            Assert.Equal(fingerprint, Assert.Single(reversed.Catalogs).Fingerprint,
                testCase.Id + " changed with input order.");
            if (fingerprints.TryGetValue(testCase.FingerprintGroup, out string? prior))
            {
                Assert.Equal(prior, fingerprint, testCase.FingerprintGroup + " differs across document partitioning.");
            }
            else
            {
                fingerprints.Add(testCase.FingerprintGroup, fingerprint);
            }
        }

        Assert.True(fingerprints.Count != 0, "The corpus must define at least one cross-partition fingerprint group.");
    }

    private static void LocationQuality()
    {
        int attributable = 0;
        int exact = 0;
        foreach (CorpusCase testCase in Index.Cases)
        {
            if (testCase.ExpectedDiagnostics.Count == 0)
            {
                continue;
            }

            CompilerModel.TranslationCompilation compilation = Compile(testCase, reverseDocuments: false);
            int count = Math.Min(testCase.ExpectedDiagnostics.Count, compilation.Diagnostics.Count);
            attributable += testCase.ExpectedDiagnostics.Count;
            for (int i = 0; i < count; i++)
            {
                ExpectedDiagnostic expected = testCase.ExpectedDiagnostics[i];
                CompilerModel.TranslationDiagnostic actual = compilation.Diagnostics[i];
                if (string.Equals(expected.Path, actual.Location.Path, StringComparison.Ordinal) &&
                    expected.Line == actual.Location.Line && expected.Column == actual.Location.Column &&
                    expected.EndLine == actual.Location.EndLine && expected.EndColumn == actual.Location.EndColumn)
                {
                    exact++;
                }
            }
        }

        Assert.True(attributable != 0, "The invalid-source corpus contains no attributable diagnostics.");
        double quality = (double)exact / attributable;
        Assert.True(quality >= 0.95,
            $"Exact source-location quality was {quality:P2} ({exact}/{attributable}); required minimum is 95%. ");
    }

    private static void AdditionalHostileJson()
    {
        AssertStrictJsonFailure("comment.json", Encoding.UTF8.GetBytes("{/*comment*/}"), 1, 2);
        AssertStrictJsonFailure("nan.json", Encoding.UTF8.GetBytes("{\"value\":NaN}"), 1, 10);
        AssertStrictJsonFailure("infinity.json", Encoding.UTF8.GetBytes("{\"value\":Infinity}"), 1, 10);
        AssertStrictJsonFailure("utf8.json", [0xff], 1, 1);
    }

    private static void AssertStrictJsonFailure(string path, byte[] bytes, int line, int column)
    {
        CompilerModel.TranslationCompilation compilation = CompilerModel.TranslationCompiler.Compile(
            [new CompilerModel.TranslationSource(path, bytes)],
            Array.Empty<CompilerModel.TranslationSource>());
        Assert.True(compilation.Diagnostics.Count != 0, path + " unexpectedly produced no diagnostics.");
        CompilerModel.TranslationDiagnostic diagnostic = compilation.Diagnostics[0];
        Assert.Equal("RTR0001", diagnostic.Id);
        Assert.Equal(CompilerModel.TranslationDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(path, diagnostic.Location.Path);
        Assert.Equal(line, diagnostic.Location.Line);
        Assert.Equal(column, diagnostic.Location.Column);
        foreach (CompilerModel.TranslationDiagnostic item in compilation.Diagnostics)
        {
            Assert.True(item.Id != "RTR0099", path + " was incorrectly reported as an internal compiler failure.");
        }
    }

    private static CompilerModel.TranslationCompilation Compile(CorpusCase testCase, bool reverseDocuments)
    {
        List<CompilerModel.TranslationSource> manifests = new();
        if (testCase.Manifest is not null)
        {
            manifests.Add(ReadCorpusSource(testCase.Manifest));
        }

        List<CompilerModel.TranslationSource> documents = new();
        foreach (string path in testCase.Documents)
        {
            documents.Add(ReadCorpusSource(path));
        }

        if (reverseDocuments)
        {
            documents.Reverse();
        }

        return CompilerModel.TranslationCompiler.Compile(manifests, documents);
    }

    private static CompilerModel.TranslationSource ReadCorpusSource(string path)
    {
        string absolutePath = RepositoryPaths.Resolve(
            "spec", "corpus", path.Replace('/', Path.DirectorySeparatorChar));
        return new CompilerModel.TranslationSource(path, File.ReadAllBytes(absolutePath));
    }

    private static void AssertDiagnostics(
        IReadOnlyList<ExpectedDiagnostic> expected,
        IReadOnlyList<CompilerModel.TranslationDiagnostic> actual,
        string caseId)
    {
        Assert.Equal(expected.Count, actual.Count,
            caseId + " diagnostic count differs.\n" + CompilerTests.DiagnosticsText(actual));
        for (int i = 0; i < expected.Count; i++)
        {
            ExpectedDiagnostic expectedItem = expected[i];
            CompilerModel.TranslationDiagnostic actualItem = actual[i];
            string context = caseId + " diagnostic " + i;
            Assert.Equal(expectedItem.Id, actualItem.Id, context);
            CompilerModel.TranslationDiagnosticSeverity severity = expectedItem.Severity == "warning"
                ? CompilerModel.TranslationDiagnosticSeverity.Warning
                : CompilerModel.TranslationDiagnosticSeverity.Error;
            Assert.Equal(severity, actualItem.Severity, context + " severity");
            Assert.Equal(expectedItem.Path, actualItem.Location.Path, context + " path");
            Assert.Equal(expectedItem.Line, actualItem.Location.Line, context + " line");
            Assert.Equal(expectedItem.Column, actualItem.Location.Column, context + " column");
            Assert.Equal(expectedItem.EndLine, actualItem.Location.EndLine, context + " end line");
            Assert.Equal(expectedItem.EndColumn, actualItem.Location.EndColumn, context + " end column");
        }
    }

    private static CorpusIndex ReadIndex()
    {
        string path = RepositoryPaths.Resolve("spec", "corpus", "index.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
        JsonElement root = document.RootElement;
        JsonElement contracts = root.GetProperty("sourceContracts");
        JsonElement locations = root.GetProperty("locationConvention");
        List<string> excluded = new();
        foreach (JsonElement item in root.GetProperty("excludedDiagnostics").EnumerateArray())
        {
            excluded.Add(RequiredString(item, "id"));
        }

        List<CorpusCase> cases = new();
        foreach (JsonElement item in root.GetProperty("cases").EnumerateArray())
        {
            JsonElement expected = item.GetProperty("expected");
            List<ExpectedDiagnostic> diagnostics = new();
            foreach (JsonElement diagnostic in expected.GetProperty("diagnostics").EnumerateArray())
            {
                diagnostics.Add(new ExpectedDiagnostic(
                    RequiredString(diagnostic, "id"), RequiredString(diagnostic, "severity"),
                    RequiredString(diagnostic, "path"), diagnostic.GetProperty("line").GetInt32(),
                    diagnostic.GetProperty("column").GetInt32(), diagnostic.GetProperty("endLine").GetInt32(),
                    diagnostic.GetProperty("endColumn").GetInt32()));
            }

            cases.Add(new CorpusCase(
                RequiredString(item, "id"), item.GetProperty("valid").GetBoolean(), OptionalString(item, "manifest"),
                StringArray(item.GetProperty("documents")), OptionalString(item, "fingerprintGroup"), diagnostics,
                OptionalString(expected, "catalog"), OptionalString(expected, "defaultLocale"),
                OptionalStringArray(expected, "locales"), OptionalStringArray(expected, "layers"),
                OptionalStringArray(expected, "orderedKeys"), OptionalStringArray(expected, "placeholderOrder"),
                OptionalStringDictionary(expected, "effectiveValues"), OptionalStringDictionary(expected, "fallbackValues"),
                OptionalInt(expected, "fingerprintHexLength")));
        }

        return new CorpusIndex(
            root.GetProperty("formatVersion").GetInt32(), RequiredString(root, "identity"),
            contracts.GetProperty("catalogSchemaVersion").GetInt32(), contracts.GetProperty("resourceSchemaVersion").GetInt32(),
            contracts.GetProperty("messageGrammarVersion").GetInt32(), locations.GetProperty("lineBase").GetInt32(),
            locations.GetProperty("columnBase").GetInt32(), locations.GetProperty("startInclusive").GetBoolean(),
            locations.GetProperty("endExclusive").GetBoolean(), RequiredString(locations, "columnUnits"), excluded, cases);
    }

    private static string RequiredString(JsonElement element, string propertyName) =>
        element.GetProperty(propertyName).GetString() ?? throw new InvalidDataException(propertyName + " cannot be null.");

    private static string? OptionalString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement property) && property.ValueKind != JsonValueKind.Null
            ? property.GetString()
            : null;
    }

    private static int? OptionalInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out JsonElement property) ? property.GetInt32() : null;

    private static List<string>? OptionalStringArray(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out JsonElement property) ? StringArray(property) : null;

    private static List<string> StringArray(JsonElement element)
    {
        List<string> values = new();
        foreach (JsonElement item in element.EnumerateArray())
        {
            values.Add(item.GetString() ?? throw new InvalidDataException("Array value cannot be null."));
        }

        return values;
    }

    private static Dictionary<string, string> OptionalStringDictionary(JsonElement element, string propertyName)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return values;
        }

        foreach (JsonProperty item in property.EnumerateObject())
        {
            values.Add(item.Name, item.Value.GetString() ?? throw new InvalidDataException(item.Name + " cannot be null."));
        }

        return values;
    }

    private static (string Locale, string Key) SplitLocaleKey(string value)
    {
        int separator = value.IndexOf('/');
        Assert.True(separator > 0 && separator < value.Length - 1, "Expected locale/key fact: " + value);
        return (value.Substring(0, separator), value.Substring(separator + 1));
    }

    private static CompilerModel.CompiledTextLocale FindLocale(CompilerModel.CompiledTextCatalog catalog, string tag)
    {
        foreach (CompilerModel.CompiledTextLocale locale in catalog.Locales)
        {
            if (string.Equals(locale.Tag, tag, StringComparison.Ordinal)) return locale;
        }

        throw new InvalidOperationException("Locale not found: " + tag);
    }

    private static CompilerModel.CompiledTranslation FindResource(
        IReadOnlyList<CompilerModel.CompiledTranslation> resources,
        string key)
    {
        foreach (CompilerModel.CompiledTranslation resource in resources)
        {
            if (string.Equals(resource.Key, key, StringComparison.Ordinal)) return resource;
        }

        throw new InvalidOperationException("Resource not found: " + key);
    }

    private static string[] LocaleTags(IReadOnlyList<CompilerModel.CompiledTextLocale> locales)
    {
        string[] result = new string[locales.Count];
        for (int i = 0; i < result.Length; i++) result[i] = locales[i].Tag;
        return result;
    }

    private static string[] LayerNames(IReadOnlyList<CompilerModel.CompiledTextLayer> layers)
    {
        string[] result = new string[layers.Count];
        for (int i = 0; i < result.Length; i++) result[i] = layers[i].Name;
        return result;
    }

    private static string[] ResourceKeys(IReadOnlyList<CompilerModel.CompiledTranslation> resources)
    {
        string[] result = new string[resources.Count];
        for (int i = 0; i < result.Length; i++) result[i] = resources[i].Key;
        return result;
    }

    private static void AssertSequence(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        Assert.Equal(expected.Count, actual.Count, "Sequence lengths differ.");
        for (int i = 0; i < expected.Count; i++) Assert.Equal(expected[i], actual[i], $"Sequences differ at {i}.");
    }

    private sealed class CorpusIndex
    {
        public CorpusIndex(int formatVersion, string identity, int catalogSchemaVersion, int resourceSchemaVersion,
            int messageGrammarVersion, int lineBase, int columnBase, bool startInclusive, bool endExclusive,
            string columnUnits, IReadOnlyList<string> excludedDiagnostics, IReadOnlyList<CorpusCase> cases)
        {
            FormatVersion = formatVersion; Identity = identity; CatalogSchemaVersion = catalogSchemaVersion;
            ResourceSchemaVersion = resourceSchemaVersion; MessageGrammarVersion = messageGrammarVersion;
            LineBase = lineBase; ColumnBase = columnBase; StartInclusive = startInclusive; EndExclusive = endExclusive;
            ColumnUnits = columnUnits; ExcludedDiagnostics = excludedDiagnostics; Cases = cases;
        }

        public int FormatVersion { get; }
        public string Identity { get; }
        public int CatalogSchemaVersion { get; }
        public int ResourceSchemaVersion { get; }
        public int MessageGrammarVersion { get; }
        public int LineBase { get; }
        public int ColumnBase { get; }
        public bool StartInclusive { get; }
        public bool EndExclusive { get; }
        public string ColumnUnits { get; }
        public IReadOnlyList<string> ExcludedDiagnostics { get; }
        public IReadOnlyList<CorpusCase> Cases { get; }
    }

    private sealed class CorpusCase
    {
        public CorpusCase(string id, bool valid, string? manifest, IReadOnlyList<string> documents,
            string? fingerprintGroup, IReadOnlyList<ExpectedDiagnostic> expectedDiagnostics, string? catalog,
            string? defaultLocale, IReadOnlyList<string>? locales, IReadOnlyList<string>? layers,
            IReadOnlyList<string>? orderedKeys, IReadOnlyList<string>? placeholderOrder,
            IReadOnlyDictionary<string, string> effectiveValues, IReadOnlyDictionary<string, string> fallbackValues,
            int? fingerprintHexLength)
        {
            Id = id; Valid = valid; Manifest = manifest; Documents = documents; FingerprintGroup = fingerprintGroup;
            ExpectedDiagnostics = expectedDiagnostics; Catalog = catalog; DefaultLocale = defaultLocale; Locales = locales;
            Layers = layers; OrderedKeys = orderedKeys; PlaceholderOrder = placeholderOrder;
            EffectiveValues = effectiveValues; FallbackValues = fallbackValues; FingerprintHexLength = fingerprintHexLength;
        }

        public string Id { get; }
        public bool Valid { get; }
        public string? Manifest { get; }
        public IReadOnlyList<string> Documents { get; }
        public string? FingerprintGroup { get; }
        public IReadOnlyList<ExpectedDiagnostic> ExpectedDiagnostics { get; }
        public string? Catalog { get; }
        public string? DefaultLocale { get; }
        public IReadOnlyList<string>? Locales { get; }
        public IReadOnlyList<string>? Layers { get; }
        public IReadOnlyList<string>? OrderedKeys { get; }
        public IReadOnlyList<string>? PlaceholderOrder { get; }
        public IReadOnlyDictionary<string, string> EffectiveValues { get; }
        public IReadOnlyDictionary<string, string> FallbackValues { get; }
        public int? FingerprintHexLength { get; }
    }

    private sealed class ExpectedDiagnostic
    {
        public ExpectedDiagnostic(string id, string severity, string path, int line, int column, int endLine, int endColumn)
        {
            Id = id; Severity = severity; Path = path; Line = line; Column = column; EndLine = endLine; EndColumn = endColumn;
        }

        public string Id { get; }
        public string Severity { get; }
        public string Path { get; }
        public int Line { get; }
        public int Column { get; }
        public int EndLine { get; }
        public int EndColumn { get; }
    }
}
