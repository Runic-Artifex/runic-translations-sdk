using System;
using System.IO;
using System.Linq;
using System.Text;
using TUnit.Core;

namespace Runic.Translations.Build.Tests;

internal sealed class BuildIntegrationTests
{
    [Test, DisplayName("build props and targets expose stable import sentinels")]
    public void ImportsExposeSentinels()
    {
        using TemporaryDirectory temporary = CreateConsumer(false);
        ProcessResult result = Processes.DotNet(temporary.Path, "msbuild", "Consumer.csproj", "/nologo", "/t:DumpTranslationItems", "/v:minimal");
        Assert.Equal(0, result.ExitCode, result.Combined);
        string dump = File.ReadAllText(temporary.Resolve("dump.txt"), Encoding.UTF8).Replace('\\', '/');
        Assert.Contains("PropsImported=true", dump); Assert.Contains("TargetsImported=true", dump); Assert.Contains("Hello|Mf2", dump);
    }

    [Test, DisplayName("build tracks the nearest tool manifest above the project")]
    public void RepositoryToolManifestIsTracked()
    {
        using TemporaryDirectory temporary = new();
        Directory.CreateDirectory(temporary.Resolve(".config")); Directory.CreateDirectory(temporary.Resolve("src", "App"));
        File.WriteAllText(temporary.Resolve(".config", "dotnet-tools.json"), "{\"version\":1,\"isRoot\":true,\"tools\":{}}\n", new UTF8Encoding(false));
        string props = XmlPath(RepositoryPaths.Resolve("packages", "dotnet", "Runic.Translations.Build", "build", "Runic.Translations.Build.props"));
        File.WriteAllText(temporary.Resolve("src", "App", "Probe.proj"), $"""<Project><Import Project="{props}" /></Project>""", new UTF8Encoding(false));
        ProcessResult result = Processes.DotNet(temporary.Resolve("src", "App"), "msbuild", "Probe.proj", "/nologo", "-getItem:TranslationsToolInput");
        Assert.Equal(0, result.ExitCode, result.Combined);
        Assert.Contains(temporary.Path.Replace('\\', '/') + "/.config/dotnet-tools.json", result.StandardOutput.Replace("\\\\", "/").Replace('\\', '/'));
    }

    // The editor runs the source-built tool through dotnet run, which no tool manifest describes. Without its sources as
    // generation inputs, a compiler change (such as the generated ESM runtime) left the editor's catalog stale under obj/.
    [Test, DisplayName("the editor regenerates its catalog when the source-built tool changes")]
    public void EditorTracksSourceToolInputs()
    {
        ProcessResult result = Processes.DotNet(RepositoryPaths.Resolve("apps", "translations-editor"), "msbuild", "Runic.Translations.Editor.csproj", "/nologo", "-getItem:TranslationsToolInput");
        Assert.Equal(0, result.ExitCode, result.Combined);
        string items = result.StandardOutput.Replace("\\\\", "/").Replace('\\', '/');
        foreach (string input in new[] { "tools/Runic.Translations.Compiler/Generation/Rmf2EsmRuntimeV5.js", "tools/Runic.Translations.Compiler/Generation/Rmf2EsmOutputRendererV5.cs",
            "tools/dotnet-runic-translations/Program.cs", "tools/dotnet-runic-translations/TranslationsToolCommandModule.cs", "packages/dotnet/Runic.Translations.Tooling/TranslationInterchange.cs" })
            Assert.Contains(RepositoryPaths.Resolve(input.Split('/')).Replace('\\', '/'), items);
        Assert.True(!items.Contains("/bin/", StringComparison.Ordinal) && !items.Contains("/obj/", StringComparison.Ordinal), "Build output is a tool input.");
    }

    [Test, DisplayName("build discovers direct MF2 sources and generates semantic artifacts")]
    public void DirectMf2GenerationIsIncremental()
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

    [Test, DisplayName("build discovers mounted grouped RMF2 sources and membership changes")]
    public void MountedRmf2MembershipIsIncremental()
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

    [Test, DisplayName("build emit properties select v5 groups and reject retired outputs")]
    public void EmitFlagsAreExact()
    {
        foreach ((string property, string expected) in new[] { ("<TranslationsEmitJson>true</TranslationsEmitJson>", "minimal.en.locale-v5.json"), ("<TranslationsEmitEsm>true</TranslationsEmitEsm>", "web-module-manifest-v3.json") })
        {
            using TemporaryDirectory temporary = CreateConsumer(false, extraProperties: property); ProcessResult result = Build(temporary); Assert.Equal(0, result.ExitCode, result.Combined);
            string output = FindGeneratedDirectory(temporary, expected); string[] files = GeneratedArtifacts(output);
            if (expected == "web-module-manifest-v3.json") Assert.True(files.Contains("web-module-manifest-v3.json", StringComparer.Ordinal) &&
                files.All(static path => !path.EndsWith(".locale-v5.json", StringComparison.Ordinal) && !path.EndsWith(".g.cs", StringComparison.Ordinal)),
                "ESM selection emitted another output group: " + string.Join('|', files));
            else Assert.Equal("minimal.asset-manifest-v1.json|minimal.en.locale-v5.json", string.Join('|', files));
        }
        foreach ((string property, string flag) in new[] { ("<TranslationsEmitTypeScript>true</TranslationsEmitTypeScript>", "--emit-typescript"), ("<TranslationsEmitTemplateManifest>true</TranslationsEmitTemplateManifest>", "--emit-template-manifest"), ("<TranslationsEmitCpp>true</TranslationsEmitCpp>", "--emit-cpp") })
        {
            using TemporaryDirectory temporary = CreateConsumer(false, extraProperties: property); ProcessResult result = Build(temporary);
            Assert.True(result.ExitCode != 0, "Build accepted retired output selection " + flag); Assert.Contains("RTR0065", result.Combined); Assert.Contains(flag, result.Combined);
        }
    }

    [Test, DisplayName("build rejects an output path outside the intermediate root")]
    public void OutputContainmentIsEnforced()
    {
        using TemporaryDirectory temporary = CreateConsumer(true, "escaped-output/"); ProcessResult result = Build(temporary);
        Assert.True(result.ExitCode != 0, "Build unexpectedly accepted an output path outside IntermediateOutputPath."); Assert.Contains("RTR0020", result.Combined); Assert.False(Directory.Exists(temporary.Resolve("escaped-output")), "Rejected output path was created.");
    }

    [Test, DisplayName("build reports each translation diagnostic once, with its help link")]
    public void DiagnosticsAppearOnce()
    {
        using TemporaryDirectory temporary = CreateConsumer(true);
        // With the source generator present, as in a package consumer, it owns the compilation diagnostics.
        AddSourceGenerator(temporary);
        File.WriteAllText(temporary.Resolve("translations", "en", "Hello.mf2"), ".local $a = {$name}\n.input {$name :string}\n{{Hello {$a}}}\n", new UTF8Encoding(false));

        ProcessResult failed = Build(temporary);
        Assert.True(failed.ExitCode != 0, failed.Combined);
        string[] errors = failed.Combined.Split('\n').Select(line => line.Trim()).Where(line => line.Contains(": error ", StringComparison.Ordinal)).Distinct(StringComparer.Ordinal).ToArray();
        Assert.True(errors.Length == 1, "Expected one distinct translation error:\n" + failed.Combined);
        Assert.Contains("error RTR0067:", errors[0]);
        Assert.Contains("Hello.mf2(", errors[0]);
        Assert.Contains("docs/guides/translations/diagnostics.md#rtr0067", errors[0]);
        Assert.False(failed.Combined.Contains("RCLI9012", StringComparison.Ordinal) || failed.Combined.Contains("MSB3073", StringComparison.Ordinal), failed.Combined);

        File.WriteAllText(temporary.Resolve("translations", "en", "Hello.mf2"), "Hello {$name}\n", new UTF8Encoding(false));
        ProcessResult fixedBuild = Build(temporary, true);
        Assert.Equal(0, fixedBuild.ExitCode, fixedBuild.Combined);
        Assert.True(File.Exists(Path.Combine(FindGeneratedDirectory(temporary, "minimal.en.locale-v5.json"), "minimal.asset-manifest-v1.json")), "The fixed build did not generate.");
    }

    [Test, DisplayName("build fails when the tool reports translation errors without the source generator")]
    public void ToolErrorsFailWithoutGenerator()
    {
        using TemporaryDirectory temporary = CreateConsumer(true);
        File.WriteAllText(temporary.Resolve("translations", "en", "Hello.mf2"), ".local $a = {$name}\n.input {$name :string}\n{{Hello {$a}}}\n", new UTF8Encoding(false));
        ProcessResult failed = Build(temporary);
        Assert.True(failed.ExitCode != 0, "Build succeeded although the tool reported a translation error:\n" + failed.Combined);
        Assert.Contains("error RTR0067:", failed.Combined);
        Assert.False(failed.Combined.Contains("exited with code", StringComparison.Ordinal) || failed.Combined.Contains("MSB3073", StringComparison.Ordinal), failed.Combined);
        Assert.False(Directory.EnumerateFiles(temporary.Path, ".generate.stamp", SearchOption.AllDirectories).Any(), "A failed generation wrote its stamp.");
    }

    // A tool that fails without logging a canonical error must not leave a successful build, whether or
    // not the source generator is present to report deferred diagnostics.
    [Test, DisplayName("build fails when the tool exits with $exitCode and logs no error (generator: $withGenerator)")]
    [Arguments(2, false)]
    [Arguments(3, false)]
    [Arguments(1, true)]
    [Arguments(2, true)]
    public void SilentToolFailureFailsTheBuild(int exitCode, bool withGenerator)
    {
        bool windows = OperatingSystem.IsWindows();
        string tool = windows ? "cmd /d /c &quot;$(MSBuildProjectDirectory)\\failing-tool.cmd&quot;" : "sh &quot;$(MSBuildProjectDirectory)/failing-tool.sh&quot;";
        using TemporaryDirectory temporary = CreateConsumer(true, extraProperties: $"<TranslationsToolCommand>{tool}</TranslationsToolCommand>");
        File.WriteAllText(temporary.Resolve(windows ? "failing-tool.cmd" : "failing-tool.sh"), windows ? $"@exit /b {exitCode}\r\n" : $"exit {exitCode}\n", new UTF8Encoding(false));
        if (withGenerator) AddSourceGenerator(temporary);
        ProcessResult failed = Build(temporary);
        Assert.True(failed.ExitCode != 0, $"Build succeeded although the tool exited with {exitCode}:\n" + failed.Combined);
        Assert.Contains(withGenerator && exitCode == 2 ? "The Runic Translations tool reported translation errors" : $"The Runic Translations tool exited with code {exitCode}.", failed.Combined);
        Assert.False(failed.Combined.Contains("MSB3073", StringComparison.Ordinal), failed.Combined);
    }

    private static void AddSourceGenerator(TemporaryDirectory temporary)
    {
        string generator = Path.Combine(RepositoryPaths.Resolve("packages", "dotnet", "Runic.Translations.Build"), "bin", BuildConfiguration, "net10.0");
        string import = "<Import Project=\"" + XmlPath(RepositoryPaths.Resolve("packages", "dotnet", "Runic.Translations.Build", "build", "Runic.Translations.Build.targets")) + "\" />";
        string items = $"""<ItemGroup><Reference Include="Runic.Translations" HintPath="{XmlPath(RepositoryPaths.Resolve("packages", "dotnet", "Runic.Translations", "bin", BuildConfiguration, "net10.0", "Runic.Translations.dll"))}" /><Analyzer Include="{XmlPath(Path.Combine(generator, "Runic.Translations.Generator.dll"))}" /><Analyzer Include="{XmlPath(Path.Combine(generator, "Runic.Translations.Compiler.dll"))}" /></ItemGroup>{import}""";
        string project = File.ReadAllText(temporary.Resolve("Consumer.csproj"));
        Assert.Contains(import, project);
        File.WriteAllText(temporary.Resolve("Consumer.csproj"), project.Replace(import, items, StringComparison.Ordinal), new UTF8Encoding(false));
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
