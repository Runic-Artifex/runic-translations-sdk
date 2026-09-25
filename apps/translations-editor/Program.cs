using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CsWebUi;
using Microsoft.Extensions.DependencyInjection;
using Runic.Application.Views.CsWebUi.DependencyInjection;
using Runic.CommandLine;

namespace Runic.Translations.Editor;

internal static class Program
{
    private const int SuccessExitCode = 0;
    private const int ValidationFailureExitCode = 1;
    private const int UsageFailureExitCode = 2;

    private const string HelpText = """
        Runic Translations Editor

        Usage:
          runic-translations-editor [edit] [<workspace>] [--workspace <path>] [--webview] [--smoke-test] [--native-shell-canary]
          runic-translations-editor validate [<workspace>] [--workspace <path>]
          runic-translations-editor diagnostics [<workspace>] [--workspace <path>]
          runic-translations-editor export [<workspace>] --format xliff --output <directory> [--workspace <path>]
          runic-translations-editor export [<workspace>] --format review --output <path> [--workspace <path>]
          runic-translations-editor report [<workspace>] --format xliff|review --source <path> [--workspace <path>]
          runic-translations-editor import [<workspace>] --format xliff|review --source <path> --apply [--workspace <path>]
          runic-translations-editor serve [<workspace>] [--workspace <path>]
          runic-translations-editor help | --help | -h
          runic-translations-editor --version

        The packaged launcher opens the current directory when no workspace is given.
        Validation uses the same compiler path and diagnostics as editor load and save.
        `report` is the read-only, reviewable import preview. `import --apply` previews
        and then applies one import in the same process; it never accepts a reusable token.
        Select machine output with --runic-output json.
        Exit codes: 0 success; 1 validation failure; 2 usage failure.
        """ + "\n";

    public static int Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return RunAsync(args).GetAwaiter().GetResult();
    }

    private static async Task<int> RunAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length > 0 && args[0] == "manual-replacement-preflight")
        {
            Console.WriteLine(EditorManualReplacementPreflight.Run(args[1..]));
            return 0;
        }
        bool bareInvocation = args.Length == 0;
        CommandCatalog catalog = EditorCommandModule.CreateCatalog();
        return await new CommandApp(catalog)
        {
            Name = "runic-translations-editor",
            Version = VersionText(),
            Console = new Runic.CommandLine.Spectre.SpectreCommandConsole(),
            ParseSettings = new ParseSettings(Environment.GetEnvironmentVariable(CommandOutputClassifier.EnvironmentVariableName), transportOutputOptionName: "--runic-output"),
            ExitCodePolicy = EditorExitCodePolicy.Instance,
            OutcomeSink = new EditorOutcomeSink(),
            CreateScopeFactory = invocation => new EditorExecutionScopeFactory(new EditorCommandLineOperations(
                opensPackagedExample: bareInvocation || (!catalog.TryGetCommand(args[0], out _) && invocation.Arguments.Count == 0))),
            PresentFrameworkRequest = async (parse, console, cancellationToken) =>
            {
                if (parse.Kind == ParseOutcomeKind.Error)
                {
                    await PresentParseFailureAsync(parse, console).ConfigureAwait(false);
                    return UsageFailureExitCode;
                }
                string text = parse.Kind == ParseOutcomeKind.Version ? VersionText() :
                    parse.HelpRequest!.Path.Count == 0 ? HelpText : CommandHelpFormatter.Format(catalog, "runic-translations-editor", parse.HelpRequest.Path, "--runic-output");
                if (parse.OutputClassification?.Mode == CommandOutputMode.Json)
                    await CommandOutputDispatcher.DispatchAsync(CommandOutputMode.Json, console, CultureInfo.InvariantCulture,
                        CommandResponse.Succeeded("runic-translations-editor", parse.Kind == ParseOutcomeKind.Version ? "version" : "help", CommandResultCodecs.String.PayloadType, text),
                        CommandResultCodecs.String, cancellationToken).ConfigureAwait(false);
                else await console.WriteOutAsync(text.AsMemory(), cancellationToken).ConfigureAwait(false);
                return SuccessExitCode;
            },
        }.RunAsync(bareInvocation ? ["edit"] : args).ConfigureAwait(false);
    }

    private static string VersionText()
    {
        EditorAbout about = EditorDiagnostics.About();
        return $"{about.Product} {about.Version}\n" +
            $"Channel: {about.UpdateChannel}\n" +
            $"Commit: {about.Commit ?? "development"}\n" +
            $"Runtime: {about.RuntimeIdentifier}\n";
    }

    private static async Task PresentParseFailureAsync(ParseOutcome parse, ICommandConsole console)
    {
        CommandDiagnostic diagnostic = parse.Diagnostics.Count > 0
            ? parse.Diagnostics[0]
            : new CommandDiagnostic(
                "RCLI1002",
                "unknown-command",
                "The command line is invalid.",
                CommandDiagnosticPhase.Parse,
                CommandDiagnosticSeverity.Error);
        CommandOutputMode outputMode = parse.OutputClassification is { IsValid: true, Mode: CommandOutputMode mode }
            ? mode
            : CommandOutputMode.Human;
        await EditorCommandModule.PresentAsync(
            outputMode,
            console,
            CultureInfo.InvariantCulture,
            "edit",
            UsageFailureExitCode,
            CommandExitCategory.Usage,
            result: null,
            fault: new CommandFault(diagnostic.Code, diagnostic.Message),
            diagnostics: parse.Diagnostics).ConfigureAwait(false);
    }

    internal sealed class EditorExitCodePolicy : IExitCodePolicy
    {
        internal static EditorExitCodePolicy Instance { get; } = new();

        private EditorExitCodePolicy()
        {
        }

        public int GetExitCode(CommandExitCategory category) => category switch
        {
            CommandExitCategory.Success => SuccessExitCode,
            CommandExitCategory.Usage or CommandExitCategory.Unavailable or CommandExitCategory.HostFailure => UsageFailureExitCode,
            _ => ValidationFailureExitCode,
        };
    }

    private sealed class EditorExecutionScopeFactory(IEditorCommandOperations operations) : ICommandExecutionScopeFactory
    {
        public ICommandExecutionScope CreateScope() => new EditorExecutionScope(operations);

        private sealed class EditorExecutionScope : ICommandExecutionScope
        {
            private readonly EditorServices _services;

            internal EditorExecutionScope(IEditorCommandOperations operations) => _services = new EditorServices(operations);

            public IServiceProvider Services => _services;

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        private sealed class EditorServices(IEditorCommandOperations operations) : IServiceProvider
        {
            public object? GetService(Type serviceType) =>
                serviceType == typeof(IEditorCommandOperations) ? operations : null;
        }
    }

    private sealed class EditorOutcomeSink : ICommandOutcomeSink
    {
        public ValueTask WriteAsync<T>(
            CommandDescriptor command,
            CommandExecutionContext context,
            CommandOutcome<T> outcome,
            ICommandResultCodec<T> codec,
            int exitCode,
            IReadOnlyList<CommandDiagnostic> diagnostics,
            CancellationToken cancellationToken)
        {
            if (typeof(T) == typeof(EditorCommandResult))
            {
                var typedOutcome = (CommandOutcome<EditorCommandResult>)(object)outcome;
                return EditorCommandModule.PresentAsync(
                    context.OutputMode,
                    context.Console,
                    context.Culture,
                    command.Name,
                    exitCode,
                    typedOutcome.ExitCategory,
                    typedOutcome.IsSuccess ? typedOutcome.Value : null,
                    typedOutcome.Fault,
                    diagnostics,
                    typedOutcome.HumanOutput,
                    cancellationToken);
            }

            return new CommandOutputDispatcher().WriteAsync(command, context, outcome, codec, exitCode, diagnostics, cancellationToken);
        }
    }
}

/// <summary>Routes generated editor commands onto the pre-existing editor code paths.</summary>
internal sealed class EditorCommandLineOperations(bool opensPackagedExample) : IEditorCommandOperations
{
    public async Task<CommandOutcome<EditorCommandResult>> ExecuteAsync(EditorCommandRequest request)
    {
        string workspacePath = ResolveWorkspace(request);
        if (request.Command == "validate")
            return await ValidateWorkspaceAsync(request, workspacePath).ConfigureAwait(false);
        if (request.Command == "diagnostics")
            return await CreateDiagnosticBundleAsync(workspacePath).ConfigureAwait(false);
        if (request.Command == "serve")
            return await ServeHostedWebAsync(ResolveHostedWorkspace(request)).ConfigureAwait(false);
        if (request.Command == "export")
            return await ExportInterchangeAsync(workspacePath, request).ConfigureAwait(false);
        if (request.Command == "report")
            return await ReportInterchangeAsync(workspacePath, request).ConfigureAwait(false);
        if (request.Command == "import")
            return await ImportInterchangeAsync(workspacePath, request).ConfigureAwait(false);
        if (request.Command != "edit")
            return CommandOutcome.Failure<EditorCommandResult>(
                CommandExitCategory.Usage,
                new CommandFault("REDIT0005", "The requested editor command is not part of this catalog."));
        if (request.SmokeTest)
            return await RunSmokeTestAsync(workspacePath).ConfigureAwait(false);
        if (request.NativeShellCanary)
            return await RunNativeShellCanaryAsync(workspacePath).ConfigureAwait(false);
        if (request.Validate)
            return await ValidateWorkspaceAsync(request, workspacePath).ConfigureAwait(false);
        return await OpenEditorAsync(workspacePath, request.Webview).ConfigureAwait(false);
    }

    // Default workspace rules: an explicit edit or validate verb (or --validate)
    // defaults to the current directory; a bare invocation and verb-less option-only
    // forms open the packaged example.
    private string ResolveWorkspace(EditorCommandRequest request)
    {
        if (request.WorkspacePath is not null) return request.WorkspacePath;
        if (request.Workspace.Count > 0) return request.Workspace[0];
        if (request.Command == "validate") return Environment.CurrentDirectory;
        return opensPackagedExample
            ? Path.Combine(AppContext.BaseDirectory, "ExampleWorkspace")
            : Environment.CurrentDirectory;
    }

    // The hosted-web boot mode mirrors the packaged-example default of a bare
    // invocation: serving a browser against the caller's current directory would
    // otherwise be an easy way to expose unrelated files.
    private static string ResolveHostedWorkspace(EditorCommandRequest request)
    {
        if (request.WorkspacePath is not null) return request.WorkspacePath;
        if (request.Workspace.Count > 0) return request.Workspace[0];
        return Path.Combine(AppContext.BaseDirectory, "ExampleWorkspace");
    }

    private static async Task<CommandOutcome<EditorCommandResult>> RunSmokeTestAsync(string workspacePath)
    {
        int exitCode = await EditorSmokeTest.RunAsync(workspacePath).ConfigureAwait(false);
        return exitCode == 0
            ? CommandOutcome.Success(new EditorCommandResult(string.Empty))
            : CommandOutcome.Failure<EditorCommandResult>(
                CommandExitCategory.Validation,
                new CommandFault("REDIT0001", $"The editor smoke test failed for '{workspacePath}'."));
    }

    private static async Task<CommandOutcome<EditorCommandResult>> RunNativeShellCanaryAsync(string workspacePath)
    {
        if (!EditorUiAvailable())
            return CommandOutcome.Failure<EditorCommandResult>(CommandExitCategory.Unavailable,
                new CommandFault("REDIT0004", "The packaged web UI is unavailable."));
        try
        {
            using var services = CreateEditorServices(workspacePath);
            await using var window = services.OpenWindow<EditorWindow, EditorViewModel>(host => new EditorWindow(host));
            window.SetRootFolder(EditorUiRoot());
            await Task.Yield();
            var evidence = new
            {
                schema = "runic.translations.editor-native-shell/3",
                host = "cs-webui",
                packagedUiPresent = true,
                cleanup = "closed-disposed"
            };
            return CommandOutcome.Success(new EditorCommandResult(
                System.Text.Json.JsonSerializer.Serialize(evidence)));
        }
        catch (Exception exception)
        {
            return CommandOutcome.Failure<EditorCommandResult>(CommandExitCategory.Unavailable,
                new CommandFault("REDIT0008", $"Native shell capability unavailable: {exception.Message}."));
        }
        finally { WebUiApplication.Clean(); }
    }

    private static Task<CommandOutcome<EditorCommandResult>> OpenEditorAsync(
        string workspacePath,
        bool useWebView)
    {
        if (!EditorUiAvailable())
            return Task.FromResult(CommandOutcome.Failure<EditorCommandResult>(CommandExitCategory.Unavailable,
                new CommandFault("REDIT0004", "The packaged web UI is unavailable.")));
        using var services = CreateEditorServices(workspacePath);
        using (var window = services.OpenWindow<EditorWindow, EditorViewModel>(host => new EditorWindow(host)))
        {
            window.SetRootFolder(EditorUiRoot());
            window.SetSize(1440, 900);
            if (useWebView) window.ShowWebView("index.html");
            else window.Show("index.html");
            WebUiApplication.Wait();
        }
        WebUiApplication.Clean();
        return Task.FromResult(CommandOutcome.Success(new EditorCommandResult(string.Empty)));
    }

    private static async Task<CommandOutcome<EditorCommandResult>> ServeHostedWebAsync(string workspacePath)
    {
        if (!EditorUiAvailable())
            return CommandOutcome.Failure<EditorCommandResult>(CommandExitCategory.Unavailable,
                new CommandFault("REDIT0004", "The packaged web UI is unavailable."));
        using var services = CreateEditorServices(workspacePath);
        using (var window = services.OpenWindow<EditorWindow, EditorViewModel>(host => new EditorWindow(host)))
        {
            window.SetRootFolder(EditorUiRoot());
            string url = window.StartServer("index.html");
            Console.WriteLine($"Runic Translations Editor is serving '{Path.GetFullPath(workspacePath)}' at {url}");
            Console.Out.Flush();
            await Task.Delay(Timeout.InfiniteTimeSpan).ConfigureAwait(false);
        }
        WebUiApplication.Clean();
        return CommandOutcome.Success(new EditorCommandResult(string.Empty));
    }

    private static ServiceProvider CreateEditorServices(string workspacePath)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => new EditorSession(workspacePath));
        services.AddScoped(provider => new EditorViewModel(provider.GetRequiredService<EditorSession>()));
        services.AddRunicBridges();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static string EditorUiRoot() => Path.Combine(AppContext.BaseDirectory, "www");
    private static bool EditorUiAvailable() => File.Exists(Path.Combine(EditorUiRoot(), "index.html"));

    private static async Task<CommandOutcome<EditorCommandResult>> ValidateWorkspaceAsync(EditorCommandRequest request, string workspacePath)
    {
        try
        {
            using var workspace = new EditorWorkspace(workspacePath);
            WorkspaceSnapshot snapshot = await workspace.LoadAsync().ConfigureAwait(false);
            bool machineOutput = request.OutputMode == CommandOutputMode.Json;
            List<CommandDiagnostic> commandDiagnostics = new(snapshot.Diagnostics.Count);
            foreach (EditorDiagnostic diagnostic in snapshot.Diagnostics)
            {
                string path = string.IsNullOrWhiteSpace(diagnostic.Path) ? "workspace" : diagnostic.Path;
                string line = $"{path}({diagnostic.Line},{diagnostic.Column}): {diagnostic.Severity} {diagnostic.Id}: {diagnostic.Message}";
                if (machineOutput)
                {
                    if (commandDiagnostics.Count < 32) commandDiagnostics.Add(new CommandDiagnostic(
                        "RCLI9050",
                        "workspace-diagnostic",
                        line,
                        CommandDiagnosticPhase.Execution,
                        string.Equals(diagnostic.Severity, "error", StringComparison.OrdinalIgnoreCase)
                            ? CommandDiagnosticSeverity.Error
                            : CommandDiagnosticSeverity.Warning));
                }
                else
                {
                    Console.WriteLine(line);
                }
            }

            if (!snapshot.Success)
            {
                Console.Error.WriteLine($"Validation failed with {snapshot.Diagnostics.Count} diagnostic(s).");
                return ValidationFailed(commandDiagnostics);
            }

            if (snapshot.Catalog is null)
            {
                Console.Error.WriteLine("Validation found no catalog in the workspace.");
                return ValidationFailed(commandDiagnostics);
            }

            string catalogName = snapshot.Catalog.Id;
            string summary = $"Validation passed for '{catalogName}' ({snapshot.Documents.Count} document(s)).";
            if (machineOutput) return CommandOutcome.Success(new EditorCommandResult(summary), commandDiagnostics);
            Console.WriteLine(summary);
            return CommandOutcome.Success(new EditorCommandResult(string.Empty), commandDiagnostics);
        }
        catch (Exception exception) when (exception is ArgumentException or DirectoryNotFoundException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Validation could not start: {exception.Message}");
            return CommandOutcome.Failure<EditorCommandResult>(
                CommandExitCategory.Usage,
                new CommandFault("REDIT0003", "The workspace could not be opened for validation."),
                []);
        }
    }

    private static async Task<CommandOutcome<EditorCommandResult>> CreateDiagnosticBundleAsync(string workspacePath)
    {
        try
        {
            using var session = new EditorSession(workspacePath);
            EditorDiagnosticBundleResult bundle = await session.CreateDiagnosticBundleAsync().ConfigureAwait(false);
            return bundle.Ok && bundle.Path is not null
                ? CommandOutcome.Success(new EditorCommandResult($"Diagnostic bundle created: {bundle.Path}", Diagnostics: bundle))
                : CommandOutcome.Failure<EditorCommandResult>(
                    CommandExitCategory.CommandFailure,
                    new CommandFault("REDIT0007", "The diagnostic bundle could not be created."));
        }
        catch (Exception exception) when (exception is ArgumentException or DirectoryNotFoundException or IOException or UnauthorizedAccessException)
        {
            return CommandOutcome.Failure<EditorCommandResult>(
                CommandExitCategory.Usage,
                new CommandFault("REDIT0003", "The workspace could not be opened for diagnostics."));
        }
    }

    private static CommandOutcome<EditorCommandResult> ValidationFailed(IReadOnlyList<CommandDiagnostic> diagnostics) =>
        CommandOutcome.Failure<EditorCommandResult>(
            CommandExitCategory.Validation,
            new CommandFault("REDIT0002", "The workspace did not validate."),
            diagnostics);

    private static async Task<CommandOutcome<EditorCommandResult>> ExportInterchangeAsync(
        string workspacePath,
        EditorCommandRequest request)
    {
        if (!TryInterchangeFormat(request.Format, out string format))
            return Usage("REDIT0006", "Use --format xliff or --format review for interchange commands.");
        if (string.IsNullOrWhiteSpace(request.Output))
            return Usage("REDIT0006", "Export requires --output <directory-or-path>.");

        try
        {
            using var session = new EditorSession(workspacePath);
            if (format == "xliff")
            {
                EditorXliffExportResult export = await session.ExportXliffAsync(request.Output).ConfigureAwait(false);
                string summary = XliffExportSummary(export);
                return export.Ok
                    ? CommandOutcome.Success(new EditorCommandResult(summary, XliffExport: export))
                    : ValidationFailure("REDIT0006", "The XLIFF export could not be completed.", summary);
            }

            EditorReviewFileResult reviewExport = await session.ExportReviewJsonAsync(request.Output).ConfigureAwait(false);
            string reviewSummary = ReviewExportSummary(reviewExport);
            return reviewExport.Ok
                ? CommandOutcome.Success(new EditorCommandResult(reviewSummary, ReviewExport: reviewExport))
                : ValidationFailure("REDIT0006", "The review export could not be completed.", reviewSummary);
        }
        catch (Exception exception) when (exception is ArgumentException or DirectoryNotFoundException or IOException or UnauthorizedAccessException)
        {
            return ValidationFailure("REDIT0003", "The workspace could not be opened for interchange.", "The interchange export could not start.");
        }
    }

    private static async Task<CommandOutcome<EditorCommandResult>> ReportInterchangeAsync(
        string workspacePath,
        EditorCommandRequest request)
    {
        if (!TryInterchangeFormat(request.Format, out string format))
            return Usage("REDIT0006", "Use --format xliff or --format review for interchange commands.");
        if (string.IsNullOrWhiteSpace(request.Source))
            return Usage("REDIT0006", "Report requires --source <path>.");

        try
        {
            using var session = new EditorSession(workspacePath);
            if (format == "xliff")
            {
                EditorXliffImportPlan report = await session.PreviewXliffImportAsync(request.Source).ConfigureAwait(false);
                string summary = XliffReportSummary(report);
                return report.Ok
                    ? CommandOutcome.Success(new EditorCommandResult(summary, XliffImport: report with { ConfirmationToken = null }))
                    : ValidationFailure("REDIT0006", "The XLIFF report contains refusals.", summary, RefusalDetails(report.Refusals));
            }

            EditorReviewImportPlan reviewReport = await session.PreviewReviewJsonImportAsync(request.Source).ConfigureAwait(false);
            string reviewSummary = ReviewReportSummary(reviewReport);
            return reviewReport.Ok
                ? CommandOutcome.Success(new EditorCommandResult(reviewSummary, ReviewImport: reviewReport with { ConfirmationToken = null }))
                : ValidationFailure("REDIT0006", "The review report contains refusals.", reviewSummary, RefusalDetails(reviewReport.Refusals));
        }
        catch (Exception exception) when (exception is ArgumentException or DirectoryNotFoundException or IOException or UnauthorizedAccessException)
        {
            return ValidationFailure("REDIT0003", "The workspace could not be opened for interchange.", "The interchange report could not start.");
        }
    }

    private static async Task<CommandOutcome<EditorCommandResult>> ImportInterchangeAsync(
        string workspacePath,
        EditorCommandRequest request)
    {
        if (!request.Apply)
            return Usage("REDIT0006", "Import is irreversible. Run report first, then pass --apply to import the reviewed source.");
        if (!TryInterchangeFormat(request.Format, out string format))
            return Usage("REDIT0006", "Use --format xliff or --format review for interchange commands.");
        if (string.IsNullOrWhiteSpace(request.Source))
            return Usage("REDIT0006", "Import requires --source <path>.");

        try
        {
            using var session = new EditorSession(workspacePath);
            if (format == "xliff")
            {
                EditorXliffImportPlan report = await session.PreviewXliffImportAsync(request.Source).ConfigureAwait(false);
                string summary = XliffReportSummary(report);
                if (!report.Ok || report.ConfirmationToken is null)
                    return ValidationFailure("REDIT0006", "The XLIFF import contains refusals.", summary, RefusalDetails(report.Refusals));
                EditorOperationResult applied = await session.ApplyXliffImportAsync(report.ConfirmationToken).ConfigureAwait(false);
                string appliedSummary = applied.Ok ? summary + " Applied." : summary + " Apply failed.";
                return applied.Ok
                    ? CommandOutcome.Success(new EditorCommandResult(appliedSummary, XliffImport: report with { ConfirmationToken = null }, Applied: true))
                    : ValidationFailure("REDIT0006", "The XLIFF import could not be applied.", appliedSummary);
            }

            EditorReviewImportPlan reviewReport = await session.PreviewReviewJsonImportAsync(request.Source).ConfigureAwait(false);
            string reviewSummary = ReviewReportSummary(reviewReport);
            if (!reviewReport.Ok || reviewReport.ConfirmationToken is null)
                return ValidationFailure("REDIT0006", "The review import contains refusals.", reviewSummary, RefusalDetails(reviewReport.Refusals));
            EditorReviewOperationResult reviewApplied = await session.ApplyReviewJsonImportAsync(reviewReport.ConfirmationToken).ConfigureAwait(false);
            string reviewAppliedSummary = reviewApplied.Ok ? reviewSummary + " Applied." : reviewSummary + " Apply failed.";
            return reviewApplied.Ok
                ? CommandOutcome.Success(new EditorCommandResult(reviewAppliedSummary, ReviewImport: reviewReport with { ConfirmationToken = null }, Applied: true))
                : ValidationFailure("REDIT0006", "The review import could not be applied.", reviewAppliedSummary);
        }
        catch (Exception exception) when (exception is ArgumentException or DirectoryNotFoundException or IOException or UnauthorizedAccessException)
        {
            return ValidationFailure("REDIT0003", "The workspace could not be opened for interchange.", "The interchange import could not start.");
        }
    }

    private static bool TryInterchangeFormat(string? value, out string format)
    {
        format = value?.ToLowerInvariant() ?? string.Empty;
        return format is "xliff" or "review";
    }

    private static string XliffExportSummary(EditorXliffExportResult result) => result.Ok
        ? $"Exported {result.Documents.Count} XLIFF document(s) for '{result.CatalogId}' with {result.Losses.Count} loss report item(s)."
        : "XLIFF export failed.";

    private static string ReviewExportSummary(EditorReviewFileResult result) => result.Ok
        ? $"Exported {result.EntryCount} review entry(ies) to '{result.Path}'."
        : "Review export failed.";

    private static string XliffReportSummary(EditorXliffImportPlan result) => result.Ok
        ? $"XLIFF report: {result.AddedCount} added, {result.ChangedCount} changed, {result.RemovedCount} untouched, {result.UnchangedCount} unchanged, and {result.ReviewUpdateCount} review update(s)."
        : "XLIFF report refused: " + RefusalCodes(result.Refusals);

    private static string ReviewReportSummary(EditorReviewImportPlan result) => result.Ok
        ? $"Review report: {result.AddedCount} added, {result.ChangedCount} changed, and {result.RemovedCount} untouched."
        : "Review report refused: " + RefusalCodes(result.Refusals);

    private static Dictionary<string, string> RefusalDetails(IReadOnlyList<EditorInterchangeRefusal> refusals) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["refusalCodes"] = RefusalCodes(refusals),
            ["refusalCount"] = refusals.Count.ToString(CultureInfo.InvariantCulture),
        };

    private static string RefusalCodes(IReadOnlyList<EditorInterchangeRefusal> refusals) =>
        string.Join(",", refusals.Select(static refusal => refusal.Code).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));

    private static CommandOutcome<EditorCommandResult> Usage(string code, string message) =>
        CommandOutcome.Failure<EditorCommandResult>(CommandExitCategory.Usage, new CommandFault(code, message));

    private static CommandOutcome<EditorCommandResult> ValidationFailure(
        string code,
        string message,
        string humanOutput,
        IReadOnlyDictionary<string, string>? details = null) =>
        CommandOutcome.Failure<EditorCommandResult>(
            CommandExitCategory.Validation,
            new CommandFault(code, message, details),
            null,
            humanOutput + "\n");
}
