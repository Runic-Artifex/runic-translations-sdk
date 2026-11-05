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

// The SDK review file and the XLIFF import plan that the command line, the editor and the language server share.
internal sealed class TranslationReviewFileTests
{
    private const string Project = """
        {
          "schemaVersion": 1,
          "catalog": "app",
          "code": { "namespace": "App", "className": "Text" },
          "baseLocale": "en",
          "locales": ["en", { "tag": "de", "fallback": "en" }],
          "validation": { "translationCompleteness": "allow" }
        }
        """;

    private const string English = "greet = Hello {$name}!\nlater = Later\nplain = Plain\n";
    private const string German = "greet = Hallo {$name}!\nplain = Schlicht\n";

    [Test, DisplayName("the review file lives beside runic.json, round-trips, and refuses another catalog's file")]
    public void FileRoundTrips()
    {
        string directory = Path.Combine(Path.GetTempPath(), "runic-review-" + Guid.NewGuid().ToString("N"));
        try
        {
            string path = TranslationReviewFile.PathFor(Path.Combine(directory, "translations", "runic.json"), "app");
            Require(path == Path.Combine(directory, "translations", "app.review.json"), "The review file is not beside runic.json: " + path);
            Require(TranslationReviewFile.Read(path, "app") is null, "A missing review file was not null.");
            var review = new TranslationInterchangeReview("app", [new TranslationInterchangeReviewEntry("greet", "de", TranslationReviewState.Approved, "Checked", "sha256:x")]);
            TranslationReviewFile.Write(path, review);
            Require(Directory.GetFiles(Path.GetDirectoryName(path)!).Single() == path, "The atomic write left a temporary file.");
            TranslationInterchangeReview read = TranslationReviewFile.Read(path, "app")!;
            Require(read.Entries.Single() is { Key: "greet", Locale: "de", State: TranslationReviewState.Approved, Note: "Checked", SourceFingerprint: "sha256:x" }, "The entry did not round-trip.");
            Throws<TranslationInterchangeException>(() => TranslationReviewFile.Read(path, "other"), "A review file of another catalog was read.");
            foreach (string invalid in new[] { "..", "a/b", "a\\b", "" })
                Throws<ArgumentException>(() => TranslationReviewFile.PathFor(Path.Combine(directory, "runic.json"), invalid), $"'{invalid}' named a review file.");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Test, DisplayName("an entry is stale when its source changed; a removed draft is not, and RTR0090 names the message and locale")]
    public void StaleEntries()
    {
        Rmf2ProjectCompilation before = Compile(English, German);
        IReadOnlyDictionary<string, string> old = TranslationInterchange.ComputeSourceFingerprints(before);
        var review = new TranslationInterchangeReview("app",
        [
            new TranslationInterchangeReviewEntry("greet", "de", TranslationReviewState.Approved, null, old["greet"]),
            new TranslationInterchangeReviewEntry("plain", "de", TranslationReviewState.Approved, null, old["plain"]),
            new TranslationInterchangeReviewEntry("later", "de", TranslationReviewState.Draft, null, old["later"]),
            new TranslationInterchangeReviewEntry("greet", "en", TranslationReviewState.Approved, null, "sha256:old"),
            new TranslationInterchangeReviewEntry("missing", "de", TranslationReviewState.Approved, null, "sha256:old"),
        ]);
        Rmf2ProjectCompilation after = Compile(English.Replace("plain = Plain", "plain = Plain text", StringComparison.Ordinal).Replace("later = Later", "later = Later on", StringComparison.Ordinal), German);
        IReadOnlyDictionary<string, string> current = TranslationInterchange.ComputeSourceFingerprints(after);
        Require(!TranslationReviewFile.IsStale(review.Entries[0], current) && TranslationReviewFile.IsStale(review.Entries[1], current), "IsStale does not compare the per-unit fingerprint.");
        IReadOnlyList<TranslationReviewStaleEntry> stale = TranslationReviewFile.Stale(after, review);
        // later (a draft without a translation), the base locale and an unknown key are not reported.
        Require(stale.Select(static item => item.Entry.Key + "/" + item.Entry.Locale).SequenceEqual(["plain/de"]), "Unexpected stale entries: " + string.Join(", ", stale.Select(static item => item.Entry.Key)));
        Require(stale[0].Location.Path == "translations/de.rmf2", "A stale translation is not located at the translation.");
        TranslationDiagnostic diagnostic = TranslationReviewFile.StaleDiagnostics(after, review).Single();
        Require(diagnostic is { Id: "RTR0090", Severity: TranslationDiagnosticSeverity.Warning, MessageKey: "plain", Locale: "de" }, "RTR0090 does not name its message and locale.");
        Require(diagnostic.Message.Contains("review mark --key plain --locale de", StringComparison.Ordinal), "RTR0090 does not say how to record the review: " + diagnostic.Message);
        // A stale approval of a removed translation is still reported, at the base message.
        var removed = new TranslationInterchangeReview("app", [new TranslationInterchangeReviewEntry("later", "de", TranslationReviewState.Approved, null, old["later"])]);
        Require(TranslationReviewFile.Stale(after, removed).Single().Location.Path == "translations/en.rmf2", "A removed translation's stale approval is not located at the base message.");
    }

    [Test, DisplayName("review mark stamps the current fingerprint, keeps notes and other entries, and refuses an unknown key")]
    public void MarkStampsCurrentSource()
    {
        Rmf2ProjectCompilation compilation = Compile(English, German);
        IReadOnlyDictionary<string, string> fingerprints = TranslationInterchange.ComputeSourceFingerprints(compilation);
        var existing = new TranslationInterchangeReview("app",
        [
            new TranslationInterchangeReviewEntry("greet", "de", TranslationReviewState.NeedsReview, "Tone?", "sha256:old"),
            new TranslationInterchangeReviewEntry("plain", "de", TranslationReviewState.Translated, null, fingerprints["plain"]),
        ]);
        TranslationInterchangeReview marked = TranslationReviewFile.Mark(existing, "app", ["greet", "greet", "later"], "de", TranslationReviewState.Approved, null, fingerprints);
        Require(marked.Entries.Count == 3, "Mark did not keep the other entry or duplicated a key.");
        Require(marked.Entries.Single(entry => entry.Key == "greet") is { State: TranslationReviewState.Approved, Note: "Tone?" } greet && greet.SourceFingerprint == fingerprints["greet"], "Mark did not stamp the current fingerprint or lost the note.");
        Require(marked.Entries.Single(entry => entry.Key == "later").SourceFingerprint == fingerprints["later"], "A new entry has no fingerprint.");
        Require(TranslationReviewFile.Stale(compilation, marked).Count == 0, "A freshly marked entry is stale.");
        Throws<ArgumentException>(() => TranslationReviewFile.Mark(existing, "app", ["unknown"], "de", TranslationReviewState.Approved, null, fingerprints), "An unknown key was marked.");
    }

    [Test, DisplayName("the import plan classifies each unit, and the merged review keeps unchanged entries and stamps applied ones")]
    public void ImportPlanAndReviewMerge()
    {
        Rmf2ProjectCompilation exported = Compile(English, German);
        string xml = Encoding.UTF8.GetString(TranslationInterchange.ExportXliff21(exported).Documents.Single().Bytes.Span);
        xml = SetTarget(xml, "later", "Später");
        xml = SetTarget(xml, "plain", "Einfach");
        // The source of "later" changes after export: the per-unit policy applies it as needs-review, not a whole-file refusal.
        Rmf2ProjectCompilation live = Compile(English.Replace("later = Later", "later = Later today", StringComparison.Ordinal), German);
        IReadOnlyDictionary<string, string> current = TranslationInterchange.ComputeSourceFingerprints(live);
        TranslationXliffImportResult import = TranslationInterchange.ImportXliff21(Encoding.UTF8.GetBytes(xml), live);
        TranslationXliffImportPlan plan = TranslationXliffImportPlan.Create(live, import);
        Require(Outcome(plan, "later") is { Outcome: "added", SourceChanged: true, Code: "XLIFF21-SOURCE-CHANGED", State: TranslationReviewState.NeedsReview, Before: null, After: "Später" },
            "A translation of a changed source was not added as needs-review.");
        Require(Outcome(plan, "plain") is { Outcome: "changed", Before: "Schlicht", After: "Einfach" }, "A changed translation was not planned as changed.");
        Require(Outcome(plan, "greet") is { Outcome: "unchanged", SourceChanged: false }, "An unchanged translation was not planned as unchanged.");
        Require(plan.Values.Keys.Order(StringComparer.Ordinal).SequenceEqual(["later", "plain"]), "The plan writes more than the added and changed units.");
        Require(plan.Count("added") == 1 && plan.Count("changed") == 1 && plan.Count("unchanged") == 1 && plan.SourceChanged == 1, "The plan counts are wrong.");

        var existing = new TranslationInterchangeReview("app",
        [
            new TranslationInterchangeReviewEntry("greet", "de", TranslationReviewState.Approved, "Keep", current["greet"]),
            new TranslationInterchangeReviewEntry("plain", "de", TranslationReviewState.Approved, "Old", current["plain"]),
        ]);
        (TranslationInterchangeReview merged, bool changed) = TranslationReviewFile.MergeImport(existing, "app", import, plan);
        Require(changed, "The merge reported no change.");
        Require(merged.Entries.Single(entry => entry.Key == "greet") is { State: TranslationReviewState.Approved, Note: "Keep" }, "An unchanged approved translation lost its entry.");
        Require(merged.Entries.Single(entry => entry.Key == "plain") is { State: TranslationReviewState.Translated, Note: "Old" } plain && plain.SourceFingerprint == current["plain"],
            "A changed translation was not recorded as translated for the current source.");
        TranslationInterchangeReviewEntry later = merged.Entries.Single(entry => entry.Key == "later");
        Require(later.State == TranslationReviewState.NeedsReview && later.SourceFingerprint != current["later"], "The translation of a changed source does not keep the fingerprint of the source it was made for.");
        // Merged again with the same input, nothing changes.
        Require(!TranslationReviewFile.MergeImport(merged, "app", import, plan).Changed, "A repeated merge reported a change.");
    }

    [Test, DisplayName("review fingerprints are computed while translations are incomplete or invalid, and not when the base locale has errors")]
    public void FingerprintsOfAnIncompleteCatalog()
    {
        IReadOnlyDictionary<string, string> complete = TranslationInterchange.ComputeSourceFingerprints(Compile(English, German));
        // translationCompleteness defaults to error: de misses 'later' (RTR0010), so the catalog does not compile.
        string strict = Project.Replace(",\n  \"validation\": { \"translationCompleteness\": \"allow\" }", "", StringComparison.Ordinal);
        TranslationSource project = Source("translations/runic.json", strict);
        TranslationSource[] incomplete = [Source("translations/en.rmf2", English), Source("translations/de.rmf2", German)];
        Rmf2ProjectCompilation failed = TranslationCompiler.CompileProject(project, incomplete);
        Require(!failed.Success && failed.Diagnostics.Any(static diagnostic => diagnostic.Id == "RTR0010"), "The fixture compiled without RTR0010.");
        Rmf2ProjectCompilation relaxed = TranslationReviewFile.ForReview(failed, project, incomplete);
        Require(relaxed.Success && relaxed.Project!.Locales.Single(static locale => locale.Tag == "de").DirectResources.Count == 2, "The relaxed compile lost the translations.");
        Require(Same(TranslationReviewFile.Fingerprints(failed, project, incomplete), complete), "The relaxed fingerprints differ from a complete catalog's.");

        // A translation with a syntax error: the base locale alone gives the same fingerprints; stale entries sit at the base message.
        TranslationSource[] broken = [Source("translations/en.rmf2", English), Source("translations/de.rmf2", "greet = Hallo {$name!\nplain = Schlicht\n")];
        Rmf2ProjectCompilation brokenCompilation = TranslationCompiler.CompileProject(project, broken);
        Rmf2ProjectCompilation baseOnly = TranslationReviewFile.ForReview(brokenCompilation, project, broken);
        Require(baseOnly.Success && baseOnly.Project!.Locales.Single(static locale => locale.Tag == "de").DirectResources.Count == 0, "The base-locale compile read the broken translation.");
        Require(Same(TranslationReviewFile.Fingerprints(brokenCompilation, project, broken), complete), "The base-locale fingerprints differ from a complete catalog's.");
        var review = new TranslationInterchangeReview("app", [new TranslationInterchangeReviewEntry("plain", "de", TranslationReviewState.Approved, null, "sha256:old")]);
        Require(TranslationReviewFile.Stale(baseOnly, review).Single().Location.Path == "translations/en.rmf2", "A stale entry of a base-locale compile is not at the base message.");

        // An error in the base locale gives no fingerprints; a successful compilation is used as it is.
        TranslationSource[] baseError = [Source("translations/en.rmf2", "greet = Hello {$name!\n"), Source("translations/de.rmf2", German)];
        Require(TranslationReviewFile.Fingerprints(TranslationCompiler.CompileProject(project, baseError), project, baseError) is null, "A broken base locale gave fingerprints.");
        Rmf2ProjectCompilation success = Compile(English, German);
        Require(ReferenceEquals(TranslationReviewFile.ForReview(success, project, incomplete), success), "A successful compilation was recompiled.");

        static bool Same(IReadOnlyDictionary<string, string>? left, IReadOnlyDictionary<string, string> right) =>
            left is not null && left.Count == right.Count && right.All(pair => left.TryGetValue(pair.Key, out string? value) && value == pair.Value);
    }

    [Test, DisplayName("review file writes compare and swap: a changed file is refused, and Update re-applies the change to it")]
    public void WritesCompareAndSwap()
    {
        string directory = Path.Combine(Path.GetTempPath(), "runic-review-" + Guid.NewGuid().ToString("N"));
        try
        {
            string path = TranslationReviewFile.PathFor(Path.Combine(directory, "runic.json"), "app");
            var first = new TranslationInterchangeReview("app", [new TranslationInterchangeReviewEntry("greet", "de", TranslationReviewState.Translated, null, "sha256:a")]);
            TranslationReviewFile.Write(path, first, expected: null);
            Require(Code(() => TranslationReviewFile.Write(path, first, expected: null)) == TranslationReviewFile.ChangedCode, "A write expecting no file replaced one.");
            (TranslationInterchangeReview? read, byte[]? bytes) = TranslationReviewFile.ReadSnapshot(path, "app");
            Require(read is not null && bytes is not null, "The snapshot is empty.");
            // Another writer replaces the file after the snapshot.
            TranslationReviewFile.Write(path, new TranslationInterchangeReview("app",
                [.. first.Entries, new TranslationInterchangeReviewEntry("plain", "de", TranslationReviewState.Approved, null, "sha256:b")]));
            Require(Code(() => TranslationReviewFile.Write(path, read!, bytes)) == TranslationReviewFile.ChangedCode, "A stale snapshot overwrote another writer's change.");
            Require(TranslationReviewFile.Read(path, "app")!.Entries.Count == 2, "The refused write changed the file.");

            // Update applies the change to the current file, so both writers' entries are kept.
            int calls = 0;
            TranslationReviewFile.Update(path, "app", current =>
            {
                if (calls++ == 0) TranslationReviewFile.Write(path, new TranslationInterchangeReview("app",
                    [.. current!.Entries, new TranslationInterchangeReviewEntry("later", "de", TranslationReviewState.Draft, null, null)]));
                return TranslationReviewFile.Mark(current, "app", ["greet"], "de", TranslationReviewState.Approved, "Checked",
                    new Dictionary<string, string> { ["greet"] = "sha256:c" });
            });
            TranslationInterchangeReview updated = TranslationReviewFile.Read(path, "app")!;
            Require(calls == 2 && updated.Entries.Count == 3 && updated.Entries.Single(static entry => entry.Key == "greet") is { State: TranslationReviewState.Approved, SourceFingerprint: "sha256:c" },
                "Update did not merge with the concurrent change.");
            Require(TranslationReviewFile.Update(path, "app", static _ => null) is { Entries.Count: 3 }, "Update without a change did not return the current file.");
            Require(Directory.GetFiles(directory).Single() == path, "A write left a temporary file.");

            // The 8 MiB limit applies to every read, including the bytes the import parses.
            File.WriteAllBytes(path, new byte[TranslationReviewFile.MaximumBytes + 1]);
            Require(Code(() => TranslationReviewFile.ReadBytes(path)) == "REVIEW-SIZE", "An oversized review file was read.");
            Require(Code(() => TranslationReviewFile.Parse(new byte[TranslationReviewFile.MaximumBytes + 1], "app")) == "REVIEW-SIZE", "Oversized review bytes were parsed.");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

        static string? Code(Action action)
        {
            try { action(); return null; }
            catch (TranslationInterchangeException exception) { return exception.Code; }
        }
    }

    private static TranslationXliffImportUnitPlan Outcome(TranslationXliffImportPlan plan, string key) => plan.Units.Single(unit => unit.Key == key);

    private static Rmf2ProjectCompilation Compile(string english, string german)
    {
        Rmf2ProjectCompilation compilation = TranslationCompiler.CompileProject(Source("translations/runic.json", Project),
            [Source("translations/en.rmf2", english), Source("translations/de.rmf2", german)]);
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

    private static void Throws<TException>(Func<object?> action, string message) where TException : Exception
    {
        try { _ = action(); }
        catch (TException) { return; }
        throw new InvalidOperationException(message);
    }
}
