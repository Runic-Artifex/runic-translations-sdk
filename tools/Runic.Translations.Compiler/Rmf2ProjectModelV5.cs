using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Runic.Translations.Compiler;

// Project hosts now have one execution contract. Keep the named selection
// carrier so callers can retain their explicit v5 hand-off without a second,
// incompatible catalog result.
internal enum TranslationProjectProfile { Rmf2ExecutionV2 }
internal sealed record TranslationProjectProfileSelection(TranslationProjectProfile Profile,
    IReadOnlyList<TranslationDiagnostic> Diagnostics)
{
    internal bool Success => !Diagnostics.Any(diagnostic => diagnostic.Severity == TranslationDiagnosticSeverity.Error);
}
internal sealed record TranslationProfileCompilation(TranslationProjectProfile Profile,
    Rmf2ProjectCompilationV5 Rmf2)
{
    internal bool Success => Rmf2.Success;
    internal IReadOnlyList<TranslationDiagnostic> Diagnostics => Rmf2.Diagnostics;
}

/// <summary>The result of compiling a project for the single supported RMF2 execution contract.</summary>
public sealed class Rmf2ProjectCompilationV5
{
    /// <summary>The semantic execution profile produced by this compiler.</summary>
    public const string ExecutionProfile = "rmf2-execution-v2";
    /// <summary>The normalized message grammar version produced by this compiler.</summary>
    public const int MessageGrammarVersion = 5;
    /// <summary>The required .NET runtime ABI version.</summary>
    public const int RuntimeAbiVersion = 2;

    internal Rmf2ProjectCompilationV5(Rmf2ProjectV5? project, IReadOnlyList<TranslationDiagnostic> diagnostics)
    {
        Project = project;
        Diagnostics = diagnostics;
        CatalogId = project?.Id;
        DefaultLocale = project?.DefaultLocale;
        Locales = project is null
            ? Array.Empty<string>()
            : Array.AsReadOnly(project.Locales.Select(static locale => locale.Tag).ToArray());
        CallerFingerprint = project?.CallerFingerprint;
        SourceHash = project?.SourceHash;
    }

    internal Rmf2ProjectV5? Project { get; }
    /// <summary>The compiled catalog identifier, or <see langword="null"/> when compilation failed.</summary>
    public string? CatalogId { get; }
    /// <summary>The compiled default locale, or <see langword="null"/> when compilation failed.</summary>
    public string? DefaultLocale { get; }
    /// <summary>The deterministic locale set. It is empty when compilation failed.</summary>
    public IReadOnlyList<string> Locales { get; }
    /// <summary>The generated caller-contract fingerprint, or <see langword="null"/> when compilation failed.</summary>
    public string? CallerFingerprint { get; }
    /// <summary>The complete project-source freshness hash, or <see langword="null"/> when compilation failed.</summary>
    public string? SourceHash { get; }
    public IReadOnlyList<TranslationDiagnostic> Diagnostics { get; }
    public bool Success => Project is not null && !Diagnostics.Any(d => d.Severity == TranslationDiagnosticSeverity.Error);
}

// The canonical caller contract and the executable locale payload are separate:
// a locale may omit inputs, but generated APIs and pack validation use Contract.
internal sealed record Rmf2SlotV5(string Kind, int Min, int Max);
internal sealed record Rmf2MessageContractV5(int Id, string Key, IReadOnlyList<string> Path,
    IReadOnlyList<Rmf2InputV5> Inputs, IReadOnlyDictionary<string, Rmf2SlotV5> Slots,
    bool Structured, IReadOnlyList<string> MarkupNames);
internal sealed record Rmf2TranslationV5(string Key, string ContentLocale, Rmf2MessageV5 Message,
    TextSourceLocation SourceLocation, string? Description);
internal sealed record Rmf2LocaleV5(string Tag, string? FallbackTag,
    IReadOnlyList<Rmf2TranslationV5> DirectResources, IReadOnlyList<Rmf2TranslationV5> ResolvedResources);
internal sealed record Rmf2MarkupOptionContractV5(string Type, IReadOnlyList<string> Values, string? Default, bool LiteralOnly);
internal sealed record Rmf2MarkupContractV5(string Name, bool Standalone, bool Interactive, string PlainText,
    IReadOnlyDictionary<string, Rmf2MarkupOptionContractV5> Options);
internal sealed record Rmf2ProjectV5(string Id, string CodeNamespace, string ClassName, TranslationVisibility Visibility,
    string DefaultLocale, TranslationUnsupportedLocalePolicy UnsupportedLocale, TranslationMissingKeyPolicy MissingKey,
    IReadOnlyList<Rmf2MessageContractV5> CanonicalMessages, IReadOnlyList<Rmf2MessageContractV5> ExtraMessages,
    IReadOnlyList<Rmf2LocaleV5> Locales,
    IReadOnlyDictionary<string, Rmf2MarkupContractV5> MarkupContracts, string MarkupContract,
    string CallerFingerprint, string SourceHash)
{
    internal const string Profile = "rmf2-execution-v2";
    internal const int MessageGrammarVersion = 5;
    internal const int RuntimeAbiVersion = 2;
}

internal static class Rmf2ProjectV5EmissionEligibility
{
    internal const string DiagnosticId = "RTR0009";
    internal const string Message = "The effective default locale defines no canonical keys; RMF2 v5 generated runtime catalogs cannot be empty.";
    internal static bool CanEmit(Rmf2ProjectV5 project) =>
        (project ?? throw new ArgumentNullException(nameof(project))).CanonicalMessages.Count != 0;
}

internal sealed record Rmf2V5Definition(int Id, Rmf2MessageContractV5 Contract, bool Canonical);

internal sealed class Rmf2V5DefinitionTable
{
    private readonly Dictionary<string, Rmf2V5Definition> _byKey;
    private Rmf2V5DefinitionTable(IReadOnlyList<Rmf2V5Definition> definitions)
    {
        Definitions = definitions;
        _byKey = definitions.ToDictionary(item => item.Contract.Key, StringComparer.Ordinal);
    }

    internal IReadOnlyList<Rmf2V5Definition> Definitions { get; }
    internal int Id(string key) => _byKey[key].Id;
    internal Rmf2MessageContractV5 Contract(string key) => _byKey[key].Contract;

    internal static Rmf2V5DefinitionTable Create(Rmf2ProjectV5 project)
    {
        var result = new List<Rmf2V5Definition>();
        foreach (Rmf2MessageContractV5 contract in project.CanonicalMessages.OrderBy(item => item.Id))
        {
            if (contract.Id != result.Count) throw new InvalidOperationException("Canonical v5 message IDs must be contiguous.");
            result.Add(new(contract.Id, contract, true));
        }
        foreach (Rmf2MessageContractV5 contract in project.ExtraMessages.OrderBy(item => item.Key, StringComparer.Ordinal))
            result.Add(new(result.Count, contract, false));
        if (result.Select(item => item.Contract.Key).Distinct(StringComparer.Ordinal).Count() != result.Count)
            throw new InvalidOperationException("Duplicate v5 message contract key.");
        return new(result.ToArray());
    }
}

// Versioned, injective mapping over NFC names; no transliteration, keyword table,
// case folding or collision suffix depends on which other messages are present.
// Always encoding also distinguishes an input named "r_61" from an input "a".
internal static class Rmf2GeneratedNamesV1
{
    internal const int Version = 1;
    internal static string Identifier(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return "r_" + Convert.ToHexStringLower(StrictJsonParser.StrictUtf8.GetBytes(name.Normalize(NormalizationForm.FormC)));
    }

    internal static string Path(IReadOnlyList<string> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        if (segments.Count == 0) throw new ArgumentException("A generated path requires at least one segment.", nameof(segments));
        // UTF-8 hex contains no underscore, making segment boundaries unambiguous.
        return string.Join("_", segments.Select(Identifier));
    }
}
