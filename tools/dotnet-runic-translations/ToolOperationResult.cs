using System;
using System.Collections.Generic;
using System.Text;
using Runic.CommandLine;
using Runic.Translations.Tooling;

namespace Runic.Translations.Tool;

/// <summary>Bounded, invocation-local output and diagnostic contract for tool operations.</summary>
internal sealed class ToolOperationResult
{
    private const int MaximumDiagnostics = 32;
    private const int MaximumOutputCharacters = 1_048_576;
    private readonly StringBuilder _output = new();
    private readonly List<CommandDiagnostic> _diagnostics = new();
    private readonly List<TranslationsToolDiagnostic> _translationDiagnostics = new();

    internal int ExitCode { get; set; }
    internal CommandExitCategory ExitCategory { get; set; } = CommandExitCategory.Success;
    internal string Output => _output.ToString().TrimEnd();
    internal string? HumanOutput { get; private set; }
    internal IReadOnlyList<CommandDiagnostic> Diagnostics => _diagnostics;
    internal IReadOnlyList<TranslationsToolDiagnostic> TranslationDiagnostics => _translationDiagnostics;

    internal void WriteOutput(string value)
    {
        if (_output.Length + value.Length > MaximumOutputCharacters)
        {
            throw new ToolOutputException("tool output exceeded the supported size.");
        }

        _output.Append(value);
    }

    internal void WriteOutputLine(string value) => WriteOutput(value + Environment.NewLine);

    internal void SetHumanOutput(string value)
    {
        if (value.Length > MaximumOutputCharacters)
        {
            throw new ToolOutputException("tool output exceeded the supported size.");
        }

        HumanOutput = value;
    }

    internal void AddTranslationDiagnostic(TranslationsToolDiagnostic diagnostic)
    {
        if (_translationDiagnostics.Count < MaximumDiagnostics) _translationDiagnostics.Add(diagnostic);
    }

    /// <summary>One <c>path(line,column,endLine,endColumn): severity RTRnnnn: message</c> line per diagnostic.</summary>
    internal static string FormatHuman(IReadOnlyList<TranslationsToolDiagnostic> diagnostics)
    {
        var text = new StringBuilder();
        foreach (TranslationsToolDiagnostic diagnostic in diagnostics) text.Append(diagnostic).Append('\n');
        return text.ToString();
    }

    internal void AddDiagnostic(
        string code,
        string kind,
        string message,
        CommandDiagnosticSeverity severity,
        string? argument = null)
    {
        if (_diagnostics.Count == MaximumDiagnostics)
        {
            return;
        }

        _diagnostics.Add(new CommandDiagnostic(
            code,
            kind,
            message,
            CommandDiagnosticPhase.Execution,
            severity,
            arguments: argument is null ? null : [argument]));
    }
}
