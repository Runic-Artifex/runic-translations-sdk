using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using Runic.Translations.Tooling;
using Runic.Translations.Compiler;
using Runic.Translations;
using Runic.Translations.PackageConsumer;

namespace Runic.Translations.PackageTests;

internal static class Program
{
    private const string Fingerprint = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string RepositoryUrl = "https://github.com/Runic-Artifex/runic-translations";
    private static readonly Guid SourceLinkKind = new("CC110556-A091-4D38-9FEC-25AB9A351A6A");
    private static readonly string PackageVersion =
        Environment.GetEnvironmentVariable("RUNIC_PACKAGE_VERSION") ?? "1.0.0-preview.1";
    private static int _passed;

    public static async Task<int> Main(string[] args)
    {
        try
        {
            string? feed = ParseFeed(args);
            if (feed is not null)
            {
                InspectPackages(feed);
            }

            await ExerciseRuntimePackageAsync().ConfigureAwait(false);
            Console.WriteLine($"PASS: {_passed} package-consumer checks");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static string? ParseFeed(string[] args)
    {
        if (args.Length == 0)
        {
            return null;
        }

        if (args.Length != 2 || !string.Equals(args[0], "--feed", StringComparison.Ordinal))
        {
            throw new ArgumentException("Usage: Runic.Translations.PackageTests [--feed <directory>]");
        }

        string feed = Path.GetFullPath(args[1]);
        Assert(Directory.Exists(feed), "the isolated package feed exists");
        return feed;
    }

    private static void InspectPackages(string feed)
    {
        AssertExactFeedContents(feed);
        string runtime = RequireSingle(feed, $"Runic.Translations.{PackageVersion}.nupkg");
        string tooling = RequireSingle(feed, $"Runic.Translations.Tooling.{PackageVersion}.nupkg");
        string build = RequireSingle(feed, $"Runic.Translations.Build.{PackageVersion}.nupkg");
        string tool = RequireSingle(feed, $"dotnet-runic-translations.{PackageVersion}.nupkg");
        string templates = RequireSingle(feed, $"Runic.Translations.Templates.{PackageVersion}.nupkg");

        AssertPackageShape(runtime,
            "Runic.Translations.nuspec",
            "README.md",
            "THIRD-PARTY-NOTICES.md",
            "licenses/LICENSE",
            "lib/net10.0/Runic.Translations.dll",
            "lib/net10.0/Runic.Translations.xml");
        AssertPackageShape(tooling,
            "Runic.Translations.Tooling.nuspec",
            "README.md",
            "THIRD-PARTY-NOTICES.md",
            "licenses/LICENSE",
            "lib/net10.0/Runic.Translations.Tooling.dll",
            "lib/net10.0/Runic.Translations.Authoring.dll",
            "lib/net10.0/Runic.Translations.Compiler.dll",
            "schemas/resources-v3.schema.json",
            "schemas/message-ast-v3.schema.json",
            "schemas/locale-pack-v2.schema.json",
            "schemas/locale-artifact-v2.schema.json");
        AssertPackageShape(build,
            "Runic.Translations.Build.nuspec",
            "README.md",
            "build/Runic.Translations.Build.props",
            "build/Runic.Translations.Build.targets",
            "analyzers/dotnet/cs/Runic.Translations.Generator.dll",
            "analyzers/dotnet/cs/Runic.Translations.Compiler.dll");
        AssertPackageShape(tool,
            "dotnet-runic-translations.nuspec",
            "README.md",
            "THIRD-PARTY-NOTICES.md",
            "licenses/LICENSE",
            "tools/net10.0/any/DotnetToolSettings.xml",
            "tools/net10.0/any/dotnet-runic-translations.deps.json",
            "tools/net10.0/any/dotnet-runic-translations.dll",
            "tools/net10.0/any/dotnet-runic-translations.runtimeconfig.json",
            "tools/net10.0/any/Runic.Translations.Authoring.dll",
            "tools/net10.0/any/Runic.Translations.Compiler.dll",
            "tools/net10.0/any/Runic.Translations.Tooling.dll",
            "tools/net10.0/any/Runic.CommandLine.dll",
            "tools/net10.0/any/Runic.CommandLine.xml");
        AssertPackageShape(templates,
            "Runic.Translations.Templates.nuspec",
            "README.md",
            "content/templates/item/.template.config/template.json",
            "content/templates/item/_catalog_._defaultLocale_.json",
            "content/templates/item/_catalog_.catalog.json",
            "content/templates/project/.config/dotnet-tools.json",
            "content/templates/project/.template.config/template.json",
            "content/templates/project/Resources/_catalog_._defaultLocale_.json",
            "content/templates/project/Resources/_catalog_.catalog.json",
            "content/templates/project/RunicTranslationsProject.csproj");

        AssertDependencies(runtime, Array.Empty<string>());
        AssertDependencies(tooling, ["Runic.CommandLine"]);
        AssertDependencies(build, Array.Empty<string>());
        AssertDependencies(tool, Array.Empty<string>());
        AssertDependencies(templates, Array.Empty<string>());

        AssertLicense(runtime);
        AssertLicense(tooling);
        AssertLicense(build);
        AssertLicense(tool);
        AssertLicense(templates);

        AssertRepositoryMetadata(runtime);
        AssertRepositoryMetadata(tooling);
        AssertRepositoryMetadata(build);
        AssertRepositoryMetadata(tool);
        AssertRepositoryMetadata(templates);

        AssertEmbeddedSourceLink(runtime, "lib/net10.0/Runic.Translations.dll");
        AssertEmbeddedSourceLink(tooling, "lib/net10.0/Runic.Translations.Tooling.dll");
        AssertEmbeddedSourceLink(tooling, "lib/net10.0/Runic.Translations.Authoring.dll");
        AssertEmbeddedSourceLink(tooling, "lib/net10.0/Runic.Translations.Compiler.dll");
        AssertToolingSchemaClosure(tooling);
        AssertEmbeddedSourceLink(build, "analyzers/dotnet/cs/Runic.Translations.Generator.dll");
        AssertEmbeddedSourceLink(tool, "tools/net10.0/any/dotnet-runic-translations.dll");
    }

    private static async Task ExerciseRuntimePackageAsync()
    {
        SourceV3MigrationResult migration = TranslationsTooling.MigrateV2ToV3(Encoding.UTF8.GetBytes("{\"schemaVersion\":2,\"catalog\":\"app\",\"locale\":\"en\",\"layer\":\"base\",\"resources\":{}}"));
        Assert(migration.DocumentBytes.Length > 0 && migration.Report.IsLossless,
            "packed tooling facade migrates a valid v2 document");
        TranslationCompilation interchangeCompilation = TranslationsTooling.Compile(
            [new TranslationSource("app.catalog.json", Encoding.UTF8.GetBytes("{\"schemaVersion\":2,\"catalog\":\"app\",\"code\":{\"namespace\":\"App\",\"className\":\"Text\"},\"defaultLocale\":\"en\",\"locales\":[{\"tag\":\"en\"},{\"tag\":\"de\",\"fallback\":\"en\"}],\"layers\":[{\"name\":\"base\",\"priority\":0}]}"))],
            [new TranslationSource("app.en.json", Encoding.UTF8.GetBytes("{\"schemaVersion\":2,\"catalog\":\"app\",\"locale\":\"en\",\"layer\":\"base\",\"resources\":{\"Hello\":\"Hello\"}}")), new TranslationSource("app.de.json", Encoding.UTF8.GetBytes("{\"schemaVersion\":2,\"catalog\":\"app\",\"locale\":\"de\",\"layer\":\"base\",\"resources\":{\"Hello\":\"Hallo\"}}"))]);
        TranslationXliffExportResult interchange = TranslationInterchange.ExportXliff21(interchangeCompilation);
        TranslationXliffImportResult imported = TranslationInterchange.ImportXliff21(interchange.Documents.Single().Bytes);
        Assert(imported.ResourceDocumentBytes.Length > 0 && imported.TargetLocale == "de",
            "packed tooling facade exchanges the closed XLIFF 2.1 profile");
        LocalePackV2BuildResult localePacks = TranslationsTooling.BuildLocalePackV2(interchangeCompilation);
        Assert(localePacks.Documents.Count == 2 && localePacks.Documents.All(document => document.Text.Contains("\"artifactVersion\":2", StringComparison.Ordinal)),
            "packed tooling facade builds canonical locale-pack-v2 artifacts");
        ITranslationManager generatedManager = await ConsumerTextCatalog.CreateManagerAsync().ConfigureAwait(false);
        var generatedText = new ConsumerText(generatedManager);
        Assert(string.Equals(generatedText.Greeting("Ada"), "Hello Ada", StringComparison.Ordinal),
            "typed generated accessor compiles and formats through the packed runtime");

        byte[] generatedPack = Encoding.UTF8.GetBytes(
            "{\"artifactVersion\":1,\"messageGrammarVersion\":1,\"catalog\":\"packageconsumer\",\"locale\":\"en\"," +
            "\"contractFingerprint\":\"" + ConsumerTextCatalog.ContractFingerprint + "\",\"messages\":{\"Greeting\":{" +
            "\"pattern\":\"External {name}\",\"arguments\":[{\"name\":\"name\",\"type\":\"string\",\"format\":\"none\"}]}}}");
        ITranslationManager generatedExternalManager = await ConsumerTextCatalog.CreateExternalManagerAsync(
            new MemoryPackSource("packageconsumer", "en", generatedPack),
            integrityVerifier: static (content, _) => ValueTask.FromResult(content.Length > 0)).ConfigureAwait(false);
        var generatedExternalText = new ConsumerText(generatedExternalManager);
        Assert(string.Equals(generatedExternalText.Greeting("Ada"), "External Ada", StringComparison.Ordinal),
            "generated external manager composes a verified pack through the shipped snapshot factory");

        CompiledTranslationCatalog catalog = CreateCatalog();
        var provider = new CompiledTranslationProvider(catalog);
        ITranslationSnapshot initial = await provider.GetSnapshotAsync("en").ConfigureAwait(false);
        var manager = new TranslationManager(provider, initial);

        var greeting = new TranslationKey("app", 0, "greeting");
        var title = new TranslationKey("app", 1, "title");
        string formatted = manager.Current.Format(greeting, [new TextArgument("name", "Ada")]);
        Assert(string.Equals(formatted, "Hello Ada", StringComparison.Ordinal), "compiled snapshot formats a typed argument");

        int transitions = 0;
        manager.LocaleChanged += (_, change) =>
        {
            Assert(string.Equals(change.OldLocale, "en", StringComparison.Ordinal), "locale event reports the old snapshot");
            Assert(string.Equals(change.NewLocale, "de", StringComparison.Ordinal), "locale event reports the new snapshot");
            transitions++;
        };
        await manager.SetLocaleAsync("de").ConfigureAwait(false);
        Assert(string.Equals(manager.CurrentLocale, "de", StringComparison.Ordinal), "manager atomically swaps locale snapshots");
        Assert(transitions == 1, "one successful swap raises one event");
        Assert(string.Equals(manager.Current.Get(title), "Application", StringComparison.Ordinal), "compiled locale fallback resolves default text");

        TranslationPackContract contract = CreatePackContract();
        byte[] packBytes = Encoding.UTF8.GetBytes(
            "{\"artifactVersion\":1,\"messageGrammarVersion\":1,\"catalog\":\"app\",\"locale\":\"de\"," +
            "\"contractFingerprint\":\"" + Fingerprint + "\",\"messages\":{\"greeting\":{" +
            "\"pattern\":\"Extern {name}\",\"arguments\":[{\"name\":\"name\",\"type\":\"string\",\"format\":\"none\"}]}}}");
        VerifiedExternalTranslationPack verified = await TranslationPackLoader.VerifyAsync(
            new ExternalTranslationPack(packBytes),
            contract,
            integrityVerifier: static (content, _) => ValueTask.FromResult(content.Length > 0)).ConfigureAwait(false);
        Assert(verified.TryGetPattern(greeting, out string externalPattern), "verified external pack contains the generated key");
        Assert(string.Equals(externalPattern, "Extern {name}", StringComparison.Ordinal), "external pack preserves the verified pattern");
    }

    private static CompiledTranslationCatalog CreateCatalog() => new(
        "app",
        "en",
        [
            new CompiledTranslationDefinition("greeting",
                [new TranslationPlaceholderDescriptor("name", TextArgumentType.String, TextArgumentFormat.None)]),
            new CompiledTranslationDefinition("title", Array.Empty<TranslationPlaceholderDescriptor>()),
        ],
        [
            new CompiledTranslationLocale("de", "en", [new CompiledTranslationValue(0, "Hallo {name}")]),
            new CompiledTranslationLocale("en", null,
                [new CompiledTranslationValue(0, "Hello {name}"), new CompiledTranslationValue(1, "Application")]),
        ]);

    private static TranslationPackContract CreatePackContract() => new(
        "app",
        "de",
        Fingerprint,
        [
            new TranslationPackMessageContract(
                new TranslationKey("app", 0, "greeting"),
                [new TranslationPackArgumentContract("name", TextArgumentType.String, TextArgumentFormat.None)]),
        ]);

    private static void AssertExactFeedContents(string feed)
    {
        string[] actual = Directory.GetFiles(feed, "*.nupkg")
            .Select(static path => Path.GetFileName(path)!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        string[] approved =
        [
            $"Runic.Translations.Tooling.{PackageVersion}.nupkg",
            $"Runic.Translations.{PackageVersion}.nupkg",
            $"Runic.Translations.Build.{PackageVersion}.nupkg",
            $"Runic.Translations.Templates.{PackageVersion}.nupkg",
            $"dotnet-runic-translations.{PackageVersion}.nupkg",
        ];
        Assert(actual.SequenceEqual(approved.OrderBy(name => name, StringComparer.Ordinal), StringComparer.Ordinal),
            "feed contains exactly the approved package set");
    }

    private static string RequireSingle(string feed, string fileName)
    {
        string[] matches = Directory.GetFiles(feed, fileName);
        Assert(matches.Length == 1, $"feed contains exactly one {fileName}");
        return matches.Single();
    }

    private static void AssertPackageShape(string package, params string[] expectedEntries)
    {
        using ZipArchive archive = ZipFile.OpenRead(package);
        var expected = new HashSet<string>(expectedEntries, StringComparer.OrdinalIgnoreCase);
        expected.Add("icon.png");
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string name = entry.FullName;
            if (IsNuGetInfrastructure(name))
            {
                continue;
            }

            Assert(expected.Remove(name), $"{Path.GetFileName(package)} contains only approved payload entry {name}");
        }

        Assert(expected.Count == 0,
            $"{Path.GetFileName(package)} contains every approved payload entry ({string.Join(", ", expected)})");
    }

    private static bool IsNuGetInfrastructure(string entry) =>
        string.Equals(entry, "_rels/.rels", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(entry, "[Content_Types].xml", StringComparison.OrdinalIgnoreCase) ||
        (entry.StartsWith("package/services/metadata/core-properties/", StringComparison.OrdinalIgnoreCase) &&
         entry.EndsWith(".psmdcp", StringComparison.OrdinalIgnoreCase));

    private static void AssertDependencies(string package, IReadOnlyCollection<string> approvedDependencies)
    {
        using ZipArchive archive = ZipFile.OpenRead(package);
        ZipArchiveEntry nuspec = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
        XDocument document;
        using (Stream stream = nuspec.Open())
        {
            document = XDocument.Load(stream, LoadOptions.None);
        }

        string[] actual = document.Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "dependency", StringComparison.Ordinal))
            .Select(element => (string?)element.Attribute("id") ?? string.Empty)
            .OrderBy(identity => identity, StringComparer.Ordinal)
            .ToArray();
        string[] approved = approvedDependencies.OrderBy(identity => identity, StringComparer.Ordinal).ToArray();
        Assert(actual.SequenceEqual(approved, StringComparer.Ordinal),
            $"{Path.GetFileName(package)} has exactly the approved dependency set");
    }

    private static void AssertToolingSchemaClosure(string package)
    {
        using ZipArchive archive = ZipFile.OpenRead(package);
        var entries = new HashSet<string>(archive.Entries.Select(static entry => entry.FullName), StringComparer.Ordinal);
        foreach (ZipArchiveEntry schema in archive.Entries.Where(static entry => entry.FullName.StartsWith("schemas/", StringComparison.Ordinal) && entry.FullName.EndsWith(".json", StringComparison.Ordinal)))
        {
            using Stream stream = schema.Open();
            using JsonDocument document = JsonDocument.Parse(stream);
            AssertSchemaReferences(document.RootElement, schema.FullName, entries);
        }
    }

    private static void AssertSchemaReferences(JsonElement element, string source, ISet<string> entries)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (property.NameEquals("$ref") && property.Value.ValueKind == JsonValueKind.String)
                {
                    string reference = property.Value.GetString()!;
                    if (!reference.StartsWith('#'))
                    {
                        string target = "schemas/" + reference.Split('#')[0];
                        Assert(entries.Contains(target), $"{source} has its referenced schema {target} in the tooling package");
                    }
                }
                AssertSchemaReferences(property.Value, source, entries);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray()) AssertSchemaReferences(item, source, entries);
        }
    }

    private static void AssertLicense(string package)
    {
        using ZipArchive archive = ZipFile.OpenRead(package);
        ZipArchiveEntry nuspec = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
        XDocument document;
        using (Stream stream = nuspec.Open())
        {
            document = XDocument.Load(stream, LoadOptions.None);
        }

        XElement license = document.Descendants()
            .Single(element => string.Equals(element.Name.LocalName, "license", StringComparison.Ordinal));
        Assert(
            string.Equals((string?)license.Attribute("type"), "expression", StringComparison.Ordinal) &&
            string.Equals(license.Value, "MIT", StringComparison.Ordinal),
            $"{Path.GetFileName(package)} declares the MIT SPDX license expression");
    }

    private static void AssertRepositoryMetadata(string package)
    {
        using ZipArchive archive = ZipFile.OpenRead(package);
        ZipArchiveEntry nuspec = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
        XDocument document;
        using (Stream stream = nuspec.Open())
        {
            document = XDocument.Load(stream, LoadOptions.None);
        }

        XElement repository = document.Descendants()
            .Single(element => string.Equals(element.Name.LocalName, "repository", StringComparison.Ordinal));
        string commit = (string?)repository.Attribute("commit") ?? string.Empty;
        Assert(
            string.Equals((string?)repository.Attribute("type"), "git", StringComparison.Ordinal) &&
            string.Equals((string?)repository.Attribute("url"), RepositoryUrl, StringComparison.Ordinal) &&
            commit.Length == 40 && commit.All(Uri.IsHexDigit),
            $"{Path.GetFileName(package)} identifies its Git repository and source commit");
    }

    private static void AssertEmbeddedSourceLink(string package, string assemblyPath)
    {
        using ZipArchive archive = ZipFile.OpenRead(package);
        ZipArchiveEntry assembly = archive.GetEntry(assemblyPath)
            ?? throw new InvalidOperationException($"FAIL: {Path.GetFileName(package)} contains {assemblyPath}");
        using var assemblyBytes = new MemoryStream();
        using (Stream stream = assembly.Open())
        {
            stream.CopyTo(assemblyBytes);
        }

        assemblyBytes.Position = 0;
        using var peReader = new PEReader(assemblyBytes);
        DebugDirectoryEntry embeddedPdb = peReader.ReadDebugDirectory()
            .Single(entry => entry.Type == DebugDirectoryEntryType.EmbeddedPortablePdb);
        using MetadataReaderProvider provider = peReader.ReadEmbeddedPortablePdbDebugDirectoryData(embeddedPdb);
        MetadataReader reader = provider.GetMetadataReader();
        CustomDebugInformation sourceLink = reader.GetCustomDebugInformation(MetadataTokens.EntityHandle(0x00000001))
            .Select(reader.GetCustomDebugInformation)
            .Single(information => reader.GetGuid(information.Kind) == SourceLinkKind);
        string documentMap = Encoding.UTF8.GetString(reader.GetBlobBytes(sourceLink.Value));
        Assert(
            documentMap.Contains("raw.githubusercontent.com/Runic-Artifex/runic-translations/", StringComparison.Ordinal),
            $"{Path.GetFileName(package)} embeds Source Link metadata for the organization repository");
    }

    private static void Assert(bool condition, string description)
    {
        if (!condition)
        {
            throw new InvalidOperationException("FAIL: " + description);
        }

        _passed++;
    }

    private sealed class MemoryPackSource(string catalog, string locale, byte[] bytes) : IExternalTranslationSource
    {
        public ValueTask<ExternalTranslationPack?> LoadAsync(
            string requestedCatalog,
            string requestedLocale,
            System.Threading.CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExternalTranslationPack? pack =
                string.Equals(requestedCatalog, catalog, StringComparison.Ordinal) &&
                string.Equals(requestedLocale, locale, StringComparison.Ordinal)
                    ? new ExternalTranslationPack(bytes)
                    : null;
            return ValueTask.FromResult(pack);
        }
    }
}
