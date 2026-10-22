using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Runic.CommandLine;
using Runic.CommandLine.Spectre;
using Runic.Translations.Tooling;
using Runic.Translations.Authoring;
using Runic.Translations.Compiler;
using Runic.Translations.Internal;

namespace Runic.Translations.Tool;

internal static class Program
{
    private static readonly ICommandConsole PresentationConsole = new SpectreCommandConsole();
    // Exit codes, shared by every Runic tool and Runic.CommandLine's defaults since 0.8.
    internal const int Success = CommandExitCodes.Success;
    /// <summary>The command failed, such as a verify difference or an I/O failure, or a resource was unavailable.</summary>
    internal const int CommandFailure = CommandExitCodes.CommandFailure;
    /// <summary>An invalid invocation, or input that failed validation, such as translation diagnostics.</summary>
    internal const int UsageFailure = CommandExitCodes.Usage;
    /// <summary>An internal or host failure.</summary>
    internal const int InternalFailure = CommandExitCodes.HostFailure;
    // Run's result: not successful; the category in ToolOperationResult decides the exit code.
    private const int Failed = 1;
    private const string ToolVersion = "0.2";

    /// <summary>Selects human or JSON output, as in every Runic tool.</summary>
    internal const string FormatOptionName = "--format";
    /// <summary>The former spelling of <see cref="FormatOptionName"/>, a hidden alias until 2.0.</summary>
    internal const string FormerFormatOptionName = "--runic-output";

    /// <summary>The translations commands and <c>support</c>.</summary>
    internal static CommandCatalog CreateCatalog() =>
        SupportCommandModule.CreateCatalog();

    public static int Main(string[] arguments)
    {
        try
        {
            if (arguments.Length == 1 && arguments[0] == "lsp") return new Rmf2LanguageServer(Console.OpenStandardInput(), Console.OpenStandardOutput()).Run();
            if (arguments.Length == 1 && arguments[0] == "serve") return CompileServer.RunOnStandardStreams(ToolVersion);
            // The catalog declares response files, so CommandApp expands @file arguments before parsing.
            return CreateApp(CreateCatalog(), PresentationConsole).RunAsync(arguments).GetAwaiter().GetResult();
        }
        catch (ToolOutputException exception)
        {
            return Fatal(ToolCodes.Output, "tool-output", $"error {exception.Message}", CommandFailure);
        }
        catch (ToolDiagnosticException exception)
        {
            return Fatal(ToolCodes.Diagnostic, "tool-diagnostic", exception.Message, CommandFailure);
        }
        catch (ToolUsageException exception)
        {
            return Fatal(ToolCodes.Usage, "tool-usage", exception.Message, UsageFailure, true);
        }
        catch (TranslationAuthoringException exception)
        {
            return Fatal(ToolCodes.Authoring, "authoring", exception.Message, UsageFailure);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Fatal(ToolCodes.Io, "io", exception.Message, ExitCodeOf(IoCategory(exception)));
        }
        catch (Exception exception)
        {
            return Fatal(ToolCodes.Internal, "internal", $"internal failure: {exception.Message}", InternalFailure);
        }
    }

    /// <summary>
    /// The tool's application. It sets no <see cref="ParseSettings"/>, so <see cref="CommandApp"/> captures the process
    /// environment: <c>RUNIC_COMMANDLINE_OUTPUT</c>, <c>RUNIC_COMMANDLINE_NO_INPUT</c> and option fallbacks. The
    /// output selector and its hidden alias, response files and the translation commands' human output are declared on
    /// the catalog; the outcome sink and the exit codes are the defaults.
    /// </summary>
    internal static CommandApp CreateApp(CommandCatalog catalog, ICommandConsole console) =>
        new(catalog)
        {
            Name = "runic-translations",
            Version = ToolVersion,
            Console = console,
            ScopeFactory = ToolExecutionScopeFactory.Instance,
            PresentFrameworkRequest = static (parsed, _, _) => ValueTask.FromResult(PresentFramework(parsed)),
        };

    internal static int ExitCodeOf(CommandExitCategory category) => CommandExitCodes.GetDefault(category);

    // An inaccessible file is a command failure; an invalid path argument is a usage failure.
    private static CommandExitCategory IoCategory(Exception exception) =>
        exception is ArgumentException ? CommandExitCategory.Usage : CommandExitCategory.CommandFailure;

    private static int PresentFramework(ParseOutcome parsed)
    {
            if (parsed.Kind == ParseOutcomeKind.Help)
            {
                Present(parsed, "runic-translations", Success, new(CommandHelpFormatter.Format(CreateCatalog(), "runic-translations", parsed.HelpRequest!.Path), string.Empty), null, []);
                return Success;
            }

            if (parsed.Kind == ParseOutcomeKind.Version)
            {
                Present(parsed, "runic-translations", Success, new("runic-translations " + ToolVersion, string.Empty), null, []);
                return Success;
            }

            if (parsed.Kind == ParseOutcomeKind.Error)
            {
                CommandDiagnostic original = parsed.Diagnostics[0];
                if (IsUnknownSupportMode(original)) return PresentUnknownSupportMode(parsed);
                string message = FormatDiagnostic(original);
                CommandDiagnostic diagnostic = new(
                    original.Code,
                    original.Kind,
                    message,
                    original.Phase,
                    original.Severity,
                    original.TokenIndex,
                    original.Arguments,
                    original.Path,
                    original.MessageKey);

                Present(
                    parsed,
                    "runic-translations",
                    UsageFailure,
                    null,
                    new CommandFault(diagnostic.Code, diagnostic.Message),
                    [diagnostic],
                    $"runic-translations: {message}\n{UsageText(original.Path)}\n",
                    TranslationsToolFailurePresentation.ErrorOnly);
                return UsageFailure;
            }

        return UsageFailure;
    }

    // `support <mode>` names its mode as a subcommand; an unknown mode keeps the code of the former
    // dotnet runic support --mode, RTRSUP001. `support` alone prints the group's help.
    private static bool IsUnknownSupportMode(CommandDiagnostic diagnostic) =>
        diagnostic.Code == "RCLI1002" && diagnostic.Path.Count >= 1 && diagnostic.Path[0] == SupportCommandModule.Group;

    private static int PresentUnknownSupportMode(ParseOutcome parsed)
    {
        CommandOutcome<SupportCommandResult> outcome = SupportCommandModule.Failure(
            CommandExitCategory.Usage, "RTRSUP001", "Use support preview, collect, or remove.");
        var context = new CommandExecutionContext(
            EmptyServices.Instance,
            PresentationConsole,
            new CommandPath([SupportCommandModule.Group]),
            OutputMode(parsed),
            CultureInfo.InvariantCulture,
            "runic-translations");
        int exitCode = ExitCodeOf(outcome.ExitCategory);
        CommandDescriptor command = CreateCatalog().Commands.First(descriptor => descriptor.Name == SupportCommandModule.Group);
        new CommandOutputDispatcher().WriteAsync(command, context, outcome, SupportResultCodec.Instance, exitCode, [], CancellationToken.None)
            .AsTask().GetAwaiter().GetResult();
        return exitCode;
    }

    private sealed class EmptyServices : IServiceProvider
    {
        internal static EmptyServices Instance { get; } = new();
        public object? GetService(Type serviceType) => null;
    }

    private sealed class SupportResultCodec : ICommandResultCodec<SupportCommandResult>
    {
        internal static SupportResultCodec Instance { get; } = new();
        public string PayloadType => SupportCommandModule.PayloadType;
        public System.Text.Json.Serialization.Metadata.JsonTypeInfo<SupportCommandResult> TypeInfo => SupportCommandJsonContext.Default.SupportCommandResult;
        public ValueTask WriteHumanAsync(SupportCommandResult value, ICommandConsole console, CultureInfo culture, CancellationToken cancellationToken) =>
            console.WriteOutAsync((value.ToHumanOutput() + "\n").AsMemory(), cancellationToken);
    }

    private static string FormatDiagnostic(CommandDiagnostic diagnostic) => diagnostic.Code switch
    {
        "RCLI1001" when diagnostic.Path.ToString() == "validate" && diagnostic.Arguments.Count >= 1 && diagnostic.Arguments[0] == "--output" => "validate does not accept --output.",
        "RCLI1001" when diagnostic.Path.ToString() == "validate" && diagnostic.Arguments.Count >= 1 && IsEmitSwitch(diagnostic.Arguments[0]) => "validate does not accept emit switches.",
        "RCLI1001" when diagnostic.Path.ToString() == "analyze" && diagnostic.Arguments.Count >= 1 && IsEmitSwitch(diagnostic.Arguments[0]) => "analyze does not accept emit switches.",
        "RCLI1001" when diagnostic.Path.ToString() == "schema" && diagnostic.Arguments.Count >= 1 && IsSchemaForbiddenOption(diagnostic.Arguments[0]) => "schema accepts only --output <directory>.",
        "RCLI1001" when diagnostic.Arguments.Count >= 1 => $"unknown option or positional argument '{diagnostic.Arguments[0]}'.",
        "RCLI1001" => "unknown option or positional argument.",
        "RCLI1002" when diagnostic.Arguments.Count >= 1 => $"unknown command '{diagnostic.Arguments[0]}'.",
        "RCLI1002" => "a command is required.",
        "RCLI1003" when diagnostic.Arguments.Count >= 1 => $"{diagnostic.Arguments[0]} requires exactly one value.",
        "RCLI1003" => "an option requires a value.",
        "RCLI1005" => "a required argument is missing.",
        "RCLI1007" when diagnostic.Arguments.Count >= 1 => $"{diagnostic.Arguments[0]} may be specified only once.",
        "RCLI1007" => "an option may be specified only once.",
        "RCLI1012" when diagnostic.Arguments.Count >= 2 => $"{diagnostic.Arguments[1]} requires {diagnostic.Arguments[0]} {ExpectedValueShape(diagnostic.Arguments[0])}.",
        "RCLI1013" => "help does not accept additional arguments.",
        // A response file that cannot be read or expanded keeps Runic.CommandLine's message, which names the file.
        "RCLI1017" or "RCLI1018" or "RCLI1019" or "RCLI1020" or "RCLI1021" => diagnostic.Message,
        _ => "invalid command invocation.",
    };

    private static bool IsEmitSwitch(string option) => option is
        "--emit-csharp" or "--emit-json" or "--emit-esm";

    private static bool IsSchemaForbiddenOption(string option) =>
        option == "--project" || IsEmitSwitch(option);

    private static int Fatal(string code, string kind, string message, int exitCode, bool usage = false)
    {
        var diagnostic = new CommandDiagnostic(code, kind, message, CommandDiagnosticPhase.Execution, CommandDiagnosticSeverity.Error);
        string humanOutput = usage
            ? $"runic-translations: {message}\n{UsageText(CommandPath.Root)}\n"
            : code is ToolCodes.Output or ToolCodes.Diagnostic
                ? message + "\n"
                : $"runic-translations: {message}\n";
        TranslationsToolCommandModule.PresentAsync(
            CommandOutputMode.Human,
            PresentationConsole,
            CultureInfo.InvariantCulture,
            "runic-translations",
            exitCode,
            null,
            new CommandFault(code, message),
            [diagnostic],
            humanOutput,
            TranslationsToolFailurePresentation.ErrorOnly).AsTask().GetAwaiter().GetResult();
        return exitCode;
    }

    private static string ExpectedValueShape(string option) => option switch
    {
        "--output" or "--directory" => "<directory>",
        "--default-locale" => "<tag>",
        _ => "<value>",
    };

    private static void Present(
        ParseOutcome parsed,
        string command,
        int exitCode,
        TranslationsToolCommandResult? result,
        CommandFault? fault,
        IReadOnlyList<CommandDiagnostic> diagnostics,
        string? humanFailureOutput = null,
        TranslationsToolFailurePresentation failurePresentation = TranslationsToolFailurePresentation.Standard) =>
        TranslationsToolCommandModule.PresentAsync(
            OutputMode(parsed),
            PresentationConsole,
            CultureInfo.InvariantCulture,
            command,
            exitCode,
            result,
            fault,
            diagnostics,
            humanFailureOutput,
            failurePresentation).AsTask().GetAwaiter().GetResult();

    private static CommandOutputMode OutputMode(ParseOutcome parsed)
    {
        if (parsed.OutputClassification is { IsValid: true, Mode: CommandOutputMode mode })
        {
            return mode;
        }

        return CommandOutputMode.Human;
    }

    // Usage failures print the same help as --help for the command, so there is one help text.
    private static string UsageText(CommandPath path) =>
        CommandHelpFormatter.Format(CreateCatalog(), "runic-translations", path).TrimEnd();

    private static CommandPath PathOf(ToolCommand command) => command switch
    {
        ToolCommand.Help => CommandPath.Root,
        ToolCommand.ImportText => new CommandPath(["import-text"]),
        _ => new CommandPath([command.ToString().ToLowerInvariant()]),
    };


    internal static ToolOperationResult Execute(ToolInvocation invocation, SourceUnitCache? units = null)
    {
        var result = new ToolOperationResult();
        try
        {
            // A failed run is a validation failure (translation diagnostics) unless it chose another category.
            if (Run(invocation, result, units) == Success) result.ExitCategory = CommandExitCategory.Success;
            else if (result.ExitCategory == CommandExitCategory.Success) result.ExitCategory = CommandExitCategory.Validation;
        }
        catch (ToolOutputException exception) { result.SetHumanOutput($"error {exception.Message}\n"); result.AddDiagnostic(ToolCodes.Output, "tool-output", SafeDomainMessage(exception.Message, "The requested output could not be written."), CommandDiagnosticSeverity.Error); result.ExitCategory = CommandExitCategory.CommandFailure; }
        catch (ToolDiagnosticException exception) { result.SetHumanOutput(exception.Message + "\n"); result.AddDiagnostic(ToolCodes.Diagnostic, "tool-diagnostic", SafeDomainMessage(exception.Message, "The translations operation reported diagnostics."), CommandDiagnosticSeverity.Error); result.ExitCategory = CommandExitCategory.CommandFailure; }
        catch (ToolUsageException exception) { result.SetHumanOutput($"runic-translations: {exception.Message}\n{UsageText(PathOf(invocation.Command))}\n"); result.AddDiagnostic(ToolCodes.Usage, "tool-usage", SafeDomainMessage(exception.Message, "The translations command arguments are invalid."), CommandDiagnosticSeverity.Error); result.ExitCategory = CommandExitCategory.Usage; }
        catch (TranslationAuthoringException exception)
        {
            string message = exception.Message.Contains("already exists; no files were written", StringComparison.Ordinal)
                ? "already exists; no files were written."
                : exception.Message;
            result.SetHumanOutput($"runic-translations: {exception.Message}\n");
            result.AddDiagnostic(ToolCodes.Authoring, "authoring", message, CommandDiagnosticSeverity.Error);
            result.ExitCategory = CommandExitCategory.Usage;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException) { result.SetHumanOutput($"runic-translations: {exception.Message}\n"); result.AddDiagnostic(ToolCodes.Io, "io", "The translations input or output could not be accessed.", CommandDiagnosticSeverity.Error); result.ExitCategory = IoCategory(exception); }
        catch (Exception) { result.AddDiagnostic(ToolCodes.Internal, "internal", "The translations command encountered an internal failure.", CommandDiagnosticSeverity.Error); result.ExitCategory = CommandExitCategory.HostFailure; }
        result.ExitCode = ExitCodeOf(result.ExitCategory);
        return result;
    }

    internal static ToolOperationResult ExecuteInit(string directory, string catalog, string defaultLocale, string codeNamespace, string className, IReadOnlyList<string> locales, bool noStarter)
    {
        try
        {
            return Execute(new ToolInvocation(ToolCommand.Init, null, ToolEmission.None,
                new TranslationProjectCreationRequest(directory, catalog, defaultLocale, codeNamespace, className,
                    ParseLocales(locales), !noStarter)));
        }
        catch (ToolUsageException exception)
        {
            return Usage(exception.Message, ToolCommand.Init);
        }
    }

    internal static ToolEmission Emission(bool csharp, bool json, bool esm)
    {
        ToolEmission result = ToolEmission.None;
        if (csharp) result |= ToolEmission.CSharp;
        if (json) result |= ToolEmission.Json;
        if (esm) result |= ToolEmission.Esm;
        return result;
    }

    internal static ToolOperationResult Usage(string message, ToolCommand command)
    {
        var result = new ToolOperationResult { ExitCode = UsageFailure, ExitCategory = CommandExitCategory.Usage };
        result.AddDiagnostic(ToolCodes.Usage, "tool-usage", message, CommandDiagnosticSeverity.Error);
        result.SetHumanOutput($"runic-translations: {message}\n{UsageText(PathOf(command))}\n");
        return result;
    }

    private static List<TranslationProjectLocale> ParseLocales(IReadOnlyList<string> values)
    {
        var locales = new List<TranslationProjectLocale>(values.Count);
        foreach (string value in values)
        {
            int separator = value.IndexOf(':');
            if (separator < 0) locales.Add(new TranslationProjectLocale(value));
            else if (separator > 0 && separator < value.Length - 1 && value.IndexOf(':', separator + 1) < 0) locales.Add(new TranslationProjectLocale(value[..separator], value[(separator + 1)..]));
            else throw new ToolUsageException("--locale expects <tag> or <tag>:<fallback>.");
        }
        return locales;
    }

    private static string SafeDomainMessage(string message, string fallback)
    {
        int marker = message.IndexOf("RTR", StringComparison.Ordinal);
        if (marker >= 0 && message.Length >= marker + 7 &&
            char.IsDigit(message[marker + 3]) && char.IsDigit(message[marker + 4]) &&
            char.IsDigit(message[marker + 5]) && char.IsDigit(message[marker + 6]))
        {
            string code = message.Substring(marker, 7);
            if (code == "RTR0022")
            {
                string limit = LongestDigits(message);
                return limit.Length == 0
                    ? $"{code}: The requested translations input exceeded its supported size."
                    : $"{code}: The requested translations input exceeded the supported size of {limit} bytes.";
            }

            return message.Contains("reparse point", StringComparison.OrdinalIgnoreCase)
                ? $"{code}: A reparse point was rejected."
                : $"{code}: The requested translations operation was rejected.";
        }

        return message.Contains("reparse point", StringComparison.OrdinalIgnoreCase)
            ? $"A reparse point was rejected{SafeRelativePath(message)}."
            : fallback;
    }

    private static string LongestDigits(string value)
    {
        ReadOnlySpan<char> candidate = default;
        int start = 0;
        while (start < value.Length)
        {
            while (start < value.Length && !char.IsDigit(value[start])) start++;
            int end = start;
            while (end < value.Length && char.IsDigit(value[end])) end++;
            if (end - start > candidate.Length) candidate = value.AsSpan(start, end - start);
            start = end + 1;
        }

        return candidate.ToString();
    }

    private static string SafeRelativePath(string value)
    {
        const string prefix = "documents/";
        int start = value.LastIndexOf(prefix, StringComparison.Ordinal);
        if (start < 0) return string.Empty;
        int end = start;
        while (end < value.Length && value[end] is not '\'' and not '"' and not '\r' and not '\n') end++;
        return $" at {value.Substring(start, end - start)}";
    }

    private static int Run(ToolInvocation invocation, ToolOperationResult result, SourceUnitCache? units)
    {
        if (invocation.Command == ToolCommand.Help)
        {
            result.WriteOutput(UsageText(CommandPath.Root));
            return Success;
        }

        if (invocation.Command == ToolCommand.Schema)
        {
            IReadOnlyList<ToolArtifact> schemas = SchemaResources.Read();
            ArtifactFiles.WriteAtomically(invocation.OutputPath!, schemas);
            result.WriteOutputLine($"wrote {schemas.Count} schemas.");
            return Success;
        }

        if (invocation.Command == ToolCommand.Init)
        {
            TranslationProjectPlan plan = TranslationProjectScaffolder.Render(invocation.ProjectCreation!);
            string target = TranslationProjectWriter.Create(plan);
            result.WriteOutputLine($"created {plan.Files.Count} translation file(s) in {target}.");
            return Success;
        }

        if (invocation.Command == ToolCommand.ImportText)
            return TextCatalogImport.Run(invocation.ProjectPath!, invocation.TextImport!, invocation.DryRun, result,
                planned => RunSemanticV5(invocation with { Command = ToolCommand.Validate }, planned, result, units));

        CompilerInputs inputs = InputFiles.ReadProject(invocation.ProjectPath!);
        if (invocation.Command is ToolCommand.Validate or ToolCommand.Generate or ToolCommand.Verify)
            return RunSemanticV5(invocation, inputs, result, units);

        throw new ToolUsageException("The translations command is not supported.");
    }

    private static int RunSemanticV5(ToolInvocation invocation, CompilerInputs inputs, ToolOperationResult result, SourceUnitCache? units)
    {
        Rmf2ProjectCompilation compilation = units is null
            ? TranslationCompiler.CompileRmf2ProjectV5(inputs.Project, inputs.Messages)
            : TranslationCompiler.CompileRmf2ProjectV5(inputs.Project, units.Resolve(inputs.Messages), units.Options);
        compilation = TranslationManifestReader.WithManifestErrors(compilation, inputs.Project.Path, inputs.ManifestError);
        // In a build whose source generator reports the same compilation diagnostics, they are
        // omitted here so each appears once, with its help link, from the generator.
        if (!invocation.OmitSourceDiagnostics) WriteDiagnostics(compilation.Diagnostics, result);
        if (!compilation.Success || compilation.Project is null)
        {
            if (invocation.OmitSourceDiagnostics)
                result.SetHumanOutput("runic-translations: translation artifacts were not generated; the C# compilation reports the translation errors.\n");
            return Failed;
        }
        if (invocation.Command == ToolCommand.Validate)
        {
            result.WriteOutputLine($"validated 1 project(s) and {inputs.Messages.Count} source document(s).");
            return Success;
        }

        if (!Rmf2ProjectV5EmissionEligibility.CanEmit(compilation.Project))
        {
            WriteDiagnostics([new TranslationDiagnostic(
                Rmf2ProjectV5EmissionEligibility.DiagnosticId,
                TranslationDiagnosticSeverity.Error,
                Rmf2ProjectV5EmissionEligibility.Message,
                new TextSourceLocation(inputs.Project.Path, 0, 0, 1, 1, 1, 1))], result);
            return Failed;
        }

        IReadOnlyList<ToolArtifact> artifacts;
        try
        {
            artifacts = CompilerOutputAdapter.Render(compilation.Project, invocation.Emission);
        }
        catch (ToolDiagnosticException exception)
        {
            result.AddDiagnostic(ToolCodes.UnsupportedOutput, "unsupported-output", exception.Message, CommandDiagnosticSeverity.Error);
            return Failed;
        }
        if (invocation.Command == ToolCommand.Generate)
        {
            ArtifactFiles.WriteAtomically(invocation.OutputPath!, artifacts);
            result.WriteOutputLine($"generated {artifacts.Count} artifact(s).");
            return Success;
        }

        IReadOnlyList<string> differences = ArtifactFiles.Verify(invocation.OutputPath!, artifacts);
        if (differences.Count == 0)
        {
            result.WriteOutputLine($"verified {artifacts.Count} artifact(s).");
            return Success;
        }
        for (int index = 0; index < differences.Count; index++)
            result.AddDiagnostic(ToolCodes.Verify, "verify", $"verify: {differences[index]}", CommandDiagnosticSeverity.Error);
        // The sources are valid; the retained output is stale, so the check failed.
        result.ExitCategory = CommandExitCategory.CommandFailure;
        return Failed;
    }

    private static void WriteDiagnostics(IReadOnlyList<TranslationDiagnostic> diagnostics, ToolOperationResult result)
    {
        // Reported under their own RTR codes with structured locations, not as RCLI diagnostics.
        for (int index = 0; index < diagnostics.Count; index++)
        {
            TranslationDiagnostic diagnostic = diagnostics[index];
            TextSourceLocation location = diagnostic.Location;
            result.AddTranslationDiagnostic(new TranslationsToolDiagnostic(
                diagnostic.Id,
                diagnostic.Severity switch { TranslationDiagnosticSeverity.Error => "error", TranslationDiagnosticSeverity.Warning => "warning", _ => "info" },
                diagnostic.Message,
                location.Path.Replace('\\', '/'),
                location.Line,
                location.Column,
                location.EndLine,
                location.EndColumn,
                RunicReleaseLinks.Diagnostic(diagnostic.Id)));
        }
    }
}

/// <summary>
/// The human output of the translation commands' outcomes. Runic.CommandLine calls it only in human mode; JSON output
/// stays the default, with the translation diagnostics in the payload or <c>fault.data</c> under their RTR codes.
/// </summary>
internal static class ToolOutcomePresenter
{
    internal static async ValueTask PresentAsync(CommandOutcomePresentation<TranslationsToolCommandResult> presentation, CancellationToken cancellationToken)
    {
        CommandDescriptor command = presentation.Command;
        CommandExecutionContext context = presentation.Context;
        CommandOutcome<TranslationsToolCommandResult> outcome = presentation.Outcome;
        int exitCode = presentation.ExitCode;
        IReadOnlyList<CommandDiagnostic> diagnostics = presentation.Diagnostics;
        IReadOnlyList<TranslationsToolDiagnostic> translation = ToolHostOperations.TranslationDiagnostics(outcome);
        if (translation.Count != 0)
            await context.Console.WriteErrorAsync(ToolOperationResult.FormatHuman(translation).AsMemory(), cancellationToken).ConfigureAwait(false);
        await TranslationsToolCommandModule.PresentAsync(
            context.OutputMode,
            context.Console,
            context.Culture,
            context.Path.Count == 0 ? command.Name : context.Path.ToString(),
            exitCode,
            outcome.Succeeded ? outcome.Value : null,
            outcome.Fault,
            diagnostics,
            outcome.HumanOutput,
            SelectFailurePresentation(outcome, diagnostics, translation.Count != 0),
            cancellationToken).ConfigureAwait(false);
    }

    private static TranslationsToolFailurePresentation SelectFailurePresentation(
        CommandOutcome<TranslationsToolCommandResult> outcome,
        IReadOnlyList<CommandDiagnostic> diagnostics,
        bool hasTranslationDiagnostics)
    {
        if (outcome.Succeeded) return TranslationsToolFailurePresentation.Standard;
        // A verify difference is a command failure that reports its differences like diagnostics.
        if (outcome.ExitCategory != CommandExitCategory.Validation && !HasApplicationDiagnostic(diagnostics)) return TranslationsToolFailurePresentation.ErrorOnly;
        // Translation diagnostics are the report; the generic fault line would only repeat them.
        if (hasTranslationDiagnostics) return TranslationsToolFailurePresentation.DiagnosticsOnly;
        if (diagnostics.Count == 0 && outcome.HumanOutput is { Length: > 0 }) return TranslationsToolFailurePresentation.OutputOnly;
        return diagnostics.Count != 0 && HasApplicationDiagnostic(diagnostics)
            ? TranslationsToolFailurePresentation.DiagnosticsOnly
            : TranslationsToolFailurePresentation.Standard;
    }

    private static bool HasApplicationDiagnostic(IReadOnlyList<CommandDiagnostic> diagnostics)
    {
        foreach (CommandDiagnostic diagnostic in diagnostics)
        {
            if (diagnostic.Code is ToolCodes.Verify or ToolCodes.UnsupportedOutput or ToolCodes.Import) return true;
        }

        return false;
    }
}

internal sealed class ToolExecutionScopeFactory : ICommandExecutionScopeFactory
{
    internal static ToolExecutionScopeFactory Instance { get; } = new();
    public ICommandExecutionScope CreateScope() => ToolExecutionScope.Instance;

    private sealed class ToolExecutionScope : ICommandExecutionScope
    {
        internal static ToolExecutionScope Instance { get; } = new();
        public IServiceProvider Services { get; } = ToolServices.Instance;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ToolServices : IServiceProvider
    {
        internal static ToolServices Instance { get; } = new();
        public object? GetService(Type serviceType) => serviceType == typeof(ITranslationsToolCommandOperations) ? ToolHostOperations.Instance : null;
    }
}

internal sealed class ToolHostOperations : ITranslationsToolCommandOperations
{
    internal static ToolHostOperations Instance { get; } = new();

    public CommandOutcome<TranslationsToolCommandResult> Execute(TranslationsToolCommandRequest request)
    {
        ToolOperationResult result = request.Command switch
        {
            "init" => Program.ExecuteInit(request.Directory!, request.Catalog!, request.DefaultLocale!, request.Namespace!, request.ClassName!, request.Locales ?? [], request.NoStarter),
            "validate" => ExecuteCompilation(request, ToolCommand.Validate, null, ToolEmission.None),
            "generate" => ExecuteCompilation(request, ToolCommand.Generate, request.Output, Program.Emission(request.EmitCSharp, request.EmitJson, request.EmitEsm), OmitSourceDiagnostics()),
            "verify" => ExecuteCompilation(request, ToolCommand.Verify, request.Output, Program.Emission(request.EmitCSharp, request.EmitJson, request.EmitEsm), OmitSourceDiagnostics()),
            "schema" => Program.Execute(new ToolInvocation(ToolCommand.Schema, request.Output, ToolEmission.None, null)),
            "import-text" => ExecuteImport(request),
            _ => new ToolOperationResult { ExitCode = Program.UsageFailure, ExitCategory = CommandExitCategory.Usage },
        };
        var report = new TranslationsToolCommandResult(result.Output, string.Empty) { Diagnostics = result.TranslationDiagnostics };
        if (result.ExitCode == 0) return CommandOutcome.Success(report, result.Diagnostics);
        var fault = result.Fault ?? new CommandFault(ToolCodes.CommandFailed, "The translations command could not be completed.");
        string? humanOutput = result.HumanOutput ?? (result.Output.Length == 0 ? null : result.Output + Environment.NewLine);
        return result.TranslationDiagnostics.Count == 0
            ? CommandOutcome.Failure<TranslationsToolCommandResult>(result.ExitCategory, fault, result.Diagnostics, humanOutput)
            : CommandOutcome.Failure<TranslationsToolCommandResult>(
                result.ExitCategory,
                fault,
                result.Diagnostics,
                humanOutput,
                failureData: CommandFailureData.Create(PayloadType, report, TranslationsToolCommandJsonContext.Default.TranslationsToolCommandResult));
    }

    private const string PayloadType = "runic.translations.tool/1";

    private static ToolOperationResult ExecuteImport(TranslationsToolCommandRequest request)
    {
        string? project = string.IsNullOrWhiteSpace(request.Project) ? DefaultProject() : request.Project;
        if (project is null)
            return Program.Usage("no runic.json in the current directory or in ./translations; pass --project <directory>.", ToolCommand.ImportText);
        return Program.Execute(new ToolInvocation(ToolCommand.ImportText, null, ToolEmission.None, null, project, request.DryRun,
            TextImport: new TextImportRequest(request.Group ?? string.Empty, request.Tables ?? [], request.BaseTexts)));
    }

    // The translation diagnostics of a result: the payload of a success, fault.data of a failure.
    internal static IReadOnlyList<TranslationsToolDiagnostic> TranslationDiagnostics(CommandOutcome<TranslationsToolCommandResult> outcome)
    {
        if (outcome.Succeeded) return outcome.Value?.Diagnostics ?? [];
        return outcome.FailureData is { } data && data.TryGet(PayloadType, TranslationsToolCommandJsonContext.Default.TranslationsToolCommandResult, out TranslationsToolCommandResult? report) && report is not null
            ? report.Diagnostics
            : [];
    }

    private static ToolOperationResult ExecuteCompilation(
        TranslationsToolCommandRequest request,
        ToolCommand command,
        string? output,
        ToolEmission emission,
        bool omitSourceDiagnostics = false)
    {
        string? project = string.IsNullOrWhiteSpace(request.Project) ? DefaultProject() : request.Project;
        if (project is null)
            return Program.Usage("no runic.json in the current directory or in ./translations; pass --project <directory>.", command);
        return Program.Execute(new ToolInvocation(command, output, emission, null, project, OmitSourceDiagnostics: omitSourceDiagnostics));
    }

    // Runic.Translations.Build sets this when its source generator reports the same compilation
    // diagnostics, so a build shows each one once, with its help link.
    internal const string OmitSourceDiagnosticsVariable = "RUNIC_TRANSLATIONS_OMIT_SOURCE_DIAGNOSTICS";

    private static bool OmitSourceDiagnostics() => Environment.GetEnvironmentVariable(OmitSourceDiagnosticsVariable) == "true";

    // Without --project: the current directory when it holds runic.json, else the conventional
    // ./translations directory that the templates, init and Runic.Translations.Build use.
    private static string? DefaultProject()
    {
        if (File.Exists("runic.json")) return ".";
        return File.Exists(Path.Combine("translations", "runic.json")) ? "translations" : null;
    }
}

/// <summary>
/// The tool's own codes. Diagnostics use runic-translations' block <c>RCLI82xx</c> of the
/// Runic.CommandLine application range; the fault of a failed command is an <c>RTR</c> code. Each has an entry in
/// docs/guides/translations/diagnostics.md.
/// </summary>
internal static class ToolCodes
{
    /// <summary>The fault of a failed translations command; its diagnostics name the cause.</summary>
    internal const string CommandFailed = "RTR0087";
    /// <summary>The fault of an <c>import-text</c> whose input cannot be imported; <see cref="Import"/> lists the problems.</summary>
    internal const string ImportRejected = "RTR0088";
    internal const string Output = "RCLI8201";
    internal const string Diagnostic = "RCLI8202";
    internal const string Usage = "RCLI8203";
    internal const string Authoring = "RCLI8204";
    internal const string Io = "RCLI8205";
    internal const string Internal = "RCLI8206";
    internal const string Verify = "RCLI8211";
    internal const string UnsupportedOutput = "RCLI8213";
    internal const string Import = "RCLI8214";
}

internal sealed class ToolDiagnosticException : Exception
{
    internal ToolDiagnosticException(string message)
        : base(message)
    {
    }
}
