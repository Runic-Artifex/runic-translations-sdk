using System;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace Runic.Translations.Build.Tests;

internal static class XamlBuildTests
{
    public static void Register(TestRunner runner, string? feed = null, string? version = null, bool wpf = false)
    {
        runner.Add("XAML items include WPF pages/application and catalog metadata", ItemWiring);
        runner.Add("XAML consumer builds valid bindings and fails for changed keys/inputs/kinds", () => ConsumerBuild(feed, version, wpf));
    }

    private static string Xml(string path) => new XAttribute("p", path).ToString()[3..^1];
    private static string PackageDirectory => RepositoryPaths.Resolve("packages", "dotnet", "Runic.Translations.Build");
    private static string Configuration => new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;

    private static void ItemWiring()
    {
        using TemporaryDirectory temporary = new();
        File.WriteAllText(temporary.Resolve("Probe.proj"), $$"""
            <Project>
              <Import Project="{{Xml(Path.Combine(PackageDirectory, "build", "Runic.Translations.Build.props"))}}" />
              <PropertyGroup><UseWPF>true</UseWPF><TranslationsXamlCatalog>app</TranslationsXamlCatalog></PropertyGroup>
              <ItemGroup><Page Include="View.xaml"/><ApplicationDefinition Include="App.xaml"/><TranslationXaml Include="Declared.xaml" Catalog="other"/></ItemGroup>
              <Import Project="{{Xml(Path.Combine(PackageDirectory, "build", "Runic.Translations.Build.targets"))}}" />
              <Target Name="Dump" DependsOnTargets="_RunicTranslationsDiscoverTranslationSources">
                <WriteLinesToFile File="dump.txt" Overwrite="true" Lines="@(AdditionalFiles->'%(Filename)|%(RunicTranslationKind)|%(RunicTranslationCatalog)|%(RunicTranslationDefaultCatalog)')"/>
              </Target>
            </Project>
            """);
        ProcessResult result = Processes.DotNet(temporary.Path, "msbuild", "Probe.proj", "/t:Dump", "/nologo");
        Assert.Equal(0, result.ExitCode, result.Combined);
        string dump = File.ReadAllText(temporary.Resolve("dump.txt"));
        Assert.Contains("View|Xaml||app", dump); Assert.Contains("App|Xaml||app", dump); Assert.Contains("Declared|Xaml|other|app", dump);
        result = Processes.DotNet(temporary.Path, "msbuild", "Probe.proj", "/t:Dump", "/p:TranslationsValidateXaml=false", "/nologo");
        Assert.Equal(0, result.ExitCode, result.Combined);
        Assert.Equal(string.Empty, File.ReadAllText(temporary.Resolve("dump.txt")), "disabled validator");
    }

    private static void ConsumerBuild(string? feed, string? version, bool wpf)
    {
        using TemporaryDirectory temporary = new();
        Directory.CreateDirectory(temporary.Resolve("translations"));
        File.WriteAllText(temporary.Resolve("translations", "runic.json"), """
            {"schemaVersion":1,"catalog":"app","code":{"namespace":"Consumer","className":"AppText"},"baseLocale":"en"}
            """);
        File.WriteAllText(temporary.Resolve("translations", "en.rmf2"), "title = App\ngreeting = Hi {$name}\nhelp = {#link ref=guide}Guide{/link}\n");
        File.WriteAllText(temporary.Resolve("Program.cs"), "using Consumer; internal static class Program { static void Main() { _ = typeof(AppText); } }\n");
        string wpfReference = wpf ? "<Reference Include=\"Runic.Translations.Wpf\" HintPath=\"" + Xml(RepositoryPaths.Resolve("packages", "dotnet", "Runic.Translations.Wpf", "bin", Configuration, "net10.0-windows", "Runic.Translations.Wpf.dll")) + "\"/>" : string.Empty;
        string references = feed is null ? $$"""
            <Import Project="{{Xml(Path.Combine(PackageDirectory, "build", "Runic.Translations.Build.props"))}}"/>
            <ItemGroup>
              {{wpfReference}}
              <Reference Include="Runic.Translations" HintPath="{{Xml(RepositoryPaths.Resolve("packages", "dotnet", "Runic.Translations", "bin", Configuration, "net10.0", "Runic.Translations.dll"))}}"/>
              <Analyzer Include="{{Xml(Path.Combine(PackageDirectory, "bin", Configuration, "net10.0", "Runic.Translations.Generator.dll"))}}"/>
              <Analyzer Include="{{Xml(Path.Combine(PackageDirectory, "bin", Configuration, "net10.0", "Runic.Translations.Compiler.dll"))}}"/>
            </ItemGroup>
            <Import Project="{{Xml(Path.Combine(PackageDirectory, "build", "Runic.Translations.Build.targets"))}}"/>
            """ : $$"""
            <ItemGroup><PackageReference Include="Runic.Translations" Version="[{{version}}]"/><PackageReference Include="Runic.Translations.Build" Version="[{{version}}]"/></ItemGroup>
            """;
        string framework = wpf ? "net10.0-windows" : "net10.0";
        string wpfProperties = wpf ? "<UseWPF>true</UseWPF><EnableWindowsTargeting>true</EnableWindowsTargeting>" : string.Empty;
        string xamlItem = wpf ? string.Empty : "<TranslationXaml Include=\"View.xaml\"/>";
        File.WriteAllText(temporary.Resolve("Consumer.csproj"), $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>{{framework}}</TargetFramework>{{wpfProperties}}<OutputType>Exe</OutputType><TreatWarningsAsErrors>true</TreatWarningsAsErrors><TranslationsXamlCatalog>app</TranslationsXamlCatalog></PropertyGroup>
              <ItemGroup>{{xamlItem}}</ItemGroup>
              {{references}}
            </Project>
            """);
        if (feed is not null)
            File.WriteAllText(temporary.Resolve("NuGet.config"), $$"""
                <configuration><packageSources><clear/><add key="candidate" value="{{Xml(feed)}}"/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources><packageSourceMapping><packageSource key="candidate"><package pattern="Runic.Translations*"/></packageSource><packageSource key="nuget.org"><package pattern="*"/></packageSource></packageSourceMapping></configuration>
                """);
        WriteView("{rt:Message greeting, Arg0={Binding Name, FallbackValue='Doe, Ada'}}");
        ProcessResult valid = Build(); Assert.Equal(0, valid.ExitCode, valid.Combined);
        foreach ((string value, string diagnostic) in new[] { ("{rt:Message greetng, Arg0={Binding Name}}", "RTR0081"), ("{rt:Message greetng, Arg0={Binding Source={StaticResource User}, Path=Name}}", "RTR0081"), ("{rt:Message greeting}", "RTR0082"), ("{rt:Message help}", "RTR0083") })
        {
            WriteView(value); ProcessResult invalid = Build(noRestore: true);
            Assert.True(invalid.ExitCode != 0, "Invalid XAML compiled: " + value); Assert.Contains(diagnostic, invalid.Combined); Assert.Contains("View.xaml(1,", invalid.Combined);
        }
        WriteView("{rt:Message external, Source={StaticResource External}, Arg0={Binding Name}}");
        ProcessResult external = Build(noRestore: true); Assert.Equal(0, external.ExitCode, external.Combined);
        WriteView("{rt:Message external}", """<TextBlock.Style><Style TargetType="TextBlock"><Setter Property="{x:Static Member='rt:TranslationProperties.SourceProperty'}" Value="{StaticResource External}"/></Style></TextBlock.Style>""");
        ProcessResult styledExternal = Build(noRestore: true); Assert.Equal(0, styledExternal.ExitCode, styledExternal.Combined);
        WriteView("{rt:Message typo}");
        ProcessResult disabled = Processes.DotNet(temporary.Path, "build", "Consumer.csproj", "--no-restore", "/p:TranslationsValidateXaml=false", "/nologo");
        Assert.Equal(0, disabled.ExitCode, disabled.Combined);
        // A per-file assertion checks even explicit Source declarations.
        string project = File.ReadAllText(temporary.Resolve("Consumer.csproj"));
        File.WriteAllText(temporary.Resolve("Consumer.csproj"), project.Replace("</Project>", "<ItemGroup><TranslationXaml Include=\"View.xaml\" Catalog=\"app\"/></ItemGroup></Project>", StringComparison.Ordinal).Replace("<TranslationXaml Include=\"View.xaml\"/>", string.Empty, StringComparison.Ordinal));
        WriteView("{rt:Message external, Source={StaticResource App}}");
        ProcessResult asserted = Build(noRestore: true); Assert.True(asserted.ExitCode != 0, asserted.Combined); Assert.Contains("RTR0081", asserted.Combined);
        if (feed is not null) Assert.False(File.ReadAllText(temporary.Resolve("obj", "project.assets.json")).Contains("\"type\": \"project\"", StringComparison.Ordinal), "Package consumer has a project reference.");

        void WriteView(string value, string? content = null) => File.WriteAllText(temporary.Resolve("View.xaml"), "<TextBlock xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:rt=\"clr-namespace:Runic.Translations.Wpf;assembly=Runic.Translations.Wpf\" Text=\"" + value + "\"" + (content is null ? "/>" : ">" + content + "</TextBlock>"), new UTF8Encoding(false));
        ProcessResult Build(bool noRestore = false) => Processes.DotNet(temporary.Path, noRestore ? ["build", "Consumer.csproj", "--no-restore", "/nologo"] : ["build", "Consumer.csproj", "/nologo"]);
    }
}
