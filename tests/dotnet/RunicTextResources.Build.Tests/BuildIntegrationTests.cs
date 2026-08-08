using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;

namespace RunicTextResources.Build.Tests;

internal static class BuildIntegrationTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("build props and targets expose stable import sentinels", ImportsExposeSentinels);
        runner.Add("build maps declared items to AdditionalFiles metadata", ItemsMapToAdditionalFiles);
        runner.Add("build target declares incremental Inputs and Outputs", TargetDeclaresInputsAndOutputs);
        runner.Add("build generates only below the isolated intermediate root", GenerationIsIsolated);
        runner.Add("build generation is incremental and input-sensitive", GenerationIsIncremental);
        runner.Add("build regenerates an artifact missing despite a current stamp", MissingArtifactInvalidatesStamp);
        runner.Add("build emit flags select exact non-CSharp artifact groups", EmitFlagsSelectOutputs);
        runner.Add("build fails fast when the configured tool is missing", MissingToolFailsFast);
        runner.Add("build rejects an output path outside the intermediate root", OutputContainmentIsEnforced);
        runner.Add("build rejects a reparse-point output root", ReparsePointOutputIsRejected);
        runner.Add("build reconciles All to JSON and clean preserves unrelated files", ReconcileAndCleanRespectOwnership);
    }

    private static void ImportsExposeSentinels()
    {
        using TemporaryDirectory temporary = CreateConsumer(generationEnabled: false);
        ProcessResult result = Processes.DotNet(temporary.Path, "msbuild", "Consumer.csproj", "/nologo", "/t:DumpTextResourceItems", "/v:minimal");
        Assert.Equal(0, result.ExitCode, result.Combined);
        string[] lines = File.ReadAllLines(temporary.Resolve("dump.txt"));
        Assert.True(lines.Contains("PropsImported=true", StringComparer.Ordinal), "Props import sentinel was not true.");
        Assert.True(lines.Contains("TargetsImported=true", StringComparer.Ordinal), "Targets import sentinel was not true.");
    }

    private static void ItemsMapToAdditionalFiles()
    {
        using TemporaryDirectory temporary = CreateConsumer(generationEnabled: false);
        ProcessResult result = Processes.DotNet(temporary.Path, "msbuild", "Consumer.csproj", "/nologo", "/t:DumpTextResourceItems", "/v:minimal");
        Assert.Equal(0, result.ExitCode, result.Combined);
        string dump = File.ReadAllText(temporary.Resolve("dump.txt"), Encoding.UTF8).Replace('\\', '/');
        Assert.Contains("manifest|Catalog", dump);
        Assert.Contains("en|Document", dump);
    }

    private static void TargetDeclaresInputsAndOutputs()
    {
        string targetsPath = RepositoryPaths.Resolve(
            "dotnet",
            "src",
            "RunicTextResources.Build",
            "build",
            "RunicTextResources.Build.targets");
        XDocument document = XDocument.Load(targetsPath, LoadOptions.PreserveWhitespace);
        XElement target = document
            .Descendants("Target")
            .Single(element => string.Equals((string?)element.Attribute("Name"), "_RunicTextResourcesGenerateTextResourceArtifactsCore", StringComparison.Ordinal));
        string inputs = (string?)target.Attribute("Inputs") ?? string.Empty;
        string outputs = (string?)target.Attribute("Outputs") ?? string.Empty;
        Assert.Contains("@(TextResourceCatalog)", inputs);
        Assert.Contains("@(TextResourceDocument)", inputs);
        Assert.Contains("$(MSBuildProjectFullPath)", inputs);
        Assert.Equal("$(TextResourcesOutputStamp)", outputs);
    }

    private static void GenerationIsIsolated()
    {
        using TemporaryDirectory temporary = CreateConsumer(generationEnabled: true);
        byte[] catalogBefore = File.ReadAllBytes(temporary.Resolve("Resources", "manifest.json"));
        byte[] documentBefore = File.ReadAllBytes(temporary.Resolve("Resources", "en.json"));

        ProcessResult result = Build(temporary);

        Assert.Equal(0, result.ExitCode, result.Combined);
        string output = FindGeneratedDirectory(temporary);
        string intermediate = Path.GetFullPath(temporary.Resolve("artifacts", "obj")) + Path.DirectorySeparatorChar;
        Assert.True(Path.GetFullPath(output).StartsWith(intermediate, PathComparison), $"Generated output escaped the isolated intermediate root: {output}");
        Assert.Equal(
            "minimal.asset-manifest-v1.json|minimal.en.locale-v1.json|minimal.esm/dynamic.d.ts|minimal.esm/dynamic.js|minimal.esm/messages.d.ts|minimal.esm/messages.js|minimal.esm/messages/m$Hello.js|minimal.esm/runtime.d.ts|minimal.esm/runtime.js|minimal.esm/transport.d.ts|minimal.esm/transport.js|minimal.esm/web-module-manifest-v1.json|minimal.template-manifest-v1.json|minimal.text-resources-v1.d.ts",
            string.Join('|', GeneratedArtifacts(output)));
        Assert.True(catalogBefore.AsSpan().SequenceEqual(File.ReadAllBytes(temporary.Resolve("Resources", "manifest.json"))), "Build changed the catalog source.");
        Assert.True(documentBefore.AsSpan().SequenceEqual(File.ReadAllBytes(temporary.Resolve("Resources", "en.json"))), "Build changed the document source.");
        Assert.False(Directory.EnumerateFiles(temporary.Path, "*.g.cs", SearchOption.TopDirectoryOnly).Any(), "Build wrote generated C# beside project sources.");
        Assert.False(Directory.Exists(temporary.Resolve("Resources", "generated")), "Build wrote under the source resource directory.");
    }

    private static void GenerationIsIncremental()
    {
        using TemporaryDirectory temporary = CreateConsumer(generationEnabled: true);
        ProcessResult first = Build(temporary);
        Assert.Equal(0, first.ExitCode, first.Combined);
        string stamp = Directory.EnumerateFiles(temporary.Resolve("artifacts", "obj"), ".generate.stamp", SearchOption.AllDirectories).Single();
        DateTime firstWrite = File.GetLastWriteTimeUtc(stamp);

        Thread.Sleep(1_200);
        ProcessResult second = Build(temporary, noRestore: true);
        Assert.Equal(0, second.ExitCode, second.Combined);
        Assert.Equal(firstWrite, File.GetLastWriteTimeUtc(stamp), "An unchanged build reran generation");

        Thread.Sleep(1_200);
        string document = temporary.Resolve("Resources", "en.json");
        File.SetLastWriteTimeUtc(document, DateTime.UtcNow);
        ProcessResult third = Build(temporary, noRestore: true);
        Assert.Equal(0, third.ExitCode, third.Combined);
        Assert.True(File.GetLastWriteTimeUtc(stamp) > firstWrite, "A changed input did not rerun generation.");
    }

    private static void OutputContainmentIsEnforced()
    {
        using TemporaryDirectory temporary = CreateConsumer(generationEnabled: true, outputPath: "escaped-output/");
        ProcessResult result = Build(temporary);
        Assert.True(result.ExitCode != 0, "Build unexpectedly accepted an output path outside IntermediateOutputPath.");
        Assert.Contains("RTR0020", result.Combined);
        Assert.Contains("must resolve beneath IntermediateOutputPath", result.Combined);
        Assert.False(Directory.Exists(temporary.Resolve("escaped-output")), "Rejected output path was created.");
    }

    private static void MissingArtifactInvalidatesStamp()
    {
        using TemporaryDirectory temporary = CreateConsumer(generationEnabled: true);
        ProcessResult first = Build(temporary);
        Assert.Equal(0, first.ExitCode, first.Combined);
        string output = FindGeneratedDirectory(temporary);
        string missing = Path.Combine(output, "minimal.text-resources-v1.d.ts");
        File.Delete(missing);

        ProcessResult second = Build(temporary, noRestore: true);
        Assert.Equal(0, second.ExitCode, second.Combined);
        Assert.True(File.Exists(missing), "Build trusted its stamp after a declared artifact was deleted.");
    }

    private static void EmitFlagsSelectOutputs()
    {
        using TemporaryDirectory temporary = CreateConsumer(
            generationEnabled: false,
            extraProperties: "<TextResourcesEmitTypeScript>true</TextResourcesEmitTypeScript>");
        ProcessResult result = Build(temporary);
        Assert.Equal(0, result.ExitCode, result.Combined);
        string output = FindGeneratedDirectory(temporary, "minimal.text-resources-v1.d.ts");
        Assert.Equal("minimal.asset-manifest-v1.json|minimal.text-resources-v1.d.ts", string.Join('|', GeneratedArtifacts(output)));
    }

    private static void MissingToolFailsFast()
    {
        using TemporaryDirectory temporary = CreateConsumer(
            generationEnabled: true,
            toolCommand: "definitely-missing-textresources-tool");
        ProcessResult result = Build(temporary);
        Assert.True(result.ExitCode != 0, "Build unexpectedly succeeded without its configured tool.");
        Assert.False(
            Directory.EnumerateFiles(temporary.Resolve("artifacts", "obj"), ".generate.stamp", SearchOption.AllDirectories).Any(),
            "Build stamped a failed tool invocation as successful.");
    }

    private static void ReparsePointOutputIsRejected()
    {
        using TemporaryDirectory temporary = CreateConsumer(
            generationEnabled: true,
            outputPath: "$(IntermediateOutputPath)$(TargetFramework)/linked-output/");
        string target = temporary.Resolve("link-target");
        string link = temporary.Resolve("artifacts", "obj", "Debug", "net10.0", "linked-output");
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return;
        }

        ProcessResult result = Build(temporary);
        Assert.True(result.ExitCode != 0, "Build accepted a reparse-point output root.");
        Assert.Contains("RTR0020", result.Combined);
        Assert.Contains("reparse point", result.Combined);
        Assert.False(Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories).Any(), "Build wrote through the rejected output link.");
    }

    private static void ReconcileAndCleanRespectOwnership()
    {
        using TemporaryDirectory temporary = CreateConsumer(generationEnabled: true);
        ProcessResult first = Build(temporary);
        Assert.Equal(0, first.ExitCode, first.Combined);
        string output = FindGeneratedDirectory(temporary);
        Assert.True(File.Exists(Path.Combine(output, "minimal.en.locale-v1.json")), "All emission omitted JSON.");
        Assert.True(File.Exists(Path.Combine(output, "minimal.asset-manifest-v1.json")), "All emission omitted the asset manifest.");
        Assert.True(File.Exists(Path.Combine(output, "minimal.template-manifest-v1.json")), "All emission omitted template manifest.");
        Assert.True(File.Exists(Path.Combine(output, "minimal.text-resources-v1.d.ts")), "All emission omitted TypeScript.");
        string sentinel = Path.Combine(output, "consumer-sentinel.txt");
        File.WriteAllText(sentinel, "consumer owned", new UTF8Encoding(false));

        string projectPath = temporary.Resolve("Consumer.csproj");
        string project = File.ReadAllText(projectPath, Encoding.UTF8).Replace(
            "<TextResourcesGenerateOnBuild>true</TextResourcesGenerateOnBuild>",
            "<TextResourcesGenerateOnBuild>false</TextResourcesGenerateOnBuild><TextResourcesEmitJson>true</TextResourcesEmitJson>",
            StringComparison.Ordinal);
        File.WriteAllText(projectPath, project, new UTF8Encoding(false));
        ProcessResult second = Build(temporary, noRestore: true);
        Assert.Equal(0, second.ExitCode, second.Combined);
        Assert.True(File.Exists(Path.Combine(output, "minimal.en.locale-v1.json")), "JSON-only reconciliation removed JSON.");
        Assert.True(File.Exists(Path.Combine(output, "minimal.asset-manifest-v1.json")), "JSON-only reconciliation removed the asset manifest.");
        Assert.False(File.Exists(Path.Combine(output, "minimal.template-manifest-v1.json")), "JSON-only reconciliation retained the prior template manifest.");
        Assert.False(File.Exists(Path.Combine(output, "minimal.text-resources-v1.d.ts")), "JSON-only reconciliation retained the prior TypeScript contract.");
        Assert.True(File.Exists(sentinel), "Reconciliation deleted an uninventoried consumer file.");

        string exposure = File.ReadAllText(temporary.Resolve("artifacts", "generated-items.txt"), Encoding.UTF8).Trim();
        Assert.Equal("minimal.asset-manifest-v1.json|minimal.en.locale-v1.json", exposure, "Generated item exposure included private state or an unrelated file");

        ProcessResult clean = Clean(temporary);
        Assert.Equal(0, clean.ExitCode, clean.Combined);
        Assert.False(File.Exists(Path.Combine(output, "minimal.en.locale-v1.json")), "Clean retained an inventoried generated artifact.");
        Assert.False(File.Exists(Path.Combine(output, "minimal.asset-manifest-v1.json")), "Clean retained the inventoried asset manifest.");
        Assert.True(File.Exists(sentinel), "Clean deleted an uninventoried consumer file.");
    }

    private static TemporaryDirectory CreateConsumer(
        bool generationEnabled,
        string? outputPath = null,
        string? extraProperties = null,
        string? toolCommand = null)
    {
        TemporaryDirectory temporary = new();
        Directory.CreateDirectory(temporary.Resolve("Resources"));
        string fixture = RepositoryPaths.Resolve("spec", "corpus", "valid", "minimal");
        File.Copy(Path.Combine(fixture, "manifest.json"), temporary.Resolve("Resources", "manifest.json"));
        File.Copy(Path.Combine(fixture, "en.json"), temporary.Resolve("Resources", "en.json"));
        File.WriteAllText(temporary.Resolve("Program.cs"), "internal static class Program { private static void Main() { } }\n", new UTF8Encoding(false));

        string props = XmlPath(RepositoryPaths.Resolve("dotnet", "src", "RunicTextResources.Build", "build", "RunicTextResources.Build.props"));
        string targets = XmlPath(RepositoryPaths.Resolve("dotnet", "src", "RunicTextResources.Build", "build", "RunicTextResources.Build.targets"));
        string tool = toolCommand is null
            ? $"dotnet &quot;{XmlPath(RepositoryPaths.ToolAssembly)}&quot;"
            : XmlPath(toolCommand);
        string output = outputPath is null ? string.Empty : $"<TextResourcesOutputPath>{outputPath}</TextResourcesOutputPath>";
        string enabled = generationEnabled ? "true" : "false";
        string project = $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <Import Project="{{props}}" />
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <IntermediateOutputPath>artifacts/obj/$(Configuration)/</IntermediateOutputPath>
                <BaseOutputPath>artifacts/bin/</BaseOutputPath>
                <RestorePackagesPath>{{XmlPath(RepositoryPaths.Resolve(".packages"))}}</RestorePackagesPath>
                <TextResourcesGenerateOnBuild>{{enabled}}</TextResourcesGenerateOnBuild>
                <TextResourcesToolCommand>{{tool}}</TextResourcesToolCommand>
                {{output}}
                {{extraProperties}}
              </PropertyGroup>
              <ItemGroup>
                <TextResourceCatalog Include="Resources/manifest.json" />
                <TextResourceDocument Include="Resources/en.json" />
              </ItemGroup>
              <Import Project="{{targets}}" />
              <Target Name="DumpTextResourceItems">
                <WriteLinesToFile File="dump.txt"
                                  Overwrite="true"
                                  Lines="PropsImported=$(RunicTextResourcesBuildPropsImported);TargetsImported=$(RunicTextResourcesBuildTargetsImported);@(AdditionalFiles->'%(Filename)|%(RunicTextResourceKind)')" />
              </Target>
              <Target Name="CaptureTextResourceGeneratedFiles" AfterTargets="RunicTextResourcesCollectTextResourceArtifacts">
                <WriteLinesToFile File="artifacts/generated-items.txt"
                                  Overwrite="true"
                                  Lines="@(TextResourcesGeneratedFile->'%(Filename)%(Extension)', '|')" />
              </Target>
            </Project>
            """;
        File.WriteAllText(temporary.Resolve("Consumer.csproj"), project, new UTF8Encoding(false));
        return temporary;
    }

    private static ProcessResult Build(TemporaryDirectory temporary, bool noRestore = false)
    {
        string[] arguments = noRestore
            ? ["build", "Consumer.csproj", "--no-restore", "/nologo", "/v:minimal"]
            : ["build", "Consumer.csproj", "/nologo", "/v:minimal"];
        return Processes.DotNet(temporary.Path, arguments);
    }

    private static ProcessResult Clean(TemporaryDirectory temporary) => Processes.DotNet(
        temporary.Path,
        "clean",
        "Consumer.csproj",
        "/nologo",
        "/v:minimal");

    private static string FindGeneratedDirectory(TemporaryDirectory temporary, string artifactName = "minimal.en.locale-v1.json")
    {
        string root = temporary.Resolve("artifacts", "obj");
        string artifact = Directory.EnumerateFiles(root, artifactName, SearchOption.AllDirectories).Single();
        return Path.GetDirectoryName(artifact)!;
    }

    private static string[] GeneratedArtifacts(string output) => TestFixture
        .RelativeFiles(output)
        .Where(path => !Path.GetFileName(path).StartsWith(".generate.", StringComparison.Ordinal))
        .ToArray();

    private static string XmlPath(string path) => path.Replace("&", "&amp;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal);

    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
