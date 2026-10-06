using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using Runic.CommandLine;
using Runic.Translations.Compiler;

namespace Runic.Translations.Tool;

/// <summary>
/// Persistent compiler for development servers: <c>runic-translations serve</c>.
/// </summary>
/// <remarks>
/// <para>The protocol is line-delimited JSON over standard input and output (protocol
/// <c>runic-translations-serve/1</c>). On start the server writes
/// <c>{"protocol":"runic-translations-serve/1","event":"ready","version":"..."}</c>. Each request is one
/// line, <c>{"id":1,"method":"generate","project":"translations","output":".runic/translations","emit":["esm"]}</c>,
/// with methods <c>generate</c>, <c>validate</c> and <c>shutdown</c>. Requests run one at a time and each
/// receives exactly one response line with the same <c>id</c>:
/// <c>{"id":1,"ok":true,"exitCode":0,"output":"...","message":"","diagnostics":[],"elapsedMs":12}</c>.
/// <c>exitCode</c> and <c>diagnostics</c> match the one-shot command. The server exits when standard input
/// closes or after answering <c>shutdown</c>.</para>
/// <para>Paths resolve against the server's working directory, like the one-shot commands. Unchanged
/// sources keep their parsed and lowered form between requests, so a request after an edit
/// recompiles only the edited files before relinking the catalog.</para>
/// </remarks>
internal sealed class CompileServer
{
    internal const string Protocol = "runic-translations-serve/1";
    private const int MaximumRequestCharacters = 1024 * 1024;
    private readonly TextReader _input;
    private readonly Stream _output;
    private readonly SourceUnitCache _units = new();

    internal CompileServer(TextReader input, Stream output)
    {
        _input = input;
        _output = output;
    }

    /// <summary>Runs the server on the process's standard streams until input closes.</summary>
    internal static int RunOnStandardStreams(string version)
    {
        Stream output = Console.OpenStandardOutput();
        // Standard output carries only protocol lines; anything else written to the console goes to standard error.
        Console.SetOut(Console.Error);
        var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false), false);
        return new CompileServer(input, output).Run(version);
    }

    internal int Run(string version)
    {
        Write(writer =>
        {
            writer.WriteString("protocol", Protocol);
            writer.WriteString("event", "ready");
            writer.WriteString("version", version);
        });
        while (_input.ReadLine() is { } line)
        {
            if (line.Length == 0) continue;
            if (!Handle(line)) break;
        }
        return 0;
    }

    private bool Handle(string line)
    {
        JsonElement id = default;
        bool hasId = false;
        string method;
        string? project = null, output = null;
        ToolEmission emission = ToolEmission.None;
        try
        {
            if (line.Length > MaximumRequestCharacters) throw new FormatException("The request exceeds the supported size.");
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new FormatException("A request must be a JSON object.");
            if (root.TryGetProperty("id", out JsonElement idValue))
            {
                if (idValue.ValueKind is not (JsonValueKind.Number or JsonValueKind.String)) throw new FormatException("A request id must be a number or a string.");
                id = idValue.Clone();
                hasId = true;
            }
            method = root.TryGetProperty("method", out JsonElement methodValue) && methodValue.ValueKind == JsonValueKind.String
                ? methodValue.GetString()!
                : throw new FormatException("A request requires a string method.");
            project = OptionalString(root, "project");
            output = OptionalString(root, "output");
            if (root.TryGetProperty("emit", out JsonElement emit))
            {
                if (emit.ValueKind != JsonValueKind.Array) throw new FormatException("emit must be an array of output groups.");
                foreach (JsonElement item in emit.EnumerateArray())
                    emission |= item.ValueKind == JsonValueKind.String ? Emission(item.GetString()!) : throw new FormatException("emit must contain only strings.");
            }
        }
        catch (Exception exception) when (exception is JsonException or FormatException)
        {
            Respond(hasId, id, Usage(exception.Message), 0);
            return true;
        }

        if (method == "shutdown")
        {
            Respond(hasId, id, new ToolOperationResult(), 0);
            return false;
        }

        long started = Stopwatch.GetTimestamp();
        ToolOperationResult result = method switch
        {
            "generate" when project is null || output is null => Usage("generate requires project and output."),
            "generate" => Program.Execute(new ToolInvocation(ToolCommand.Generate, output, emission, null, project), _units),
            "validate" when project is null => Usage("validate requires project."),
            "validate" => Program.Execute(new ToolInvocation(ToolCommand.Validate, null, ToolEmission.None, null, project), _units),
            _ => Usage($"unknown method '{method}'."),
        };
        Respond(hasId, id, result, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return true;
    }

    private static string? OptionalString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value)) return null;
        return value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text
            ? text
            : throw new FormatException($"{name} must be a non-empty string.");
    }

    private static ToolEmission Emission(string name) => name switch
    {
        "csharp" => ToolEmission.CSharp,
        "json" => ToolEmission.Json,
        "typescript" => ToolEmission.TypeScript,
        "template-manifest" => ToolEmission.TemplateManifest,
        "esm" => ToolEmission.Esm,
        "cpp" => ToolEmission.Cpp,
        _ => throw new FormatException($"unknown output group '{name}'."),
    };

    private static ToolOperationResult Usage(string message)
    {
        var result = new ToolOperationResult { ExitCode = 2, ExitCategory = CommandExitCategory.Usage };
        result.AddDiagnostic("RCLI9003", "tool-usage", message, CommandDiagnosticSeverity.Error);
        return result;
    }

    private void Respond(bool hasId, JsonElement id, ToolOperationResult result, long elapsedMilliseconds) => Write(writer =>
    {
        writer.WritePropertyName("id");
        if (hasId) id.WriteTo(writer);
        else writer.WriteNullValue();
        writer.WriteBoolean("ok", result.ExitCode == 0);
        writer.WriteNumber("exitCode", result.ExitCode);
        writer.WriteString("output", result.Output);
        writer.WriteString("message", Message(result));
        writer.WriteStartArray("diagnostics");
        foreach (CommandDiagnostic diagnostic in result.Diagnostics)
        {
            writer.WriteStartObject();
            writer.WriteString("code", diagnostic.Code);
            writer.WriteString("severity", diagnostic.Severity == CommandDiagnosticSeverity.Error ? "error" : diagnostic.Severity == CommandDiagnosticSeverity.Warning ? "warning" : "info");
            writer.WriteString("message", diagnostic.Message);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteNumber("elapsedMs", elapsedMilliseconds);
    });

    // The text a one-shot invocation would print for this failure.
    private static string Message(ToolOperationResult result)
    {
        if (result.ExitCode == 0) return string.Empty;
        if (result.HumanOutput is { Length: > 0 } human) return human.TrimEnd();
        var builder = new StringBuilder();
        foreach (CommandDiagnostic diagnostic in result.Diagnostics)
            builder.Append(diagnostic.Message).Append('\n');
        return builder.ToString().TrimEnd();
    }

    private void Write(Action<Utf8JsonWriter> body)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            body(writer);
            writer.WriteEndObject();
        }
        buffer.WriteByte((byte)'\n');
        buffer.Position = 0;
        buffer.CopyTo(_output);
        _output.Flush();
    }
}

/// <summary>Keeps the per-source compilation units of the most recent request.</summary>
internal sealed class SourceUnitCache
{
    private Dictionary<string, Rmf2SourceUnitV5> _units = new(StringComparer.Ordinal);

    internal TranslationCompilerOptions Options { get; } = new();

    internal IReadOnlyList<Rmf2SourceUnitV5> Resolve(IReadOnlyList<TranslationSource> sources, CancellationToken cancellationToken = default)
    {
        var next = new Dictionary<string, Rmf2SourceUnitV5>(StringComparer.Ordinal);
        var result = new List<Rmf2SourceUnitV5>(sources.Count);
        foreach (TranslationSource source in sources)
        {
            if (!_units.TryGetValue(source.Path, out Rmf2SourceUnitV5? unit) || !unit.Matches(source, Options))
                unit = Rmf2SourceUnitV5.Create(source, Options, cancellationToken);
            next[source.Path] = unit;
            result.Add(unit);
        }
        _units = next;
        return result;
    }
}
