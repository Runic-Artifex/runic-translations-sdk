using System;
using System.IO;

namespace RunicTranslations.Compiler.Tests;

internal static class RepositoryPaths
{
    private static readonly Lazy<string> RepositoryRootValue = new(FindRepositoryRoot);

    public static string RepositoryRoot => RepositoryRootValue.Value;

    public static string Resolve(params string[] segments)
    {
        string path = RepositoryRoot;
        foreach (string segment in segments)
        {
            path = Path.Combine(path, segment);
        }

        return path;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")) &&
                Directory.Exists(Path.Combine(directory.FullName, "spec")) &&
                Directory.Exists(Path.Combine(directory.FullName, "dotnet", "src")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate the repository root above '{AppContext.BaseDirectory}'.");
    }
}
