using System;
using System.Collections.Generic;
using System.Linq;

namespace Runic.Translations.Compiler;

/// <summary>
/// The identity of an RMF2 message. Its logical path (the segments, written <c>common.cancel</c>) is the form people read
/// and type: diagnostics, the CLI and the editor show it, and commands accept it. Its canonical key (the segments joined by
/// <c>_</c>, <c>common_cancel</c>) is the stable identifier of generated code, review files and runtime lookups, and is
/// accepted as input too. RTR0018 makes the canonical key of a compiled catalog name exactly one logical path, and an
/// identifier cannot contain <c>.</c>, so an input resolves to at most one message.
/// </summary>
internal static class Rmf2MessageKeys
{
    /// <summary>The logical form of <paramref name="path"/>: <c>common.cancel</c>.</summary>
    internal static string Logical(IEnumerable<string> path) => string.Join(".", path);

    /// <summary>The canonical key of <paramref name="path"/>: <c>common_cancel</c>.</summary>
    internal static string Canonical(IEnumerable<string> path) => string.Join("_", path);

    /// <summary>Every message contract of <paramref name="project"/>, canonical messages first.</summary>
    internal static IEnumerable<Rmf2MessageContractV5> Messages(Rmf2ProjectV5 project) =>
        project.CanonicalMessages.Concat(project.ExtraMessages);

    /// <summary>The logical path of each canonical key of <paramref name="project"/>.</summary>
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> Paths(Rmf2ProjectV5 project)
    {
        var paths = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (Rmf2MessageContractV5 contract in Messages(project)) paths.TryAdd(contract.Key, contract.Path);
        return paths;
    }

    /// <summary>The logical form of canonical key <paramref name="key"/>, or the key itself when no message has it.</summary>
    internal static string Display(IReadOnlyDictionary<string, IReadOnlyList<string>> paths, string key) =>
        paths.TryGetValue(key, out IReadOnlyList<string>? path) ? Logical(path) : key;

    /// <summary>The message <paramref name="input"/> names, as its canonical key or its logical path, or null.</summary>
    internal static Rmf2MessageContractV5? Resolve(Rmf2ProjectV5 project, string input) =>
        Messages(project).FirstOrDefault(contract => contract.Key == input) ??
        Messages(project).FirstOrDefault(contract => Logical(contract.Path) == input);

    /// <summary>
    /// The canonical key <paramref name="input"/> names among <paramref name="keys"/> (canonical keys), given as the key
    /// or as its logical path, or null.
    /// </summary>
    internal static string? Resolve(IReadOnlyDictionary<string, IReadOnlyList<string>> paths, IEnumerable<string> keys, string input)
    {
        string[] candidates = keys as string[] ?? keys.ToArray();
        if (candidates.Contains(input, StringComparer.Ordinal)) return input;
        return candidates.FirstOrDefault(key => Display(paths, key) == input);
    }

    /// <summary>
    /// The logical key closest to <paramref name="input"/> (a logical or canonical key with a likely typo), or null when
    /// nothing is close enough.
    /// </summary>
    internal static string? Suggest(string input, IEnumerable<string> logicalKeys) =>
        TranslationCompiler.SuggestKey(input, logicalKeys as IReadOnlyList<string> ?? logicalKeys.ToArray());

    /// <summary>"<c> Did you mean 'common.cancel'?</c>", or an empty string when nothing is close.</summary>
    internal static string DidYouMean(string input, IEnumerable<string> logicalKeys) =>
        Suggest(input, logicalKeys) is { } suggestion ? " Did you mean '" + suggestion + "'?" : string.Empty;
}
