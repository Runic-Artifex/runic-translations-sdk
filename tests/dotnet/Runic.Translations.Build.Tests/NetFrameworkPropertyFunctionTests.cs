using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TUnit.Core;

namespace Runic.Translations.Build.Tests;

// runic-sdk#1299: Visual Studio's MSBuild.exe runs on .NET Framework 4.8, whose property functions bind only to the
// methods .NET Framework has. A call to a newer overload builds with `dotnet build` and fails in Visual Studio with
// MSB4186, so the MSBuild files the packages ship use the [MSBuild] path functions instead.
internal sealed class NetFrameworkPropertyFunctionTests
{
    // Static methods that .NET Framework 4.8 lacks, or has only with fewer parameters. MaxArguments is the largest
    // argument count it has; null means it has no overload at all.
    private static readonly (string Type, string Method, int? MaxArguments, string Replacement)[] Unavailable =
    [
        ("System.IO.Path", "GetFullPath", 1, "[MSBuild]::NormalizePath(base, path)"),
        ("System.IO.Path", "GetRelativePath", null, "[MSBuild]::MakeRelative(base, path)"),
        ("System.IO.Path", "TrimEndingDirectorySeparator", null, "[MSBuild]::NormalizePath(base, path, '.')"),
        ("System.IO.Path", "EndsInDirectorySeparator", null, "[MSBuild]::EnsureTrailingSlash or a string comparison"),
        ("System.IO.Path", "IsPathFullyQualified", null, "[System.IO.Path]::IsPathRooted"),
        ("System.IO.Path", "Join", null, "[MSBuild]::NormalizePath or [System.IO.Path]::Combine"),
        ("System.IO.Path", "Exists", null, "Exists()"),
        ("System.Math", "Clamp", null, "[MSBuild] arithmetic with conditions"),
        ("System.Convert", "ToHexString", null, "a task"),
        ("System.Convert", "FromHexString", null, "a task"),
    ];

    [Test, DisplayName("the shipped MSBuild files use only static property functions .NET Framework's MSBuild has")]
    public void ShippedFilesAvoidNetCoreOnlyFunctions()
    {
        string packages = RepositoryPaths.Resolve("packages");
        string[] files = Directory.EnumerateFiles(packages, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".props", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".targets", StringComparison.OrdinalIgnoreCase))
            .Where(path => !Path.GetRelativePath(packages, path).Split(Path.DirectorySeparatorChar)
                .Any(segment => segment is "bin" or "obj" or "node_modules"))
            .Order(StringComparer.Ordinal)
            .ToArray();
        foreach (string shipped in new[]
                 {
                     "build/Runic.Translations.Build.props", "build/Runic.Translations.Build.targets",
                     "build/Runic.Translations.Build.Pack.targets", "build/Runic.Translations.Build.Locales.targets",
                     "build/Runic.Translations.RenamedNames.targets",
                     "buildMultiTargeting/Runic.Translations.Build.targets",
                 })
        {
            string path = RepositoryPaths.Resolve(["packages", "dotnet", "Runic.Translations.Build", .. shipped.Split('/')]);
            Assert.True(files.Contains(path), $"scanned {shipped}");
        }

        List<string> violations = [];
        foreach (string file in files)
        {
            violations.AddRange(Find(File.ReadAllText(file, Encoding.UTF8)).Select(found => $"{Path.GetRelativePath(packages, file)}:{found}"));
        }

        Assert.True(violations.Count == 0, "MSBuild.exe on .NET Framework fails with MSB4186 on:\n" + string.Join("\n", violations));
    }

    [Test, DisplayName("the check finds a two-argument GetFullPath and GetRelativePath and accepts the one-argument GetFullPath")]
    public void FindsUnavailableCalls()
    {
        string text = """
            <A>$([System.IO.Path]::GetFullPath('$(MSBuildProjectDirectory)'))</A>
            <B>$([System.IO.Path]::GetFullPath('$(X)a(b)', '$(MSBuildProjectDirectory)'))</B>
            <C>$([system.io.path]::GetRelativePath(`$(A)`, '$(B)'))</C>
            <D>$([System.IO.Path]::GetFullPath($([System.IO.Path]::Combine('a,b', 'c'))))</D>
            <E>$([MSBuild]::NormalizePath('$(A)', 'b'))</E>
            """;
        string[] found = Find(text).ToArray();
        Assert.Equal(2, found.Length, string.Join("\n", found));
        Assert.True(found[0].StartsWith("2: [System.IO.Path]::GetFullPath with 2 arguments", StringComparison.Ordinal), found[0]);
        Assert.True(found[1].StartsWith("3: [System.IO.Path]::GetRelativePath with 2 arguments", StringComparison.Ordinal), found[1]);
    }

    private static IEnumerable<string> Find(string text)
    {
        foreach ((string type, string method, int? maxArguments, string replacement) in Unavailable)
        {
            string call = $"[{type}]::{method}(";
            for (int index = text.IndexOf(call, StringComparison.OrdinalIgnoreCase); index >= 0;
                 index = text.IndexOf(call, index + call.Length, StringComparison.OrdinalIgnoreCase))
            {
                int arguments = CountArguments(text, index + call.Length);
                if (maxArguments is null || arguments > maxArguments)
                {
                    int line = 1 + text.AsSpan(0, index).Count('\n');
                    yield return $"{line}: [{type}]::{method} with {arguments} arguments; use {replacement}";
                }
            }
        }
    }

    // Counts the top-level arguments of the call whose argument list starts at start, skipping quoted strings and
    // nested calls.
    private static int CountArguments(string text, int start)
    {
        int depth = 0;
        int commas = 0;
        bool any = false;
        char quote = '\0';
        for (int index = start; index < text.Length; index++)
        {
            char current = text[index];
            if (quote != '\0')
            {
                if (current == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            switch (current)
            {
                case '\'' or '`' or '"':
                    quote = current;
                    any = true;
                    break;
                case '(':
                    depth++;
                    any = true;
                    break;
                case ')' when depth == 0:
                    return any ? commas + 1 : 0;
                case ')':
                    depth--;
                    break;
                case ',' when depth == 0:
                    commas++;
                    break;
                default:
                    any |= !char.IsWhiteSpace(current);
                    break;
            }
        }

        throw new InvalidOperationException($"Unterminated property function at offset {start}.");
    }
}
