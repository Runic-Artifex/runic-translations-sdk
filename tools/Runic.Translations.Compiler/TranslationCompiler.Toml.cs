using System;
using System.Collections.Generic;
using System.Threading;

namespace Runic.Translations.Compiler;

public static partial class TranslationCompiler
{
    private static void ReadTomlDocument(string directory, TranslationSource source, ManifestModel manifest,
        List<DocumentModel> documents, HashSet<string> discoveredLocales, DiagnosticBag diagnostics,
        TranslationCompilerOptions options, CancellationToken cancellationToken)
    {
        string relative = source.Path.StartsWith(directory, StringComparison.Ordinal) ? source.Path.Substring(directory.Length) : string.Empty;
        if (relative.Contains('/', StringComparison.Ordinal) || !relative.EndsWith(".toml", StringComparison.OrdinalIgnoreCase) ||
            !TryCanonicalizeLocale(relative.Substring(0, Math.Max(0, relative.Length - 5)), out string locale))
        {
            diagnostics.Add("RTR0040", TranslationDiagnosticSeverity.Error,
                "Locale TOML paths must be '{locale}.toml' directly beside runic.json; mixed MF2 sources are unsupported.", source, new ByteSpan(0, 0));
            return;
        }
        if (!discoveredLocales.Add(locale))
        {
            diagnostics.Add("RTR0004", TranslationDiagnosticSeverity.Error, "More than one TOML document maps to locale '" + locale + "'.", source, new ByteSpan(0, 0));
            return;
        }
        TranslationLocaleDocument parsed = TranslationLocaleReader.Read(source, locale, options, cancellationToken);
        foreach (TranslationDiagnostic diagnostic in parsed.Diagnostics)
            diagnostics.Add(diagnostic.Id, diagnostic.Severity, diagnostic.Message, diagnostic.Location);
        if (!parsed.Success) return;
        var document = new DocumentModel(source)
        {
            SchemaVersion = 2,
            Catalog = manifest.Id,
            Locale = locale,
            Layer = "base",
            CatalogSpan = new ByteSpan(0, 0),
            LocaleSpan = new ByteSpan(0, 0),
            LayerSpan = new ByteSpan(0, 0),
        };
        foreach (TranslationLocaleEntry entry in parsed.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var messageDiagnostics = new DiagnosticBag();
            Mf2ParsedMessage? message = Mf2MessageParser.Parse(entry.Message, messageDiagnostics, options, cancellationToken);
            // The MF2 parser currently diagnoses whole messages. Preserve that honest extent in
            // the physical container instead of inventing token offsets through decoded escapes.
            foreach (TranslationDiagnostic diagnostic in messageDiagnostics.Items)
                diagnostics.Add(diagnostic.Id, diagnostic.Severity, diagnostic.Message, entry.ValueLocation);
            if (message is null) continue;
            var keySpan = new ByteSpan(entry.KeyLocation.StartByte, entry.KeyLocation.LengthBytes);
            var valueSpan = new ByteSpan(entry.ValueLocation.StartByte, entry.ValueLocation.LengthBytes);
            document.Resources.Add(new ResourceModel(entry.Key, message.Pattern, message.Message,
                null, null, null, Array.Empty<string>(), message.Placeholders, source, keySpan, keySpan, valueSpan) { KeyLocation = entry.KeyLocation });
        }
        documents.Add(document);
    }
}
