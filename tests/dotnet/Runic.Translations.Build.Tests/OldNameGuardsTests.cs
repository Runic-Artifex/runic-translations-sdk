using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TUnit.Core;

namespace Runic.Translations.Build.Tests;

// The old-name guards of the Runic Artifex rename (D079.11): build/Runic.Translations.RenamedNames.targets is generated
// by eng/rename/old-names.mjs from eng/rename/old-names.json. 0.16 ships the lists empty; the rename release fills them.
// These cases run the generator (with Bun) on the shipped input and on a test-only list.
internal sealed class OldNameGuardsTests
{
    private static readonly string Input = RepositoryPaths.Resolve("eng", "rename", "old-names.json");
    private static readonly string Generator = RepositoryPaths.Resolve("eng", "rename", "old-names.mjs");
    private static readonly string[] Kinds = ["packages", "msbuild", "environment"];
    private static readonly string[] QuietBuilds = ["-p:DesignTimeBuild=true", "-p:_TargetAssemblyProjectName=App"];

    [Test, DisplayName("old-name guards: the shipped guard file matches eng/rename/old-names.json, whose lists are empty")]
    public void ShippedFileIsCurrent()
    {
        ProcessResult check = Run(RepositoryPaths.Root, "bun", [Generator, "--check"]);
        Assert.Equal(0, check.ExitCode, "Regenerate with bun eng/rename/old-names.mjs:\n" + check.Combined);
        JsonNode input = JsonNode.Parse(File.ReadAllText(Input))!;
        foreach (string list in Kinds)
            Assert.Equal(0, input[list]!.AsArray().Count, $"0.16 ships the {list} list empty");
        Assert.Equal("RTR0110|RTR0086|RTR0111",
            string.Join('|', Kinds.Select(kind => (string)input["family"]!["diagnostics"]![kind]!)));
        string targets = File.ReadAllText(RepositoryPaths.Resolve("packages", "dotnet", "Runic.Translations.Build", "build", "Runic.Translations.Build.targets"));
        Assert.Contains("<Import Project=\"Runic.Translations.RenamedNames.targets\" Condition=\"'$(_RunicTranslationsRenamedNamesImported)' != 'true'\" />", targets);
    }

    [Test, DisplayName("old-name guards: empty lists report nothing")]
    public void EmptyListsReportNothing()
    {
        using TemporaryDirectory temporary = Fixture(packages: [], msbuild: [], environment: []);
        ProcessResult result = Build(temporary);
        Assert.Equal(0, result.ExitCode, result.Combined);
        Assert.False(result.Combined.Contains("RTR0", StringComparison.Ordinal), result.Combined);
    }

    [Test, DisplayName("old-name guards: a test-only list reports each old name once and names the new one")]
    public void ListedNamesAreReportedOnce()
    {
        using TemporaryDirectory temporary = Fixture(
            packages: [("Runic.Example.Direct", "RunicArtifex.Example.Direct", null), ("Runic.Example.Transitive", "RunicArtifex.Example.Transitive", null),
                ("Runic.Example.Absent", "RunicArtifex.Example.Absent", null)],
            msbuild: [("RunicExampleProperty", "RaxExampleProperty", "property"), ("RunicExampleItem", "RaxExampleItem", "item"),
                ("RunicExampleUnset", "RaxExampleUnset", "property")],
            environment: [("RUNIC_EXAMPLE_SET", "RAX_EXAMPLE_SET", null), ("RUNIC_EXAMPLE_UNSET", "RAX_EXAMPLE_UNSET", null)]);
        ProcessResult result = Build(temporary);
        Assert.True(result.ExitCode != 0, result.Combined);
        string[] errors = result.Combined.Split('\n').Select(line => line.Trim()).Where(line => line.Contains(": error ", StringComparison.Ordinal)).ToArray();
        // The file is imported twice, as two packages could ship it; the import guard keeps one copy.
        Assert.Equal(5, errors.Length, result.Combined);
        string[] expected =
        [
            "error RTR0086: The Runic Translations build property RunicExampleProperty is renamed to RaxExampleProperty. The old name no longer applies; rename it in App.proj, its imports or the command line.",
            "error RTR0086: The Runic Translations build item RunicExampleItem is renamed to RaxExampleItem.",
            "error RTR0111: The environment variable RUNIC_EXAMPLE_SET is renamed to RAX_EXAMPLE_SET. The old name no longer applies; set RAX_EXAMPLE_SET instead",
            "error RTR0110: App references the package Runic.Example.Direct, directly or through another package. It is renamed to RunicArtifex.Example.Direct, and packages of the old and the new names do not work together. Run rax migrate",
            "error RTR0110: App references the package Runic.Example.Transitive, directly or through another package. It is renamed to RunicArtifex.Example.Transitive,",
        ];
        foreach (string text in expected)
            Assert.Equal(1, errors.Count(line => line.Contains(text, StringComparison.Ordinal)), $"{text}\n{result.Combined}");
    }

    [Test, DisplayName("old-name guards: WPF's temporary markup project and a design-time build report nothing")]
    public void TemporaryAndDesignTimeBuildsReportNothing()
    {
        using TemporaryDirectory temporary = Fixture(packages: [("Runic.Example.Direct", "RunicArtifex.Example.Direct", null)],
            msbuild: [("RunicExampleProperty", "RaxExampleProperty", "property")], environment: [("RUNIC_EXAMPLE_SET", "RAX_EXAMPLE_SET", null)]);
        foreach (string argument in QuietBuilds)
        {
            ProcessResult result = Build(temporary, argument);
            Assert.Equal(0, result.ExitCode, argument + "\n" + result.Combined);
        }
    }

    // A project that sets old names of every kind (RunicExampleProperty, the RunicExampleItem item, a direct and, as NuGet
    // resolves it, a transitive package reference, and RUNIC_EXAMPLE_SET) and imports the guard file of the given lists twice.
    private static TemporaryDirectory Fixture(
        (string Old, string New, string? Kind)[] packages, (string Old, string New, string? Kind)[] msbuild, (string Old, string New, string? Kind)[] environment)
    {
        TemporaryDirectory temporary = new();
        JsonObject input = JsonNode.Parse(File.ReadAllText(Input))!.AsObject();
        JsonObject family = input["family"]!.AsObject();
        family["root"] = temporary.Path;
        family["outputs"] = new JsonArray("first/Runic.Translations.RenamedNames.targets", "second/Runic.Translations.RenamedNames.targets");
        static JsonArray List((string Old, string New, string? Kind)[] entries) => new(entries.Select(entry =>
        {
            JsonObject item = new() { ["old"] = entry.Old, ["new"] = entry.New };
            if (entry.Kind is not null) item["kind"] = entry.Kind;
            return (JsonNode)item;
        }).ToArray());
        input["packages"] = List(packages);
        input["msbuild"] = List(msbuild);
        input["environment"] = List(environment);
        Directory.CreateDirectory(temporary.Resolve("first"));
        Directory.CreateDirectory(temporary.Resolve("second"));
        File.WriteAllText(temporary.Resolve("old-names.json"), input.ToJsonString(), new UTF8Encoding(false));
        ProcessResult generated = Run(temporary.Path, "bun", [Generator, "--input", temporary.Resolve("old-names.json")]);
        Assert.Equal(0, generated.ExitCode, generated.Combined);
        File.WriteAllText(temporary.Resolve("App.proj"), """
            <Project>
              <PropertyGroup>
                <RunicExampleProperty>old</RunicExampleProperty>
              </PropertyGroup>
              <ItemGroup>
                <RunicExampleItem Include="one;two" />
                <PackageReference Include="runic.example.direct" Version="1.0.0" />
                <PackageDependencies Include="Runic.Example.Transitive" />
              </ItemGroup>
              <Import Project="first/Runic.Translations.RenamedNames.targets" Condition="'$(_RunicTranslationsRenamedNamesImported)' != 'true'" />
              <Import Project="second/Runic.Translations.RenamedNames.targets" Condition="'$(_RunicTranslationsRenamedNamesImported)' != 'true'" />
              <Target Name="PrepareForBuild" />
              <Target Name="ResolvePackageAssets" />
            </Project>
            """, new UTF8Encoding(false));
        return temporary;
    }

    private static ProcessResult Build(TemporaryDirectory temporary, params string[] arguments) => Run(temporary.Path, "dotnet",
        ["msbuild", "App.proj", "-t:PrepareForBuild;ResolvePackageAssets", "-nologo", "-noAutoResponse", "-nodeReuse:false", "-v:m", "-clp:NoSummary", .. arguments],
        new Dictionary<string, string> { ["RUNIC_EXAMPLE_SET"] = "1" });

    private static ProcessResult Run(string directory, string command, string[] arguments, IReadOnlyDictionary<string, string>? environment = null)
    {
        ProcessStartInfo start = new(command)
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_NOLOGO"] = "1";
        foreach ((string name, string value) in environment ?? new Dictionary<string, string>())
            start.Environment[name] = value;
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {command}.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{command} did not finish: {string.Join(' ', arguments)}");
        }

        Task.WaitAll(output, error);
        return new ProcessResult(process.ExitCode, output.Result, error.Result);
    }
}
