using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Runic.Application;
using Runic.Application.Hosting;
using Runic.Assets;
using Runic.Assets.AspNetCore;
using Runic.CommandLine;
using Runic.Translations.Editor.Contract;
using Runic.Application.Bridge;

[assembly: RunicApplicationManifest("runic-translations-editor", Version = "1.0.0", Provenance = "local")]

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

    public static async Task<int> Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length > 0 && args[0] == "manual-replacement-preflight")
        {
            Console.WriteLine(EditorManualReplacementPreflight.Run(args[1..]));
            return 0;
        }
        bool bareInvocation = args.Length == 0;
        var console = new ProcessCommandConsole();
        CommandCatalog catalog = EditorCommandModule.CreateCatalog();
        ParseOutcome parse = PortableCommandSyntaxAdapter.Instance.Parse(
            catalog,
            bareInvocation ? ["edit"] : args,
            new ParseSettings(
                Environment.GetEnvironmentVariable(CommandOutputClassifier.EnvironmentVariableName),
                transportOutputOptionName: "--runic-output"));

        switch (parse.Kind)
        {
            case ParseOutcomeKind.Help:
                await console.WriteOutAsync(HelpText.AsMemory(), CancellationToken.None).ConfigureAwait(false);
                return SuccessExitCode;
            case ParseOutcomeKind.Version:
                await console.WriteOutAsync(VersionText().AsMemory(), CancellationToken.None).ConfigureAwait(false);
                return SuccessExitCode;
            case ParseOutcomeKind.Error:
                await PresentParseFailureAsync(parse, console).ConfigureAwait(false);
                return UsageFailureExitCode;
        }

        if (parse.Invocation is null)
            return UsageFailureExitCode;

        // Parser-derived legacy default: the catalog resolves an explicit leading verb
        // itself, positional workspaces surface as parsed argument bindings, and a
        // verb-less option-only form falls back to the packaged example like a bare one.
        bool hasExplicitVerb = !bareInvocation && catalog.TryGetCommand(args[0], out _);
        bool hasPositional = parse.Invocation.Arguments.Count > 0;
        var operations = new EditorCommandLineOperations(
            args,
            opensPackagedExample: bareInvocation || (!hasExplicitVerb && !hasPositional));
        CommandExecutionResult result = await new CommandExecutor(
                new EditorExecutionScopeFactory(operations),
                EditorExitCodePolicy.Instance)
            .ExecuteAsync(
                new CommandExecutionRequest(parse.Invocation, console, CultureInfo.InvariantCulture, "runic-translations-editor"),
                new EditorOutcomeSink())
            .ConfigureAwait(false);
        return result.ExitCode;
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
internal sealed class EditorCommandLineOperations(string[] launchArguments, bool opensPackagedExample) : IEditorCommandOperations
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
        try
        {
            EditorNativeShellEvidence evidence = await EditorNativeShellCanary.RunAsync(
                workspacePath,
                CancellationToken.None).ConfigureAwait(false);
            string summary = System.Text.Json.JsonSerializer.Serialize(evidence);
            return CommandOutcome.Success(new EditorCommandResult(summary));
        }
        catch (EditorNativeShellCapabilityException exception)
        {
            return CommandOutcome.Failure<EditorCommandResult>(
                CommandExitCategory.Unavailable,
                new CommandFault(
                    "REDIT0008",
                    $"Native shell capability unavailable: {exception.Code}.",
                    exception.Evidence?.ToFaultDetails()));
        }
    }

    private async Task<CommandOutcome<EditorCommandResult>> OpenEditorAsync(
        string workspacePath,
        bool useWebView)
    {
        if (!EditorDesktopHost.PackagedUiEmbedded)
            return CommandOutcome.Failure<EditorCommandResult>(
                CommandExitCategory.Unavailable,
                new CommandFault("REDIT0004", "The packaged web UI was not embedded into this editor build."));
        using var session = new EditorSession(workspacePath);
        await using ApplicationHost application = RunicApplication.CreateBuilder(launchArguments)
            .UseHost(new EditorDesktopHost(session, workspacePath, useWebView))
            .Build();
        await application.RunAsync().ConfigureAwait(false);
        return CommandOutcome.Success(new EditorCommandResult(string.Empty));
    }

    // Hosted-web boot mode: the exact session stack of the native window
    // (EditorSession -> EditorBridgeHandler -> generated dispatcher -> bridge
    // session) attached to the toolkit's ASP.NET Core WebSocket transport. No
    // native window is created on this path.
    private static async Task<CommandOutcome<EditorCommandResult>> ServeHostedWebAsync(string workspacePath)
    {
        if (!EditorDesktopHost.PackagedUiEmbedded)
            return CommandOutcome.Failure<EditorCommandResult>(
                CommandExitCategory.Unavailable,
                new CommandFault("REDIT0004", "The packaged web UI was not embedded into this editor build."));
        using var session = new EditorSession(workspacePath);
        var allowedOrigins = new HashSet<string>(StringComparer.Ordinal);
        await using var transport = new ApplicationBridgeWebSocketTransport(
            new ApplicationBridgeSession(new EditorBridgeDispatcher(new EditorBridgeHandler(session))),
            new ApplicationBridgeWebSocketOptions { AllowedOrigins = allowedOrigins });
        await using EditorHostedWebServer server =
            await EditorHostedWebServer.StartAsync(workspacePath, transport, allowedOrigins).ConfigureAwait(false);
        await server.WaitForShutdownAsync(CancellationToken.None).ConfigureAwait(false);
        return CommandOutcome.Success(new EditorCommandResult(string.Empty));
    }

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

/// <summary>
/// Loopback ASP.NET Core host for the <c>serve</c> boot mode: serves the embedded
/// packaged UI archive at the root and maps the Application Bridge WebSocket
/// endpoint against one application-owned transport.
/// </summary>
internal sealed partial class EditorHostedWebServer : IAsyncDisposable
{
    private readonly WebApplication _application;

    private EditorHostedWebServer(WebApplication application) => _application = application;

    public static async Task<EditorHostedWebServer> StartAsync(
        string workspacePath,
        ApplicationBridgeWebSocketTransport transport,
        ISet<string> allowedOrigins)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        WebApplication application = builder.Build();
        application.UseWebSockets();

        // The same embedded Runic Assets archive the native window serves.
        AssetArchiveSource assets = AssetArchive.ReadEmbedded(
            typeof(EditorDesktopHost).Assembly,
            EditorDesktopHost.PackagedUiResourceName);
        application.MapGet("/", context =>
            RunicAssetEndpointExtensions.WriteAssetAsync(context, assets, assets.Manifest.EntryPoint));
        application.MapRunicAssetSource(assets);
        MapTestFixtures(application);
        application.MapRunicApplicationBridge("/bridge", transport);

        await application.StartAsync().ConfigureAwait(false);
        Uri httpUri = new(application.Urls.Single());
        allowedOrigins.Add(httpUri.GetLeftPart(UriPartial.Authority));
        Console.WriteLine($"Runic Translations Editor is serving '{Path.GetFullPath(workspacePath)}' at {httpUri}");
        return new(application);
    }

    public Task WaitForShutdownAsync(CancellationToken cancellationToken) =>
        _application.WaitForShutdownAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await _application.StopAsync().ConfigureAwait(false);
        await _application.DisposeAsync().ConfigureAwait(false);
    }

    // The test build supplies this partial method. An unimplemented private
    // partial method and its invocation are erased from production builds.
    static partial void MapTestFixtures(WebApplication application);
}

/// <summary>Writes editor command output through the process console streams.</summary>
internal sealed class ProcessCommandConsole : ICommandConsole
{
    public bool IsInteractive => !Console.IsInputRedirected && !Console.IsOutputRedirected;
    public bool IsInputRedirected => Console.IsInputRedirected;
    public bool IsOutputRedirected => Console.IsOutputRedirected;
    public bool IsErrorRedirected => Console.IsErrorRedirected;
    public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Console.ReadLine());
    public ValueTask WriteOutAsync(ReadOnlyMemory<char> value, CancellationToken cancellationToken) { Console.Out.Write(value.Span); return ValueTask.CompletedTask; }
    public ValueTask WriteOutBytesAsync(ReadOnlyMemory<byte> value, CancellationToken cancellationToken) { Console.OpenStandardOutput().Write(value.Span); return ValueTask.CompletedTask; }
    public ValueTask WriteErrorAsync(ReadOnlyMemory<char> value, CancellationToken cancellationToken) { Console.Error.Write(value.Span); return ValueTask.CompletedTask; }
}
