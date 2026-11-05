using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TUnit.Core;

namespace Runic.Translations.Build.Tests;

// The version rule's dependency range (T1g), run through MSBuild with the shipped Pack.targets: [N, next minor-0) before
// 1.0 and [N, next major-0) from 1.0, written into the .nupkg, .snupkg and .nuspec of a pack.
internal sealed class RuntimeDependencyRangeTests
{
    [Test, DisplayName("packing stamps the Runic.Translations range into the nupkg, snupkg and nuspec and warns about other formats once")]
    public void StampsPackOutputs()
    {
        using TemporaryDirectory temporary = new();
        string[] versions = ["0.9.0", "0.9.0-preview.1", "1.2.0", "0.9.0.1", "[0.8.0, 0.9.0)", "0.9.*"];
        string nuspec = Nuspec(versions);
        WritePackage(temporary.Resolve("Library.1.0.0.nupkg"), nuspec);
        WritePackage(temporary.Resolve("Library.1.0.0.snupkg"), nuspec);
        File.WriteAllText(temporary.Resolve("Library.nuspec"), nuspec, new UTF8Encoding(false));
        string targets = XmlPath(RepositoryPaths.Resolve("packages", "dotnet", "Runic.Translations.Build", "build", "Runic.Translations.Build.Pack.targets"));
        File.WriteAllText(temporary.Resolve("Probe.proj"), $$"""
            <Project>
              <Import Project="{{targets}}" />
              <ItemGroup>
                <NuGetPackOutput Include="Library.1.0.0.nupkg;Library.1.0.0.snupkg;Library.nuspec" />
              </ItemGroup>
              <Target Name="GenerateNuspec" />
            </Project>
            """, new UTF8Encoding(false));

        ProcessResult result = Processes.DotNet(temporary.Path, "msbuild", "Probe.proj", "/nologo", "/t:GenerateNuspec", "/v:minimal",
            "/p:_RunicTranslationsPackTaskAssembly=" + BuildTaskAssembly());
        Assert.Equal(0, result.ExitCode, result.Combined);
        Assert.Equal(1, Regex.Count(result.Combined, "warning RTR0093"), "one warning for the floating version: " + result.Combined);
        Assert.Contains("'0.9.*'", result.Combined);

        string expected = string.Join("|", "[0.9.0, 0.10.0-0)", "[0.9.0-preview.1, 0.10.0-0)", "[1.2.0, 2.0.0-0)", "[0.9.0.1, 0.10.0-0)",
            "[0.8.0, 0.9.0)", "0.9.*");
        Assert.Equal(expected, Ranges(ReadPackage(temporary.Resolve("Library.1.0.0.nupkg"))), "nupkg");
        Assert.Equal(expected, Ranges(ReadPackage(temporary.Resolve("Library.1.0.0.snupkg"))), "snupkg");
        Assert.Equal(expected, Ranges(File.ReadAllText(temporary.Resolve("Library.nuspec"), Encoding.UTF8)), "nuspec file");
    }

    [Test, DisplayName("RunicTranslationsStampRuntimeRange=false keeps the open range")]
    public void StampCanBeDisabled()
    {
        using TemporaryDirectory temporary = new();
        File.WriteAllText(temporary.Resolve("Library.nuspec"), Nuspec(["0.9.0"]), new UTF8Encoding(false));
        string targets = XmlPath(RepositoryPaths.Resolve("packages", "dotnet", "Runic.Translations.Build", "build", "Runic.Translations.Build.Pack.targets"));
        File.WriteAllText(temporary.Resolve("Probe.proj"), $$"""
            <Project>
              <Import Project="{{targets}}" />
              <ItemGroup><NuGetPackOutput Include="Library.nuspec" /></ItemGroup>
              <Target Name="GenerateNuspec" />
            </Project>
            """, new UTF8Encoding(false));
        ProcessResult result = Processes.DotNet(temporary.Path, "msbuild", "Probe.proj", "/nologo", "/t:GenerateNuspec", "/v:minimal",
            "/p:_RunicTranslationsPackTaskAssembly=" + BuildTaskAssembly(), "/p:RunicTranslationsStampRuntimeRange=false");
        Assert.Equal(0, result.ExitCode, result.Combined);
        Assert.Equal("0.9.0", Ranges(File.ReadAllText(temporary.Resolve("Library.nuspec"), Encoding.UTF8)), "unchanged");
    }

    [Test, DisplayName("a package the range cannot be written into fails the pack with RTR0089")]
    public void UnwritablePackageFails()
    {
        using TemporaryDirectory temporary = new();
        // Not a zip archive, so the package cannot be opened for update.
        File.WriteAllText(temporary.Resolve("Library.1.0.0.nupkg"), "not a package", new UTF8Encoding(false));
        string targets = XmlPath(RepositoryPaths.Resolve("packages", "dotnet", "Runic.Translations.Build", "build", "Runic.Translations.Build.Pack.targets"));
        File.WriteAllText(temporary.Resolve("Probe.proj"), $$"""
            <Project>
              <Import Project="{{targets}}" />
              <ItemGroup><NuGetPackOutput Include="Library.1.0.0.nupkg" /></ItemGroup>
              <Target Name="GenerateNuspec" />
            </Project>
            """, new UTF8Encoding(false));
        ProcessResult result = Processes.DotNet(temporary.Path, "msbuild", "Probe.proj", "/nologo", "/t:GenerateNuspec", "/v:minimal",
            "/p:_RunicTranslationsPackTaskAssembly=" + BuildTaskAssembly());
        Assert.True(result.ExitCode != 0, "the pack fails: " + result.Combined);
        Assert.Equal(1, Regex.Count(result.Combined, "error RTR0089"), "one RTR0089 error: " + result.Combined);
    }

    [Test, DisplayName("packing removes the dependency on Runic.Translations.Build, also with the range stamp off")]
    [Arguments(true)]
    [Arguments(false)]
    public void RemovesBuildDependency(bool stampRange)
    {
        using TemporaryDirectory temporary = new();
        File.WriteAllText(temporary.Resolve("Library.nuspec"),
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<package xmlns=\"http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd\"><metadata><id>Library</id><version>1.0.0</version><dependencies>" +
            "<group targetFramework=\"net10.0\">\n  <dependency id=\"Runic.Translations\" version=\"0.9.0\" exclude=\"Build,Analyzers\" />\n" +
            "  <dependency id=\"Runic.Translations.Build\" version=\"0.9.0\" exclude=\"Build,Analyzers\" />\n</group></dependencies></metadata></package>", new UTF8Encoding(false));
        string targets = XmlPath(RepositoryPaths.Resolve("packages", "dotnet", "Runic.Translations.Build", "build", "Runic.Translations.Build.Pack.targets"));
        File.WriteAllText(temporary.Resolve("Probe.proj"), $$"""
            <Project>
              <Import Project="{{targets}}" />
              <ItemGroup><NuGetPackOutput Include="Library.nuspec" /></ItemGroup>
              <Target Name="GenerateNuspec" />
            </Project>
            """, new UTF8Encoding(false));
        ProcessResult result = Processes.DotNet(temporary.Path, "msbuild", "Probe.proj", "/nologo", "/t:GenerateNuspec", "/v:minimal",
            "/p:_RunicTranslationsPackTaskAssembly=" + BuildTaskAssembly(), "/p:RunicTranslationsStampRuntimeRange=" + (stampRange ? "true" : "false"));
        Assert.Equal(0, result.ExitCode, result.Combined);
        string nuspec = File.ReadAllText(temporary.Resolve("Library.nuspec"), Encoding.UTF8);
        Assert.Equal(stampRange ? "[0.9.0, 0.10.0-0)" : "0.9.0", Ranges(nuspec), "range");
        Assert.True(!nuspec.Contains("Runic.Translations.Build", StringComparison.Ordinal), nuspec);
    }

    [Test, DisplayName("RunicTranslationsPackCatalog packs runic/<catalog>/ and buildTransitive props, and checks the catalog ID")]
    public void PacksCatalog()
    {
        using TemporaryDirectory temporary = new();
        Directory.CreateDirectory(temporary.Resolve("translations", "text"));
        Directory.CreateDirectory(temporary.Resolve("translations", "overrides", "other"));
        void Project(string catalog) => File.WriteAllText(temporary.Resolve("translations", "runic.json"),
            "{ \"schemaVersion\": 1, \"catalog\": \"" + catalog + "\", \"code\": { \"namespace\": \"Acme.Grid\", \"className\": \"GridText\" }, \"baseLocale\": \"en\", " +
            "\"sourceRoots\": [{ \"path\": \"text\", \"namespace\": [\"grid\"] }], \"overrides\": [{ \"catalog\": \"other\", \"from\": \"Other\" }] }", new UTF8Encoding(false));
        File.WriteAllText(temporary.Resolve("translations", "text", "en.rmf2"), "empty = No rows\n", new UTF8Encoding(false));
        File.WriteAllText(temporary.Resolve("translations", "text", "de.rmf2"), "empty = Keine Zeilen\n", new UTF8Encoding(false));
        File.WriteAllText(temporary.Resolve("translations", "overrides", "other", "en.rmf2"), "title = Other\n", new UTF8Encoding(false));
        string targets = XmlPath(RepositoryPaths.Resolve("packages", "dotnet", "Runic.Translations.Build", "build", "Runic.Translations.Build.Pack.targets"));
        ProcessResult Pack(string catalog, string package, string ownProps = "")
        {
            Project(catalog);
            File.WriteAllText(temporary.Resolve("Probe.proj"), $$"""
                <Project>
                  <PropertyGroup><BaseIntermediateOutputPath>obj/</BaseIntermediateOutputPath><RunicTranslationsPackCatalog>true</RunicTranslationsPackCatalog></PropertyGroup>
                  <ItemGroup>{{ownProps}}</ItemGroup>
                  <Import Project="{{targets}}" />
                  <Target Name="_GetPackageFiles">
                    <Message Importance="high" Text="PACKED %(None.PackagePath)" Condition="'%(None.Pack)' == 'true'" />
                  </Target>
                </Project>
                """, new UTF8Encoding(false));
            return Processes.DotNet(temporary.Path, "msbuild", "Probe.proj", "/nologo", "/t:_GetPackageFiles", "/v:minimal",
                "/p:_RunicTranslationsPackTaskAssembly=" + BuildTaskAssembly(), "/p:PackageId=" + package);
        }

        ProcessResult packed = Pack("acme.grid", "Acme.Grid");
        Assert.Equal(0, packed.ExitCode, packed.Combined);
        string paths = string.Join("|", Regex.Matches(packed.Combined, "PACKED (\\S+)").Select(static match => match.Groups[1].Value).Order(StringComparer.Ordinal));
        Assert.Equal("buildTransitive/Acme.Grid.props|runic/acme.grid/catalog.props|runic/acme.grid/contract.json|runic/acme.grid/runic.json|runic/acme.grid/text/de.rmf2|runic/acme.grid/text/en.rmf2",
            paths, "packed files, without the library's own overrides");
        Assert.True(!packed.Combined.Contains("RTR0105", StringComparison.Ordinal), packed.Combined);
        string contract = File.ReadAllText(temporary.Resolve("obj", "runic-pack", "acme.grid", "contract.json"), Encoding.UTF8);
        Assert.True(contract.Contains("\"locale\":\"en\"", StringComparison.Ordinal) && contract.Contains("\"grid_empty\"", StringComparison.Ordinal), contract);
        string props = File.ReadAllText(temporary.Resolve("obj", "runic-pack", "acme.grid", "catalog.props"), Encoding.UTF8);
        Assert.True(props.Contains("Catalog=\"acme.grid\" Package=\"Acme.Grid\"", StringComparison.Ordinal), props);

        ProcessResult renamed = Pack("grid", "Acme.Grid");
        Assert.Equal(0, renamed.ExitCode, renamed.Combined);
        Assert.Equal(1, Regex.Count(renamed.Combined, "warning RTR0105"), renamed.Combined);

        ProcessResult reserved = Pack("runic-grid", "Acme.Grid");
        Assert.True(reserved.ExitCode != 0 && reserved.Combined.Contains("error RTR0106", StringComparison.Ordinal), reserved.Combined);
        ProcessResult firstParty = Pack("runic-grid", "Runic.Grid");
        Assert.Equal(0, firstParty.ExitCode, firstParty.Combined);
        Assert.True(!firstParty.Combined.Contains("RTR0105", StringComparison.Ordinal), "first-party catalogs keep their runic- ID: " + firstParty.Combined);

        // NuGet imports buildTransitive/<id>.props instead of build/<id>.props, so the generated props import the library's own.
        string generated = temporary.Resolve("obj", "runic-pack", "buildTransitive", "Acme.Grid.props");
        Assert.True(!File.ReadAllText(generated, Encoding.UTF8).Contains("../build/", StringComparison.Ordinal), "no build props to import");
        ProcessResult importsBuild = Pack("acme.grid", "Acme.Grid", "<None Include=\"Acme.Grid.props\" Pack=\"true\" PackagePath=\"build/\" />");
        Assert.Equal(0, importsBuild.ExitCode, importsBuild.Combined);
        string transitive = File.ReadAllText(generated, Encoding.UTF8);
        Assert.True(transitive.Contains("<Import Project=\"$(MSBuildThisFileDirectory)../build/Acme.Grid.props\"", StringComparison.Ordinal) &&
            transitive.IndexOf("../build/", StringComparison.Ordinal) < transitive.IndexOf("catalog.props", StringComparison.Ordinal), transitive);
        ProcessResult importsNamed = Pack("acme.grid", "Acme.Grid", "<None Include=\"Acme.Grid.props\" Pack=\"true\" PackagePath=\"build\\Acme.Grid.props\" />");
        Assert.Equal(0, importsNamed.ExitCode, importsNamed.Combined);
        Assert.True(File.ReadAllText(generated, Encoding.UTF8).Contains("../build/Acme.Grid.props", StringComparison.Ordinal), "a file PackagePath");
        // A library's own buildTransitive/<id>.props would collide with the generated one.
        ProcessResult collides = Pack("acme.grid", "Acme.Grid", "<None Include=\"Acme.Grid.props\" Pack=\"true\" PackagePath=\"buildTransitive\" />");
        Assert.True(collides.ExitCode != 0 && collides.Combined.Contains("error RTR0104", StringComparison.Ordinal) &&
            collides.Combined.Contains("RunicTranslationsPackCatalogProps", StringComparison.Ordinal), collides.Combined);
    }

    private static string Nuspec(string[] versions)
    {
        var builder = new StringBuilder("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<package xmlns=\"http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd\"><metadata><id>Library</id><version>1.0.0</version><dependencies>");
        for (int index = 0; index < versions.Length; index++)
            builder.Append("<group targetFramework=\"net10.0").Append(index == 0 ? "" : "-g" + index.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append("\"><dependency id=\"Runic.Translations\" version=\"").Append(versions[index]).Append("\" exclude=\"Build,Analyzers\" /></group>");
        return builder.Append("</dependencies></metadata></package>").ToString();
    }

    private static string Ranges(string nuspec) =>
        string.Join("|", Regex.Matches(nuspec, "id=\"Runic.Translations\" version=\"([^\"]*)\"").Select(static match => match.Groups[1].Value));

    private static void WritePackage(string path, string nuspec)
    {
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(archive.CreateEntry("Library.nuspec").Open(), new UTF8Encoding(false))) writer.Write(nuspec);
        using (var writer = new StreamWriter(archive.CreateEntry("lib/net10.0/readme.txt").Open(), new UTF8Encoding(false))) writer.Write("x");
    }

    private static string ReadPackage(string path)
    {
        using ZipArchive archive = ZipFile.OpenRead(path);
        Assert.Equal(2, archive.Entries.Count, "the package keeps its entries");
        using var reader = new StreamReader(archive.GetEntry("Library.nuspec")!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    // The Build task assembly of the configuration these tests were built in (bin/<configuration>/net10.0).
    private static string BuildTaskAssembly()
    {
        string configuration = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).Parent!.Name;
        string path = RepositoryPaths.Resolve("packages", "dotnet", "Runic.Translations.Build", "bin", configuration, "net10.0", "Runic.Translations.Build.dll");
        Assert.True(File.Exists(path), "Build task assembly missing: " + path);
        return path;
    }

    private static string XmlPath(string path) => path.Replace("&", "&amp;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal);
}
