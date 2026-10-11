using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
                TranslationsToolFailurePresentation.DiagnosticsOnly => new DiagnosticsOnlyHumanConsole(console, diagnostics),
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

    private const string ExitCodes = "Exit codes: 0 success; 1 translation or verification diagnostics; 2 invalid invocation or operational failure.";
    private const string ProjectDescription = "Translations directory or its runic.json. Defaults to ./runic.json, then ./translations/runic.json.";
    private const string EmitDescription = "With no --emit-* option, renders C#, locale-v5 JSON with its asset manifest, and ESM. With any, renders only the selected groups.";

    [Command("init", Description = "Create an RMF2 translation project and starter resources.", LongDescription = "Writes runic.json and one .rmf2 file per locale into a new or empty directory.", Examples = ["runic-translations init --directory translations --catalog app --default-locale en --locale de --namespace MyApp --class AppText"])][CommandResult("runic.translations.tool/1", typeof(TranslationsToolCommandJsonContext))]
    public static CommandOutcome<TranslationsToolCommandResult> Init([FromServices] ITranslationsToolCommandOperations operations, [Option("--directory", Required = true, Description = "Directory to create the project in.")] string directory, [Option("--catalog", Required = true, Description = "Portable catalog identifier.")] string catalog, [Option("--default-locale", Required = true, ValueName = "locale", Description = "Base locale tag, such as en.")] string defaultLocale, [Option("--namespace", Required = true, ValueName = "namespace", Description = "Namespace of the generated C# API.")] string codeNamespace, [Option("--class", Required = true, ValueName = "name", Description = "Class name of the generated C# API.")] string className, [Option("--locale", AllowMultipleValues = true, ValueName = "locale", Description = "Additional locale as <tag> or <tag>:<fallback>; repeatable.")] IReadOnlyList<string> locales, [Option("--no-starter", Description = "Create the files without the starter message.")] bool noStarter) => operations.Execute(new("init", Directory: directory, Catalog: catalog, DefaultLocale: defaultLocale, Namespace: codeNamespace, ClassName: className, Locales: locales, NoStarter: noStarter));

    [Command("lsp", Description = "Run the RMF2 language server over standard input/output.")][CommandResult("runic.translations.tool/1", typeof(TranslationsToolCommandJsonContext))]
    public static CommandOutcome<TranslationsToolCommandResult> Lsp([FromServices] ITranslationsToolCommandOperations operations) => operations.Execute(new("lsp"));

    [Command("serve", Description = "Run a persistent compiler for development servers over standard input/output.")][CommandResult("runic.translations.tool/1", typeof(TranslationsToolCommandJsonContext))]
    public static CommandOutcome<TranslationsToolCommandResult> Serve([FromServices] ITranslationsToolCommandOperations operations) => operations.Execute(new("serve"));

    [Command("validate", Description = "Validate catalogs and report translation diagnostics.", LongDescription = ExitCodes, Examples = ["runic-translations validate", "runic-translations validate --project src/App/translations"])][CommandResult("runic.translations.tool/1", typeof(TranslationsToolCommandJsonContext))]
    public static CommandOutcome<TranslationsToolCommandResult> Validate([FromServices] ITranslationsToolCommandOperations operations, [Option("--project", Description = ProjectDescription, ValueName = "directory")] string? project) => operations.Execute(new("validate", Project: project));

    // --emit-typescript, --emit-template-manifest and --emit-cpp have no renderer yet. They stay
    // accepted so a request (also from MSBuild) fails with RTR0065, but help does not list them.
    [Command("generate", Description = "Generate translation artifacts.", LongDescription = EmitDescription + "\n\n" + ExitCodes, Examples = ["runic-translations generate --output obj/translations --emit-esm"])][CommandResult("runic.translations.tool/1", typeof(TranslationsToolCommandJsonContext))]
    public static CommandOutcome<TranslationsToolCommandResult> Generate([FromServices] ITranslationsToolCommandOperations operations, [Option("--project", Description = ProjectDescription, ValueName = "directory")] string? project, [Option("--output", Required = true, Description = "Directory to write the artifacts to.", ValueName = "directory")] string output, [Option("--emit-csharp", Description = "Render the typed C# API.")] bool csharp, [Option("--emit-json", Description = "Render locale-v5 JSON and its asset manifest.")] bool json, [Option("--emit-typescript", Hidden = true)] bool typescript, [Option("--emit-template-manifest", Hidden = true)] bool manifest, [Option("--emit-esm", Description = "Render the ESM package and web-module-manifest-v3.json.")] bool esm, [Option("--emit-cpp", Hidden = true)] bool cpp) => operations.Execute(new("generate", Project: project, Output: output, EmitCSharp: csharp, EmitJson: json, EmitTypeScript: typescript, EmitTemplateManifest: manifest, EmitEsm: esm, EmitCpp: cpp));

    [Command("verify", Description = "Check that generated artifacts match the sources.", LongDescription = "Renders in memory and compares byte for byte with --output, including extra files. " + EmitDescription + "\n\n" + ExitCodes, Examples = ["runic-translations verify --output obj/translations --emit-esm"])][CommandResult("runic.translations.tool/1", typeof(TranslationsToolCommandJsonContext))]
    public static CommandOutcome<TranslationsToolCommandResult> Verify([FromServices] ITranslationsToolCommandOperations operations, [Option("--project", Description = ProjectDescription, ValueName = "directory")] string? project, [Option("--output", Required = true, Description = "Directory with the artifacts to compare.", ValueName = "directory")] string output, [Option("--emit-csharp", Description = "Compare the typed C# API.")] bool csharp, [Option("--emit-json", Description = "Compare locale-v5 JSON and its asset manifest.")] bool json, [Option("--emit-typescript", Hidden = true)] bool typescript, [Option("--emit-template-manifest", Hidden = true)] bool manifest, [Option("--emit-esm", Description = "Compare the ESM package and web-module-manifest-v3.json.")] bool esm, [Option("--emit-cpp", Hidden = true)] bool cpp) => operations.Execute(new("verify", Project: project, Output: output, EmitCSharp: csharp, EmitJson: json, EmitTypeScript: typescript, EmitTemplateManifest: manifest, EmitEsm: esm, EmitCpp: cpp));

    [Command("schema", Description = "Write the bundled JSON schemas to a directory.")][CommandResult("runic.translations.tool/1", typeof(TranslationsToolCommandJsonContext))]
    public static CommandOutcome<TranslationsToolCommandResult> Schema([FromServices] ITranslationsToolCommandOperations operations, [Option("--output", Required = true, Description = "Directory to write the schemas to.", ValueName = "directory")] string output) => operations.Execute(new("schema", Output: output));


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

    private sealed class DiagnosticsOnlyHumanConsole : ICommandConsole
    {
        private readonly ICommandConsole _inner;
        private readonly string _diagnostics;
        private bool _written;

        internal DiagnosticsOnlyHumanConsole(ICommandConsole inner, IReadOnlyList<CommandDiagnostic> diagnostics)
        {
            _inner = inner;
            var text = new StringBuilder();
            foreach (CommandDiagnostic diagnostic in diagnostics)
            {
                text.Append(diagnostic.Message);
                text.Append('\n');
            }

            _diagnostics = text.ToString();
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
            if (_written || _diagnostics.Length == 0) return ValueTask.CompletedTask;
            _written = true;
            return _inner.WriteErrorAsync(_diagnostics.AsMemory(), cancellationToken);
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
    /// <summary>Writes application-owned report text and diagnostics through their original streams.</summary>
    DiagnosticsOnly,
}

/// <summary>Host operation bridge for the composable generated command catalog.</summary>
public interface ITranslationsToolCommandOperations { CommandOutcome<TranslationsToolCommandResult> Execute(TranslationsToolCommandRequest request); }

/// <summary>Typed values bound by the generated catalog before host operation policy runs.</summary>
public sealed record TranslationsToolCommandRequest(
    string Command,
    string? Project = null,
    string? Output = null,
    string? Directory = null,
    string? Catalog = null,
    string? DefaultLocale = null,
    string? Namespace = null,
    string? ClassName = null,
    IReadOnlyList<string>? Locales = null,
    bool NoStarter = false,
    bool EmitCSharp = false,
    bool EmitJson = false,
    bool EmitTypeScript = false,
    bool EmitTemplateManifest = false,
    bool EmitEsm = false,
    bool EmitCpp = false,
    bool DryRun = false);

/// <summary>Portable command payload; the standard dispatcher renders its human text or JSON envelope.</summary>
/// <remarks>A failed command carries the same shape as <c>fault.data</c> of type <c>runic.translations.tool/1</c>.</remarks>
public sealed record TranslationsToolCommandResult(string Output, string Error)
{
    /// <summary>Translation diagnostics under their own RTR codes, with source locations.</summary>
    public IReadOnlyList<TranslationsToolDiagnostic> Diagnostics { get; init; } = [];

    /// <inheritdoc />
    public override string ToString() => Output;
}

/// <summary>A compiler diagnostic for a translation source, as reported by the tool.</summary>
/// <param name="Code">The RTR code, such as <c>RTR0010</c>.</param>
/// <param name="Severity"><c>error</c> or <c>warning</c>.</param>
/// <param name="Message">The message, without location or code.</param>
/// <param name="Path">The source path, with forward slashes, relative to the working directory where possible.</param>
/// <param name="Line">One-based start line.</param>
/// <param name="Column">One-based start column.</param>
/// <param name="EndLine">One-based end line.</param>
/// <param name="EndColumn">One-based end column.</param>
/// <param name="HelpUri">Documentation for the code.</param>
public sealed record TranslationsToolDiagnostic(string Code, string Severity, string Message, string Path, int Line, int Column, int EndLine, int EndColumn, string HelpUri)
{
    /// <summary>The <c>path(line,column,endLine,endColumn): severity code: message</c> form that MSBuild and editors recognize.</summary>
    public override string ToString() => Path + "(" + Line + "," + Column + "," + EndLine + "," + EndColumn + "): " + Severity + " " + Code + ": " + Message;
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TranslationsToolCommandResult))]
[SuppressMessage("ApiDesign", "RS0041:Public members should not use oblivious types", Justification = "The System.Text.Json source generator emits nullable-oblivious JsonTypeInfo properties.")]
public sealed partial class TranslationsToolCommandJsonContext : JsonSerializerContext;
