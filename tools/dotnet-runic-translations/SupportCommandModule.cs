using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Runic.CommandLine;
using Runic.Translations.Internal;

namespace Runic.Translations.Tool;

/// <summary>
/// <c>runic-translations support preview|collect|remove</c>: a local, unsigned support envelope
/// (<c>runic.support-envelope/1</c>) built from the Translations Editor's diagnostic ZIP. It moved here from
/// <c>dotnet runic support --mode &lt;mode&gt;</c>, which forwards to it until 2.0. The command never uploads anything.
/// </summary>
[CommandGroup(Group, Description = "Preview, collect or remove a local support envelope built from an Editor diagnostic ZIP.")]
internal static class SupportCommandModule
{
    internal const string Group = "support";
    internal const string PayloadType = "runic.translations.support/1";
    internal const string IoFailureCode = "RTRSUP025";
    internal static readonly string[] Modes = ["preview", "collect", "remove"];

    private const string EditorDiagnosticsDescription = "The Editor diagnostic ZIP to read. Required.";

    /// <summary>
    /// The tool's generated catalog (translation and support commands), with the output selector <c>--format</c>, its
    /// former spelling <c>--runic-output</c> as a hidden alias until 2.0, response files (<c>@file</c>), the human
    /// output of the translation commands' outcomes and the support commands' human output.
    /// </summary>
    internal static CommandCatalog CreateCatalog() =>
        Runic.CommandLine.Generated.GeneratedCommandCatalog.Create(builder =>
        {
            builder.OutputSelector(Program.FormatOptionName, Program.FormerFormatOptionName);
            builder.ResponseFiles();
            foreach (string command in TranslationsToolCommandModule.Commands)
                builder.PresentOutcome<TranslationsToolCommandResult>(command, ToolOutcomePresenter.PresentAsync);
            foreach (string mode in Modes) builder.Present<SupportCommandResult>(Group + " " + mode, PresentAsync);
        });

    [Command("support preview", Description = "List what a support envelope would contain, without writing it.",
        Examples = ["runic-translations support preview --editor-diagnostics ./editor-diagnostics.zip"])]
    [CommandResult(PayloadType, typeof(SupportCommandJsonContext))]
    internal static Task<CommandOutcome<SupportCommandResult>> Preview(
        [Option("--editor-diagnostics", ValueName = "zip", Description = EditorDiagnosticsDescription)] string editorDiagnostics = "",
        // Accepted and ignored, as by the former dotnet runic support --mode preview, whose arguments are forwarded.
        [Option("--destination", Hidden = true, ValueName = "path")] string destination = "",
        CancellationToken cancellationToken = default) =>
        ExecuteAsync("preview", editorDiagnostics, destination, cancellationToken);

    [Command("support collect", Description = "Write the support envelope to a new file.",
        Examples = ["runic-translations support collect --editor-diagnostics ./editor-diagnostics.zip --destination ./support-envelope.json"])]
    [CommandResult(PayloadType, typeof(SupportCommandJsonContext))]
    internal static Task<CommandOutcome<SupportCommandResult>> Collect(
        [Option("--editor-diagnostics", ValueName = "zip", Description = EditorDiagnosticsDescription)] string editorDiagnostics = "",
        [Option("--destination", ValueName = "path", Description = "The support envelope JSON file to create. It must not exist. Required.")] string destination = "",
        CancellationToken cancellationToken = default) =>
        ExecuteAsync("collect", editorDiagnostics, destination, cancellationToken);

    [Command("support remove", Description = "Verify and delete a support envelope that collect wrote.",
        Examples = ["runic-translations support remove --destination ./support-envelope.json"])]
    [CommandResult(PayloadType, typeof(SupportCommandJsonContext))]
    internal static Task<CommandOutcome<SupportCommandResult>> Remove(
        [Option("--destination", ValueName = "path", Description = "The support envelope JSON file to delete. Required.")] string destination = "",
        // Accepted so that a collector source is rejected with RTRSUP005, as by the former command.
        [Option("--editor-diagnostics", Hidden = true, ValueName = "zip")] string editorDiagnostics = "",
        CancellationToken cancellationToken = default) =>
        ExecuteAsync("remove", editorDiagnostics, destination, cancellationToken);

    private static async Task<CommandOutcome<SupportCommandResult>> ExecuteAsync(
        string mode,
        string editorDiagnostics,
        string destination,
        CancellationToken cancellationToken)
    {
        try
        {
            SupportCommandResult result = await SupportApplication.ExecuteAsync(
                new SupportOptions(mode, string.IsNullOrWhiteSpace(editorDiagnostics) ? null : editorDiagnostics, string.IsNullOrWhiteSpace(destination) ? null : destination),
                cancellationToken).ConfigureAwait(false);
            return CommandOutcome.Success(result);
        }
        catch (SupportUsageException exception)
        {
            return Failure(CommandExitCategory.Usage, exception.Code, exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failure(CommandExitCategory.CommandFailure, IoFailureCode, "The local support envelope could not access a required file.");
        }
    }

    /// <summary>A usage failure of <c>support</c>, such as an unknown mode (<c>RTRSUP001</c>).</summary>
    internal static CommandOutcome<SupportCommandResult> Failure(CommandExitCategory category, string code, string message) =>
        CommandOutcome.Failure<SupportCommandResult>(
            category,
            new CommandFault(code, message),
            diagnostics: [],
            // Human output only: the catalog entry of the code at this release's tag.
            humanOutput: $"Help for {code}: {RunicReleaseLinks.Diagnostic(code)}\n");

    private static ValueTask PresentAsync(SupportCommandResult result, ICommandConsole console, CultureInfo culture, CancellationToken cancellationToken) =>
        console.WriteOutAsync((result.ToHumanOutput() + "\n").AsMemory(), cancellationToken);
}

/// <summary>The <c>runic.translations.support/1</c> payload of <c>support preview</c>, <c>collect</c> and <c>remove</c>.</summary>
/// <param name="Mode"><c>preview</c>, <c>collect</c> or <c>remove</c>.</param>
/// <param name="Destination">The full path of the envelope that was written or removed; <see langword="null"/> for <c>preview</c>.</param>
/// <param name="Digest"><c>sha256:</c> and the lowercase hex SHA-256 of the envelope's bytes.</param>
/// <param name="Collectors">The selected collector IDs; empty for <c>remove</c>.</param>
/// <param name="Omissions">The sources the envelope never collects; empty for <c>remove</c>.</param>
/// <param name="OutboundTransportAttempts">Always 0: the command never sends anything.</param>
internal sealed record SupportCommandResult(
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("destination")] string? Destination,
    [property: JsonPropertyName("digest")] string Digest,
    [property: JsonPropertyName("collectors")] IReadOnlyList<string> Collectors,
    [property: JsonPropertyName("omissions")] IReadOnlyList<string> Omissions,
    [property: JsonPropertyName("outboundTransportAttempts")] int OutboundTransportAttempts)
{
    /// <summary>The human text of the former <c>dotnet runic support</c>.</summary>
    public string ToHumanOutput() => $"Support {Mode}: selected {string.Join(", ", Collectors.DefaultIfEmpty("none"))}; omitted {string.Join(", ", Omissions.DefaultIfEmpty("none"))}; outbound transport attempts: {OutboundTransportAttempts}; digest: {Digest}" + (Destination is null ? string.Empty : $"; path: {Destination}");
}

// The properties name themselves; the policy states the camelCase payload rule for any later property, as the command generator asks.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SupportCommandResult))]
internal sealed partial class SupportCommandJsonContext : JsonSerializerContext;
