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
