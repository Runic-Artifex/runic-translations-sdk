using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;

namespace Runic.Translations.Build.Tests;

internal static class BuildIntegrationTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("RMF2 discovery follows custom project paths and membership changes", Rmf2MembershipIsIncremental);
        runner.Add("build props and targets expose stable import sentinels", ImportsExposeSentinels);
        runner.Add("build maps declared items to AdditionalFiles metadata", ItemsMapToAdditionalFiles);
        runner.Add("build target declares incremental Inputs and Outputs", TargetDeclaresInputsAndOutputs);
        runner.Add("build discovers one conventional MF2 translation project", ConventionalProjectIsDiscovered);
        runner.Add("build generates only below the isolated intermediate root", GenerationIsIsolated);
        runner.Add("build generation is incremental and input-sensitive", GenerationIsIncremental);
        runner.Add("build profile changes regenerate and reconcile v4 and v5 artifacts", ExecutionProfileChangesReconcileArtifacts);
        runner.Add("build v5 emit properties select exact supported groups and refuse unsupported groups", V5EmitFlagsAreExact);
        runner.Add("build regenerates an artifact missing despite a current stamp", MissingArtifactInvalidatesStamp);
        runner.Add("build emit flags select exact non-CSharp artifact groups", EmitFlagsSelectOutputs);
        runner.Add("build fails fast when the configured tool is missing", MissingToolFailsFast);
        runner.Add("build rejects an output path outside the intermediate root", OutputContainmentIsEnforced);
        runner.Add("build rejects a reparse-point output root", ReparsePointOutputIsRejected);
        runner.Add("build reconciles All to JSON and clean preserves unrelated files", ReconcileAndCleanRespectOwnership);
    }

    private static void Rmf2MembershipIsIncremental()
    {
        using TemporaryDirectory temporary = CreateConsumer(generationEnabled: true);
        string custom = temporary.Resolve("Locale Sources");
        Directory.Move(temporary.Resolve("translations"), custom);
        Directory.Delete(Path.Combine(custom, "en"), recursive: true);
        string projectPath = Path.Combine(custom, "runic.json");
        string project = File.ReadAllText(projectPath).Replace("\"schemaVersion\":1,", "\"schemaVersion\":1,\"sourceLayout\":\"rmf2-v1\",", StringComparison.Ordinal);
        File.WriteAllText(projectPath, project);
        File.WriteAllText(Path.Combine(custom, "en.rmf2"), "ui {\n  dialog {\n    Hello = Hello\n  }\n}\n");
        string consumerPath = temporary.Resolve("Consumer.csproj");
        string consumer = File.ReadAllText(consumerPath);
        int lastImport = consumer.LastIndexOf("<Import Project=", StringComparison.Ordinal);
        consumer = consumer.Insert(lastImport, "<ItemGroup><TranslationProject Include=\"Locale Sources/runic.json\" /></ItemGroup>\n");
        File.WriteAllText(consumerPath, consumer);
        ProcessResult first = Build(temporary);
        Assert.Equal(0, first.ExitCode, first.Combined);
        string output = FindGeneratedDirectory(temporary, "minimal.en.locale-v4.json");
        string stamp = Path.Combine(output, ".generate.stamp");
        DateTime firstWrite = File.GetLastWriteTimeUtc(stamp);
        ProcessResult unchanged = Build(temporary, noRestore: true);
        Assert.Equal(0, unchanged.ExitCode, unchanged.Combined);
        Assert.Equal(firstWrite, File.GetLastWriteTimeUtc(stamp), "unchanged RMF2 build regenerated");
        Thread.Sleep(1_200);
        File.WriteAllText(Path.Combine(custom, "en.rmf2"), "ui {\n  dialog {\n    Hello = Welcome\n  }\n}\n");
        ProcessResult edited = Build(temporary, noRestore: true);
        Assert.Equal(0, edited.ExitCode, edited.Combined);
        Assert.Contains("Welcome", File.ReadAllText(Path.Combine(output, "minimal.en.locale-v4.json")));
        string german = Path.Combine(custom, "de.rmf2");
        File.WriteAllText(german, "ui {\n  dialog {\n    Hello = Hallo\n  }\n}\n");
        File.SetLastWriteTimeUtc(german, firstWrite.AddMinutes(-1));
        ProcessResult added = Build(temporary, noRestore: true);
        Assert.Equal(0, added.ExitCode, added.Combined);
        Assert.True(File.Exists(Path.Combine(output, "minimal.de.locale-v4.json")), "new older-dated locale was ignored");
        File.Delete(german);
        ProcessResult removed = Build(temporary, noRestore: true);
        Assert.Equal(0, removed.ExitCode, removed.Combined);
        Assert.False(File.Exists(Path.Combine(output, "minimal.de.locale-v4.json")), "deleted locale artifact survived");
    }

    private static void ImportsExposeSentinels()
    {
        using TemporaryDirectory temporary = CreateConsumer(generationEnabled: false);
        ProcessResult result = Processes.DotNet(temporary.Path, "msbuild", "Consumer.csproj", "/nologo", "/t:DumpTranslationItems", "/v:minimal");
        Assert.Equal(0, result.ExitCode, result.Combined);
        string[] lines = File.ReadAllLines(temporary.Resolve("dump.txt"));
        Assert.True(lines.Contains("PropsImported=true", StringComparer.Ordinal), "Props import sentinel was not true.");
        Assert.True(lines.Contains("TargetsImported=true", StringComparer.Ordinal), "Targets import sentinel was not true.");
    }

    private static void ItemsMapToAdditionalFiles()
    {
        using TemporaryDirectory temporary = CreateConsumer(generationEnabled: false);
        File.Move(temporary.Resolve("translations", "en", "Hello.mf2"), temporary.Resolve("translations", "en", "Hello.mF2"));
        ProcessResult result = Processes.DotNet(temporary.Path, "msbuild", "Consumer.csproj", "/nologo", "/t:DumpTranslationItems", "/v:minimal");
        Assert.Equal(0, result.ExitCode, result.Combined);
        string dump = File.ReadAllText(temporary.Resolve("dump.txt"), Encoding.UTF8).Replace('\\', '/');
        Assert.Contains("runic|Project", dump);
        Assert.Contains("Hello|Mf2", dump);
    }

    private static void TargetDeclaresInputsAndOutputs()
    {
        string targetsPath = RepositoryPaths.Resolve(
            "packages",
            "dotnet",
            "Runic.Translations.Build",
            "build",
            "Runic.Translations.Build.targets");
        XDocument document = XDocument.Load(targetsPath, LoadOptions.PreserveWhitespace);
        XElement target = document
            .Descendants("Target")
            .Single(element => string.Equals((string?)element.Attribute("Name"), "_RunicTranslationsGenerateTranslationArtifactsCore", StringComparison.Ordinal));
        string inputs = (string?)target.Attribute("Inputs") ?? string.Empty;
        string outputs = (string?)target.Attribute("Outputs") ?? string.Empty;
        Assert.Contains("@(TranslationProject)", inputs);
        Assert.Contains("@(TranslationMf2)", inputs);
        Assert.Contains("$(MSBuildProjectFullPath)", inputs);
        Assert.Equal("$(TranslationsOutputStamp)", outputs);

        string responseProject = document
            .Descendants("_TranslationsDesiredResponseLine")
            .Select(element => (string?)element.Attribute("Include"))
            .Single(value => value?.StartsWith("@(TranslationProject", StringComparison.Ordinal) == true)!;
        Assert.Equal("@(TranslationProject->'\"%(FullPath)\"')", responseProject);
    }

    private static void GenerationIsIsolated()
    {
        using TemporaryDirectory temporary = CreateConsumer(generationEnabled: true);
        byte[] catalogBefore = File.ReadAllBytes(temporary.Resolve("translations", "runic.json"));
        byte[] documentBefore = File.ReadAllBytes(temporary.Resolve("translations", "en", "Hello.mf2"));

        ProcessResult result = Build(temporary);

        Assert.Equal(0, result.ExitCode, result.Combined);
        string output = FindGeneratedDirectory(temporary);
        string intermediate = Path.GetFullPath(temporary.Resolve("artifacts", "obj")) + Path.DirectorySeparatorChar;
        Assert.True(Path.GetFullPath(output).StartsWith(intermediate, PathComparison), $"Generated output escaped the isolated intermediate root: {output}");
        Assert.Equal(
            "minimal.asset-manifest-v1.json|minimal.en.locale-v2.json|minimal.esm/dynamic.d.ts|minimal.esm/dynamic.js|minimal.esm/messages.d.ts|minimal.esm/messages.js|minimal.esm/messages/_index.js|minimal.esm/messages/m$Hello.js|minimal.esm/runtime.d.ts|minimal.esm/runtime.js|minimal.esm/server.d.ts|minimal.esm/server.js|minimal.esm/transport.d.ts|minimal.esm/transport.js|minimal.esm/web-module-manifest-v2.json|minimal.template-manifest-v2.json|minimal.translations-v1.d.ts",
            string.Join('|', GeneratedArtifacts(output)));
        Assert.True(catalogBefore.AsSpan().SequenceEqual(File.ReadAllBytes(temporary.Resolve("translations", "runic.json"))), "Build changed the project source.");
        Assert.True(documentBefore.AsSpan().SequenceEqual(File.ReadAllBytes(temporary.Resolve("translations", "en", "Hello.mf2"))), "Build changed the MF2 source.");
        Assert.False(Directory.EnumerateFiles(temporary.Path, "*.g.cs", SearchOption.TopDirectoryOnly).Any(), "Build wrote generated C# beside project sources.");
        Assert.False(Directory.Exists(temporary.Resolve("translations", "generated")), "Build wrote under the source translation directory.");
    }

    private static void ConventionalProjectIsDiscovered()
    {
        using TemporaryDirectory temporary = new();
        Directory.CreateDirectory(temporary.Resolve("translations", "en"));
        Directory.CreateDirectory(temporary.Resolve("translations", "de"));
        File.WriteAllText(temporary.Resolve("translations", "runic.json"), """
            { "schemaVersion": 1, "catalog": "app", "code": { "namespace": "Example", "className": "AppText" }, "baseLocale": "en", "locales": ["en", "de"] }
            """, new UTF8Encoding(false));
        File.WriteAllText(temporary.Resolve("translations", "en", "application_title.mf2"), "Application title\n", new UTF8Encoding(false));
        File.WriteAllText(temporary.Resolve("translations", "de", "application_title.mf2"), "Anwendungstitel\n", new UTF8Encoding(false));
        File.WriteAllText(temporary.Resolve("Program.cs"), "internal static class Program { private static void Main() { _ = typeof(Example.AppText); } }\n", new UTF8Encoding(false));
        string props = XmlPath(RepositoryPaths.Resolve("packages", "dotnet", "Runic.Translations.Build", "build", "Runic.Translations.Build.props"));
        string targets = XmlPath(RepositoryPaths.Resolve("packages", "dotnet", "Runic.Translations.Build", "build", "Runic.Translations.Build.targets"));
        string runtimeProject = XmlPath(RepositoryPaths.Resolve("packages", "dotnet", "Runic.Translations", "Runic.Translations.csproj"));
        string compilerProject = XmlPath(RepositoryPaths.Resolve("tools", "Runic.Translations.Compiler", "Runic.Translations.Compiler.csproj"));
        string generatorProject = XmlPath(RepositoryPaths.Resolve("packages", "dotnet", "Runic.Translations.Generator", "Runic.Translations.Generator.csproj"));
        string compilerAssembly = XmlPath(RepositoryPaths.Resolve("tools", "Runic.Translations.Compiler", "bin", "$(Configuration)", "net10.0", "Runic.Translations.Compiler.dll"));
        string tool = $"dotnet &quot;{XmlPath(RepositoryPaths.ToolAssembly)}&quot;";
        string project = $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <Import Project="{{props}}" />
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <IntermediateOutputPath>artifacts/obj/$(Configuration)/</IntermediateOutputPath>
                <BaseOutputPath>artifacts/bin/</BaseOutputPath>
                <RestorePackagesPath>{{XmlPath(RepositoryPaths.Resolve(".packages"))}}</RestorePackagesPath>
                <TranslationsGenerateOnBuild>true</TranslationsGenerateOnBuild>
                <TranslationsToolCommand>{{tool}}</TranslationsToolCommand>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{runtimeProject}}" />
                <ProjectReference Include="{{compilerProject}}" />
                <ProjectReference Include="{{generatorProject}}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
                <Analyzer Include="{{compilerAssembly}}" />
              </ItemGroup>
              <Import Project="{{targets}}" />
            </Project>
            """;
        File.WriteAllText(temporary.Resolve("Consumer.csproj"), project, new UTF8Encoding(false));

        ProcessResult result = Build(temporary);

        Assert.Equal(0, result.ExitCode, result.Combined);
        Assert.True(Directory.EnumerateFiles(temporary.Resolve("artifacts", "obj"), "app.esm", SearchOption.AllDirectories).Any() ||
            Directory.EnumerateFiles(temporary.Resolve("artifacts", "obj"), "messages.js", SearchOption.AllDirectories).Any(),
            "Conventional project did not generate ESM artifacts.");
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
        string document = temporary.Resolve("translations", "en", "Hello.mf2");
        File.SetLastWriteTimeUtc(document, DateTime.UtcNow);
        ProcessResult third = Build(temporary, noRestore: true);
        Assert.Equal(0, third.ExitCode, third.Combined);
        Assert.True(File.GetLastWriteTimeUtc(stamp) > firstWrite, "A changed input did not rerun generation.");
    }

    private static void ExecutionProfileChangesReconcileArtifacts()
    {
        using TemporaryDirectory temporary = CreateConsumer(
            generationEnabled: true,
            extraProperties: """
                <TranslationsEmitJson>false</TranslationsEmitJson>
                <TranslationsEmitTypeScript>false</TranslationsEmitTypeScript>
                <TranslationsEmitTemplateManifest>false</TranslationsEmitTemplateManifest>
                <TranslationsEmitEsm>false</TranslationsEmitEsm>
                <TranslationsEmitCpp>false</TranslationsEmitCpp>
                """);
        // Simulate an inherited repository/CI preference. The consumer's explicit
        // false values above request profile defaults rather than this ambient group.
        File.WriteAllText(temporary.Resolve("Directory.Build.props"),
            "<Project><PropertyGroup><TranslationsEmitTypeScript>true</TranslationsEmitTypeScript></PropertyGroup></Project>\n",
            new UTF8Encoding(false));
        Directory.Delete(temporary.Resolve("translations", "en"), recursive: true);
        Directory.CreateDirectory(temporary.Resolve("feature"));
        string configPath = temporary.Resolve("translations", "runic.json");
        const string current = "{\"schemaVersion\":1,\"catalog\":\"minimal\",\"code\":{\"namespace\":\"Example\",\"className\":\"MinimalText\"},\"baseLocale\":\"en\",\"sourceLayout\":\"rmf2-v1\",\"sourceRoots\":[{\"path\":\"../feature\",\"namespace\":[\"shop\"]}]}\n";
        File.WriteAllText(configPath, current, new UTF8Encoding(false));
        File.WriteAllText(temporary.Resolve("feature", "en.rmf2"), "greeting = Hello\n", new UTF8Encoding(false));

        ProcessResult first = Build(temporary);
        Assert.Equal(0, first.ExitCode, first.Combined);
        string output = FindGeneratedDirectory(temporary, "minimal.en.locale-v4.json");
        Assert.True(File.Exists(Path.Combine(output, "minimal.esm", "web-module-manifest-v2.json")), "default RMF2 build omitted v4 web manifest");
        Assert.True(File.Exists(Path.Combine(output, "minimal.translations-v1.d.ts")), "default RMF2 build omitted v4 TypeScript contract");
        Assert.True(Directory.EnumerateFiles(output, "minimal.template-manifest-*.json", SearchOption.TopDirectoryOnly).Any(), "default RMF2 build omitted v4 template manifest");

        Thread.Sleep(1_200);
        string activated = current.Replace("\"sourceLayout\":\"rmf2-v1\"",
            "\"sourceLayout\":\"rmf2-v1\",\"executionProfile\":\"rmf2-execution-v2\"", StringComparison.Ordinal);
        File.WriteAllText(configPath, activated, new UTF8Encoding(false));
        ProcessResult second = Build(temporary, noRestore: true);
        Assert.Equal(0, second.ExitCode, second.Combined);
        Assert.True(File.Exists(Path.Combine(output, "minimal.en.locale-v5.json")), "profile change did not generate v5 locale artifact");
        Assert.True(File.Exists(Path.Combine(output, "minimal.asset-manifest-v1.json")), "profile change did not generate v5 asset manifest");
        Assert.True(File.Exists(Path.Combine(output, "minimal.esm-v5", "web-module-manifest-v3.json")), "profile change did not generate v5 web manifest");
        Assert.False(File.Exists(Path.Combine(output, "minimal.translations-v1.d.ts")), "v5 default emitted the v4 TypeScript edge contract");
        Assert.False(Directory.EnumerateFiles(output, "minimal.template-manifest-*.json", SearchOption.TopDirectoryOnly).Any(), "v5 default emitted a v4 template manifest");
        Assert.False(File.Exists(Path.Combine(output, "minimal.en.locale-v4.json")), "profile change retained stale v4 locale artifact");
        Assert.False(File.Exists(Path.Combine(output, "minimal.esm", "web-module-manifest-v2.json")), "profile change retained stale v4 ESM manifest");

        string german = temporary.Resolve("feature", "de.rmf2");
        File.WriteAllText(german, "greeting = Hallo\n", new UTF8Encoding(false));
        File.SetLastWriteTimeUtc(german, DateTime.UtcNow.AddMinutes(-1));
        ProcessResult added = Build(temporary, noRestore: true);
        Assert.Equal(0, added.ExitCode, added.Combined);
        Assert.True(File.Exists(Path.Combine(output, "minimal.de.locale-v5.json")), "mounted v5 membership addition did not regenerate");
        File.Delete(german);
        ProcessResult removed = Build(temporary, noRestore: true);
        Assert.Equal(0, removed.ExitCode, removed.Combined);
        Assert.False(File.Exists(Path.Combine(output, "minimal.de.locale-v5.json")), "mounted v5 membership deletion left stale output");

        Thread.Sleep(1_200);
        File.WriteAllText(configPath, current, new UTF8Encoding(false));
        ProcessResult third = Build(temporary, noRestore: true);
        Assert.Equal(0, third.ExitCode, third.Combined);
        Assert.True(File.Exists(Path.Combine(output, "minimal.en.locale-v4.json")), "profile removal did not restore v4 output");
        Assert.True(File.Exists(Path.Combine(output, "minimal.translations-v1.d.ts")), "profile removal did not restore v4 TypeScript contract");
        Assert.True(Directory.EnumerateFiles(output, "minimal.template-manifest-*.json", SearchOption.TopDirectoryOnly).Any(), "profile removal did not restore v4 template manifest");
        Assert.False(File.Exists(Path.Combine(output, "minimal.en.locale-v5.json")), "profile removal retained stale v5 locale artifact");
        Assert.False(File.Exists(Path.Combine(output, "minimal.esm-v5", "web-module-manifest-v3.json")), "profile removal retained stale v5 ESM manifest");
    }

    private static void V5EmitFlagsAreExact()
    {
        foreach ((string Property, string Expected) in new[]
        {
            ("<TranslationsEmitJson>true</TranslationsEmitJson>", "minimal.asset-manifest-v1.json|minimal.en.locale-v5.json"),
            ("<TranslationsEmitEsm>true</TranslationsEmitEsm>", "esm"),
        })
        {
            using TemporaryDirectory temporary = CreateV5Consumer(Property);
            ProcessResult result = Build(temporary);
            Assert.Equal(0, result.ExitCode, result.Combined);
            string output = FindGenerationRoot(temporary);
            string[] artifacts = GeneratedArtifacts(output);
            if (Expected == "esm")
                Assert.True(artifacts.Length > 0 && artifacts.All(static path => path.StartsWith("minimal.esm-v5/", StringComparison.Ordinal)),
                    "TranslationsEmitEsm produced another v5 output group");
            else Assert.Equal(Expected, string.Join('|', artifacts));
        }

        foreach ((string Property, string Switch) in new[]
        {
            ("<TranslationsEmitTypeScript>true</TranslationsEmitTypeScript>", "--emit-typescript"),
            ("<TranslationsEmitTemplateManifest>true</TranslationsEmitTemplateManifest>", "--emit-template-manifest"),
            ("<TranslationsEmitCpp>true</TranslationsEmitCpp>", "--emit-cpp"),
        })
        {
            using TemporaryDirectory temporary = CreateV5Consumer(Property);
            ProcessResult result = Build(temporary);
            Assert.True(result.ExitCode != 0, "MSBuild accepted unsupported v5 switch " + Switch);
            Assert.Contains("RTR0065", result.Combined);
            Assert.Contains(Switch, result.Combined);
            Assert.False(Directory.EnumerateFiles(temporary.Resolve("artifacts", "obj"), ".generate.stamp", SearchOption.AllDirectories).Any(),
                "MSBuild stamped unsupported v5 selection " + Switch);
        }
    }

    private static TemporaryDirectory CreateV5Consumer(string property)
    {
        TemporaryDirectory temporary = CreateConsumer(generationEnabled: false, extraProperties: property);
        Directory.Delete(temporary.Resolve("translations", "en"), recursive: true);
        File.WriteAllText(temporary.Resolve("translations", "runic.json"),
            "{\"schemaVersion\":1,\"catalog\":\"minimal\",\"code\":{\"namespace\":\"Example\",\"className\":\"MinimalText\"},\"baseLocale\":\"en\",\"sourceLayout\":\"rmf2-v1\",\"executionProfile\":\"rmf2-execution-v2\"}\n",
            new UTF8Encoding(false));
        File.WriteAllText(temporary.Resolve("translations", "en.rmf2"), "greeting = Hello\n", new UTF8Encoding(false));
        return temporary;
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
        string missing = Path.Combine(output, "minimal.translations-v1.d.ts");
        File.Delete(missing);

        ProcessResult second = Build(temporary, noRestore: true);
        Assert.Equal(0, second.ExitCode, second.Combined);
        Assert.True(File.Exists(missing), "Build trusted its stamp after a declared artifact was deleted.");
    }

    private static void EmitFlagsSelectOutputs()
    {
        using TemporaryDirectory temporary = CreateConsumer(
            generationEnabled: false,
            extraProperties: "<TranslationsEmitTypeScript>true</TranslationsEmitTypeScript>");
        ProcessResult result = Build(temporary);
        Assert.Equal(0, result.ExitCode, result.Combined);
        string output = FindGeneratedDirectory(temporary, "minimal.translations-v1.d.ts");
        Assert.Equal("minimal.asset-manifest-v1.json|minimal.translations-v1.d.ts", string.Join('|', GeneratedArtifacts(output)));
    }

    private static void MissingToolFailsFast()
    {
        using TemporaryDirectory temporary = CreateConsumer(
            generationEnabled: true,
            toolCommand: "definitely-missing-translations-tool");
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
        string link = temporary.Resolve("artifacts", "obj", BuildConfiguration, "net10.0", "linked-output");
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
        Assert.True(File.Exists(Path.Combine(output, "minimal.en.locale-v2.json")), "All emission omitted JSON.");
        Assert.True(File.Exists(Path.Combine(output, "minimal.asset-manifest-v1.json")), "All emission omitted the asset manifest.");
        Assert.True(File.Exists(Path.Combine(output, "minimal.template-manifest-v2.json")), "All emission omitted template manifest.");
        Assert.True(File.Exists(Path.Combine(output, "minimal.translations-v1.d.ts")), "All emission omitted TypeScript.");
        string sentinel = Path.Combine(output, "consumer-sentinel.txt");
        File.WriteAllText(sentinel, "consumer owned", new UTF8Encoding(false));

        string projectPath = temporary.Resolve("Consumer.csproj");
        string project = File.ReadAllText(projectPath, Encoding.UTF8).Replace(
            "<TranslationsGenerateOnBuild>true</TranslationsGenerateOnBuild>",
            "<TranslationsGenerateOnBuild>false</TranslationsGenerateOnBuild><TranslationsEmitJson>true</TranslationsEmitJson>",
            StringComparison.Ordinal);
        File.WriteAllText(projectPath, project, new UTF8Encoding(false));
        ProcessResult second = Build(temporary, noRestore: true);
        Assert.Equal(0, second.ExitCode, second.Combined);
        Assert.True(File.Exists(Path.Combine(output, "minimal.en.locale-v2.json")), "JSON-only reconciliation removed JSON.");
        Assert.True(File.Exists(Path.Combine(output, "minimal.asset-manifest-v1.json")), "JSON-only reconciliation removed the asset manifest.");
        Assert.False(File.Exists(Path.Combine(output, "minimal.template-manifest-v2.json")), "JSON-only reconciliation retained the prior template manifest.");
        Assert.False(File.Exists(Path.Combine(output, "minimal.translations-v1.d.ts")), "JSON-only reconciliation retained the prior TypeScript contract.");
        Assert.True(File.Exists(sentinel), "Reconciliation deleted an uninventoried consumer file.");

        string exposure = File.ReadAllText(temporary.Resolve("artifacts", "generated-items.txt"), Encoding.UTF8).Trim();
        Assert.Equal("minimal.asset-manifest-v1.json|minimal.en.locale-v2.json", exposure, "Generated item exposure included private state or an unrelated file");

        ProcessResult clean = Clean(temporary);
        Assert.Equal(0, clean.ExitCode, clean.Combined);
        Assert.False(File.Exists(Path.Combine(output, "minimal.en.locale-v2.json")), "Clean retained an inventoried generated artifact.");
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
        Directory.CreateDirectory(temporary.Resolve("translations", "en"));
        File.WriteAllText(temporary.Resolve("translations", "runic.json"), "{\"schemaVersion\":1,\"catalog\":\"minimal\",\"code\":{\"namespace\":\"Example\",\"className\":\"MinimalText\"},\"baseLocale\":\"en\"}\n", new UTF8Encoding(false));
        File.WriteAllText(temporary.Resolve("translations", "en", "Hello.mf2"), ".input {$name :string}\nHello {$name}\n", new UTF8Encoding(false));
        File.WriteAllText(temporary.Resolve("Program.cs"), "internal static class Program { private static void Main() { } }\n", new UTF8Encoding(false));

        string props = XmlPath(RepositoryPaths.Resolve("packages", "dotnet", "Runic.Translations.Build", "build", "Runic.Translations.Build.props"));
        string targets = XmlPath(RepositoryPaths.Resolve("packages", "dotnet", "Runic.Translations.Build", "build", "Runic.Translations.Build.targets"));
        string tool = toolCommand is null
            ? $"dotnet &quot;{XmlPath(RepositoryPaths.ToolAssembly)}&quot;"
            : XmlPath(toolCommand);
        string output = outputPath is null ? string.Empty : $"<TranslationsOutputPath>{outputPath}</TranslationsOutputPath>";
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
                <TranslationsGenerateOnBuild>{{enabled}}</TranslationsGenerateOnBuild>
                <TranslationsToolCommand>{{tool}}</TranslationsToolCommand>
                {{output}}
                {{extraProperties}}
              </PropertyGroup>
              <Import Project="{{targets}}" />
              <Target Name="DumpTranslationItems" DependsOnTargets="_RunicTranslationsDiscoverTranslationSources">
                <WriteLinesToFile File="dump.txt"
                                  Overwrite="true"
                                  Lines="PropsImported=$(RunicTranslationsBuildPropsImported);TargetsImported=$(RunicTranslationsBuildTargetsImported);@(AdditionalFiles->'%(Filename)|%(RunicTranslationKind)')" />
              </Target>
              <Target Name="CaptureTranslationGeneratedFiles" AfterTargets="RunicTranslationsCollectTranslationArtifacts">
                <WriteLinesToFile File="artifacts/generated-items.txt"
                                  Overwrite="true"
                                  Lines="@(TranslationsGeneratedFile->'%(Filename)%(Extension)', '|')" />
              </Target>
            </Project>
            """;
        File.WriteAllText(temporary.Resolve("Consumer.csproj"), project, new UTF8Encoding(false));
        return temporary;
    }

    private static ProcessResult Build(TemporaryDirectory temporary, bool noRestore = false)
    {
        // Match the configuration that produced the test and its referenced build
        // task/tool assemblies. Hard-coding Debug makes Release CI silently fall
        // back to wildcard discovery, which cannot report executionProfile.
        string[] arguments = noRestore
            ? ["build", "Consumer.csproj", "--configuration", BuildConfiguration, "--no-restore", "/nologo", "/v:minimal"]
            : ["build", "Consumer.csproj", "--configuration", BuildConfiguration, "/nologo", "/v:minimal"];
        return Processes.DotNet(temporary.Path, arguments);
    }

    private static ProcessResult Clean(TemporaryDirectory temporary) => Processes.DotNet(
        temporary.Path,
        "clean",
        "Consumer.csproj",
        "--configuration",
        BuildConfiguration,
        "/nologo",
        "/v:minimal");

    private static string FindGeneratedDirectory(TemporaryDirectory temporary, string artifactName = "minimal.en.locale-v2.json")
    {
        string root = temporary.Resolve("artifacts", "obj");
        string artifact = Directory.EnumerateFiles(root, artifactName, SearchOption.AllDirectories).Single();
        return Path.GetDirectoryName(artifact)!;
    }

    private static string FindGenerationRoot(TemporaryDirectory temporary)
    {
        string state = Directory.EnumerateFiles(temporary.Resolve("artifacts", "obj"), ".generate.inputs", SearchOption.AllDirectories).Single();
        return Path.GetDirectoryName(state)!;
    }

    private static string[] GeneratedArtifacts(string output) => TestFixture
        .RelativeFiles(output)
        .Where(path => !Path.GetFileName(path).StartsWith(".generate.", StringComparison.Ordinal))
        .ToArray();

    private static string XmlPath(string path) => path.Replace("&", "&amp;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal);

    private static string BuildConfiguration => new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Debug";

    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
