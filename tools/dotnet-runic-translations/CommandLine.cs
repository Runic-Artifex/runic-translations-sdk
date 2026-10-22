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
    TextImportRequest? TextImport = null);

internal sealed class ToolUsageException : Exception
{
    internal ToolUsageException(string message)
        : base(message)
    {
    }
}
