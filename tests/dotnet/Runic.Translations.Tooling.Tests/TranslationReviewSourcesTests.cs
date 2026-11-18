using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Runic.Translations.Compiler;
using Runic.Translations.Tooling;
using TUnit.Core;

namespace Runic.Translations.Tooling.Tests;

// The base texts review states were recorded for (#225 P20): <catalog>.review-sources.json beside the review file,
// which keeps the runic.translations.interchange-review/2 format the published readers accept.
internal sealed class TranslationReviewSourcesTests
{
    private const string Project = """
        {
          "schemaVersion": 1,
          "catalog": "app",
          "code": { "namespace": "App", "className": "Text" },
          "baseLocale": "en",
          "locales": ["en", { "tag": "de", "fallback": "en" }, { "tag": "fr", "fallback": "en" }],
          "validation": { "translationCompleteness": "allow" }
        }
        """;

    private const string English = "greet = Hello {$name}!\nlater = Later\nplain = Plain\n";
    private const string German = "greet = Hallo {$name}!\nplain = Schlicht\n";

    [Test, DisplayName("mark records the base text beside the review file, once per key and fingerprint, and the review file stays version 2")]
    public void MarkRecordsTheSourceText()
    {
        string directory = Temporary();
        try
        {
            Rmf2ProjectCompilation compilation = Compile(English);
            IReadOnlyDictionary<string, string> fingerprints = TranslationInterchange.ComputeSourceFingerprints(compilation);
            IReadOnlyDictionary<string, TranslationReviewSource> current = TranslationReviewFile.CurrentSources(compilation);
            Require(current["greet"] is { Text: "Hello {$name}!" } greet && greet.Fingerprint == fingerprints["greet"], "The current source is not the base message's MF2 text with its fingerprint.");

            string path = TranslationReviewFile.PathFor(Path.Combine(directory, "runic.json"), "app");
            string companion = TranslationReviewSources.PathFor(path);
            Require(companion == Path.Combine(directory, "app.review-sources.json"), "The companion is not beside the review file: " + companion);
            foreach (string locale in new[] { "de", "fr" })
                TranslationReviewFile.Update(path, "app", existing => TranslationReviewFile.Mark(existing, "app", ["greet", "plain"], locale, TranslationReviewState.Approved, null, fingerprints),
                    TranslationReviewFile.SourcesOf(current, ["greet", "plain", "plain"]));

            // The review file is exactly what it was before: no new member, the version 2 schema, read by the strict reader.
            string review = File.ReadAllText(path);
            Require(review.Contains("\"$schema\": \"runic.translations.interchange-review/2\"", StringComparison.Ordinal) && !review.Contains("\"source\"", StringComparison.Ordinal),
                "The review file changed its format: " + review);
            Require(TranslationInterchange.ImportReviewJson(File.ReadAllBytes(path)).Entries.Count == 4, "The strict reader refused the review file.");
            Require(review == Encoding.UTF8.GetString(TranslationInterchange.ExportReviewJson(TranslationReviewFile.Read(path, "app")!)), "The review file is not the version 2 rendering.");

            // Two locales, one text per key and fingerprint; sorted, indented, readable, one trailing newline.
            string expected = "{\n  \"$schema\": \"runic.translations.review-sources/1\",\n  \"catalog\": \"app\",\n  \"sources\": [\n" +
                "    {\n      \"key\": \"greet\",\n      \"sourceFingerprint\": \"" + fingerprints["greet"] + "\",\n      \"source\": \"Hello {$name}!\"\n    },\n" +
                "    {\n      \"key\": \"plain\",\n      \"sourceFingerprint\": \"" + fingerprints["plain"] + "\",\n      \"source\": \"Plain\"\n    }\n  ]\n}\n";
            Require(File.ReadAllText(companion).ReplaceLineEndings("\n") == expected, "The companion file is not deterministic: " + File.ReadAllText(companion));
            Require(Directory.GetFiles(directory).Order(StringComparer.Ordinal).SequenceEqual([companion, path]), "A write left a temporary file.");

            // The source changes and one locale is approved again: the old text stays while the other locale's entry
            // refers to it, and goes once no entry does.
            Rmf2ProjectCompilation changed = Compile(English.Replace("plain = Plain", "plain = Plain text", StringComparison.Ordinal));
            IReadOnlyDictionary<string, string> next = TranslationInterchange.ComputeSourceFingerprints(changed);
            IReadOnlyDictionary<string, TranslationReviewSource> nextSources = TranslationReviewFile.CurrentSources(changed);
            void Approve(string locale) => TranslationReviewFile.Update(path, "app",
                existing => TranslationReviewFile.Mark(existing, "app", ["plain"], locale, TranslationReviewState.Approved, null, next), TranslationReviewFile.SourcesOf(nextSources, ["plain"]));
            Approve("de");
            Approve("de");
            IReadOnlyDictionary<string, string> texts = TranslationReviewSources.Read(path, "app");
            Require(TranslationReviewSources.Text(texts, "plain", fingerprints["plain"]) == "Plain" && TranslationReviewSources.Text(texts, "plain", next["plain"]) == "Plain text",
                "The old text went while fr still refers to it, or the new one is missing.");
            Approve("fr");
            Approve("fr");
            texts = TranslationReviewSources.Read(path, "app");
            Require(texts.Count == 2 && TranslationReviewSources.Text(texts, "plain", fingerprints["plain"]) is null, "A text no entry refers to was kept.");
        }
        finally { Directory.Delete(directory, true); }
    }

    [Test, DisplayName("a stale entry exposes the text it was recorded for; without the companion it has none and loads as before")]
    public void StaleEntryExposesTheOldText()
    {
        string directory = Temporary();
        try
        {
            Rmf2ProjectCompilation before = Compile(English, German);
            string path = TranslationReviewFile.PathFor(Path.Combine(directory, "runic.json"), "app");
            // A version 2 review file as Tooling 0.14 wrote it: fingerprints, no companion.
            IReadOnlyDictionary<string, string> old = TranslationInterchange.ComputeSourceFingerprints(before);
            File.WriteAllText(path, "{\"$schema\":\"runic.translations.interchange-review/2\",\"catalog\":\"app\",\"entries\":[" +
                "{\"key\":\"greet\",\"locale\":\"de\",\"state\":\"approved\",\"sourceFingerprint\":\"" + old["greet"] + "\"}," +
                "{\"key\":\"plain\",\"locale\":\"de\",\"state\":\"approved\",\"note\":\"Checked\",\"sourceFingerprint\":\"" + old["plain"] + "\"}]}\n");
            Rmf2ProjectCompilation after = Compile(English.Replace("Hello {$name}!", "Welcome back, {$name}!", StringComparison.Ordinal).Replace("plain = Plain", "plain = Plain text", StringComparison.Ordinal), German);
            TranslationInterchangeReview review = TranslationReviewFile.Read(path, "app")!;
            IReadOnlyList<TranslationReviewStaleEntry> stale = TranslationReviewFile.Stale(after, review, TranslationReviewSources.Read(path, "app"));
            Require(stale.Count == 2 && stale.All(static entry => entry.RecordedSource is null), "An entry without a recorded text has one.");
            Require(TranslationReviewFile.StaleDiagnostics(after, review).Count == 2, "A version 2 file without texts does not report its stale entries as before.");

            // Recording the text of greet's old source (what a 0.15 writer did when it was approved).
            TranslationReviewSources.Record(path, "app", TranslationReviewFile.SourcesOf(TranslationReviewFile.CurrentSources(before), ["greet"]));
            stale = TranslationReviewFile.Stale(after, review, TranslationReviewSources.Read(path, "app"));
            Require(stale.Single(static entry => entry.Entry.Key == "greet").RecordedSource == "Hello {$name}!", "The stale entry does not expose the text it was recorded for.");
            Require(stale.Single(static entry => entry.Entry.Key == "plain").RecordedSource is null, "An entry was given another entry's text.");
            Require(File.ReadAllText(path).Contains("\"note\":\"Checked\"", StringComparison.Ordinal), "Recording a text rewrote the review file.");
        }
        finally { Directory.Delete(directory, true); }
    }

    [Test, DisplayName("readers ignore unknown members and skip an unreadable or foreign companion; a writer replaces an unreadable one")]
    public void ReadersAreTolerant()
    {
        string directory = Temporary();
        try
        {
            string path = TranslationReviewFile.PathFor(Path.Combine(directory, "runic.json"), "app");
            string companion = TranslationReviewSources.PathFor(path);
            File.WriteAllText(companion, "{\"$schema\":\"runic.translations.review-sources/1\",\"catalog\":\"app\",\"future\":1,\"sources\":[" +
                "{\"key\":\"greet\",\"sourceFingerprint\":\"sha256:a\",\"source\":\"Hello\",\"inputs\":[]}," +
                "{\"key\":\"plain\",\"sourceFingerprint\":\"sha256:b\"},\"text\"]}");
            Require(TranslationReviewSources.Read(path, "app") is { Count: 1 } texts && TranslationReviewSources.Text(texts, "greet", "sha256:a") == "Hello",
                "Unknown members or unusable entries were not ignored.");
            Require(TranslationReviewSources.Read(path, "other").Count == 0, "Another catalog's texts were read.");
            foreach (string invalid in new[] { "{ not json", "[]", "{\"$schema\":\"runic.translations.review-sources/9\",\"catalog\":\"app\",\"sources\":[]}" })
            {
                File.WriteAllText(companion, invalid);
                Require(TranslationReviewSources.Read(path, "app").Count == 0, "An unreadable companion gave texts: " + invalid);
            }
            TranslationReviewSources.Record(path, "app", [new TranslationReviewSource("greet", "sha256:a", "Hello")]);
            Require(TranslationReviewSources.Read(path, "app").Count == 1, "An unreadable companion was not replaced.");
            // Nothing to record writes nothing; a text over the limit is not recorded.
            File.Delete(companion);
            TranslationReviewSources.Record(path, "app", []);
            TranslationReviewSources.Record(path, "app", [new TranslationReviewSource("greet", "sha256:a", new string('x', TranslationReviewSources.MaximumTextLength + 1))]);
            Require(!File.Exists(companion), "A companion was written without a text to record.");
        }
        finally { Directory.Delete(directory, true); }
    }

    [Test, DisplayName("an XLIFF import records the text each unit was translated from, also when the source changed since export")]
    public void XliffImportRoundTrip()
    {
        Rmf2ProjectCompilation exported = Compile(English, German);
        IReadOnlyDictionary<string, string> exportedFingerprints = TranslationInterchange.ComputeSourceFingerprints(exported);
        string xml = Encoding.UTF8.GetString(TranslationInterchange.ExportXliff21(exported).Documents.Single(static document => document.TargetLocale == "de").Bytes.Span);
        xml = SetTarget(xml, "later", "Später");
        xml = SetTarget(xml, "plain", "Einfach");
        // "later" changes after export; "plain" stays.
        Rmf2ProjectCompilation live = Compile(English.Replace("later = Later", "later = Later today", StringComparison.Ordinal), German);
        IReadOnlyDictionary<string, string> current = TranslationInterchange.ComputeSourceFingerprints(live);
        TranslationXliffImportResult import = TranslationInterchange.ImportXliff21(Encoding.UTF8.GetBytes(xml), live);
        TranslationXliffImportPlan plan = TranslationXliffImportPlan.Create(live, import);
        (TranslationInterchangeReview merged, _) = TranslationReviewFile.MergeImport(null, "app", import, plan);

        TranslationReviewSource[] sources = TranslationReviewFile.ImportSources(import, merged);
        Require(sources.Single(static source => source.Key == "later") is { Text: "Later" } later && later.Fingerprint == exportedFingerprints["later"] && later.Fingerprint != current["later"],
            "The import does not record the source the translator saw for a changed source.");
        Require(sources.Single(static source => source.Key == "plain") is { Text: "Plain" } plain && plain.Fingerprint == current["plain"], "The import does not record the live source of an unchanged one.");
        Require(sources.All(source => merged.Entries.Any(entry => entry.Key == source.Key && entry.SourceFingerprint == source.Fingerprint)), "A recorded text has no entry.");

        // Round trip through the files: the stale needs-review entry of "later" shows the text it was translated from.
        string directory = Temporary();
        try
        {
            string path = TranslationReviewFile.PathFor(Path.Combine(directory, "runic.json"), "app");
            TranslationReviewSources.Record(path, "app", sources);
            TranslationReviewFile.Write(path, merged);
            TranslationReviewStaleEntry stale = TranslationReviewFile.Stale(live, TranslationReviewFile.Read(path, "app")!, TranslationReviewSources.Read(path, "app"))
                .Single(static entry => entry.Entry.Key == "later");
            Require(stale.Entry.State == TranslationReviewState.NeedsReview && stale.RecordedSource == "Later", "The imported stale entry does not show the source it was translated from.");

            // Exported again from the live catalog and imported: the new fingerprint gets the new text, the old one goes.
            string again = SetTarget(Encoding.UTF8.GetString(TranslationInterchange.ExportXliff21(live, merged).Documents.Single(static document => document.TargetLocale == "de").Bytes.Span), "later", "Später heute");
            TranslationXliffImportResult second = TranslationInterchange.ImportXliff21(Encoding.UTF8.GetBytes(again), live);
            (TranslationInterchangeReview remerged, _) = TranslationReviewFile.MergeImport(merged, "app", second, TranslationXliffImportPlan.Create(live, second));
            TranslationReviewSources.Record(path, "app", TranslationReviewFile.ImportSources(second, remerged));
            TranslationReviewFile.Write(path, remerged);
            TranslationReviewSources.Record(path, "app", TranslationReviewFile.ImportSources(second, remerged));
            IReadOnlyDictionary<string, string> texts = TranslationReviewSources.Read(path, "app");
            Require(TranslationReviewSources.Text(texts, "later", current["later"]) == "Later today", "The re-import did not record the current source.");
            Require(TranslationReviewSources.Text(texts, "later", exportedFingerprints["later"]) is null, "The old text stayed after no entry referred to it.");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static string Temporary()
    {
        string directory = Path.Combine(Path.GetTempPath(), "runic-review-sources-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static Rmf2ProjectCompilation Compile(string english, string german = German)
    {
        Rmf2ProjectCompilation compilation = TranslationCompiler.CompileProject(Source("translations/runic.json", Project),
            [Source("translations/en.rmf2", english), Source("translations/de.rmf2", german), Source("translations/fr.rmf2", "plain = Simple\n")]);
        Require(compilation.Success, "The fixture did not compile: " + string.Join("; ", compilation.Diagnostics.Select(diagnostic => diagnostic.Message)));
        return compilation;
    }

    private static string SetTarget(string xml, string key, string target)
    {
        XDocument document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        XNamespace ns = "urn:oasis:names:tc:xliff:document:2.0";
        XElement segment = document.Descendants(ns + "unit").Single(unit => (string?)unit.Attribute("id") == key).Element(ns + "segment")!;
        segment.Element(ns + "target")?.Remove();
        segment.Element(ns + "source")!.AddAfterSelf(new XElement(ns + "target", target));
        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static TranslationSource Source(string path, string text) => new(path, Encoding.UTF8.GetBytes(text));
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
