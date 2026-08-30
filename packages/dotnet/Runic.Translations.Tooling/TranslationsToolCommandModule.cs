using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Runic.CommandLine;
using Runic.CommandLine.Generated;

namespace Runic.Translations.Tooling;

/// <summary>Generated command catalog that a standalone tool or <c>dotnet runic</c> host can compose.</summary>
public static class TranslationsToolCommandModule
{
    /// <summary>Creates the single parser-neutral translations command catalog.</summary>
    public static CommandCatalog CreateCatalog() => GeneratedCommandCatalog.Create();

    /// <summary>Writes a catalog-level response through the standard command-line transport.</summary>
    public static ValueTask PresentAsync(
        CommandOutputMode outputMode,
        ICommandConsole console,
        CultureInfo culture,
        string command,
        int exitCode,
        TranslationsToolCommandResult? result,
        CommandFault? fault,
        IReadOnlyList<CommandDiagnostic> diagnostics,
        string? humanFailureOutput = null,
        TranslationsToolFailurePresentation failurePresentation = TranslationsToolFailurePresentation.Standard,
        CancellationToken cancellationToken = default)
    {
        CommandDescriptor commandDescriptor = CreateCatalog().Commands[0];
        ICommandConsole presentationConsole = outputMode == CommandOutputMode.Human
            ? failurePresentation switch
            {
                TranslationsToolFailurePresentation.ErrorOnly => new ErrorOnlyHumanConsole(console),
                TranslationsToolFailurePresentation.OutputOnly => new OutputOnlyHumanConsole(console),
                TranslationsToolFailurePresentation.LegacyDiagnostics => new LegacyDiagnosticsHumanConsole(console, diagnostics),
                _ => console,
            }
            : console;
        var context = new CommandExecutionContext(
            EmptyServices.Instance,
            presentationConsole,
            new CommandPath([command]),
            outputMode,
            culture,
            "runic-translations");
        CommandOutcome<TranslationsToolCommandResult> outcome = fault is null
            ? CommandOutcome.Success(result!, null)
            : CommandOutcome.Failure<TranslationsToolCommandResult>(
                CommandExitCategory.Usage,
                fault,
                null,
                humanFailureOutput);
        IReadOnlyList<CommandDiagnostic> responseDiagnostics = outputMode == CommandOutputMode.Human && failurePresentation is TranslationsToolFailurePresentation.ErrorOnly or TranslationsToolFailurePresentation.OutputOnly
            ? []
            : diagnostics;
        return new CommandOutputDispatcher().WriteAsync(
            commandDescriptor,
            context,
            outcome,
            ResultCodec.Instance,
            exitCode,
            responseDiagnostics,
            cancellationToken);
    }

    [Command("init")][CommandResult("runic.translations.tool/1", typeof(TranslationsToolCommandJsonContext))]
    public static CommandOutcome<TranslationsToolCommandResult> Init([FromServices] ITranslationsToolCommandOperations operations, [Option("--directory", Required = true)] string directory, [Option("--catalog", Required = true)] string catalog, [Option("--default-locale", Required = true)] string defaultLocale, [Option("--namespace", Required = true)] string codeNamespace, [Option("--class", Required = true)] string className, [Option("--locale", AllowMultipleValues = true)] IReadOnlyList<string> locales, [Option("--no-esm")] bool noEsm, [Option("--no-starter")] bool noStarter, [Option("--vscode")] bool vscode, [Option("--layer")] string? layer = null) => operations.Execute(new("init", catalog, locales, null, null, directory, defaultLocale, codeNamespace, className, layer, null, null, null, null, noEsm, noStarter, vscode, false, false, false, null, null, null, null));

    [Command("validate")][CommandResult("runic.translations.tool/1", typeof(TranslationsToolCommandJsonContext))]
    public static CommandOutcome<TranslationsToolCommandResult> Validate([FromServices] ITranslationsToolCommandOperations operations, [Option("--catalog", Required = true)] string catalog, [Option("--documents", AllowMultipleValues = true, AllowMultipleOccurrences = false, Required = true)] IReadOnlyList<string> documents) => operations.Execute(new("validate", catalog, documents, null, null, null, null, null, null, null, null, null, null, null, false, false, false, false, false, false, null, null, null, null));

    [Command("generate")][CommandResult("runic.translations.tool/1", typeof(TranslationsToolCommandJsonContext))]
    public static CommandOutcome<TranslationsToolCommandResult> Generate([FromServices] ITranslationsToolCommandOperations operations, [Option("--catalog", Required = true)] string catalog, [Option("--documents", AllowMultipleValues = true, AllowMultipleOccurrences = false, Required = true)] IReadOnlyList<string> documents, [Option("--output", Required = true)] string output, [Option("--emit-csharp")] bool csharp, [Option("--emit-json")] bool json, [Option("--emit-typescript")] bool typescript, [Option("--emit-template-manifest")] bool manifest, [Option("--emit-esm")] bool esm, [Option("--emit-cpp")] bool cpp) => operations.Execute(new("generate", catalog, documents, output, null, null, null, null, null, null, null, null, null, null, csharp, json, typescript, manifest, esm, cpp, null, null, null, null));

    [Command("verify")][CommandResult("runic.translations.tool/1", typeof(TranslationsToolCommandJsonContext))]
    public static CommandOutcome<TranslationsToolCommandResult> Verify([FromServices] ITranslationsToolCommandOperations operations, [Option("--catalog", Required = true)] string catalog, [Option("--documents", AllowMultipleValues = true, AllowMultipleOccurrences = false, Required = true)] IReadOnlyList<string> documents, [Option("--output", Required = true)] string output, [Option("--emit-csharp")] bool csharp, [Option("--emit-json")] bool json, [Option("--emit-typescript")] bool typescript, [Option("--emit-template-manifest")] bool manifest, [Option("--emit-esm")] bool esm, [Option("--emit-cpp")] bool cpp) => operations.Execute(new("verify", catalog, documents, output, null, null, null, null, null, null, null, null, null, null, csharp, json, typescript, manifest, esm, cpp, null, null, null, null));

    [Command("schema")][CommandResult("runic.translations.tool/1", typeof(TranslationsToolCommandJsonContext))]
    public static CommandOutcome<TranslationsToolCommandResult> Schema([FromServices] ITranslationsToolCommandOperations operations, [Option("--output", Required = true)] string output) => operations.Execute(new("schema", null, [], output, null, null, null, null, null, null, null, null, null, null, false, false, false, false, false, false, null, null, null, null));

    [Command("import")][CommandResult("runic.translations.tool/1", typeof(TranslationsToolCommandJsonContext))]
    public static CommandOutcome<TranslationsToolCommandResult> Import([FromServices] ITranslationsToolCommandOperations operations, [Option("--source", AllowMultipleValues = true, Required = true)] IReadOnlyList<string> source, [Option("--catalog", Required = true)] string catalog, [Option("--default-locale", Required = true)] string defaultLocale, [Option("--namespace", Required = true)] string codeNamespace, [Option("--class", Required = true)] string className, [Option("--output", Required = true)] string output, [Option("--dry-run")] bool dryRun, [Option("--allow-partial")] bool allowPartial, [Option("--format")] string? format = null) => operations.Execute(new("import", catalog, [], output, source, null, defaultLocale, codeNamespace, className, null, format, null, null, null, false, false, false, false, false, false, dryRun, allowPartial, null, null));

    [Command("analyze")][CommandResult("runic.translations.tool/1", typeof(TranslationsToolCommandJsonContext))]
    public static CommandOutcome<TranslationsToolCommandResult> Analyze([FromServices] ITranslationsToolCommandOperations operations, [Option("--catalog", Required = true)] string catalog, [Option("--documents", AllowMultipleValues = true, AllowMultipleOccurrences = false, Required = true)] IReadOnlyList<string> documents, [Option("--sources", AllowMultipleValues = true, AllowMultipleOccurrences = false)] IReadOnlyList<string> sources, [Option("--fail-on-findings")] bool fail, [Option("--unsafe-ignore-dynamic")] bool ignore, [Option("--artifact-fingerprint")] string? fingerprint = null, [Option("--artifact-path")] string? artifactPath = null, [Option("--format")] string? format = null) => operations.Execute(new("analyze", catalog, documents, null, sources, null, null, null, null, null, format, fingerprint, artifactPath, null, false, false, false, false, false, false, fail, ignore, null, null));

    [Command("inspect")][CommandResult("runic.translations.tool/1", typeof(TranslationsToolCommandJsonContext))]
    public static CommandOutcome<TranslationsToolCommandResult> Inspect([FromServices] ITranslationsToolCommandOperations operations, [Option("--source", Required = true)] string source) => operations.Execute(new("inspect", null, [], null, [source], null, null, null, null, null, null, null, null, null, false, false, false, false, false, false, false, false, null, null));

    [Command("migrate")][CommandResult("runic.translations.tool/1", typeof(TranslationsToolCommandJsonContext))]
    public static CommandOutcome<TranslationsToolCommandResult> Migrate([FromServices] ITranslationsToolCommandOperations operations, [Option("--source", Required = true)] string source, [Option("--output", Required = true)] string output, [Option("--report")] string? report = null) => operations.Execute(new("migrate", null, [], output, [source], null, null, null, null, null, null, null, null, report, false, false, false, false, false, false, false, false, null, null));

    [Command("xliff-export")][CommandResult("runic.translations.tool/1", typeof(TranslationsToolCommandJsonContext))]
    public static CommandOutcome<TranslationsToolCommandResult> XliffExport([FromServices] ITranslationsToolCommandOperations operations, [Option("--catalog", Required = true)] string catalog, [Option("--documents", AllowMultipleValues = true, AllowMultipleOccurrences = false, Required = true)] IReadOnlyList<string> documents, [Option("--output", Required = true)] string output, [Option("--review")] string? review = null) => operations.Execute(new("xliff-export", catalog, documents, output, null, null, null, null, null, null, null, null, null, review, false, false, false, false, false, false, false, false, null, null));

    [Command("xliff-import")][CommandResult("runic.translations.tool/1", typeof(TranslationsToolCommandJsonContext))]
    public static CommandOutcome<TranslationsToolCommandResult> XliffImport([FromServices] ITranslationsToolCommandOperations operations, [Option("--source", Required = true)] string source, [Option("--output", Required = true)] string output, [Option("--review-output")] string? review = null) => operations.Execute(new("xliff-import", null, [], output, [source], null, null, null, null, null, null, null, null, review, false, false, false, false, false, false, false, false, null, null));

    [Command("review-export")][CommandResult("runic.translations.tool/1", typeof(TranslationsToolCommandJsonContext))]
    public static CommandOutcome<TranslationsToolCommandResult> ReviewExport([FromServices] ITranslationsToolCommandOperations operations, [Option("--catalog", Required = true)] string catalog, [Option("--output", Required = true)] string output) => operations.Execute(new("review-export", catalog, [], output, null, null, null, null, null, null, null, null, null, null, false, false, false, false, false, false, false, false, null, null));

    [Command("review-import")][CommandResult("runic.translations.tool/1", typeof(TranslationsToolCommandJsonContext))]
    public static CommandOutcome<TranslationsToolCommandResult> ReviewImport([FromServices] ITranslationsToolCommandOperations operations, [Option("--source", Required = true)] string source) => operations.Execute(new("review-import", null, [], null, [source], null, null, null, null, null, null, null, null, null, false, false, false, false, false, false, false, false, null, null));

    [Command("review-report")][CommandResult("runic.translations.tool/1", typeof(TranslationsToolCommandJsonContext))]
    public static CommandOutcome<TranslationsToolCommandResult> ReviewReport([FromServices] ITranslationsToolCommandOperations operations, [Option("--source", Required = true)] string source) => operations.Execute(new("review-report", null, [], null, [source], null, null, null, null, null, null, null, null, null, false, false, false, false, false, false, false, false, null, null));

    [Command("locale-pack")][CommandResult("runic.translations.tool/1", typeof(TranslationsToolCommandJsonContext))]
    public static CommandOutcome<TranslationsToolCommandResult> LocalePack([FromServices] ITranslationsToolCommandOperations operations, [Option("--catalog", Required = true)] string catalog, [Option("--documents", AllowMultipleValues = true, AllowMultipleOccurrences = false, Required = true)] IReadOnlyList<string> documents, [Option("--output", Required = true)] string output) => operations.Execute(new("locale-pack", catalog, documents, output, null, null, null, null, null, null, null, null, null, null, false, false, false, false, false, false, false, false, null, null));

    private sealed class ResultCodec : ICommandResultCodec<TranslationsToolCommandResult>
    {
        internal static ResultCodec Instance { get; } = new();

        public string PayloadType => "runic.translations.tool/1";

        public System.Text.Json.Serialization.Metadata.JsonTypeInfo<TranslationsToolCommandResult> TypeInfo =>
            TranslationsToolCommandJsonContext.Default.TranslationsToolCommandResult;

        public ValueTask WriteHumanAsync(
            TranslationsToolCommandResult value,
            ICommandConsole console,
            CultureInfo culture,
            CancellationToken cancellationToken) =>
            console.WriteOutAsync((value.Output + "\n").AsMemory(), cancellationToken);
    }

    private sealed class EmptyServices : IServiceProvider
    {
        internal static EmptyServices Instance { get; } = new();

        public object? GetService(Type serviceType) => null;
    }

    private sealed class ErrorOnlyHumanConsole(ICommandConsole inner) : ICommandConsole
    {
        public bool IsInteractive => inner.IsInteractive;
        public bool IsInputRedirected => inner.IsInputRedirected;
        public bool IsOutputRedirected => inner.IsOutputRedirected;
        public bool IsErrorRedirected => inner.IsErrorRedirected;
        public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken) => inner.ReadLineAsync(cancellationToken);
        public ValueTask WriteOutAsync(ReadOnlyMemory<char> value, CancellationToken cancellationToken) => inner.WriteErrorAsync(value, cancellationToken);
        public ValueTask WriteOutBytesAsync(ReadOnlyMemory<byte> value, CancellationToken cancellationToken) => inner.WriteErrorAsync(Encoding.UTF8.GetString(value.Span).AsMemory(), cancellationToken);
        public ValueTask WriteErrorAsync(ReadOnlyMemory<char> value, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class OutputOnlyHumanConsole(ICommandConsole inner) : ICommandConsole
    {
        public bool IsInteractive => inner.IsInteractive;
        public bool IsInputRedirected => inner.IsInputRedirected;
        public bool IsOutputRedirected => inner.IsOutputRedirected;
        public bool IsErrorRedirected => inner.IsErrorRedirected;
        public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken) => inner.ReadLineAsync(cancellationToken);
        public ValueTask WriteOutAsync(ReadOnlyMemory<char> value, CancellationToken cancellationToken) => inner.WriteOutAsync(value, cancellationToken);
        public ValueTask WriteOutBytesAsync(ReadOnlyMemory<byte> value, CancellationToken cancellationToken) => inner.WriteOutBytesAsync(value, cancellationToken);
        public ValueTask WriteErrorAsync(ReadOnlyMemory<char> value, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class LegacyDiagnosticsHumanConsole : ICommandConsole
    {
        private readonly ICommandConsole _inner;
        private readonly string _legacyDiagnostics;
        private bool _written;

        internal LegacyDiagnosticsHumanConsole(ICommandConsole inner, IReadOnlyList<CommandDiagnostic> diagnostics)
        {
            _inner = inner;
            var text = new StringBuilder();
            foreach (CommandDiagnostic diagnostic in diagnostics)
            {
                text.Append(diagnostic.Message);
                text.Append('\n');
            }

            _legacyDiagnostics = text.ToString();
        }

        public bool IsInteractive => _inner.IsInteractive;
        public bool IsInputRedirected => _inner.IsInputRedirected;
        public bool IsOutputRedirected => _inner.IsOutputRedirected;
        public bool IsErrorRedirected => _inner.IsErrorRedirected;
        public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken) => _inner.ReadLineAsync(cancellationToken);
        public ValueTask WriteOutAsync(ReadOnlyMemory<char> value, CancellationToken cancellationToken) => _inner.WriteOutAsync(value, cancellationToken);
        public ValueTask WriteOutBytesAsync(ReadOnlyMemory<byte> value, CancellationToken cancellationToken) => _inner.WriteOutBytesAsync(value, cancellationToken);

        public ValueTask WriteErrorAsync(ReadOnlyMemory<char> value, CancellationToken cancellationToken)
        {
            if (_written || _legacyDiagnostics.Length == 0) return ValueTask.CompletedTask;
            _written = true;
            return _inner.WriteErrorAsync(_legacyDiagnostics.AsMemory(), cancellationToken);
        }
    }
}

/// <summary>Controls the human-only projection of a bounded command failure.</summary>
public enum TranslationsToolFailurePresentation
{
    /// <summary>Uses the normal standard dispatcher presentation.</summary>
    Standard,
    /// <summary>Writes application-owned failure text to standard error only.</summary>
    ErrorOnly,
    /// <summary>Writes application-owned report text to standard output only.</summary>
    OutputOnly,
    /// <summary>Writes application-owned report text and legacy diagnostics through their original streams.</summary>
    LegacyDiagnostics,
}

/// <summary>Host operation bridge for the composable generated command catalog.</summary>
public interface ITranslationsToolCommandOperations { CommandOutcome<TranslationsToolCommandResult> Execute(TranslationsToolCommandRequest request); }

/// <summary>Typed values bound by the generated catalog before host operation policy runs.</summary>
public sealed record TranslationsToolCommandRequest(string Command, string? Catalog, IReadOnlyList<string> Documents, string? Output, IReadOnlyList<string>? Sources, string? Directory, string? DefaultLocale, string? Namespace, string? ClassName, string? Layer, string? Format, string? ArtifactFingerprint, string? ArtifactPath, string? AuxiliaryPath, bool EmitCSharp, bool EmitJson, bool EmitTypeScript, bool EmitTemplateManifest, bool EmitEsm, bool EmitCpp, bool? FlagOne, bool? FlagTwo, string? UnusedOne, string? UnusedTwo);

/// <summary>Portable command payload; the standard dispatcher renders its human text or JSON envelope.</summary>
public sealed record TranslationsToolCommandResult(string Output, string Error) { public override string ToString() => Output; }

[JsonSerializable(typeof(TranslationsToolCommandResult))]
public sealed partial class TranslationsToolCommandJsonContext : JsonSerializerContext;
