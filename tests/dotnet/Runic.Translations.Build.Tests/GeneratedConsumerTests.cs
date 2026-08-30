using System;
using System.IO;
using System.Text;

namespace Runic.Translations.Build.Tests;

internal static class GeneratedConsumerTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("generated C# compiles and runs against the runtime", GeneratedCodeCompilesAndRuns);
    }

    private static void GeneratedCodeCompilesAndRuns()
    {
        using TemporaryDirectory temporary = new();
        TestFixture.CopyMinimal(temporary);
        ProcessResult generation = TestFixture.Generate(temporary, "Generated");
        Assert.Equal(0, generation.ExitCode, generation.Combined);

        string runtimeProject = XmlPath(RepositoryPaths.Resolve(
            "dotnet",
            "src",
            "Runic.Translations",
            "Runic.Translations.csproj"));
        string project = $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RestorePackagesPath>{{XmlPath(RepositoryPaths.Resolve(".packages"))}}</RestorePackagesPath>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{runtimeProject}}" />
              </ItemGroup>
            </Project>
            """;
        File.WriteAllText(temporary.Resolve("GeneratedConsumer.csproj"), project, new UTF8Encoding(false));
        File.WriteAllText(
            temporary.Resolve("Program.cs"),
            "using Runic.Translations.Examples.Localization;\n" +
            "Runic.Translations.ITranslationManager manager = await MinimalTextCatalog.CreateManagerAsync();\n" +
            "var text = new MinimalText(manager);\n" +
            "System.Console.Write(text.Hello);\n",
            new UTF8Encoding(false));

        ProcessResult build = Processes.DotNet(temporary.Path, "build", "GeneratedConsumer.csproj", "/nologo", "/v:minimal");
        Assert.Equal(0, build.ExitCode, build.Combined);
        ProcessResult run = Processes.DotNet(temporary.Path, "run", "--project", "GeneratedConsumer.csproj", "--no-build");
        Assert.Equal(0, run.ExitCode, run.Combined);
        Assert.Equal("Hello", run.StandardOutput.Trim());
    }

    private static string XmlPath(string path) => path.Replace("&", "&amp;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal);
}
