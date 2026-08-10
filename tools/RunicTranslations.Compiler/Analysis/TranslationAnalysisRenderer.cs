using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RunicTranslations.Compiler.Analysis;

public static class TranslationAnalysisRenderer
{
    public static string RenderJson(TranslationAnalysisReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var json = new StringBuilder();
        json.Append("{\"analysisReportVersion\":").Append(TranslationAnalysisReport.ReportVersion)
            .Append(",\"dynamicUsagePolicy\":").Append(Json(DynamicPolicy(report.DynamicUsagePolicy)))
            .Append(",\"hasFindings\":").Append(Boolean(report.HasFindings)).Append(",\"catalogs\":[");
        for (int catalogIndex = 0; catalogIndex < report.Catalogs.Count; catalogIndex++)
        {
            if (catalogIndex != 0) json.Append(',');
            TranslationCatalogAnalysis catalog = report.Catalogs[catalogIndex];
            json.Append("{\"catalog\":").Append(Json(catalog.CatalogId))
                .Append(",\"contractFingerprint\":").Append(Json(catalog.ContractFingerprint))
                .Append(",\"sourceFingerprint\":").Append(Json(catalog.SourceFingerprint))
                .Append(",\"artifactStatus\":").Append(Json(ArtifactStatus(catalog.ArtifactStatus)))
                .Append(",\"artifactPath\":").Append(catalog.ArtifactPath is null ? "null" : Json(catalog.ArtifactPath))
                .Append(",\"requiresRegeneration\":").Append(Boolean(catalog.RequiresRegeneration))
                .Append(",\"keys\":[");
            for (int keyIndex = 0; keyIndex < catalog.Keys.Count; keyIndex++)
            {
                if (keyIndex != 0) json.Append(',');
                TranslationKeyAnalysis key = catalog.Keys[keyIndex];
                json.Append("{\"key\":").Append(Json(key.Key))
                    .Append(",\"usage\":").Append(Json(Usage(key.Usage)))
                    .Append(",\"usageLanguages\":[");
                bool wroteLanguage = false;
                if ((key.UsageLanguages & TranslationUsageLanguage.CSharp) != 0)
                {
                    json.Append("\"csharp\"");
                    wroteLanguage = true;
                }
                if ((key.UsageLanguages & TranslationUsageLanguage.TypeScript) != 0)
                {
                    if (wroteLanguage) json.Append(',');
                    json.Append("\"typescript\"");
                }
                json.Append("],\"isDeletionCandidate\":").Append(Boolean(key.IsDeletionCandidate)).Append(",\"locales\":[");
                for (int localeIndex = 0; localeIndex < key.Locales.Count; localeIndex++)
                {
                    if (localeIndex != 0) json.Append(',');
                    TranslationLocaleAnalysis locale = key.Locales[localeIndex];
                    json.Append("{\"locale\":").Append(Json(locale.Locale))
                        .Append(",\"availability\":").Append(Json(Availability(locale.Availability)))
                        .Append(",\"contract\":").Append(Json(Contract(locale.ContractStatus)))
                        .Append(",\"resolvedFromLocale\":")
                        .Append(locale.ResolvedFromLocale is null ? "null" : Json(locale.ResolvedFromLocale)).Append('}');
                }
                json.Append("],\"evidence\":[");
                for (int evidenceIndex = 0; evidenceIndex < key.Evidence.Count; evidenceIndex++)
                {
                    if (evidenceIndex != 0) json.Append(',');
                    TranslationUsageEvidence evidence = key.Evidence[evidenceIndex];
                    json.Append("{\"path\":").Append(Json(evidence.Path))
                        .Append(",\"line\":").Append(evidence.Line.ToString(CultureInfo.InvariantCulture))
                        .Append(",\"column\":").Append(evidence.Column.ToString(CultureInfo.InvariantCulture))
                        .Append(",\"language\":").Append(Json(Language(evidence.Language)))
                        .Append(",\"kind\":").Append(Json(EvidenceKind(evidence.Kind))).Append('}');
                }
                json.Append("]}");
            }
            json.Append("]}");
        }
        return json.Append("]}\n").ToString();
    }

    public static string RenderText(TranslationAnalysisReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var text = new StringBuilder();
        text.Append("Runic Translations analysis v").Append(TranslationAnalysisReport.ReportVersion)
            .Append(" (dynamic usage: ").Append(DynamicPolicy(report.DynamicUsagePolicy)).Append(")\n");
        for (int catalogIndex = 0; catalogIndex < report.Catalogs.Count; catalogIndex++)
        {
            TranslationCatalogAnalysis catalog = report.Catalogs[catalogIndex];
            int direct = 0;
            int fallback = 0;
            int missing = 0;
            int drift = 0;
            int proven = 0;
            int possible = 0;
            int unknown = 0;
            int candidates = 0;
            for (int keyIndex = 0; keyIndex < catalog.Keys.Count; keyIndex++)
            {
                TranslationKeyAnalysis key = catalog.Keys[keyIndex];
                if (key.Usage == TranslationUsageClassification.Proven) proven++;
                else if (key.Usage == TranslationUsageClassification.PossibleDynamic) possible++;
                else unknown++;
                if (key.IsDeletionCandidate) candidates++;
                for (int localeIndex = 0; localeIndex < key.Locales.Count; localeIndex++)
                {
                    TranslationLocaleAnalysis locale = key.Locales[localeIndex];
                    if (locale.Availability == TranslationLocaleAvailability.Direct) direct++;
                    else if (locale.Availability == TranslationLocaleAvailability.FallbackOnly) fallback++;
                    else missing++;
                    if (locale.ContractStatus == TranslationContractStatus.Drift) drift++;
                }
            }

            text.Append("catalog ").Append(catalog.CatalogId)
                .Append(" artifact=").Append(ArtifactStatus(catalog.ArtifactStatus))
                .Append(" source=").Append(catalog.SourceFingerprint).Append('\n')
                .Append("  completeness direct=").Append(direct)
                .Append(" fallback-only=").Append(fallback)
                .Append(" missing=").Append(missing)
                .Append(" contract-drift=").Append(drift).Append('\n')
                .Append("  usage proven=").Append(proven)
                .Append(" possible-dynamic=").Append(possible)
                .Append(" unknown=").Append(unknown)
                .Append(" deletion-candidates=").Append(candidates).Append('\n');

            for (int keyIndex = 0; keyIndex < catalog.Keys.Count; keyIndex++)
            {
                TranslationKeyAnalysis key = catalog.Keys[keyIndex];
                text.Append("  ").Append(key.Key)
                    .Append(" usage=").Append(Usage(key.Usage))
                    .Append(" languages=").Append(Languages(key.UsageLanguages))
                    .Append(" deletion-candidate=").Append(Boolean(key.IsDeletionCandidate))
                    .Append(" locales=");
                for (int localeIndex = 0; localeIndex < key.Locales.Count; localeIndex++)
                {
                    if (localeIndex != 0) text.Append(',');
                    TranslationLocaleAnalysis locale = key.Locales[localeIndex];
                    text.Append(locale.Locale).Append(':').Append(Availability(locale.Availability));
                    if (locale.ResolvedFromLocale is not null) text.Append('(').Append(locale.ResolvedFromLocale).Append(')');
                    if (locale.ContractStatus == TranslationContractStatus.Drift) text.Append("[contract-drift]");
                }
                text.Append('\n');
                for (int evidenceIndex = 0; evidenceIndex < key.Evidence.Count; evidenceIndex++)
                {
                    TranslationUsageEvidence evidence = key.Evidence[evidenceIndex];
                    text.Append("    ").Append(evidence.Path).Append(':')
                        .Append(evidence.Line).Append(':').Append(evidence.Column)
                        .Append(' ').Append(EvidenceKind(evidence.Kind)).Append('\n');
                }
            }
        }
        return text.ToString();
    }

    private static string DynamicPolicy(TranslationDynamicUsagePolicy value) => value switch
    {
        TranslationDynamicUsagePolicy.Conservative => "conservative",
        TranslationDynamicUsagePolicy.IgnoreForDeletionCandidates => "ignore-for-deletion-candidates",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static string Usage(TranslationUsageClassification value) => value switch
    {
        TranslationUsageClassification.Proven => "proven",
        TranslationUsageClassification.PossibleDynamic => "possible-dynamic",
        TranslationUsageClassification.Unknown => "unknown",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static string Availability(TranslationLocaleAvailability value) => value switch
    {
        TranslationLocaleAvailability.Direct => "direct",
        TranslationLocaleAvailability.FallbackOnly => "fallback-only",
        TranslationLocaleAvailability.Missing => "missing",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static string Contract(TranslationContractStatus value) => value switch
    {
        TranslationContractStatus.Matches => "matches",
        TranslationContractStatus.Drift => "drift",
        TranslationContractStatus.Missing => "missing",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static string ArtifactStatus(TranslationArtifactStatus value) => value switch
    {
        TranslationArtifactStatus.Unknown => "unknown",
        TranslationArtifactStatus.Current => "current",
        TranslationArtifactStatus.Stale => "stale",
        TranslationArtifactStatus.Missing => "missing",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static string Language(TranslationUsageLanguage value) => value switch
    {
        TranslationUsageLanguage.CSharp => "csharp",
        TranslationUsageLanguage.TypeScript => "typescript",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static string Languages(TranslationUsageLanguage value)
    {
        if (value == TranslationUsageLanguage.None) return "none";
        if (value == (TranslationUsageLanguage.CSharp | TranslationUsageLanguage.TypeScript)) return "csharp,typescript";
        return Language(value);
    }

    private static string EvidenceKind(TranslationUsageEvidenceKind value) => value switch
    {
        TranslationUsageEvidenceKind.CSharpGeneratedKey => "csharp-generated-key",
        TranslationUsageEvidenceKind.CSharpGeneratedAccessor => "csharp-generated-accessor",
        TranslationUsageEvidenceKind.CSharpTranslationKey => "csharp-translation-key",
        TranslationUsageEvidenceKind.TypeScriptMessageNamespace => "typescript-message-namespace",
        TranslationUsageEvidenceKind.TypeScriptGeneratedIdentifier => "typescript-generated-identifier",
        TranslationUsageEvidenceKind.DynamicLookup => "dynamic-lookup",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static string Boolean(bool value) => value ? "true" : "false";

    private static string Json(string value)
    {
        var result = new StringBuilder(value.Length + 2).Append('"');
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            switch (character)
            {
                case '"': result.Append("\\\""); break;
                case '\\': result.Append("\\\\"); break;
                case '\b': result.Append("\\b"); break;
                case '\f': result.Append("\\f"); break;
                case '\n': result.Append("\\n"); break;
                case '\r': result.Append("\\r"); break;
                case '\t': result.Append("\\t"); break;
                default:
                    if (character < 0x20) result.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    else result.Append(character);
                    break;
            }
        }
        return result.Append('"').ToString();
    }
}
