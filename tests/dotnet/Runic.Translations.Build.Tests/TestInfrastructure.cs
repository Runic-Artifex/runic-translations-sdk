using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Runic.Translations.Build.Tests;

internal static class RepositoryPaths
{
    private static readonly Lazy<string> RootValue = new(FindRoot);

    public static string Root => RootValue.Value;

    public static string Resolve(params string[] segments)
    {
        string path = Root;
        foreach (string segment in segments)
        {
            path = Path.Combine(path, segment);
        }

        return path;
    }

    public static string ToolAssembly
    {
        get
        {
            string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Debug";
            return Resolve("dotnet", "tools", "dotnet-runic-translations", "bin", configuration, "net10.0", "dotnet-runic-translations.dll");
        }
    }

    private static string FindRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "global.json")) &&
                Directory.Exists(Path.Combine(current.FullName, "spec", "schemas")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"runic-translations-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Resolve(params string[] segments)
    {
        string result = Path;
        foreach (string segment in segments)
        {
            result = System.IO.Path.Combine(result, segment);
        }

        return result;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public string Combined => StandardOutput + Environment.NewLine + StandardError;
}

internal static class Processes
{
    public static ProcessResult DotNet(string workingDirectory, params string[] arguments)
    {
        ProcessStartInfo start = new("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_NOLOGO"] = "1";
        start.Environment["NUGET_XMLDOC_MODE"] = "skip";
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start dotnet.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"dotnet did not finish: {string.Join(' ', arguments)}");
        }

        Task.WaitAll(output, error);
        return new ProcessResult(process.ExitCode, output.Result, error.Result);
    }
}

internal static class TestFixture
{
    public static ProcessResult RunTool(TemporaryDirectory temporary, params string[] arguments)
    {
        Assert.True(File.Exists(RepositoryPaths.ToolAssembly), $"Tool assembly is missing: {RepositoryPaths.ToolAssembly}");
        string[] all = new string[arguments.Length + 1];
        all[0] = RepositoryPaths.ToolAssembly;
        Array.Copy(arguments, 0, all, 1, arguments.Length);
        return Processes.DotNet(temporary.Path, all);
    }

    public static string[] RelativeFiles(string directory) => Directory
        .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
        .Select(path => Path.GetRelativePath(directory, path).Replace('\\', '/'))
        .Order(StringComparer.Ordinal)
        .ToArray();

    public static Dictionary<string, (long Length, DateTime LastWriteUtc)> SnapshotFiles(string root) => Directory
        .EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .ToDictionary(
            path => Path.GetRelativePath(root, path).Replace('\\', '/'),
            path => (new FileInfo(path).Length, File.GetLastWriteTimeUtc(path)),
            StringComparer.Ordinal);
}
