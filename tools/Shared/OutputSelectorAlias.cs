using System;

namespace Runic.Tools.Shared;

/// <summary>
/// Accepts a former spelling of the output selector as a hidden alias. Every Runic tool selects human or JSON
/// output with <c>--format human|json</c>; a tool that shipped another spelling (<c>dotnet runic --output json</c>)
/// keeps it working until 2.0. Runic.CommandLine has one selector name and no hidden alias for it, so the tool
/// rewrites the alias to the selector before parsing: <c>--output json</c> and <c>--output=json</c> become
/// <c>--format json</c> and <c>--format=json</c>. Arguments after <c>--</c> belong to the application and are never
/// rewritten. Help and completion list only the selector.
/// </summary>
internal static class OutputSelectorAlias
{
    internal const string Selector = "--format";

    internal static string[] Rewrite(string[] arguments, string alias)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrEmpty(alias);
        string[]? rewritten = null;
        for (int index = 0; index < arguments.Length; index++)
        {
            string argument = arguments[index];
            if (argument == "--") break;
            string? replacement =
                argument == alias ? Selector
                : argument.StartsWith(alias + "=", StringComparison.Ordinal) ? Selector + argument[alias.Length..]
                : null;
            if (replacement is null) continue;
            rewritten ??= (string[])arguments.Clone();
            rewritten[index] = replacement;
        }
        return rewritten ?? arguments;
    }
}
