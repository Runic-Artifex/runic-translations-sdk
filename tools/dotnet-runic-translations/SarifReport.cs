using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Runic.Translations.Internal;

namespace Runic.Translations.Tool;

/// <summary>
/// The <c>validate --sarif &lt;file&gt;</c> report: SARIF 2.1.0 with one rule per RTR code (linking its
/// diagnostics reference entry) and one result per diagnostic, for code scanning and CI annotations.
/// </summary>
internal static class SarifReport
{
    internal const string Schema = "https://json.schemastore.org/sarif-2.1.0.json";

    /// <summary>The base that relative artifact URIs resolve against: the repository root, as code scanning expects.</summary>
    internal const string SourceRoot = "%SRCROOT%";

    /// <summary>The repository root above <paramref name="directory"/> (a directory with <c>.git</c>), else the directory itself.</summary>
    internal static string FindSourceRoot(string directory)
    {
        for (DirectoryInfo? current = new(directory); current is not null; current = current.Parent)
            if (Directory.Exists(Path.Combine(current.FullName, ".git")) || File.Exists(Path.Combine(current.FullName, ".git"))) return current.FullName;
        return Path.GetFullPath(directory);
    }

    // A diagnostic path (relative to the working directory) as an artifact location: percent-encoded and relative to
    // %SRCROOT% when it is below the root, else an absolute file URI.
    private static (string Uri, bool Relative)? Location(string path, string currentDirectory, string root)
    {
        if (string.IsNullOrEmpty(path)) return null;
        string full = Path.GetFullPath(path, currentDirectory);
        string relative = Path.GetRelativePath(root, full);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, System.StringComparison.Ordinal) ||
            relative.StartsWith("../", System.StringComparison.Ordinal))
            return (new System.Uri(full).AbsoluteUri, false);
        return (string.Join('/', relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Select(System.Uri.EscapeDataString)), true);
    }

    /// <param name="diagnostics">Every translation diagnostic; paths are relative to <paramref name="currentDirectory"/>.</param>
    /// <param name="toolVersion">The tool's version.</param>
    /// <param name="currentDirectory">The working directory the diagnostic paths are relative to.</param>
    /// <param name="faults">Projects that could not be validated, reported as run-level notifications.</param>
    internal static byte[] Render(IReadOnlyList<TranslationsToolDiagnostic> diagnostics, string toolVersion, string currentDirectory, IReadOnlyList<string>? faults = null)
    {
        string root = FindSourceRoot(currentDirectory);
        faults ??= [];
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteString("$schema", Schema);
            writer.WriteString("version", "2.1.0");
            writer.WriteStartArray("runs");
            writer.WriteStartObject();
            writer.WriteStartObject("tool");
            writer.WriteStartObject("driver");
            writer.WriteString("name", "runic-translations");
            writer.WriteString("version", toolVersion);
            writer.WriteString("informationUri", RunicReleaseLinks.Diagnostic("diagnostics"));
            writer.WriteStartArray("rules");
            string[] rules = diagnostics.Select(static diagnostic => diagnostic.Code).Distinct().Order(System.StringComparer.Ordinal).ToArray();
            foreach (string rule in rules)
            {
                writer.WriteStartObject();
                writer.WriteString("id", rule);
                writer.WriteString("helpUri", RunicReleaseLinks.Diagnostic(rule));
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteStartArray("invocations");
            writer.WriteStartObject();
            writer.WriteBoolean("executionSuccessful", faults.Count == 0);
            if (faults.Count != 0)
            {
                writer.WriteStartArray("toolExecutionNotifications");
                foreach (string fault in faults)
                {
                    writer.WriteStartObject();
                    writer.WriteString("level", "error");
                    writer.WriteStartObject("message");
                    writer.WriteString("text", fault);
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }
            writer.WriteEndObject();
            writer.WriteEndArray();
            // Relative artifact URIs resolve against the repository root, wherever validate ran.
            writer.WriteStartObject("originalUriBaseIds");
            writer.WriteStartObject(SourceRoot);
            writer.WriteString("uri", new System.Uri(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar).AbsoluteUri);
            writer.WriteEndObject();
            writer.WriteEndObject();
            // Diagnostic columns count UTF-16 code units, as every Runic host reports them.
            writer.WriteString("columnKind", "utf16CodeUnits");
            writer.WriteStartArray("results");
            foreach (TranslationsToolDiagnostic diagnostic in diagnostics)
            {
                writer.WriteStartObject();
                writer.WriteString("ruleId", diagnostic.Code);
                writer.WriteNumber("ruleIndex", System.Array.IndexOf(rules, diagnostic.Code));
                writer.WriteString("level", diagnostic.Severity switch { "error" => "error", "warning" => "warning", _ => "note" });
                writer.WriteStartObject("message");
                writer.WriteString("text", diagnostic.Message);
                writer.WriteEndObject();
                if (Location(diagnostic.Path, currentDirectory, root) is not { } location) { writer.WriteEndObject(); continue; }
                writer.WriteStartArray("locations");
                writer.WriteStartObject();
                writer.WriteStartObject("physicalLocation");
                writer.WriteStartObject("artifactLocation");
                writer.WriteString("uri", location.Uri);
                if (location.Relative) writer.WriteString("uriBaseId", SourceRoot);
                writer.WriteEndObject();
                writer.WriteStartObject("region");
                writer.WriteNumber("startLine", System.Math.Max(1, diagnostic.Line));
                writer.WriteNumber("startColumn", System.Math.Max(1, diagnostic.Column));
                writer.WriteNumber("endLine", System.Math.Max(System.Math.Max(1, diagnostic.Line), diagnostic.EndLine));
                writer.WriteNumber("endColumn", System.Math.Max(1, diagnostic.EndColumn));
                writer.WriteEndObject();
                writer.WriteEndObject();
                writer.WriteEndObject();
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }
}
