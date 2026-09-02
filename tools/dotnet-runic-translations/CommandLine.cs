using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Runic.Translations.Authoring;

namespace Runic.Translations.Tool;

internal enum ToolCommand
{
    Help,
    Init,
    Validate,
    Generate,
    Verify,
    Schema,
}

[Flags]
internal enum ToolEmission
{
    None = 0,
    CSharp = 1,
    Json = 2,
    TypeScript = 4,
    TemplateManifest = 8,
    Esm = 16,
    Cpp = 32,
    All = CSharp | Json | TypeScript | TemplateManifest | Esm,
}

internal sealed record ToolInvocation(
    ToolCommand Command,
    string? OutputPath,
    ToolEmission Emission,
    TranslationProjectCreationRequest? ProjectCreation,
    string? ProjectPath = null);

internal static class CommandLine
{
    private const int MaximumResponseDepth = 16;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>Expands the tool's established UTF-8 response-file syntax before catalog parsing.</summary>
    internal static List<string> ExpandResponseFiles(IEnumerable<string> arguments)
    {
        var result = new List<string>();
        var activeFiles = new HashSet<string>(PathComparer);
        foreach (string argument in arguments)
        {
            ExpandArgument(argument, Environment.CurrentDirectory, 0, activeFiles, result);
        }

        return result;
    }

    private static void ExpandArgument(
        string argument,
        string baseDirectory,
        int depth,
        HashSet<string> activeFiles,
        List<string> result)
    {
        if (argument.StartsWith("@@", StringComparison.Ordinal))
        {
            result.Add(argument[1..]);
            return;
        }

        if (!argument.StartsWith('@') || argument.Length == 1)
        {
            result.Add(argument);
            return;
        }

        if (depth == MaximumResponseDepth)
        {
            throw new ToolUsageException($"response-file nesting exceeds {MaximumResponseDepth} levels.");
        }

        string path = Path.GetFullPath(argument[1..], baseDirectory);
        if (!activeFiles.Add(path))
        {
            throw new ToolUsageException($"response-file cycle detected at '{NormalizePath(path)}'.");
        }

        try
        {
            string content = StrictUtf8.GetString(File.ReadAllBytes(path));
            string directory = Path.GetDirectoryName(path) ?? baseDirectory;
            foreach (string token in TokenizeResponseFile(content, path))
            {
                ExpandArgument(token, directory, depth + 1, activeFiles, result);
            }
        }
        catch (DecoderFallbackException exception)
        {
            throw new ToolUsageException($"response file '{NormalizePath(path)}' is not valid UTF-8.", exception);
        }
        finally
        {
            activeFiles.Remove(path);
        }
    }

    private static IEnumerable<string> TokenizeResponseFile(string content, string path)
    {
        var token = new StringBuilder();
        bool inQuotes = false;
        bool hasToken = false;
        bool atLineStart = true;
        for (int index = content.Length > 0 && content[0] == '\uFEFF' ? 1 : 0; index < content.Length; index++)
        {
            char value = content[index];
            if (!inQuotes && atLineStart && value == '#')
            {
                while (index < content.Length && content[index] is not '\r' and not '\n')
                {
                    index++;
                }

                atLineStart = true;
                continue;
            }

            if (value == '"')
            {
                inQuotes = !inQuotes;
                hasToken = true;
                atLineStart = false;
                continue;
            }

            if (value == '\\' && inQuotes && index + 1 < content.Length && content[index + 1] is '"' or '\\')
            {
                token.Append(content[++index]);
                hasToken = true;
                atLineStart = false;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(value))
            {
                if (hasToken)
                {
                    yield return token.ToString();
                    token.Clear();
                    hasToken = false;
                }

                if (value is '\r' or '\n')
                {
                    atLineStart = true;
                }
                continue;
            }

            token.Append(value);
            hasToken = true;
            atLineStart = false;
        }

        if (inQuotes)
        {
            throw new ToolUsageException($"response file '{NormalizePath(path)}' contains an unterminated quote.");
        }

        if (hasToken)
        {
            yield return token.ToString();
        }
    }

    private static string CommandName(ToolCommand command) => command.ToString().ToLowerInvariant();

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static string NormalizePath(string path) => path.Replace('\\', '/');
}

internal sealed class ToolUsageException : Exception
{
    internal ToolUsageException(string message)
        : base(message)
    {
    }

    internal ToolUsageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
