using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Runic.CommandLine;
using Runic.CommandLine.Generated;

namespace Runic.Translations.Editor;

/// <summary>
/// Generated command catalog that routes the editor's interactive and headless surface.
/// </summary>
/// <remarks>
/// <para>
/// Verbs are matched case-sensitively: unlike the legacy ad-hoc parser, upper-case
/// spellings such as <c>EDIT</c> or <c>Validate</c> are not recognized as verbs and fall
/// through to unknown-command or positional-workspace handling instead.
/// </para>
/// <para>
/// Recorded strictness deltas versus the legacy parser: <c>validate</c> rejects the
/// editor-only <c>--webview</c> and <c>--smoke-test</c> options as unknown options with a
/// usage error; repeated or unknown options fail fast with focused usage diagnostics
/// instead of being silently ignored or resolved last-one-wins; and a build without
/// the embedded packaged web UI fails gracefully as unavailable (fault <c>REDIT0004</c>,
/// exit code 2) instead of crashing with an unhandled directory exception.
/// </para>
/// </remarks>
internal static class EditorCommandModule
{
    /// <summary>Creates the single parser-neutral editor command catalog.</summary>
    public static CommandCatalog CreateCatalog() => GeneratedCommandCatalog.Create();

    /// <summary>Writes a completed editor command outcome through the standard command-line transport.</summary>
    public static ValueTask PresentAsync(
        CommandOutputMode outputMode,
        ICommandConsole console,
        CultureInfo culture,
        string command,
        int exitCode,
        CommandExitCategory exitCategory,
        EditorCommandResult? result,
        CommandFault? fault,
        IReadOnlyList<CommandDiagnostic> diagnostics,
        string? humanFailureOutput = null,
        CancellationToken cancellationToken = default)
    {
        CommandCatalog catalog = CreateCatalog();
        CommandDescriptor descriptor = !catalog.TryGetCommand(command, out CommandDescriptor? resolved) || resolved is null
            ? catalog.DefaultCommand ?? catalog.Commands[0]
            : resolved;
        var context = new CommandExecutionContext(
            EmptyServices.Instance,
            console,
            new CommandPath([command]),
            outputMode,
            culture,
            "runic-translations-editor");
        CommandOutcome<EditorCommandResult> outcome = fault is null
            ? CommandOutcome.Success(result!, null)
            : CommandOutcome.Failure<EditorCommandResult>(exitCategory, fault, null, humanFailureOutput);
        return new CommandOutputDispatcher().WriteAsync(
            descriptor,
            context,
            outcome,
            EditorCommandResultCodec.Instance,
            exitCode,
            diagnostics,
            cancellationToken);
    }

    /// <summary>Opens the interactive editor workspace; this is the root fallback for bare invocations.</summary>
    [Command("edit")]
    [DefaultCommand]
    [CommandResult("runic.editor.command/1", typeof(EditorCommandJsonContext))]
    public static Task<CommandOutcome<EditorCommandResult>> Edit(
        [FromServices] IEditorCommandOperations operations,
        [Argument(AllowMultipleValues = true)] IReadOnlyList<string> workspace,
        CommandExecutionContext context,
        [Option("--webview")] bool webview,
        [Option("--smoke-test")] bool smokeTest,
        [Option("--native-shell-canary")] bool nativeShellCanary,
        [Option("--validate")] bool validate,
        [Option("--workspace")] string? workspacePath = null,
        [Option("--catalog")] string? catalog = null) =>
        operations.ExecuteAsync(new EditorCommandRequest(
            "edit",
            workspace,
            workspacePath,
            catalog,
            webview,
            smokeTest,
            validate,
            context.OutputMode,
            NativeShellCanary: nativeShellCanary));

    /// <summary>
    /// Validates one workspace headlessly without opening the editor.
    /// Editor-only options (<c>--webview</c>, <c>--smoke-test</c>) are rejected with a usage error.
    /// </summary>
    [Command("validate")]
    [CommandResult("runic.editor.command/1", typeof(EditorCommandJsonContext))]
    public static Task<CommandOutcome<EditorCommandResult>> Validate(
        [FromServices] IEditorCommandOperations operations,
        [Argument(AllowMultipleValues = true)] IReadOnlyList<string> workspace,
        CommandExecutionContext context,
        [Option("--workspace")] string? workspacePath = null,
        [Option("--catalog")] string? catalog = null) =>
        operations.ExecuteAsync(new EditorCommandRequest(
            "validate",
            workspace,
            workspacePath,
            catalog,
            false,
            false,
            false,
            context.OutputMode));

    /// <summary>Creates the privacy-bounded diagnostic ZIP for explicit local support collection.</summary>
    [Command("diagnostics")]
    [CommandResult("runic.editor.command/1", typeof(EditorCommandJsonContext))]
    public static Task<CommandOutcome<EditorCommandResult>> Diagnostics(
        [FromServices] IEditorCommandOperations operations,
        [Argument(AllowMultipleValues = true)] IReadOnlyList<string> workspace,
        CommandExecutionContext context,
        [Option("--workspace")] string? workspacePath = null,
        [Option("--catalog")] string? catalog = null) =>
        operations.ExecuteAsync(new EditorCommandRequest(
            "diagnostics", workspace, workspacePath, catalog, false, false, false, context.OutputMode));

    /// <summary>Exports XLIFF documents or portable review JSON from one workspace.</summary>
    [Command("export")]
    [CommandResult("runic.editor.command/1", typeof(EditorCommandJsonContext))]
    public static Task<CommandOutcome<EditorCommandResult>> Export(
        [FromServices] IEditorCommandOperations operations,
        [Argument(AllowMultipleValues = true)] IReadOnlyList<string> workspace,
        CommandExecutionContext context,
        [Option("--format", Required = true)] string format,
        [Option("--output", Required = true)] string output,
        [Option("--workspace")] string? workspacePath = null,
        [Option("--catalog")] string? catalog = null) =>
        operations.ExecuteAsync(new EditorCommandRequest(
            "export", workspace, workspacePath, catalog, false, false, false, context.OutputMode,
            Format: format, Output: output));

    /// <summary>Reports the reviewable diff and refusals for an XLIFF or review JSON import without writing files.</summary>
    [Command("report")]
    [CommandResult("runic.editor.command/1", typeof(EditorCommandJsonContext))]
    public static Task<CommandOutcome<EditorCommandResult>> Report(
        [FromServices] IEditorCommandOperations operations,
        [Argument(AllowMultipleValues = true)] IReadOnlyList<string> workspace,
        CommandExecutionContext context,
        [Option("--format", Required = true)] string format,
        [Option("--source", Required = true)] string source,
        [Option("--workspace")] string? workspacePath = null,
        [Option("--catalog")] string? catalog = null) =>
        operations.ExecuteAsync(new EditorCommandRequest(
            "report", workspace, workspacePath, catalog, false, false, false, context.OutputMode,
            Format: format, Source: source));

    /// <summary>Previews and, only with <c>--apply</c>, commits one XLIFF or review JSON import.</summary>
    [Command("import")]
    [CommandResult("runic.editor.command/1", typeof(EditorCommandJsonContext))]
    public static Task<CommandOutcome<EditorCommandResult>> Import(
        [FromServices] IEditorCommandOperations operations,
        [Argument(AllowMultipleValues = true)] IReadOnlyList<string> workspace,
        CommandExecutionContext context,
        [Option("--format", Required = true)] string format,
        [Option("--source", Required = true)] string source,
        [Option("--apply")] bool apply,
        [Option("--workspace")] string? workspacePath = null,
        [Option("--catalog")] string? catalog = null) =>
        operations.ExecuteAsync(new EditorCommandRequest(
            "import", workspace, workspacePath, catalog, false, false, false, context.OutputMode,
            Format: format, Source: source, Apply: apply));

    /// <summary>
    /// Hosts the editor over an HTTP + WebSocket bridge without a native window;
    /// the same session stack as <c>edit</c>, served to browsers at a loopback URL.
    /// </summary>
    [Command("serve")]
    [CommandResult("runic.editor.command/1", typeof(EditorCommandJsonContext))]
    public static Task<CommandOutcome<EditorCommandResult>> Serve(
        [FromServices] IEditorCommandOperations operations,
        [Argument(AllowMultipleValues = true)] IReadOnlyList<string> workspace,
        CommandExecutionContext context,
        [Option("--workspace")] string? workspacePath = null,
        [Option("--catalog")] string? catalog = null) =>
        operations.ExecuteAsync(new EditorCommandRequest(
            "serve",
            workspace,
            workspacePath,
            catalog,
            false,
            false,
            false,
            context.OutputMode));

    private sealed class EmptyServices : IServiceProvider
    {
        internal static EmptyServices Instance { get; } = new();

        public object? GetService(Type serviceType) => null;
    }
}

/// <summary>Host operation bridge for the composable generated editor command catalog.</summary>
internal interface IEditorCommandOperations
{
    /// <summary>Routes one bound editor command request to its pre-existing code path.</summary>
    Task<CommandOutcome<EditorCommandResult>> ExecuteAsync(EditorCommandRequest request);
}

/// <summary>Typed values bound by the generated catalog before host operation policy runs.</summary>
internal sealed record EditorCommandRequest(
    string Command,
    IReadOnlyList<string> Workspace,
    string? WorkspacePath,
    string? Catalog,
    bool Webview,
    bool SmokeTest,
    bool Validate,
    CommandOutputMode OutputMode,
    string? Format = null,
    string? Source = null,
    string? Output = null,
    bool Apply = false,
    bool NativeShellCanary = false);

/// <summary>Portable command payload; silent commands carry an empty summary.</summary>
internal sealed record EditorCommandResult(
    string Summary,
    EditorXliffExportResult? XliffExport = null,
    EditorReviewFileResult? ReviewExport = null,
    EditorXliffImportPlan? XliffImport = null,
    EditorReviewImportPlan? ReviewImport = null,
    bool? Applied = null,
    EditorDiagnosticBundleResult? Diagnostics = null)
{
    /// <inheritdoc />
    public override string ToString() => Summary;
}

[JsonSerializable(typeof(EditorCommandResult))]
[JsonSerializable(typeof(EditorXliffExportResult))]
[JsonSerializable(typeof(EditorReviewFileResult))]
[JsonSerializable(typeof(EditorXliffImportPlan))]
[JsonSerializable(typeof(EditorReviewImportPlan))]
[JsonSerializable(typeof(EditorDiagnosticBundleResult))]
internal sealed partial class EditorCommandJsonContext : JsonSerializerContext;

/// <summary>Presents editor results without appending output for intentionally silent successes.</summary>
internal sealed class EditorCommandResultCodec : ICommandResultCodec<EditorCommandResult>
{
    internal static EditorCommandResultCodec Instance { get; } = new();

    public string PayloadType => "runic.editor.command/1";

    public JsonTypeInfo<EditorCommandResult> TypeInfo => EditorCommandJsonContext.Default.EditorCommandResult;

    public ValueTask WriteHumanAsync(
        EditorCommandResult value,
        ICommandConsole console,
        CultureInfo culture,
        CancellationToken cancellationToken) =>
        value.Summary.Length == 0
            ? ValueTask.CompletedTask
            : console.WriteOutAsync((value.Summary + "\n").AsMemory(), cancellationToken);
}
