using System;
using System.Collections.Generic;
using System.Text;
using Runic.CommandLine;

namespace Runic.Translations.Tool;

/// <summary>Bounded, invocation-local output and diagnostic contract for tool operations.</summary>
internal sealed class ToolOperationResult
{
    private const int MaximumDiagnostics = 32;
    private const int MaximumOutputCharacters = 1_048_576;
    private readonly StringBuilder _output = new();
    private readonly List<CommandDiagnostic> _diagnostics = new();

    internal int ExitCode { get; set; }
    internal CommandExitCategory ExitCategory { get; set; } = CommandExitCategory.Success;
    internal string Output => _output.ToString().TrimEnd();
    internal string? HumanOutput { get; private set; }
    internal IReadOnlyList<CommandDiagnostic> Diagnostics => _diagnostics;

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
