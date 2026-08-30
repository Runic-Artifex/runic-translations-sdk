using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Runic.CommandLine;
using Runic.Translations.Tooling;
using Runic.Translations.Authoring;
using Runic.Translations.Compiler;
using Runic.Translations.Compiler.Analysis;

namespace Runic.Translations.Tool;

internal static class Program
{
    private const int Success = 0;
    private const int DiagnosticFailure = 1;
    private const int InvocationFailure = 2;

    public static int Main(string[] arguments)
    {
        try
        {
            List<string> expanded = CommandLine.ExpandResponseFiles(arguments);
            ParseOutcome parsed = PortableCommandSyntaxAdapter.Instance.Parse(
                TranslationsToolCommandModule.CreateCatalog(),
                expanded.ToArray(),
                new ParseSettings(transportOutputOptionName: "--runic-output"));
            if (parsed.Kind == ParseOutcomeKind.Help)
            {
                Present(parsed, "runic-translations", Success, new(UsageText(), string.Empty), null, []);
                return Success;
            }

            if (parsed.Kind == ParseOutcomeKind.Version)
            {
                Present(parsed, "runic-translations", Success, new("runic-translations 0.2", string.Empty), null, []);
                return Success;
            }

            if (parsed.Kind == ParseOutcomeKind.Error)
            {
                CommandDiagnostic original = parsed.Diagnostics[0];
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
                    InvocationFailure,
                    null,
                    new CommandFault(diagnostic.Code, diagnostic.Message),
                    [diagnostic],
                    $"runic-translations: {message}\n{UsageText()}\n",
                    TranslationsToolFailurePresentation.ErrorOnly);
                return InvocationFailure;
            }

            var sink = new ToolExecutionSink();
            _ = new CommandExecutor(ToolExecutionScopeFactory.Instance, ToolExitCodePolicy.Instance).ExecuteAsync(
                new CommandExecutionRequest(parsed.Invocation!, SystemConsole.Instance, CultureInfo.InvariantCulture, "runic-translations"),
                sink).AsTask().GetAwaiter().GetResult();
            return sink.ExitCode;
        }
        catch (ToolOutputException exception)
        {
            return Fatal("RCLI9001", "tool-output", $"error {exception.Message}", DiagnosticFailure);
        }
        catch (ToolDiagnosticException exception)
        {
            return Fatal("RCLI9002", "tool-diagnostic", exception.Message, DiagnosticFailure);
        }
        catch (ToolUsageException exception)
        {
            return Fatal("RCLI9003", "tool-usage", exception.Message, InvocationFailure, true);
        }
        catch (TranslationAuthoringException exception)
        {
            return Fatal("RCLI9004", "authoring", exception.Message, InvocationFailure);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Fatal("RCLI9005", "io", exception.Message, InvocationFailure);
        }
        catch (Exception exception)
        {
            return Fatal("RCLI9006", "internal", $"internal failure: {exception.Message}", InvocationFailure);
        }
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
        "RCLI1003" when diagnostic.Arguments.Count >= 1 && diagnostic.Arguments[0] == "--documents" => "--documents requires at least one explicit path or glob.",
        "RCLI1003" when diagnostic.Arguments.Count >= 1 && diagnostic.Arguments[0] == "--sources" => "--sources requires at least one explicit path or glob.",
        "RCLI1003" when diagnostic.Arguments.Count >= 1 => $"{diagnostic.Arguments[0]} requires exactly one value.",
        "RCLI1003" => "an option requires a value.",
        "RCLI1005" => "a required argument is missing.",
        "RCLI1007" when diagnostic.Arguments.Count >= 1 => $"{diagnostic.Arguments[0]} may be specified only once.",
        "RCLI1007" => "an option may be specified only once.",
        "RCLI1012" when diagnostic.Arguments.Count >= 2 && diagnostic.Arguments[0] == "--source" && diagnostic.Arguments[1] == "import" => "import requires at least one --source <locale>=<file>.",
        "RCLI1012" when diagnostic.Arguments.Count >= 2 => $"{diagnostic.Arguments[1]} requires {diagnostic.Arguments[0]} {ExpectedValueShape(diagnostic.Arguments[0])}.",
        "RCLI1013" => "help does not accept additional arguments.",
        _ => "invalid command invocation.",
    };

    private static bool IsEmitSwitch(string option) => option is
        "--emit-csharp" or "--emit-json" or "--emit-typescript" or
        "--emit-template-manifest" or "--emit-esm" or "--emit-cpp";

    private static bool IsSchemaForbiddenOption(string option) =>
        option is "--catalog" or "--documents" || IsEmitSwitch(option);

    private static int Fatal(string code, string kind, string message, int exitCode, bool usage = false)
    {
        var diagnostic = new CommandDiagnostic(code, kind, message, CommandDiagnosticPhase.Execution, CommandDiagnosticSeverity.Error);
        string humanOutput = usage
            ? $"runic-translations: {message}\n{UsageText()}\n"
            : code is "RCLI9001" or "RCLI9002"
                ? message + "\n"
                : $"runic-translations: {message}\n";
        TranslationsToolCommandModule.PresentAsync(
            CommandOutputMode.Human,
            SystemConsole.Instance,
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
        "--documents" => "<path-or-glob...>",
        "--source" => "<file> or <locale>=<file>",
        "--catalog" => "<file>",
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
            SystemConsole.Instance,
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

    private static string UsageText()
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        WriteUsage(writer);
        return writer.ToString().TrimEnd();
    }


    internal static ToolOperationResult Execute(ToolInvocation invocation)
    {
        var result = new ToolOperationResult();
        try { result.ExitCode = Run(invocation, result); result.ExitCategory = result.ExitCode == Success ? CommandExitCategory.Success : CommandExitCategory.Validation; }
        catch (ToolOutputException exception) { result.SetHumanOutput($"error {exception.Message}\n"); result.AddDiagnostic("RCLI9001", "tool-output", SafeDomainMessage(exception.Message, "The requested output could not be written."), CommandDiagnosticSeverity.Error); result.ExitCode = DiagnosticFailure; result.ExitCategory = CommandExitCategory.CommandFailure; }
        catch (ToolDiagnosticException exception) { result.SetHumanOutput(exception.Message + "\n"); result.AddDiagnostic("RCLI9002", "tool-diagnostic", SafeDomainMessage(exception.Message, "The translations operation reported diagnostics."), CommandDiagnosticSeverity.Error); result.ExitCode = DiagnosticFailure; result.ExitCategory = CommandExitCategory.CommandFailure; }
        catch (ToolUsageException exception) { result.SetHumanOutput($"runic-translations: {exception.Message}\n{UsageText()}\n"); result.AddDiagnostic("RCLI9003", "tool-usage", SafeDomainMessage(exception.Message, "The translations command arguments are invalid."), CommandDiagnosticSeverity.Error); result.ExitCode = InvocationFailure; result.ExitCategory = CommandExitCategory.Usage; }
        catch (TranslationAuthoringException exception)
        {
            string message = exception.Message.Contains("already exists; no files were written", StringComparison.Ordinal)
                ? "already exists; no files were written."
                : exception.Message;
            result.SetHumanOutput($"runic-translations: {exception.Message}\n");
            result.AddDiagnostic("RCLI9004", "authoring", message, CommandDiagnosticSeverity.Error);
            result.ExitCode = InvocationFailure;
            result.ExitCategory = CommandExitCategory.Usage;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException) { result.SetHumanOutput($"runic-translations: {exception.Message}\n"); result.AddDiagnostic("RCLI9005", "io", "The translations input or output could not be accessed.", CommandDiagnosticSeverity.Error); result.ExitCode = InvocationFailure; result.ExitCategory = CommandExitCategory.Usage; }
        catch (Exception) { result.AddDiagnostic("RCLI9006", "internal", "The translations command encountered an internal failure.", CommandDiagnosticSeverity.Error); result.ExitCode = InvocationFailure; result.ExitCategory = CommandExitCategory.Usage; }
        return result;
    }

    internal static ToolOperationResult ExecuteInit(string directory, string catalog, string defaultLocale, string codeNamespace, string className, IReadOnlyList<string> locales, string? layer, bool noEsm, bool noStarter, bool vscode) =>
        Execute(new ToolInvocation(ToolCommand.Init, null, Array.Empty<string>(), null, ToolEmission.None,
            new TranslationProjectCreationRequest(directory, catalog, defaultLocale, codeNamespace, className,
                ParseLocales(locales), layer ?? "base", !noEsm, !noStarter, vscode), null, null));

    internal static ToolOperationResult ExecuteImport(IReadOnlyList<string> sourceValues, string catalog, string defaultLocale, string codeNamespace, string className, string output, string? format, bool dryRun, bool allowPartial) =>
        Execute(new ToolInvocation(ToolCommand.Import, null, Array.Empty<string>(), null, ToolEmission.None, null,
            new CatalogImportRequest(ParseSources(sourceValues), output, catalog, defaultLocale, codeNamespace, className, dryRun, allowPartial, format ?? "auto"), null));

    internal static ToolOperationResult ExecuteAnalyze(string catalog, IReadOnlyList<string> documents, IReadOnlyList<string> sources, string? format, bool failOnFindings, bool unsafeIgnoreDynamic, string? artifactFingerprint, string? artifactPath)
    {
        if (format is not null && format is not ("text" or "json")) return Usage("--format expects text or json.");
        if ((artifactFingerprint is null) != (artifactPath is null)) return Usage("--artifact-fingerprint and --artifact-path must be supplied together.");
        if (artifactFingerprint is not null && (artifactFingerprint.Length != 71 || !artifactFingerprint.StartsWith("sha256:", StringComparison.Ordinal) || !IsLowerHex(artifactFingerprint.AsSpan(7)))) return Usage("--artifact-fingerprint expects sha256: followed by 64 lowercase hexadecimal characters.");
        return Execute(new ToolInvocation(ToolCommand.Analyze, catalog, documents, null, ToolEmission.None, null, null,
            new CatalogAnalysisRequest(catalog, documents, sources, format == "json", failOnFindings,
                unsafeIgnoreDynamic ? TranslationDynamicUsagePolicy.IgnoreForDeletionCandidates : TranslationDynamicUsagePolicy.Conservative,
                artifactFingerprint, artifactPath)));
    }

    internal static ToolEmission Emission(bool csharp, bool json, bool typescript, bool templateManifest, bool esm, bool cpp)
    {
        ToolEmission result = ToolEmission.None;
        if (csharp) result |= ToolEmission.CSharp;
        if (json) result |= ToolEmission.Json;
        if (typescript) result |= ToolEmission.TypeScript;
        if (templateManifest) result |= ToolEmission.TemplateManifest;
        if (esm) result |= ToolEmission.Esm;
        if (cpp) result |= ToolEmission.Cpp;
        return result == ToolEmission.None ? ToolEmission.All : result;
    }

    internal static ToolOperationResult ExecuteTooling(Action<ToolOperationResult> operation)
    {
        var result = new ToolOperationResult();
        try { operation(result); }
        catch (TranslationInterchangeException exception)
        {
            result.AddDiagnostic("RCLI9007", "tooling", $"{exception.Code}: {exception.Message}", CommandDiagnosticSeverity.Error, exception.Code);
            result.ExitCode = DiagnosticFailure;
            result.ExitCategory = CommandExitCategory.Validation;
        }
        catch (SourceMigrationException exception)
        {
            result.AddDiagnostic("RCLI9008", "migration", $"{exception.Code}: {exception.Message}", CommandDiagnosticSeverity.Error, exception.Code);
            result.ExitCode = DiagnosticFailure;
            result.ExitCategory = CommandExitCategory.Validation;
        }
        catch (LocalePackBuildException exception)
        {
            result.AddDiagnostic("RCLI9009", "locale-pack", $"{exception.Code}: {exception.Message}", CommandDiagnosticSeverity.Error, exception.Code);
            result.ExitCode = DiagnosticFailure;
            result.ExitCategory = CommandExitCategory.Validation;
        }
        catch (ToolUsageException exception) { result.AddDiagnostic("RCLI9003", "tool-usage", exception.Message, CommandDiagnosticSeverity.Error); result.ExitCode = InvocationFailure; result.ExitCategory = CommandExitCategory.Usage; }
        catch (IOException exception) { result.AddDiagnostic("RCLI9005", "io", exception.Message, CommandDiagnosticSeverity.Error); result.ExitCode = InvocationFailure; result.ExitCategory = CommandExitCategory.Usage; }
        return result;
    }

    private static ToolOperationResult Usage(string message)
    {
        var result = new ToolOperationResult { ExitCode = InvocationFailure, ExitCategory = CommandExitCategory.Usage };
        result.AddDiagnostic("RCLI9003", "tool-usage", message, CommandDiagnosticSeverity.Error);
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

    private static List<CatalogImportSource> ParseSources(IReadOnlyList<string> values)
    {
        if (values.Count == 0) throw new ToolUsageException("import requires at least one --source <locale>=<file>.");
        var sources = new List<CatalogImportSource>(values.Count);
        foreach (string value in values)
        {
            int separator = value.IndexOf('=');
            if (separator <= 0 || separator == value.Length - 1) throw new ToolUsageException("--source expects <locale>=<file>.");
            sources.Add(new CatalogImportSource(value[..separator], value[(separator + 1)..]));
        }
        return sources;
    }

    private static bool IsLowerHex(ReadOnlySpan<char> value)
    {
        foreach (char character in value) if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')) return false;
        return true;
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

    private static int Run(ToolInvocation invocation, ToolOperationResult result)
    {
        if (invocation.Command == ToolCommand.Help)
        {
            result.WriteOutput(UsageText());
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

        if (invocation.Command == ToolCommand.Import)
        {
            CatalogImportResult importResult = CatalogImporter.Import(invocation.CatalogImport!);
            WriteImportDiagnostics(importResult.Diagnostics, result);
            if (importResult.Compilation is not null)
            {
                WriteDiagnostics(importResult.Compilation.Diagnostics, result);
            }

            if (invocation.CatalogImport!.DryRun)
            {
                result.WriteOutput(Encoding.UTF8.GetString(importResult.Report));
                return importResult.CanWrite ? Success : DiagnosticFailure;
            }

            if (!importResult.CanWrite)
            {
                result.AddDiagnostic("RCLI9010", "import", "no files were written; see the diagnostics or run with --dry-run for the full report.", CommandDiagnosticSeverity.Error);
                return DiagnosticFailure;
            }

            ArtifactFiles.WriteAtomically(invocation.CatalogImport.OutputPath, importResult.Artifacts);
            result.WriteOutputLine($"imported {importResult.Artifacts.Count - 1} catalog file(s) and wrote runic-import-report.json.");
            return Success;
        }

        CompilerInputs inputs = InputFiles.Read(invocation.CatalogPath!, invocation.DocumentPatterns);
        TranslationCompilation compilation = TranslationCompiler.Compile(
            [inputs.Catalog],
            inputs.Documents);
        WriteDiagnostics(compilation.Diagnostics, result);
        if (!compilation.Success)
        {
            return DiagnosticFailure;
        }

        if (invocation.Command == ToolCommand.Validate)
        {
            result.WriteOutputLine($"validated {compilation.Catalogs.Count} catalog(s) and {inputs.Documents.Count} document(s).");
            return Success;
        }

        if (invocation.Command == ToolCommand.Analyze)
        {
            CatalogAnalysisRequest request = invocation.CatalogAnalysis!;
            CompiledTextCatalog catalog = compilation.Catalogs[0];
            IReadOnlyList<TranslationUsageSource> sources = InputFiles.ReadUsageSources(request.SourcePatterns, catalog.Id);
            var options = new TranslationAnalysisOptions(request.DynamicUsagePolicy);
            TranslationAnalysisReport report = request.ArtifactFingerprint is null
                ? TranslationAnalyzer.Analyze(compilation, sources, options)
                : TranslationAnalyzer.Analyze(
                    compilation,
                    sources,
                    [new TranslationArtifactSnapshot(catalog.Id, request.ArtifactFingerprint, request.ArtifactPath!)],
                    options);
            result.WriteOutput(request.Json
                ? TranslationAnalysisRenderer.RenderJson(report)
                : TranslationAnalysisRenderer.RenderText(report));
            return request.FailOnFindings && report.HasFindings ? DiagnosticFailure : Success;
        }

        IReadOnlyList<ToolArtifact> artifacts = CompilerOutputAdapter.Render(compilation.Catalogs, invocation.Emission);
        if (invocation.Command == ToolCommand.Generate)
        {
            ArtifactFiles.WriteAtomically(invocation.OutputPath!, artifacts);
            result.WriteOutputLine($"generated {artifacts.Count} artifact(s).");
            return Success;
        }

        IReadOnlyList<string> differences = ArtifactFiles.Verify(invocation.OutputPath!, artifacts);
        if (differences.Count != 0)
        {
            for (int index = 0; index < differences.Count; index++)
            {
                result.AddDiagnostic("RCLI9011", "verify", $"verify: {differences[index]}", CommandDiagnosticSeverity.Error);
            }

            return DiagnosticFailure;
        }

        result.WriteOutputLine($"verified {artifacts.Count} artifact(s).");
        return Success;
    }

    private static void WriteDiagnostics(IReadOnlyList<TranslationDiagnostic> diagnostics, ToolOperationResult result)
    {
        for (int index = 0; index < diagnostics.Count; index++)
        {
            TranslationDiagnostic diagnostic = diagnostics[index];
            TextSourceLocation location = diagnostic.Location;
            string path = location.Path.Replace('\\', '/');
            string severity = diagnostic.Severity == TranslationDiagnosticSeverity.Error ? "error" : "warning";
            result.AddDiagnostic(
                "RCLI9012",
                "translation-diagnostic",
                $"{path}({location.Line},{location.Column},{location.EndLine},{location.EndColumn}): {severity} {diagnostic.Id}: {diagnostic.Message}",
                diagnostic.Severity == TranslationDiagnosticSeverity.Error ? CommandDiagnosticSeverity.Error : CommandDiagnosticSeverity.Warning,
                diagnostic.Id);
        }
    }

    private static void WriteImportDiagnostics(IReadOnlyList<CatalogImportDiagnostic> diagnostics, ToolOperationResult result)
    {
        for (int index = 0; index < diagnostics.Count; index++)
        {
            CatalogImportDiagnostic diagnostic = diagnostics[index];
            string path = string.IsNullOrEmpty(diagnostic.Path) ? "import" : diagnostic.Path.Replace('\\', '/');
            string key = diagnostic.Key is null ? string.Empty : $" [{diagnostic.Key}]";
            result.AddDiagnostic(
                "RCLI9013",
                "import-diagnostic",
                $"{path}: {diagnostic.Severity} {diagnostic.Code}{key}: {diagnostic.Message}",
                diagnostic.Severity.Equals("warning", StringComparison.OrdinalIgnoreCase) ? CommandDiagnosticSeverity.Warning : CommandDiagnosticSeverity.Error,
                diagnostic.Code);
        }
    }

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("Usage:");
        writer.WriteLine("  runic-translations init --directory <directory> --catalog <id> --default-locale <tag> --namespace <namespace> --class <name> [init-options]");
        writer.WriteLine("  runic-translations validate --catalog <file> --documents <path-or-glob...>");
        writer.WriteLine("  runic-translations generate --catalog <file> --documents <path-or-glob...> --output <directory> [emit-switches]");
        writer.WriteLine("  runic-translations verify --catalog <file> --documents <path-or-glob...> --output <directory> [emit-switches]");
        writer.WriteLine("  runic-translations schema --output <directory>");
        writer.WriteLine("  runic-translations import --source <locale>=<file>... --catalog <id> --default-locale <tag> --namespace <namespace> --class <name> --output <directory> [--format auto|json|inlang] [--dry-run] [--allow-partial]");
        writer.WriteLine("  runic-translations analyze --catalog <file> --documents <path-or-glob...> [--sources <path-or-glob...>] [analysis-options]");
        writer.WriteLine("  runic-translations inspect --source <file>");
        writer.WriteLine("  runic-translations migrate --source <v2-file> --output <v3-file> [--report <file>]");
        writer.WriteLine("  runic-translations xliff-export --catalog <file> --documents <path-or-glob...> --output <directory> [--review <file>]");
        writer.WriteLine("  runic-translations xliff-import --source <file> --output <directory> [--review-output <file>]");
        writer.WriteLine("  runic-translations review-export --catalog <id> --output <file>");
        writer.WriteLine("  runic-translations review-import|review-report --source <file>");
        writer.WriteLine();
        writer.WriteLine("Arguments may be read from a UTF-8 response file with @<file>.");
        writer.WriteLine("Framework transport uses --runic-output human|json; --output remains the tool destination option.");
        writer.WriteLine("Init options: --locale <tag>[:<fallback>] (repeatable) --layer <name> --no-esm --no-starter --vscode.");
        writer.WriteLine("Emit switches: --emit-csharp --emit-json --emit-typescript --emit-template-manifest --emit-esm --emit-cpp.");
        writer.WriteLine("Analysis options: --format text|json --fail-on-findings --unsafe-ignore-dynamic --artifact-fingerprint <sha256:...> --artifact-path <path>.");
        writer.WriteLine("With no emit switches, generate and verify use all output groups.");
        writer.WriteLine("Exit codes: 0 success; 1 validation or verification diagnostics; 2 invocation or operational failure.");
    }
}

internal sealed class ToolExecutionSink : ICommandOutcomeSink
{
    internal int ExitCode { get; private set; } = 2;

    public ValueTask WriteAsync<T>(CommandDescriptor command, CommandExecutionContext context, CommandOutcome<T> outcome, ICommandResultCodec<T> codec, int exitCode, IReadOnlyList<CommandDiagnostic> diagnostics, CancellationToken cancellationToken)
    {
        ExitCode = exitCode;
        if (typeof(T) == typeof(TranslationsToolCommandResult))
        {
            var translationsOutcome = (CommandOutcome<TranslationsToolCommandResult>)(object)outcome;
            TranslationsToolFailurePresentation presentation = SelectFailurePresentation(translationsOutcome, diagnostics);
            return TranslationsToolCommandModule.PresentAsync(
                context.OutputMode,
                context.Console,
                context.Culture,
                context.Path.Count == 0 ? command.Name : context.Path.ToString(),
                exitCode,
                translationsOutcome.IsSuccess ? translationsOutcome.Value : null,
                translationsOutcome.Fault,
                diagnostics,
                translationsOutcome.HumanOutput,
                presentation,
                cancellationToken);
        }

        return new CommandOutputDispatcher().WriteAsync(command, context, outcome, codec, exitCode, diagnostics, cancellationToken);
    }

    private static TranslationsToolFailurePresentation SelectFailurePresentation(
        CommandOutcome<TranslationsToolCommandResult> outcome,
        IReadOnlyList<CommandDiagnostic> diagnostics)
    {
        if (outcome.IsSuccess) return TranslationsToolFailurePresentation.Standard;
        if (outcome.ExitCategory != CommandExitCategory.Validation) return TranslationsToolFailurePresentation.ErrorOnly;
        if (diagnostics.Count == 0 && outcome.HumanOutput is { Length: > 0 }) return TranslationsToolFailurePresentation.OutputOnly;
        return diagnostics.Count != 0 && HasLegacyToolDiagnostic(diagnostics)
            ? TranslationsToolFailurePresentation.LegacyDiagnostics
            : TranslationsToolFailurePresentation.Standard;
    }

    private static bool HasLegacyToolDiagnostic(IReadOnlyList<CommandDiagnostic> diagnostics)
    {
        foreach (CommandDiagnostic diagnostic in diagnostics)
        {
            if (diagnostic.Code is "RCLI9010" or "RCLI9011" or "RCLI9012" or "RCLI9013") return true;
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
            "init" => Program.ExecuteInit(request.Directory!, request.Catalog!, request.DefaultLocale!, request.Namespace!, request.ClassName!, request.Documents, request.Layer, request.EmitCSharp, request.EmitJson, request.EmitTypeScript),
            "validate" => Program.Execute(new ToolInvocation(ToolCommand.Validate, request.Catalog, request.Documents, null, ToolEmission.None, null, null, null)),
            "generate" => Program.Execute(new ToolInvocation(ToolCommand.Generate, request.Catalog, request.Documents, request.Output, Program.Emission(request.EmitCSharp, request.EmitJson, request.EmitTypeScript, request.EmitTemplateManifest, request.EmitEsm, request.EmitCpp), null, null, null)),
            "verify" => Program.Execute(new ToolInvocation(ToolCommand.Verify, request.Catalog, request.Documents, request.Output, Program.Emission(request.EmitCSharp, request.EmitJson, request.EmitTypeScript, request.EmitTemplateManifest, request.EmitEsm, request.EmitCpp), null, null, null)),
            "schema" => Program.Execute(new ToolInvocation(ToolCommand.Schema, null, Array.Empty<string>(), request.Output, ToolEmission.None, null, null, null)),
            "import" => Program.ExecuteImport(request.Sources!, request.Catalog!, request.DefaultLocale!, request.Namespace!, request.ClassName!, request.Output!, request.Format, request.FlagOne == true, request.FlagTwo == true),
            "analyze" => Program.ExecuteAnalyze(request.Catalog!, request.Documents, request.Sources ?? Array.Empty<string>(), request.Format, request.FlagOne == true, request.FlagTwo == true, request.ArtifactFingerprint, request.ArtifactPath),
            "inspect" => Program.ExecuteTooling(result => ToolingOperations.Inspect(request.Sources![0], result)),
            "migrate" => Program.ExecuteTooling(result => ToolingOperations.Migrate(request.Sources![0], request.Output!, request.AuxiliaryPath, result)),
            "xliff-export" => Program.ExecuteTooling(result => ToolingOperations.ExportXliff(request.Catalog!, request.Documents, request.Output!, request.AuxiliaryPath, result)),
            "xliff-import" => Program.ExecuteTooling(result => ToolingOperations.ImportXliff(request.Sources![0], request.Output!, request.AuxiliaryPath, result)),
            "review-export" => Program.ExecuteTooling(result => ToolingOperations.ExportReview(request.Catalog!, request.Output!, result)),
            "review-import" => Program.ExecuteTooling(result => ToolingOperations.ImportReview(request.Sources![0], result)),
            "review-report" => Program.ExecuteTooling(result => ToolingOperations.ReportReview(request.Sources![0], result)),
            "locale-pack" => Program.ExecuteTooling(result => ToolingOperations.BuildLocalePack(request.Catalog!, request.Documents, request.Output!, result)),
            _ => new ToolOperationResult { ExitCode = 2 },
        };
        return result.ExitCode == 0
            ? CommandOutcome.Success(new TranslationsToolCommandResult(result.Output, string.Empty), result.Diagnostics)
            : CommandOutcome.Failure<TranslationsToolCommandResult>(
                result.ExitCategory,
                new CommandFault("RCLI9000", "The translations command could not be completed."),
                result.Diagnostics,
                result.HumanOutput ?? (result.Output.Length == 0 ? null : result.Output + Environment.NewLine));
    }
}

internal sealed class ToolExitCodePolicy : IExitCodePolicy
{
    internal static ToolExitCodePolicy Instance { get; } = new();
    public int GetExitCode(CommandExitCategory category) => category switch
    {
        CommandExitCategory.Success => 0,
        CommandExitCategory.Usage or CommandExitCategory.Unavailable or CommandExitCategory.HostFailure => 2,
        _ => 1,
    };
}

internal sealed class SystemConsole : ICommandConsole
{
    internal static SystemConsole Instance { get; } = new();
    public bool IsInteractive => !Console.IsInputRedirected && !Console.IsOutputRedirected;
    public bool IsInputRedirected => Console.IsInputRedirected;
    public bool IsOutputRedirected => Console.IsOutputRedirected;
    public bool IsErrorRedirected => Console.IsErrorRedirected;
    public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Console.ReadLine());
    public ValueTask WriteOutAsync(ReadOnlyMemory<char> value, CancellationToken cancellationToken) => new(Console.Out.WriteAsync(value, cancellationToken));
    public ValueTask WriteOutBytesAsync(ReadOnlyMemory<byte> value, CancellationToken cancellationToken) => Console.OpenStandardOutput().WriteAsync(value, cancellationToken);
    public ValueTask WriteErrorAsync(ReadOnlyMemory<char> value, CancellationToken cancellationToken) => new(Console.Error.WriteAsync(value, cancellationToken));
}

internal sealed class ToolDiagnosticException : Exception
{
    internal ToolDiagnosticException(string message)
        : base(message)
    {
    }
}
