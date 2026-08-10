using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using RunicTranslations;
using RunicTranslations.PackageConsumer;

namespace RunicTranslations.PackageTests;

internal static class Program
{
    private const string Fingerprint = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string RepositoryUrl = "https://github.com/Runic-Artifex/runic-translations";
    private static readonly Guid SourceLinkKind = new("CC110556-A091-4D38-9FEC-25AB9A351A6A");
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
            throw new ArgumentException("Usage: RunicTranslations.PackageTests [--feed <directory>]");
        }

        string feed = Path.GetFullPath(args[1]);
        Assert(Directory.Exists(feed), "the isolated package feed exists");
        return feed;
    }

    private static void InspectPackages(string feed)
    {
        string runtime = RequireSingle(feed, "RunicTranslations.1.0.0.nupkg");
        string compiler = RequireSingle(feed, "RunicTranslations.Compiler.1.0.0.nupkg");
        string authoring = RequireSingle(feed, "RunicTranslations.Authoring.1.0.0.nupkg");
        string build = RequireSingle(feed, "RunicTranslations.Build.1.0.0.nupkg");
        string generator = RequireSingle(feed, "RunicTranslations.Generator.1.0.0.nupkg");
        string tool = RequireSingle(feed, "RunicTranslations.Tool.1.0.0.nupkg");
        string templates = RequireSingle(feed, "RunicTranslations.Templates.1.0.0.nupkg");

        AssertPackageShape(runtime,
            "RunicTranslations.nuspec",
            "README.md",
            "lib/net10.0/RunicTranslations.dll",
            "lib/net10.0/RunicTranslations.xml");
        AssertPackageShape(compiler,
            "RunicTranslations.Compiler.nuspec",
            "README.md",
            "lib/net10.0/RunicTranslations.Compiler.dll");
        AssertPackageShape(authoring,
            "RunicTranslations.Authoring.nuspec",
            "README.md",
            "lib/net10.0/RunicTranslations.Authoring.dll");
        AssertPackageShape(build,
            "RunicTranslations.Build.nuspec",
            "README.md",
            "build/RunicTranslations.Build.props",
            "build/RunicTranslations.Build.targets");
        AssertPackageShape(generator,
            "RunicTranslations.Generator.nuspec",
            "README.md",
            "analyzers/dotnet/cs/RunicTranslations.Generator.dll",
            "analyzers/dotnet/cs/RunicTranslations.Compiler.dll");
        AssertPackageShape(tool,
            "RunicTranslations.Tool.nuspec",
            "README.md",
            "tools/net10.0/any/DotnetToolSettings.xml",
            "tools/net10.0/any/RunicTranslations.Tool.deps.json",
            "tools/net10.0/any/RunicTranslations.Tool.dll",
            "tools/net10.0/any/RunicTranslations.Tool.runtimeconfig.json",
            "tools/net10.0/any/RunicTranslations.Authoring.dll",
            "tools/net10.0/any/RunicTranslations.Compiler.dll");
        AssertPackageShape(templates,
            "RunicTranslations.Templates.nuspec",
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
        AssertDependencies(compiler, Array.Empty<string>());
        AssertDependencies(authoring, ["RunicTranslations.Compiler"]);
        AssertDependencies(build, Array.Empty<string>());
        AssertDependencies(generator, ["RunicTranslations"]);
        AssertDependencies(tool, Array.Empty<string>());
        AssertDependencies(templates, Array.Empty<string>());

        AssertLicense(runtime);
        AssertLicense(compiler);
        AssertLicense(authoring);
        AssertLicense(build);
        AssertLicense(generator);
        AssertLicense(tool);
        AssertLicense(templates);

        AssertRepositoryMetadata(runtime);
        AssertRepositoryMetadata(compiler);
        AssertRepositoryMetadata(authoring);
        AssertRepositoryMetadata(build);
        AssertRepositoryMetadata(generator);
        AssertRepositoryMetadata(tool);
        AssertRepositoryMetadata(templates);

        AssertEmbeddedSourceLink(runtime, "lib/net10.0/RunicTranslations.dll");
        AssertEmbeddedSourceLink(compiler, "lib/net10.0/RunicTranslations.Compiler.dll");
        AssertEmbeddedSourceLink(authoring, "lib/net10.0/RunicTranslations.Authoring.dll");
        AssertEmbeddedSourceLink(generator, "analyzers/dotnet/cs/RunicTranslations.Generator.dll");
        AssertEmbeddedSourceLink(tool, "tools/net10.0/any/RunicTranslations.Tool.dll");
    }

    private static async Task ExerciseRuntimePackageAsync()
    {
        ITextResourceManager generatedManager = await ConsumerTextCatalog.CreateManagerAsync().ConfigureAwait(false);
        var generatedText = new ConsumerText(generatedManager);
        Assert(string.Equals(generatedText.Greeting("Ada"), "Hello Ada", StringComparison.Ordinal),
            "typed generated accessor compiles and formats through the packed runtime");

        byte[] generatedPack = Encoding.UTF8.GetBytes(
            "{\"artifactVersion\":1,\"messageGrammarVersion\":1,\"catalog\":\"packageconsumer\",\"locale\":\"en\"," +
            "\"contractFingerprint\":\"" + ConsumerTextCatalog.ContractFingerprint + "\",\"messages\":{\"Greeting\":{" +
            "\"pattern\":\"External {name}\",\"arguments\":[{\"name\":\"name\",\"type\":\"string\",\"format\":\"none\"}]}}}");
        ITextResourceManager generatedExternalManager = await ConsumerTextCatalog.CreateExternalManagerAsync(
            new MemoryPackSource("packageconsumer", "en", generatedPack),
            integrityVerifier: static (content, _) => ValueTask.FromResult(content.Length > 0)).ConfigureAwait(false);
        var generatedExternalText = new ConsumerText(generatedExternalManager);
        Assert(string.Equals(generatedExternalText.Greeting("Ada"), "External Ada", StringComparison.Ordinal),
            "generated external manager composes a verified pack through the shipped snapshot factory");

        CompiledTextResourceCatalog catalog = CreateCatalog();
        var provider = new CompiledTextResourceProvider(catalog);
        ITextResourceSnapshot initial = await provider.GetSnapshotAsync("en").ConfigureAwait(false);
        var manager = new TextResourceManager(provider, initial);

        var greeting = new TextResourceKey("app", 0, "greeting");
        var title = new TextResourceKey("app", 1, "title");
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

        TextResourcePackContract contract = CreatePackContract();
        byte[] packBytes = Encoding.UTF8.GetBytes(
            "{\"artifactVersion\":1,\"messageGrammarVersion\":1,\"catalog\":\"app\",\"locale\":\"de\"," +
            "\"contractFingerprint\":\"" + Fingerprint + "\",\"messages\":{\"greeting\":{" +
            "\"pattern\":\"Extern {name}\",\"arguments\":[{\"name\":\"name\",\"type\":\"string\",\"format\":\"none\"}]}}}");
        VerifiedExternalTextResourcePack verified = await TextResourcePackLoader.VerifyAsync(
            new ExternalTextResourcePack(packBytes),
            contract,
            integrityVerifier: static (content, _) => ValueTask.FromResult(content.Length > 0)).ConfigureAwait(false);
        Assert(verified.TryGetPattern(greeting, out string externalPattern), "verified external pack contains the generated key");
        Assert(string.Equals(externalPattern, "Extern {name}", StringComparison.Ordinal), "external pack preserves the verified pattern");
    }

    private static CompiledTextResourceCatalog CreateCatalog() => new(
        "app",
        "en",
        [
            new CompiledTextResourceDefinition("greeting",
                [new TextResourcePlaceholderDescriptor("name", TextArgumentType.String, TextArgumentFormat.None)]),
            new CompiledTextResourceDefinition("title", Array.Empty<TextResourcePlaceholderDescriptor>()),
        ],
        [
            new CompiledTextResourceLocale("de", "en", [new CompiledTextResourceValue(0, "Hallo {name}")]),
            new CompiledTextResourceLocale("en", null,
                [new CompiledTextResourceValue(0, "Hello {name}"), new CompiledTextResourceValue(1, "Application")]),
        ]);

    private static TextResourcePackContract CreatePackContract() => new(
        "app",
        "de",
        Fingerprint,
        [
            new TextResourcePackMessageContract(
                new TextResourceKey("app", 0, "greeting"),
                [new TextResourcePackArgumentContract("name", TextArgumentType.String, TextArgumentFormat.None)]),
        ]);

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

    private sealed class MemoryPackSource(string catalog, string locale, byte[] bytes) : IExternalTextResourceSource
    {
        public ValueTask<ExternalTextResourcePack?> LoadAsync(
            string requestedCatalog,
            string requestedLocale,
            System.Threading.CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExternalTextResourcePack? pack =
                string.Equals(requestedCatalog, catalog, StringComparison.Ordinal) &&
                string.Equals(requestedLocale, locale, StringComparison.Ordinal)
                    ? new ExternalTextResourcePack(bytes)
                    : null;
            return ValueTask.FromResult(pack);
        }
    }
}
