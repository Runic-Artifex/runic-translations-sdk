using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RunicTextResources.Authoring;

namespace RunicTextResources.Tool;

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
    string? CatalogPath,
    IReadOnlyList<string> DocumentPatterns,
    string? OutputPath,
    ToolEmission Emission,
    TextResourceProjectCreationRequest? ProjectCreation);

internal static class CommandLine
{
    private const int MaximumResponseDepth = 16;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static ToolInvocation Parse(string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        List<string> expanded = ExpandResponseFiles(arguments);
        if (expanded.Count == 0)
        {
            throw new ToolUsageException("a command is required.");
        }

        if (expanded[0] is "help" or "--help" or "-h")
        {
            if (expanded.Count != 1)
            {
                throw new ToolUsageException("help does not accept additional arguments.");
            }

            return new ToolInvocation(ToolCommand.Help, null, Array.Empty<string>(), null, ToolEmission.None, null);
        }

        if (expanded[0] == "init")
        {
            return ParseInit(expanded);
        }

        ToolCommand command = expanded[0] switch
        {
            "validate" => ToolCommand.Validate,
            "generate" => ToolCommand.Generate,
            "verify" => ToolCommand.Verify,
            "schema" => ToolCommand.Schema,
            _ => throw new ToolUsageException($"unknown command '{expanded[0]}'."),
        };

        string? catalog = null;
        string? output = null;
        ToolEmission emission = ToolEmission.None;
        var documents = new List<string>();
        for (int index = 1; index < expanded.Count; index++)
        {
            string option = expanded[index];
            switch (option)
            {
                case "--catalog":
                    catalog = ReadSingleValue(expanded, ref index, option, catalog is not null);
                    break;
                case "--output":
                    output = ReadSingleValue(expanded, ref index, option, output is not null);
                    break;
                case "--documents":
                    if (documents.Count != 0)
                    {
                        throw new ToolUsageException("--documents may be specified only once.");
                    }

                    while (index + 1 < expanded.Count && !expanded[index + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        documents.Add(expanded[++index]);
                    }

                    if (documents.Count == 0)
                    {
                        throw new ToolUsageException("--documents requires at least one explicit path or glob.");
                    }

                    break;
                case "--emit-csharp":
                    AddEmission(ref emission, ToolEmission.CSharp, option);
                    break;
                case "--emit-json":
                    AddEmission(ref emission, ToolEmission.Json, option);
                    break;
                case "--emit-typescript":
                    AddEmission(ref emission, ToolEmission.TypeScript, option);
                    break;
                case "--emit-template-manifest":
                    AddEmission(ref emission, ToolEmission.TemplateManifest, option);
                    break;
                case "--emit-esm":
                    AddEmission(ref emission, ToolEmission.Esm, option);
                    break;
                case "--emit-cpp":
                    AddEmission(ref emission, ToolEmission.Cpp, option);
                    break;
                default:
                    throw new ToolUsageException($"unknown option or positional argument '{option}'.");
            }
        }

        if (command == ToolCommand.Schema)
        {
            if (catalog is not null || documents.Count != 0 || emission != ToolEmission.None)
            {
                throw new ToolUsageException("schema accepts only --output <directory>.");
            }

            if (output is null)
            {
                throw new ToolUsageException("schema requires --output <directory>.");
            }
        }
        else
        {
            if (catalog is null)
            {
                throw new ToolUsageException($"{CommandName(command)} requires --catalog <file>.");
            }

            if (documents.Count == 0)
            {
                throw new ToolUsageException($"{CommandName(command)} requires --documents <path-or-glob...>.");
            }

            bool needsOutput = command is ToolCommand.Generate or ToolCommand.Verify;
            if (needsOutput && output is null)
            {
                throw new ToolUsageException($"{CommandName(command)} requires --output <directory>.");
            }

            if (!needsOutput && output is not null)
            {
                throw new ToolUsageException("validate does not accept --output.");
            }

            if (!needsOutput && emission != ToolEmission.None)
            {
                throw new ToolUsageException("validate does not accept emit switches.");
            }
        }

        if (command is ToolCommand.Generate or ToolCommand.Verify && emission == ToolEmission.None)
        {
            emission = ToolEmission.All;
        }

        return new ToolInvocation(command, catalog, documents, output, emission, null);
    }

    private static ToolInvocation ParseInit(List<string> arguments)
    {
        string? directory = null;
        string? catalog = null;
        string? defaultLocale = null;
        string? codeNamespace = null;
        string? className = null;
        string layer = "base";
        bool layerSpecified = false;
        bool generateEsm = true;
        bool includeStarter = true;
        var locales = new List<TextResourceProjectLocale>();

        for (int index = 1; index < arguments.Count; index++)
        {
            string option = arguments[index];
            switch (option)
            {
                case "--directory":
                    directory = ReadSingleValue(arguments, ref index, option, directory is not null);
                    break;
                case "--catalog":
                    catalog = ReadSingleValue(arguments, ref index, option, catalog is not null);
                    break;
                case "--default-locale":
                    defaultLocale = ReadSingleValue(arguments, ref index, option, defaultLocale is not null);
                    break;
                case "--namespace":
                    codeNamespace = ReadSingleValue(arguments, ref index, option, codeNamespace is not null);
                    break;
                case "--class":
                    className = ReadSingleValue(arguments, ref index, option, className is not null);
                    break;
                case "--layer":
                    layer = ReadSingleValue(arguments, ref index, option, layerSpecified);
                    layerSpecified = true;
                    break;
                case "--locale":
                    locales.Add(ParseLocale(ReadSingleValue(arguments, ref index, option, duplicate: false)));
                    break;
                case "--no-esm":
                    if (!generateEsm) throw new ToolUsageException("--no-esm may be specified only once.");
                    generateEsm = false;
                    break;
                case "--no-starter":
                    if (!includeStarter) throw new ToolUsageException("--no-starter may be specified only once.");
                    includeStarter = false;
                    break;
                default:
                    throw new ToolUsageException($"unknown option or positional argument '{option}'.");
            }
        }

        RequireInitOption(directory, "--directory");
        RequireInitOption(catalog, "--catalog");
        RequireInitOption(defaultLocale, "--default-locale");
        RequireInitOption(codeNamespace, "--namespace");
        RequireInitOption(className, "--class");
        var request = new TextResourceProjectCreationRequest(
            directory!,
            catalog!,
            defaultLocale!,
            codeNamespace!,
            className!,
            locales,
            layer,
            generateEsm,
            includeStarter);
        return new ToolInvocation(ToolCommand.Init, null, Array.Empty<string>(), null, ToolEmission.None, request);
    }

    private static TextResourceProjectLocale ParseLocale(string value)
    {
        int separator = value.IndexOf(':');
        if (separator < 0)
        {
            return new TextResourceProjectLocale(value);
        }

        if (separator == 0 || separator == value.Length - 1 || value.IndexOf(':', separator + 1) >= 0)
        {
            throw new ToolUsageException("--locale expects <tag> or <tag>:<fallback>.");
        }

        return new TextResourceProjectLocale(value[..separator], value[(separator + 1)..]);
    }

    private static void RequireInitOption(string? value, string option)
    {
        if (value is null)
        {
            throw new ToolUsageException($"init requires {option} <value>.");
        }
    }

    private static void AddEmission(ref ToolEmission emissions, ToolEmission value, string option)
    {
        if ((emissions & value) != 0)
        {
            throw new ToolUsageException($"{option} may be specified only once.");
        }

        emissions |= value;
    }

    private static string ReadSingleValue(List<string> arguments, ref int index, string option, bool duplicate)
    {
        if (duplicate)
        {
            throw new ToolUsageException($"{option} may be specified only once.");
        }

        if (index + 1 == arguments.Count || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ToolUsageException($"{option} requires exactly one value.");
        }

        return arguments[++index];
    }

    private static List<string> ExpandResponseFiles(IEnumerable<string> arguments)
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
