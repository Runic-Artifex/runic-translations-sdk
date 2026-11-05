using System;
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
    ImportText,
    Import,
    XliffExport,
    XliffImport,
    ReviewMark,
    LocaleAdd,
    LocaleRemove,
}

[Flags]
internal enum ToolEmission
{
    None = 0,
    CSharp = 1,
    Json = 2,
    Esm = 4,
    All = CSharp | Json | Esm,
}

internal sealed record ToolInvocation(
    ToolCommand Command,
    string? OutputPath,
    ToolEmission Emission,
    TranslationProjectCreationRequest? ProjectCreation,
    string? ProjectPath = null,
    bool DryRun = false,
    bool OmitSourceDiagnostics = false,
    TextImportRequest? TextImport = null,
    bool PseudoLocales = false,
    string? ContractPath = null);

internal sealed class ToolUsageException : Exception
{
    internal ToolUsageException(string message)
        : base(message)
    {
    }
}

/// <summary>Runs a shared tooling operation, reporting its invalid-request exception as a usage error.</summary>
internal static class ToolingUsage
{
    internal static T Run<T>(Func<T> operation)
    {
        try { return operation(); }
        catch (Runic.Translations.Tooling.TranslationToolingUsageException exception) { throw new ToolUsageException(exception.Message); }
    }
}
