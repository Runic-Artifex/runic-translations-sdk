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

    internal static byte[] Render(IReadOnlyList<TranslationsToolDiagnostic> diagnostics, string toolVersion)
    {
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
                writer.WriteStartArray("locations");
                writer.WriteStartObject();
                writer.WriteStartObject("physicalLocation");
                writer.WriteStartObject("artifactLocation");
                writer.WriteString("uri", diagnostic.Path);
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
