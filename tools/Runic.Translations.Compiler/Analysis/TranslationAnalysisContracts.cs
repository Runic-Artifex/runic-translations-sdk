using System;
using System.Collections.Generic;

namespace Runic.Translations.Compiler.Analysis;

internal enum TranslationUsageSourceLanguage
{
    CSharp,
    TypeScript,
}

[Flags]
internal enum TranslationUsageLanguage
{
    None = 0,
    CSharp = 1,
    TypeScript = 2,
}

internal enum TranslationUsageClassification
{
    Proven,
    PossibleDynamic,
    Unknown,
}

internal enum TranslationUsageEvidenceKind
{
    CSharpGeneratedKey,
    CSharpGeneratedAccessor,
    CSharpTranslationKey,
    TypeScriptMessageNamespace,
    TypeScriptGeneratedIdentifier,
    DynamicLookup,
}

internal enum TranslationLocaleAvailability
{
    Direct,
    FallbackOnly,
    Missing,
}

internal enum TranslationContractStatus
{
    Matches,
    Drift,
    Missing,
}

internal enum TranslationArtifactStatus
{
    Unknown,
    Current,
    Stale,
    Missing,
}

internal enum TranslationDynamicUsagePolicy
{
    Conservative,
    IgnoreForDeletionCandidates,
}

internal sealed class TranslationUsageSource
{
    public TranslationUsageSource(
        string path,
        string text,
        TranslationUsageSourceLanguage language,
        string? catalogId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(text);
        Path = path.Replace('\\', '/');
        Text = text;
        Language = language;
        CatalogId = string.IsNullOrWhiteSpace(catalogId) ? null : catalogId;
    }

    public string Path { get; }
    public string Text { get; }
    public TranslationUsageSourceLanguage Language { get; }
    public string? CatalogId { get; }
}

internal sealed class TranslationArtifactSnapshot
{
    public TranslationArtifactSnapshot(string catalogId, string sourceFingerprint, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        CatalogId = catalogId;
        SourceFingerprint = sourceFingerprint;
        Path = path.Replace('\\', '/');
    }

    public string CatalogId { get; }
    public string SourceFingerprint { get; }
    public string Path { get; }
}

internal sealed class TranslationAnalysisOptions
{
    public TranslationAnalysisOptions(
        TranslationDynamicUsagePolicy dynamicUsagePolicy = TranslationDynamicUsagePolicy.Conservative)
    {
        DynamicUsagePolicy = dynamicUsagePolicy;
    }

    public TranslationDynamicUsagePolicy DynamicUsagePolicy { get; }
}

internal sealed class TranslationUsageEvidence
{
    internal TranslationUsageEvidence(
        string path,
        int line,
        int column,
        TranslationUsageLanguage language,
        TranslationUsageEvidenceKind kind)
    {
        Path = path;
        Line = line;
        Column = column;
        Language = language;
        Kind = kind;
    }

    public string Path { get; }
    public int Line { get; }
    public int Column { get; }
    public TranslationUsageLanguage Language { get; }
    public TranslationUsageEvidenceKind Kind { get; }
}

internal sealed class TranslationLocaleAnalysis
{
    internal TranslationLocaleAnalysis(
        string locale,
        TranslationLocaleAvailability availability,
        TranslationContractStatus contractStatus,
        string? resolvedFromLocale)
    {
        Locale = locale;
        Availability = availability;
        ContractStatus = contractStatus;
        ResolvedFromLocale = resolvedFromLocale;
    }

    public string Locale { get; }
    public TranslationLocaleAvailability Availability { get; }
    public TranslationContractStatus ContractStatus { get; }
    public string? ResolvedFromLocale { get; }
}

internal sealed class TranslationKeyAnalysis
{
    internal TranslationKeyAnalysis(
        string key,
        TranslationUsageClassification usage,
        TranslationUsageLanguage usageLanguages,
        bool isDeletionCandidate,
        IReadOnlyList<TranslationLocaleAnalysis> locales,
        IReadOnlyList<TranslationUsageEvidence> evidence)
    {
        Key = key;
        Usage = usage;
        UsageLanguages = usageLanguages;
        IsDeletionCandidate = isDeletionCandidate;
        Locales = locales;
        Evidence = evidence;
    }

    public string Key { get; }
    public TranslationUsageClassification Usage { get; }
    public TranslationUsageLanguage UsageLanguages { get; }
    public bool IsDeletionCandidate { get; }
    public IReadOnlyList<TranslationLocaleAnalysis> Locales { get; }
    public IReadOnlyList<TranslationUsageEvidence> Evidence { get; }
}

internal sealed class TranslationCatalogAnalysis
{
    internal TranslationCatalogAnalysis(
        string catalogId,
        string contractFingerprint,
        string sourceFingerprint,
        TranslationArtifactStatus artifactStatus,
        string? artifactPath,
        IReadOnlyList<TranslationKeyAnalysis> keys)
    {
        CatalogId = catalogId;
        ContractFingerprint = contractFingerprint;
        SourceFingerprint = sourceFingerprint;
        ArtifactStatus = artifactStatus;
        ArtifactPath = artifactPath;
        Keys = keys;
    }

    public string CatalogId { get; }
    public string ContractFingerprint { get; }
    public string SourceFingerprint { get; }
    public TranslationArtifactStatus ArtifactStatus { get; }
    public string? ArtifactPath { get; }
    public bool RequiresRegeneration => ArtifactStatus is TranslationArtifactStatus.Stale or TranslationArtifactStatus.Missing;
    public IReadOnlyList<TranslationKeyAnalysis> Keys { get; }
}

internal sealed class TranslationAnalysisReport
{
    internal TranslationAnalysisReport(
        IReadOnlyList<TranslationCatalogAnalysis> catalogs,
        TranslationDynamicUsagePolicy dynamicUsagePolicy)
    {
        Catalogs = catalogs;
        DynamicUsagePolicy = dynamicUsagePolicy;
    }

    public const int ReportVersion = 1;
    public IReadOnlyList<TranslationCatalogAnalysis> Catalogs { get; }
    public TranslationDynamicUsagePolicy DynamicUsagePolicy { get; }

    public bool HasFindings
    {
        get
        {
            for (int catalogIndex = 0; catalogIndex < Catalogs.Count; catalogIndex++)
            {
                TranslationCatalogAnalysis catalog = Catalogs[catalogIndex];
                if (catalog.RequiresRegeneration) return true;
                for (int keyIndex = 0; keyIndex < catalog.Keys.Count; keyIndex++)
                {
                    TranslationKeyAnalysis key = catalog.Keys[keyIndex];
                    if (key.Usage != TranslationUsageClassification.Proven || key.IsDeletionCandidate) return true;
                    for (int localeIndex = 0; localeIndex < key.Locales.Count; localeIndex++)
                    {
                        TranslationLocaleAnalysis locale = key.Locales[localeIndex];
                        if (locale.Availability != TranslationLocaleAvailability.Direct ||
                            locale.ContractStatus != TranslationContractStatus.Matches)
                            return true;
                    }
                }
            }

            return false;
        }
    }
}
