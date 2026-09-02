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

    [Command("init")][CommandResult("runic.translations.tool/1", typeof(TranslationsToolCommandJsonContext))]
    public static CommandOutcome<TranslationsToolCommandResult> Init([FromServices] ITranslationsToolCommandOperations operations, [Option("--directory", Required = true)] string directory, [Option("--catalog", Required = true)] string catalog, [Option("--default-locale", Required = true)] string defaultLocale, [Option("--namespace", Required = true)] string codeNamespace, [Option("--class", Required = true)] string className, [Option("--locale", AllowMultipleValues = true)] IReadOnlyList<string> locales, [Option("--no-starter")] bool noStarter) => operations.Execute(new("init", Directory: directory, Catalog: catalog, DefaultLocale: defaultLocale, Namespace: codeNamespace, ClassName: className, Locales: locales, NoStarter: noStarter));

    [Command("validate")][CommandResult("runic.translations.tool/1", typeof(TranslationsToolCommandJsonContext))]
    public static CommandOutcome<TranslationsToolCommandResult> Validate([FromServices] ITranslationsToolCommandOperations operations, [Option("--project", Required = true)] string project) => operations.Execute(new("validate", Project: project));

    [Command("generate")][CommandResult("runic.translations.tool/1", typeof(TranslationsToolCommandJsonContext))]
    public static CommandOutcome<TranslationsToolCommandResult> Generate([FromServices] ITranslationsToolCommandOperations operations, [Option("--project", Required = true)] string project, [Option("--output", Required = true)] string output, [Option("--emit-csharp")] bool csharp, [Option("--emit-json")] bool json, [Option("--emit-typescript")] bool typescript, [Option("--emit-template-manifest")] bool manifest, [Option("--emit-esm")] bool esm, [Option("--emit-cpp")] bool cpp) => operations.Execute(new("generate", Project: project, Output: output, EmitCSharp: csharp, EmitJson: json, EmitTypeScript: typescript, EmitTemplateManifest: manifest, EmitEsm: esm, EmitCpp: cpp));

    [Command("verify")][CommandResult("runic.translations.tool/1", typeof(TranslationsToolCommandJsonContext))]
    public static CommandOutcome<TranslationsToolCommandResult> Verify([FromServices] ITranslationsToolCommandOperations operations, [Option("--project", Required = true)] string project, [Option("--output", Required = true)] string output, [Option("--emit-csharp")] bool csharp, [Option("--emit-json")] bool json, [Option("--emit-typescript")] bool typescript, [Option("--emit-template-manifest")] bool manifest, [Option("--emit-esm")] bool esm, [Option("--emit-cpp")] bool cpp) => operations.Execute(new("verify", Project: project, Output: output, EmitCSharp: csharp, EmitJson: json, EmitTypeScript: typescript, EmitTemplateManifest: manifest, EmitEsm: esm, EmitCpp: cpp));

    [Command("schema")][CommandResult("runic.translations.tool/1", typeof(TranslationsToolCommandJsonContext))]
    public static CommandOutcome<TranslationsToolCommandResult> Schema([FromServices] ITranslationsToolCommandOperations operations, [Option("--output", Required = true)] string output) => operations.Execute(new("schema", Output: output));


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
    bool EmitCpp = false);

/// <summary>Portable command payload; the standard dispatcher renders its human text or JSON envelope.</summary>
public sealed record TranslationsToolCommandResult(string Output, string Error) { public override string ToString() => Output; }

[JsonSerializable(typeof(TranslationsToolCommandResult))]
public sealed partial class TranslationsToolCommandJsonContext : JsonSerializerContext;
