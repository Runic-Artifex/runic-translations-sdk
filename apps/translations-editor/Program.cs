using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;
using ReactiveUI.Primitives.Concurrency;
using Runic.Application.Views;
using Runic.Application.Views.Desktop;
using Runic.Application.Views.ReactiveUI;
using Runic.CommandLine;
using Runic.Desktop;
#if RUNIC_PLATFORM_Linux
using Runic.Desktop.Gtk4;
#endif
using Runic.Navigation;
using Runic.Platform;
using Splat;

namespace Runic.Translations.Editor;

internal static class Program
{
    private const int SuccessExitCode = CommandExitCodes.Success;
    private const int UsageFailureExitCode = CommandExitCodes.Usage;

    private const string HelpText = """
        Runic Translations Editor

        Usage:
          runic-translations-editor [edit] [<workspace>] [--workspace <path>] [--browser] [--smoke-test] [--native-shell-canary]
          runic-translations-editor validate [<workspace>] [--workspace <path>]
          runic-translations-editor diagnostics [<workspace>] [--workspace <path>]
          runic-translations-editor export [<workspace>] --interchange xliff --output <directory> [--workspace <path>]
          runic-translations-editor export [<workspace>] --interchange review --output <path> [--workspace <path>]
          runic-translations-editor report [<workspace>] --interchange xliff|review --source <path> [--workspace <path>]
          runic-translations-editor import [<workspace>] --interchange xliff|review --source <path> --apply [--workspace <path>]
          runic-translations-editor serve [<workspace>] [--workspace <path>]
          runic-translations-editor desktop-entry install | uninstall
          runic-translations-editor help | --help | -h
          runic-translations-editor --version

        The packaged launcher opens the current directory when no workspace is given.
        `edit` opens a native window (GTK 4 with WebKitGTK 6 on Linux, WebView2 on Windows,
        WKWebView on macOS) and falls back to an installed browser when the WebView cannot
        start; `--browser` opens an installed browser instead. `serve` opens no window: it
        prints RUNIC_APPLICATION_URL=<url>, which one browser can open, and serves until a
        line is read from standard input or the process is stopped.
        `desktop-entry install` writes the Linux desktop entry and icon for the current user
        below $XDG_DATA_HOME (~/.local/share), so the desktop shows the editor's icon;
        `desktop-entry uninstall` removes them. No other command writes them.
        Validation uses the same compiler path and diagnostics as editor load and save.
        `report` is the read-only, reviewable import preview. `import --apply` previews
        and then applies one import in the same process; it never accepts a reusable token.
        Select machine output with --format json.
        Exit codes: 0 success; 1 command failure, such as an unavailable editor UI; 2 usage or validation
        failure, such as workspace diagnostics or a refused import; 3 internal failure; 130 cancelled.
        """ + "\n";

    // Synchronous, with no await before CommandApp.Run: `edit`, `serve` and the native-shell canary hand the Runic Desktop
    // host back to this thread (CommandExecutionContext.ContinueOnCallingThread), because GTK 4 and AppKit run on the
    // process main thread.
    public static int Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length > 0 && args[0] == "manual-replacement-preflight")
        {
            Console.WriteLine(EditorManualReplacementPreflight.Run(args[1..]));
            return 0;
        }
        bool bareInvocation = args.Length == 0;
        CommandCatalog catalog = EditorCommandModule.CreateCatalog();
        return new CommandApp(catalog)
        {
            Name = "runic-translations-editor",
            Version = VersionText(),
            Console = new Runic.CommandLine.Spectre.SpectreCommandConsole(),
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
                    parse.HelpRequest!.Path.Count == 0 ? HelpText : CommandHelpFormatter.Format(catalog, "runic-translations-editor", parse.HelpRequest.Path);
                if (parse.OutputClassification?.Mode == CommandOutputMode.Json)
                    await CommandOutputDispatcher.DispatchAsync(CommandOutputMode.Json, console, CultureInfo.InvariantCulture,
                        CommandResponse.Success("runic-translations-editor", parse.Kind == ParseOutcomeKind.Version ? "version" : "help", CommandResultCodecs.String.PayloadType, text),
                        CommandResultCodecs.String, cancellationToken: cancellationToken).ConfigureAwait(false);
                else await console.WriteOutAsync(text.AsMemory(), cancellationToken).ConfigureAwait(false);
                return SuccessExitCode;
            },
        }.Run(bareInvocation ? ["edit"] : args);
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
}

/// <summary>Routes generated editor commands onto the pre-existing editor code paths.</summary>
internal sealed class EditorCommandLineOperations(bool opensPackagedExample) : IEditorCommandOperations
{
    public async Task<CommandOutcome<EditorCommandResult>> ExecuteAsync(EditorCommandRequest request)
    {
        if (request.Command is "desktop-entry install" or "desktop-entry uninstall")
            return DesktopEntry(install: request.Command == "desktop-entry install");
        string workspacePath = ResolveWorkspace(request);
        if (request.Command == "validate")
            return await ValidateWorkspaceAsync(request, workspacePath).ConfigureAwait(false);
        if (request.Command == "diagnostics")
            return await CreateDiagnosticBundleAsync(workspacePath).ConfigureAwait(false);
        if (request.Command == "serve")
            return await ServeHostedWebAsync(ResolveHostedWorkspace(request), request.ContinueOnMainThread).ConfigureAwait(false);
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
            return await RunNativeShellCanaryAsync(workspacePath, request.ContinueOnMainThread).ConfigureAwait(false);
        if (request.Validate)
            return await ValidateWorkspaceAsync(request, workspacePath).ConfigureAwait(false);
        return await OpenEditorAsync(workspacePath, request.Browser, request.ContinueOnMainThread).ConfigureAwait(false);
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
            ? PackagedExampleWorkspace()
            : Environment.CurrentDirectory;
    }

    // The example workspace that ships with the editor. A macOS bundle from dotnet runic package keeps only code in
    // Contents/MacOS and leaves a symbolic link there to the rest in Contents/Resources; the workspace checks refuse a
    // path that crosses a link, so the example opens where the link leads.
    internal static string PackagedExampleWorkspace()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "ExampleWorkspace");
        try
        {
            return Directory.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName ?? path;
        }
        catch (IOException)
        {
            return path;
        }
    }

    // The hosted-web boot mode mirrors the packaged-example default of a bare
    // invocation: serving a browser against the caller's current directory would
    // otherwise be an easy way to expose unrelated files.
    private static string ResolveHostedWorkspace(EditorCommandRequest request)
    {
        if (request.WorkspacePath is not null) return request.WorkspacePath;
        if (request.Workspace.Count > 0) return request.Workspace[0];
        return EditorCommandLineOperations.PackagedExampleWorkspace();
    }

#if RUNIC_EDITOR_SMOKE_TEST
    private static async Task<CommandOutcome<EditorCommandResult>> RunSmokeTestAsync(string workspacePath)
    {
        int exitCode = await EditorSmokeTest.RunAsync(workspacePath, new EditorCommandLineOperations(opensPackagedExample: false)).ConfigureAwait(false);
        return exitCode == 0
            ? CommandOutcome.Success(new EditorCommandResult(string.Empty))
            : CommandOutcome.Failure<EditorCommandResult>(
                CommandExitCategory.Validation,
                new CommandFault("REDIT0001", $"The editor smoke test failed for '{workspacePath}'."));
    }
#else
    // A published editor carries neither the smoke test nor the SDK's window test host it drives (FD6). Verification
    // builds compile both with -p:RunicEditorSmokeTest=true (eng/run.mjs).
    private static Task<CommandOutcome<EditorCommandResult>> RunSmokeTestAsync(string workspacePath) =>
        Task.FromResult(CommandOutcome.Failure<EditorCommandResult>(
            CommandExitCategory.Unavailable,
            new CommandFault("REDIT0001",
                $"This editor build does not include the smoke test, so '{workspacePath}' was not tested. Build the editor with -p:RunicEditorSmokeTest=true.")));
#endif

    // Opens the editor's window natively, closes it as soon as its page has connected, and reports what opened. It never
    // falls back to a browser, so it fails where the embedded WebView (GTK 4 and WebKitGTK 6 on Linux) cannot open.
    private static Task<CommandOutcome<EditorCommandResult>> RunNativeShellCanaryAsync(string workspacePath, Action<Func<int>>? continueOnMainThread) =>
        StartDesktopHost(continueOnMainThread, "REDIT0008", "Native shell capability unavailable", () => RunDesktopHost(
            workspacePath,
            new RunicDesktopHostOptions
            {
                WindowOptions = WithEditorIcon(RunicDesktopHost.DefaultWindowOptions with
                {
                    Browser = BrowserKind.Embedded,
                    PresentationPolicy = DesktopPresentationPolicy.RequestedOnly,
                }),
            },
            "REDIT0008",
            "Native shell capability unavailable",
            async host =>
            {
                RunicWindowCloseResult closed;
                string presentation;
                bool connected;
                await using (var window = await host.OpenWindowAsync<EditorWindow>(EditorWindowOptions()).ConfigureAwait(false))
                {
                    DesktopWindow? native = DesktopPlatformServices.GetDesktopWindow(window);
                    presentation = native is { IsFallback: false } ? native.Browser.ToString() : "none";
                    connected = RunicDesktopHost.GetSurface(window) is { } surface
                        && await WaitForConnectedPageAsync(surface, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    closed = await window.CloseAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
                Console.WriteLine(EditorJsonText.Write(writer =>
                {
                    writer.WriteStartObject();
                    writer.WriteString("schema", "runic.translations.editor-native-shell/4");
                    writer.WriteString("host", "desktop");
                    writer.WriteString("presentation", presentation);
                    writer.WriteString("backend", NativeBackend());
                    writer.WriteString("applicationId", ApplicationId);
                    writer.WriteBoolean("packagedUiPresent", true);
                    writer.WriteBoolean("bridgeConnected", connected);
                    writer.WriteString("cleanup", closed.Drained ? "closed-disposed" : "closed-undrained");
                    writer.WriteEndObject();
                }));
                return presentation == nameof(BrowserKind.Embedded) && connected && closed.Drained ? 0 : CommandExitCodes.Unavailable;
            },
            waitForConnection: true));

    // The host's open waits for the page's first connection, but a reload disconnects it for a moment and the count can
    // be zero then (DesktopSurface.ConnectionCount), so the canary waits for a connected page rather than reading the
    // count once. A page that was reconnecting is reported on standard error.
    private static async Task<bool> WaitForConnectedPageAsync(DesktopSurface surface, TimeSpan timeout)
    {
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable subscription = surface.SubscribeConnectionEvents(connection =>
        {
            if (connection.Kind == DesktopConnectionEventKind.Connected) connected.TrySetResult();
        });
        if (surface.ConnectionCount > 0) return true;
        Console.Error.WriteLine("The page had no connection after the window opened; waiting for it to reconnect.");
        try
        {
            await connected.Task.WaitAsync(timeout).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private static Task<CommandOutcome<EditorCommandResult>> OpenEditorAsync(
        string workspacePath,
        bool browser,
        Action<Func<int>>? continueOnMainThread) =>
        StartDesktopHost(continueOnMainThread, "REDIT0009", "The editor window could not be shown", () => RunDesktopHost(
            workspacePath,
            new RunicDesktopHostOptions
            {
                // The embedded WebView, falling back to an installed browser; --browser asks for an installed browser.
                WindowOptions = browser
                    ? RunicDesktopHost.DefaultWindowOptions with
                    {
                        Browser = BrowserKind.Any,
                        PresentationPolicy = DesktopPresentationPolicy.RequestedOnly,
                    }
                    : WithEditorIcon(RunicDesktopHost.DefaultWindowOptions),
                // The window opens where it last closed, with its size and maximized state, on a monitor that exists now
                // (#225 P24). Without a stored placement it opens at 1440×900.
                Placement = window => window is EditorWindow ? EditorWindowPlacements.Read() : null,
            },
            "REDIT0009",
            "The editor window could not be shown",
            async host =>
            {
                await using var window = await host.OpenWindowAsync<EditorWindow>(EditorWindowOptions() with { Width = 1440, Height = 900 })
                    .ConfigureAwait(false);
                await window.WaitForCloseAsync().ConfigureAwait(false);
                // The state stays readable after the window closed, until it is disposed. A browser presentation reports
                // none, and the stored placement is kept.
                if (DesktopPlatformServices.GetWindowState(window) is PlatformResult<DesktopWindowState>.Success { Value: var state })
                    EditorWindowPlacements.Remember(state.Placement);
                return 0;
            }));

    // Serve-only mode of the Runic Desktop host: no window and no toolkit. The host writes RUNIC_APPLICATION_URL=<url>, the
    // window's capability URL, which admits one browser, and serves until a line is read from standard input or a
    // termination signal arrives; the end of standard input does not stop it.
    private static Task<CommandOutcome<EditorCommandResult>> ServeHostedWebAsync(string workspacePath, Action<Func<int>>? continueOnMainThread) =>
        StartDesktopHost(continueOnMainThread, "REDIT0009", "The editor could not be served", () => RunDesktopHost(
            workspacePath,
            new RunicDesktopHostOptions { ServeOnly = true },
            "REDIT0009",
            "The editor could not be served",
            async host =>
            {
                Console.WriteLine($"Runic Translations Editor is serving '{Path.GetFullPath(workspacePath)}'. Open the full RUNIC_APPLICATION_URL in one browser; write a line to standard input or press Ctrl+C to stop.");
                Console.Out.Flush();
                await using var window = await host.OpenWindowAsync<EditorWindow>(EditorWindowOptions()).ConfigureAwait(false);
                await window.WaitForCloseAsync().ConfigureAwait(false);
                return 0;
            }));

    // The command only checks that the packaged UI is present. The host starts after the command has completed, on the
    // thread that called CommandApp.Run, and its result is the process exit code.
    private static Task<CommandOutcome<EditorCommandResult>> StartDesktopHost(
        Action<Func<int>>? continueOnMainThread,
        string code,
        string message,
        Func<int> run)
    {
        if (!EditorUiAvailable())
            return Task.FromResult(CommandOutcome.Failure<EditorCommandResult>(CommandExitCategory.Unavailable,
                new CommandFault("REDIT0004", "The packaged web UI is unavailable.")));
        if (continueOnMainThread is null)
            return Task.FromResult(CommandOutcome.Failure<EditorCommandResult>(CommandExitCategory.Unavailable,
                new CommandFault(code, $"{message}: the Runic Desktop host needs the process main thread, which only CommandApp.Run hands back.")));
        continueOnMainThread(run);
        return Task.FromResult(CommandOutcome.Success(new EditorCommandResult(string.Empty)));
    }

    private static int RunDesktopHost(
        string workspacePath,
        RunicDesktopHostOptions hostOptions,
        string code,
        string message,
        Func<RunicDesktopHost, Task<int>> application,
        bool waitForConnection = false)
    {
        try
        {
            ServiceProvider services = EditorServices.Create(workspacePath);
            try
            {
                DesktopHostOptions options = new() { DiagnosticSink = ReportDiagnostic };
                if (waitForConnection) options = options with { WaitForConnection = true };
#if RUNIC_PLATFORM_Linux
                // GTK 4 with WebKitGTK 6, under the editor's application ID. Windows uses WebView2 and macOS WKWebView.
                options = options.WithGtk4(ApplicationId);
#endif
                return RunicDesktopHost.Run(services, options, hostOptions, application);
            }
            finally
            {
                // The platform services (AddRunicPlatformServices) include application services, such as the desktop
                // settings, that only dispose asynchronously. The host has stopped, so this thread may wait for them.
                services.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"{code}: {message}: {exception.Message}");
            return CommandExitCodes.Unavailable;
        }
    }

    // Fallbacks and limitations, such as a missing WebKitGTK runtime, go to standard error.
    private static void ReportDiagnostic(DesktopDiagnostic diagnostic) =>
        Console.Error.WriteLine($"{diagnostic.Severity} {diagnostic.Code}: {diagnostic.Message} {diagnostic.Remediation}".TrimEnd());

    private static string NativeBackend() =>
        OperatingSystem.IsLinux() ? "gtk4-webkitgtk6" : OperatingSystem.IsWindows() ? "webview2" : OperatingSystem.IsMacOS() ? "wkwebview" : "none";

    // The project file's ApplicationId: the GTK application ID and desktop entry name on Linux.
    internal static string ApplicationId { get; } =
        typeof(EditorCommandLineOperations).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(static attribute => attribute.Key == "ApplicationId")?.Value
        ?? throw new InvalidOperationException("The editor's project file sets no ApplicationId.");

    // The window's icon where Runic Desktop sets it from a file: on macOS the packaged PNG becomes the Dock icon. GTK 4
    // takes a window's icon from the icon theme by its app ID instead (desktop-entry install), and the Windows window
    // icon needs an .ico file, which the editor does not ship yet.
    private static DesktopWindowOptions WithEditorIcon(DesktopWindowOptions options) =>
        OperatingSystem.IsMacOS() && File.Exists(EditorDesktopEntry.PackagedIcon()) ? options with { IconFile = EditorDesktopEntry.PackagedIcon() } : options;

    private static RunicWindowOptions EditorWindowOptions() => new() { ContentRoot = EditorUiRoot(), StartPage = "index.html" };
    // The packaged web UI where Runic Desktop looks for it: www beside the editor, or a macOS bundle's
    // Contents/Resources/www, where dotnet runic package puts it (Apple expects only code in Contents/MacOS).
    internal static string EditorUiRoot() => new RunicWindowOptions().ResolveContentRoot();
    private static bool EditorUiAvailable() => File.Exists(Path.Combine(EditorUiRoot(), "index.html"));

    // desktop-entry install|uninstall: the user's Linux desktop entry and icon (EditorDesktopEntry).
    private static CommandOutcome<EditorCommandResult> DesktopEntry(bool install)
    {
        if (!OperatingSystem.IsLinux())
            return CommandOutcome.Failure<EditorCommandResult>(CommandExitCategory.Unavailable, new CommandFault("REDIT0010",
                "The desktop entry is for Linux desktops; on Windows and macOS the editor's window carries its icon."));
        if (EditorDesktopEntry.UserDataHome() is not { } dataHome)
            return CommandOutcome.Failure<EditorCommandResult>(CommandExitCategory.Unavailable, new CommandFault("REDIT0010",
                "Neither XDG_DATA_HOME nor the home directory names a fully qualified directory."));
        var entry = new EditorDesktopEntry(ApplicationId, dataHome);
        try
        {
            if (install)
            {
                IReadOnlyList<string> written = entry.Install(EditorDesktopEntry.CurrentCommand(), EditorDesktopEntry.ProductName(),
                    EditorDesktopEntry.ProductDescription(), EditorDesktopEntry.PackagedIcon());
                return CommandOutcome.Success(new EditorCommandResult("Installed the desktop entry and icon:\n  " + string.Join("\n  ", written)));
            }
            IReadOnlyList<string> removed = entry.Uninstall();
            return CommandOutcome.Success(new EditorCommandResult(removed.Count == 0
                ? "No desktop entry or icon of the editor was installed."
                : "Removed the desktop entry and icon:\n  " + string.Join("\n  ", removed)));
        }
        catch (EditorDesktopEntryException exception)
        {
            return CommandOutcome.Failure<EditorCommandResult>(CommandExitCategory.CommandFailure, new CommandFault("REDIT0010", exception.Message));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return CommandOutcome.Failure<EditorCommandResult>(CommandExitCategory.CommandFailure,
                new CommandFault("REDIT0010", $"The desktop entry could not be {(install ? "installed" : "removed")}: {exception.Message}"));
        }
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
                        "RCLI8050",
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
            return Usage("REDIT0006", "Use --interchange xliff or --interchange review for interchange commands.");
        if (string.IsNullOrWhiteSpace(request.Output))
            return Usage("REDIT0006", "Export requires --output <directory-or-path>.");

        try
        {
            using var session = new EditorSession(workspacePath);
            if (format == "xliff")
            {
                EditorXliffExportResult export = await session.ExportXliffAsync(request.Output, allowRootedOutput: true).ConfigureAwait(false);
                string summary = XliffExportSummary(export);
                return export.Ok
                    ? CommandOutcome.Success(new EditorCommandResult(summary, XliffExport: export))
                    : ValidationFailure("REDIT0006", "The XLIFF export could not be completed.", summary);
            }

            EditorReviewFileResult reviewExport = await session.ExportReviewJsonAsync(request.Output, allowRootedOutput: true).ConfigureAwait(false);
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
            return Usage("REDIT0006", "Use --interchange xliff or --interchange review for interchange commands.");
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
            return Usage("REDIT0006", "Use --interchange xliff or --interchange review for interchange commands.");
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
