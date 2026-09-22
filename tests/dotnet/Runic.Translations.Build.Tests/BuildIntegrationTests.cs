using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Runic.Translations.Build.Tests;

internal static class BuildIntegrationTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("build props and targets expose stable import sentinels", ImportsExposeSentinels);
        runner.Add("build discovers direct MF2 sources and generates semantic artifacts", DirectMf2GenerationIsIncremental);
        runner.Add("build discovers mounted grouped RMF2 sources and membership changes", MountedRmf2MembershipIsIncremental);
        runner.Add("build emit properties select v5 groups and reject retired outputs", EmitFlagsAreExact);
        runner.Add("build rejects an output path outside the intermediate root", OutputContainmentIsEnforced);
    }

    private static void ImportsExposeSentinels()
    {
        using TemporaryDirectory temporary = CreateConsumer(false);
        ProcessResult result = Processes.DotNet(temporary.Path, "msbuild", "Consumer.csproj", "/nologo", "/t:DumpTranslationItems", "/v:minimal");
        Assert.Equal(0, result.ExitCode, result.Combined);
        string dump = File.ReadAllText(temporary.Resolve("dump.txt"), Encoding.UTF8).Replace('\\', '/');
        Assert.Contains("PropsImported=true", dump); Assert.Contains("TargetsImported=true", dump); Assert.Contains("Hello|Mf2", dump);
    }

    private static void DirectMf2GenerationIsIncremental()
    {
        using TemporaryDirectory temporary = CreateConsumer(true);
        byte[] projectBefore = File.ReadAllBytes(temporary.Resolve("translations", "runic.json"));
        byte[] sourceBefore = File.ReadAllBytes(temporary.Resolve("translations", "en", "Hello.mf2"));
        ProcessResult first = Build(temporary);
        Assert.Equal(0, first.ExitCode, first.Combined);
        string output = FindGeneratedDirectory(temporary, "minimal.en.locale-v5.json");
        Assert.True(File.Exists(Path.Combine(output, "minimal.asset-manifest-v1.json")), "Default build omitted semantic asset manifest.");
        Assert.True(File.Exists(Path.Combine(output, "minimal.esm-v5", "web-module-manifest-v3.json")), "Default build omitted semantic ESM output.");
        Assert.False(File.Exists(Path.Combine(output, "minimal.translations-v1.d.ts")), "Default build emitted retired TypeScript output.");
        Assert.True(projectBefore.AsSpan().SequenceEqual(File.ReadAllBytes(temporary.Resolve("translations", "runic.json"))), "Build changed the project source.");
        Assert.True(sourceBefore.AsSpan().SequenceEqual(File.ReadAllBytes(temporary.Resolve("translations", "en", "Hello.mf2"))), "Build changed the direct MF2 source.");
        string stamp = Path.Combine(output, ".generate.stamp"); DateTime firstWrite = File.GetLastWriteTimeUtc(stamp);
        ProcessResult unchanged = Build(temporary, true); Assert.Equal(0, unchanged.ExitCode, unchanged.Combined); Assert.Equal(firstWrite, File.GetLastWriteTimeUtc(stamp), "Unchanged semantic build regenerated.");
        File.WriteAllText(temporary.Resolve("translations", "en", "Hello.mf2"), "Hello again {$name}\n", new UTF8Encoding(false));
        ProcessResult changed = Build(temporary, true); Assert.Equal(0, changed.ExitCode, changed.Combined);
        Assert.Contains("Hello again", File.ReadAllText(Path.Combine(output, "minimal.en.locale-v5.json")));
    }

    private static void MountedRmf2MembershipIsIncremental()
    {
        using TemporaryDirectory temporary = CreateConsumer(true);
        Directory.Delete(temporary.Resolve("translations", "en"), true); Directory.CreateDirectory(temporary.Resolve("feature"));
        File.WriteAllText(temporary.Resolve("translations", "runic.json"), "{\"schemaVersion\":1,\"catalog\":\"minimal\",\"code\":{\"namespace\":\"Example\",\"className\":\"MinimalText\"},\"baseLocale\":\"en\",\"sourceRoots\":[{\"path\":\"../feature\",\"namespace\":[\"shop\"]}]}\n", new UTF8Encoding(false));
        File.WriteAllText(temporary.Resolve("feature", "en.rmf2"), "greeting = Hello\n", new UTF8Encoding(false));
        ProcessResult first = Build(temporary); Assert.Equal(0, first.ExitCode, first.Combined);
        string output = FindGeneratedDirectory(temporary, "minimal.en.locale-v5.json"); string german = temporary.Resolve("feature", "de.rmf2");
        File.WriteAllText(german, "greeting = Hallo\n", new UTF8Encoding(false));
        ProcessResult added = Build(temporary, true); Assert.Equal(0, added.ExitCode, added.Combined);
        Assert.True(File.Exists(Path.Combine(output, "minimal.de.locale-v5.json")), "Mounted RMF2 addition was not discovered.");
        File.Delete(german); ProcessResult removed = Build(temporary, true); Assert.Equal(0, removed.ExitCode, removed.Combined);
        Assert.False(File.Exists(Path.Combine(output, "minimal.de.locale-v5.json")), "Deleted mounted RMF2 artifact survived.");
    }

    private static void EmitFlagsAreExact()
    {
        foreach ((string property, string expected) in new[] { ("<TranslationsEmitJson>true</TranslationsEmitJson>", "minimal.en.locale-v5.json"), ("<TranslationsEmitEsm>true</TranslationsEmitEsm>", "web-module-manifest-v3.json") })
        {
            using TemporaryDirectory temporary = CreateConsumer(false, extraProperties: property); ProcessResult result = Build(temporary); Assert.Equal(0, result.ExitCode, result.Combined);
            string output = FindGeneratedDirectory(temporary, expected); string[] files = GeneratedArtifacts(output);
            if (expected == "web-module-manifest-v3.json") Assert.True(files.Length > 0 && files.All(static path => path.StartsWith("minimal.esm-v5/", StringComparison.Ordinal)), "ESM selection emitted another output group.");
            else Assert.Equal("minimal.asset-manifest-v1.json|minimal.en.locale-v5.json", string.Join('|', files));
        }
        foreach ((string property, string flag) in new[] { ("<TranslationsEmitTypeScript>true</TranslationsEmitTypeScript>", "--emit-typescript"), ("<TranslationsEmitTemplateManifest>true</TranslationsEmitTemplateManifest>", "--emit-template-manifest"), ("<TranslationsEmitCpp>true</TranslationsEmitCpp>", "--emit-cpp") })
        {
            using TemporaryDirectory temporary = CreateConsumer(false, extraProperties: property); ProcessResult result = Build(temporary);
            Assert.True(result.ExitCode != 0, "Build accepted retired output selection " + flag); Assert.Contains("RTR0065", result.Combined); Assert.Contains(flag, result.Combined);
        }
    }

    private static void OutputContainmentIsEnforced()
    {
        using TemporaryDirectory temporary = CreateConsumer(true, "escaped-output/"); ProcessResult result = Build(temporary);
        Assert.True(result.ExitCode != 0, "Build unexpectedly accepted an output path outside IntermediateOutputPath."); Assert.Contains("RTR0020", result.Combined); Assert.False(Directory.Exists(temporary.Resolve("escaped-output")), "Rejected output path was created.");
    }

    private static TemporaryDirectory CreateConsumer(bool generationEnabled, string? outputPath = null, string? extraProperties = null)
    {
        TemporaryDirectory temporary = new(); Directory.CreateDirectory(temporary.Resolve("translations", "en"));
        File.WriteAllText(temporary.Resolve("translations", "runic.json"), "{\"schemaVersion\":1,\"catalog\":\"minimal\",\"code\":{\"namespace\":\"Example\",\"className\":\"MinimalText\"},\"baseLocale\":\"en\"}\n", new UTF8Encoding(false));
        File.WriteAllText(temporary.Resolve("translations", "en", "Hello.mf2"), "Hello {$name}\n", new UTF8Encoding(false)); File.WriteAllText(temporary.Resolve("Program.cs"), "internal static class Program { private static void Main() { } }\n", new UTF8Encoding(false));
        string props = XmlPath(RepositoryPaths.Resolve("packages", "dotnet", "Runic.Translations.Build", "build", "Runic.Translations.Build.props")); string targets = XmlPath(RepositoryPaths.Resolve("packages", "dotnet", "Runic.Translations.Build", "build", "Runic.Translations.Build.targets")); string tool = $"dotnet &quot;{XmlPath(RepositoryPaths.ToolAssembly)}&quot;"; string output = outputPath is null ? string.Empty : $"<TranslationsOutputPath>{outputPath}</TranslationsOutputPath>";
        string project = $$"""<Project Sdk="Microsoft.NET.Sdk"><Import Project="{{props}}" /><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><IntermediateOutputPath>artifacts/obj/$(Configuration)/</IntermediateOutputPath><BaseOutputPath>artifacts/bin/</BaseOutputPath><RestorePackagesPath>{{XmlPath(RepositoryPaths.Resolve(".packages"))}}</RestorePackagesPath><TranslationsGenerateOnBuild>{{generationEnabled.ToString().ToLowerInvariant()}}</TranslationsGenerateOnBuild><TranslationsToolCommand>{{tool}}</TranslationsToolCommand>{{output}}{{extraProperties}}</PropertyGroup><Import Project="{{targets}}" /><Target Name="DumpTranslationItems" DependsOnTargets="_RunicTranslationsDiscoverTranslationSources"><WriteLinesToFile File="dump.txt" Overwrite="true" Lines="PropsImported=$(RunicTranslationsBuildPropsImported);TargetsImported=$(RunicTranslationsBuildTargetsImported);@(AdditionalFiles->'%(Filename)|%(RunicTranslationKind)')" /></Target></Project>""";
        File.WriteAllText(temporary.Resolve("Consumer.csproj"), project, new UTF8Encoding(false)); return temporary;
    }

    private static ProcessResult Build(TemporaryDirectory temporary, bool noRestore = false) => Processes.DotNet(temporary.Path, noRestore ? ["build", "Consumer.csproj", "--configuration", BuildConfiguration, "--no-restore", "/nologo", "/v:minimal"] : ["build", "Consumer.csproj", "--configuration", BuildConfiguration, "/nologo", "/v:minimal"]);
    private static string FindGeneratedDirectory(TemporaryDirectory temporary, string artifactName) => Path.GetDirectoryName(Directory.EnumerateFiles(temporary.Resolve("artifacts", "obj"), artifactName, SearchOption.AllDirectories).Single())!;
    private static string[] GeneratedArtifacts(string output) => TestFixture.RelativeFiles(output).Where(path => !Path.GetFileName(path).StartsWith(".generate.", StringComparison.Ordinal)).ToArray();
    private static string XmlPath(string path) => path.Replace("&", "&amp;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal);
    private static string BuildConfiguration => new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Debug";
}
