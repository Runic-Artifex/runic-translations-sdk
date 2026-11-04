using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using Runic.Translations.Compiler;
using Runic.Translations.Tooling;
using TUnit.Core;

namespace Runic.Translations.Tooling.Tests;

// XLIFF profile v3: placeholders as <ph>, per-unit outcomes, TMS tolerance, review states and per-unit fingerprints.
internal sealed class XliffProfileV3Tests
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

    private const string English = """
        # Shown on the toolbar.
        @param $name - the signed-in user
        greet = Hello {$name}!
        items = {$count :integer} items
        later = Later
        plain = Plain
        settings {
          display {
            title = Display
          }
        }
        """;

    private const string German = """
        greet = Hallo {$name}!
        plain = Schlicht
        """;

    [Test, DisplayName("placeholders are exported as <ph> with their MF2 expression and imported losslessly")]
    public void PlaceholdersRoundTripAsPh()
    {
        Rmf2ProjectCompilation compilation = Compile(English, German);
        TranslationXliffExportResult export = TranslationInterchange.ExportXliff21(compilation);
        string xml = Text(export.Documents.Single().Bytes);
        Require(export.Report.IsLossless, "A placeholder message was reported as lossy.");
        Require(xml.Contains("<source>Hello <ph id=\"1\" equiv=\"{$name}\" disp=\"name\" />!</source><target>Hallo <ph id=\"1\" equiv=\"{$name}\" disp=\"name\" />!</target>", StringComparison.Ordinal),
            "The placeholder was not exported as <ph>: " + xml);
        Require(xml.Contains("<ph id=\"1\" equiv=\"{$count :integer}\" disp=\"count\" />", StringComparison.Ordinal), "A formatted placeholder was not exported as <ph>.");

        // A translator edits around the placeholder and adds the untranslated message.
        string edited = SetTarget(xml, "items", "<ph id=\"1\" equiv=\"{$count :integer}\" disp=\"count\"/> Elemente");
        edited = SetTarget(edited, "greet", "Hi <ph id=\"1\"/>, willkommen");
        TranslationXliffImportResult imported = TranslationInterchange.ImportXliff21(Encoding.UTF8.GetBytes(edited), compilation);
        Require(Message(imported, "items") == "{$count :integer} Elemente", "A formatted placeholder did not round-trip: " + Message(imported, "items"));
        Require(Message(imported, "greet") == "Hi {$name}, willkommen", "A placeholder resolved by id did not round-trip: " + Message(imported, "greet"));
        Require(Outcome(imported, "later").Code == "XLIFF21-UNTRANSLATED", "A unit without target was not skipped as untranslated.");
    }

    [Test, DisplayName("export validates against the pinned OASIS XLIFF 2.1 core schema")]
    public void ExportValidatesAgainstTheCoreSchema()
    {
        Rmf2ProjectCompilation compilation = Compile(English, German);
        var review = new TranslationInterchangeReview("app",
        [
            new TranslationInterchangeReviewEntry("greet", "de", TranslationReviewState.NeedsReview, "Check the tone."),
            new TranslationInterchangeReviewEntry("plain", "de", TranslationReviewState.Approved, null, TranslationInterchange.ComputeSourceFingerprints(compilation)["plain"]),
        ]);
        foreach (TranslationXliffDocument document in TranslationInterchange.ExportXliff21(compilation, review).Documents)
            ValidateSchema(document.Bytes.ToArray());
        foreach (string file in Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "specs", "translations", "corpus", "xliff-v3", "files"), "*.xliff"))
            ValidateSchema(File.ReadAllBytes(file));
    }

    [Test, DisplayName("readable description notes carry the description and @param lines and are accepted back")]
    public void DescriptionNotesAreReadable()
    {
        string xml = Text(TranslationInterchange.ExportXliff21(Compile(English, German)).Documents.Single().Bytes);
        Require(xml.Contains("<note category=\"description\">Shown on the toolbar.\n@param $name - the signed-in user</note>", StringComparison.Ordinal),
            "The readable description note is missing: " + xml);
        TranslationXliffImportResult imported = TranslationInterchange.ImportXliff21(Encoding.UTF8.GetBytes(xml));
        Require(imported.Report.Losses.All(loss => loss.Code != "XLIFF21-NOTE-IGNORED"), "Runic's own description note was reported as foreign.");
    }

    [Test, DisplayName("a TMS file with foreign notes, attributes, groups, markers and missing notes imports per unit")]
    public void TmsShapedFileImportsPerUnit()
    {
        Rmf2ProjectCompilation compilation = Compile(English, German);
        const string tms = """
            <?xml version="1.0" encoding="UTF-8"?>
            <xliff xmlns="urn:oasis:names:tc:xliff:document:2.0" xmlns:mda="urn:oasis:names:tc:xliff:metadata:2.0" version="2.0" srcLang="en" trgLang="de">
              <file id="app" original="app.de.xliff">
                <group id="g1" name="strings">
                  <unit id="greet" name="greet" canResegment="no">
                    <mda:metadata><mda:metaGroup category="tms"><mda:meta type="author">anna</mda:meta></mda:metaGroup></mda:metadata>
                    <notes>
                      <note category="comment">Is this too formal?</note>
                    </notes>
                    <segment id="s1" state="translated">
                      <source>Hello <ph id="1" equiv="{$name}" disp="name"/>!</source>
                      <target>Servus <ph id="1" equiv="{$name}" disp="name"/>!</target>
                    </segment>
                  </unit>
                  <unit id="later">
                    <segment state="initial"><source>Later</source><target></target></segment>
                  </unit>
                  <unit id="plain">
                    <segment><source>Plain</source><target/></segment>
                  </unit>
                  <unit id="settings_display_title">
                    <segment state="reviewed"><source>Display</source><target><mrk id="m1" type="term">Anzeige</mrk></target></segment>
                  </unit>
                  <unit id="items">
                    <segment state="translated"><source><ph id="1" equiv="{$count :integer}"/> items</source><target><ph id="1"/> Elemente für <ph id="2" equiv="{$user}"/></target></segment>
                  </unit>
                  <unit id="removed">
                    <segment state="translated"><source>Gone</source><target>Weg</target></segment>
                  </unit>
                </group>
              </file>
            </xliff>
            """;
        TranslationXliffImportResult imported = TranslationInterchange.ImportXliff21(Encoding.UTF8.GetBytes(tms), compilation);
        Require(Message(imported, "greet") == "Servus {$name}!", "A TMS-edited unit with a foreign note and module metadata was not applied.");
        Require(Outcome(imported, "later").Code == "XLIFF21-UNTRANSLATED", "An initial unit was not skipped.");
        Require(Outcome(imported, "plain").Code == "XLIFF21-EMPTY-TARGET", "An empty target without state was not skipped.");
        Require(Message(imported, "settings_display_title") == "Anzeige", "A nested-group key with a <mrk> annotation was not applied.");
        Require(imported.Review.Entries.Single(entry => entry.Key == "settings_display_title").State == TranslationReviewState.Approved, "XLIFF reviewed was not read as approved.");
        Require(Outcome(imported, "items") is { Outcome: TranslationXliffUnitOutcome.Refused, Code: "XLIFF21-PLACEHOLDER" }, "A placeholder outside the caller contract was not refused.");
        Require(Outcome(imported, "removed") is { Outcome: TranslationXliffUnitOutcome.Refused, Code: "XLIFF21-UNKNOWN-KEY" }, "A unit for an unknown key was not refused.");
        Require(imported.Report.Losses.Any(loss => loss.Code == "XLIFF21-NOTE-IGNORED" && loss.Location == "/greet"), "The ignored translator note was not reported.");
        Require(imported.Report.Losses.Any(loss => loss.Code == "XLIFF21-MARKER-IGNORED"), "The removed <mrk> annotation was not reported.");
    }

    [Test, DisplayName("an empty target is untranslated unless its state says translated")]
    public void EmptyTargetFollowsState()
    {
        Rmf2ProjectCompilation compilation = Compile(English, German);
        string xml = Text(TranslationInterchange.ExportXliff21(compilation).Documents.Single().Bytes);
        // An untranslated unit has no state; a TMS writes an empty target into it.
        TranslationXliffImportResult skipped = TranslationInterchange.ImportXliff21(Encoding.UTF8.GetBytes(SetTarget(xml, "later", "")), compilation);
        Require(Outcome(skipped, "later").Code == "XLIFF21-EMPTY-TARGET", "An empty target was applied without a translated state.");
        string translated = SetState(SetTarget(xml, "plain", ""), "plain", "translated");
        TranslationXliffImportResult applied = TranslationInterchange.ImportXliff21(Encoding.UTF8.GetBytes(translated), compilation);
        Require(Message(applied, "plain") == "{{}}", "An empty target in the translated state was not applied as an empty pattern.");
    }

    [Test, DisplayName("needs-review is translated with subState runic:needs-review, and segment state is authoritative without a review note")]
    public void ReviewStatesMapToXliff()
    {
        Rmf2ProjectCompilation compilation = Compile(English, German);
        IReadOnlyDictionary<string, string> fingerprints = TranslationInterchange.ComputeSourceFingerprints(compilation);
        var review = new TranslationInterchangeReview("app",
        [
            new TranslationInterchangeReviewEntry("greet", "de", TranslationReviewState.NeedsReview, "Check the tone."),
            new TranslationInterchangeReviewEntry("plain", "de", TranslationReviewState.Approved, null, fingerprints["plain"]),
        ]);
        string xml = Text(TranslationInterchange.ExportXliff21(compilation, review).Documents.Single().Bytes);
        Require(xml.Contains("<segment id=\"1\" state=\"translated\" subState=\"runic:needs-review\">", StringComparison.Ordinal), "needs-review was not exported as translated with its subState.");
        Require(xml.Contains("<segment id=\"1\" state=\"final\">", StringComparison.Ordinal), "approved was not exported as final.");
        Require(!xml.Contains("state=\"reviewed\"", StringComparison.Ordinal), "XLIFF reviewed was written.");

        TranslationXliffImportResult roundTrip = TranslationInterchange.ImportXliff21(Encoding.UTF8.GetBytes(xml), compilation);
        Require(roundTrip.Review.Entries.Single(entry => entry.Key == "greet") is { State: TranslationReviewState.NeedsReview, Note: "Check the tone." }, "needs-review did not round-trip.");
        Require(roundTrip.Review.Entries.Single(entry => entry.Key == "plain").State == TranslationReviewState.Approved, "approved did not round-trip.");

        // A reviewer approves greet in the TMS; the segment state replaces the review note's state.
        string approved = xml.Replace("state=\"translated\" subState=\"runic:needs-review\"", "state=\"final\"", StringComparison.Ordinal);
        TranslationXliffImportResult tms = TranslationInterchange.ImportXliff21(Encoding.UTF8.GetBytes(approved), compilation);
        Require(tms.Review.Entries.Single(entry => entry.Key == "greet") is { State: TranslationReviewState.Approved } entry && entry.SourceFingerprint == fingerprints["greet"],
            "The TMS approval was not authoritative or not stamped with the unit's fingerprint.");
        Require(tms.Report.Losses.Any(loss => loss.Code == "XLIFF21-STATE-CHANGED" && loss.Location == "/greet"), "The state change was not reported.");
    }

    [Test, DisplayName("approvals are per unit: editing one key leaves the others approved, and a stale approval is demoted on export")]
    public void ApprovalsArePerUnit()
    {
        Rmf2ProjectCompilation before = Compile(English, German);
        IReadOnlyDictionary<string, string> fingerprints = TranslationInterchange.ComputeSourceFingerprints(before);
        var review = new TranslationInterchangeReview("app",
        [
            new TranslationInterchangeReviewEntry("greet", "de", TranslationReviewState.Approved, null, fingerprints["greet"]),
            new TranslationInterchangeReviewEntry("plain", "de", TranslationReviewState.Approved, null, fingerprints["plain"]),
        ]);
        Rmf2ProjectCompilation after = Compile(English.Replace("plain = Plain", "plain = Plain text", StringComparison.Ordinal).Replace("later = Later", "later = Later on", StringComparison.Ordinal), German);
        IReadOnlyDictionary<string, string> changed = TranslationInterchange.ComputeSourceFingerprints(after);
        Require(changed["greet"] == fingerprints["greet"] && changed["plain"] != fingerprints["plain"], "Per-unit fingerprints are not independent of other keys.");
        TranslationXliffExportResult export = TranslationInterchange.ExportXliff21(after, review);
        string xml = Text(export.Documents.Single().Bytes);
        Require(Segment(xml, "greet").Contains("state=\"final\"", StringComparison.Ordinal), "An unaffected approval was not kept.");
        Require(Segment(xml, "plain").Contains("subState=\"runic:needs-review\"", StringComparison.Ordinal), "A stale approval was not demoted to needs-review.");
        Require(export.Report.Losses.Single(loss => loss.Code == "REVIEW-STALE").Location == "/plain", "The demotion was not reported.");
    }

    [Test, DisplayName("a unit whose source changed since export is applied as needs-review or refused by policy")]
    public void SourceChangesFollowThePolicy()
    {
        Rmf2ProjectCompilation exported = Compile(English, German);
        string xml = SetTarget(Text(TranslationInterchange.ExportXliff21(exported).Documents.Single().Bytes), "later", "Später");
        Rmf2ProjectCompilation live = Compile(English.Replace("later = Later", "later = Later today", StringComparison.Ordinal), German);
        TranslationXliffImportResult review = TranslationInterchange.ImportXliff21(Encoding.UTF8.GetBytes(xml), live);
        TranslationXliffUnitResult later = Outcome(review, "later");
        Require(later is { Outcome: TranslationXliffUnitOutcome.Applied, SourceChanged: true, State: TranslationReviewState.NeedsReview } &&
            later.SourceFingerprint == TranslationInterchange.ComputeSourceFingerprints(exported)["later"],
            "A changed source was not applied as needs-review with the fingerprint of the source the translator saw.");
        Require(review.Review.Entries.Single(entry => entry.Key == "later").State == TranslationReviewState.NeedsReview, "The needs-review state was not part of the review.");
        Require(!Outcome(review, "greet").SourceChanged, "An unchanged unit was reported as changed.");
        TranslationXliffImportResult refuse = TranslationInterchange.ImportXliff21(Encoding.UTF8.GetBytes(xml), live,
            new TranslationXliffImportOptions { SourceChangePolicy = TranslationSourceChangePolicy.Refuse });
        Require(Outcome(refuse, "later") is { Outcome: TranslationXliffUnitOutcome.Refused, Code: "XLIFF21-SOURCE-CHANGED" } && Outcome(refuse, "greet").Outcome == TranslationXliffUnitOutcome.Applied,
            "The refuse policy did not refuse only the changed unit.");
        try { _ = TranslationInterchange.ImportXliff21(Encoding.UTF8.GetBytes(xml.Replace("trgLang=\"de\"", "trgLang=\"fr\"", StringComparison.Ordinal)), live); }
        catch (TranslationInterchangeException exception) when (exception.Code == "XLIFF21-TARGET-LOCALE") { return; }
        throw new InvalidOperationException("A document for an undeclared locale was imported against the live catalog.");
    }

    [Test, DisplayName("profile 2 documents are still read")]
    public void ProfileTwoDocumentsAreRead()
    {
        string unit = Base64("{\"executionProfile\":\"rmf2-execution-v2\",\"messageGrammarVersion\":5,\"interchangeProfile\":\"runic.xliff21.closed-text\",\"interchangeProfileVersion\":2,\"structured\":false,\"tags\":\"" + Base64("[]") + "\",\"placeholders\":\"" + Base64("[]") + "\"}");
        string reviewNote = Base64("{\"state\":\"needs-review\",\"note\":\"Check\"}");
        string xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?><xliff xmlns=\"urn:oasis:names:tc:xliff:document:2.0\" version=\"2.1\" srcLang=\"en\" trgLang=\"de\"><file id=\"app\" original=\"app.de.xliff\">" +
            "<unit id=\"plain\"><notes><note category=\"runic:unit\">" + unit + "</note><note category=\"runic:review\" appliesTo=\"target\">" + reviewNote + "</note></notes><segment id=\"1\" state=\"reviewed\"><source>Plain</source><target>Schlicht \\{x\\}</target></segment></unit>" +
            "<unit id=\"later\"><notes><note category=\"runic:unit\">" + unit + "</note></notes><segment id=\"1\"><source>Later</source></segment></unit>" +
            "</file></xliff>";
        TranslationXliffImportResult imported = TranslationInterchange.ImportXliff21(Encoding.UTF8.GetBytes(xml));
        Require(imported.ProfileVersion == 2, "The profile version was not reported.");
        Require(Message(imported, "plain") == "Schlicht \\{x\\}", "A profile 2 target was not read as MF2 text.");
        Require(imported.Review.Entries.Single() is { State: TranslationReviewState.NeedsReview, Note: "Check" }, "Profile 2 reviewed with a needs-review note was not read as needs-review.");
        Require(Outcome(imported, "later").Code == "XLIFF21-UNTRANSLATED", "A profile 2 unit without target was not skipped.");
    }

    [Test, DisplayName("the source writer edits .rmf2 in place, adds missing keys beside the base file and writes direct .mf2 files")]
    public void SourceWriterAppliesValues()
    {
        TranslationSource project = Source("translations/runic.json", Project);
        TranslationSource[] sources =
        [
            Source("translations/en.rmf2", English),
            Source("translations/de.rmf2", "# German.\n" + German),
        ];
        Rmf2ProjectCompilation compilation = TranslationCompiler.CompileProject(project, sources);
        Require(compilation.Success, "The writer fixture did not compile.");
        IReadOnlyList<TranslationSourceEdit> edits = TranslationInterchangeSourceWriter.Plan(Environment.CurrentDirectory, project, sources, compilation, "de",
        [
            new("greet", "Servus {$name}!"),
            new("settings_display_title", "Anzeige"),
        ]);
        TranslationSourceEdit edit = edits.Single();
        string german = Encoding.UTF8.GetString(edit.Bytes);
        Require(edit.Path == "translations/de.rmf2" && german.StartsWith("# German.\ngreet = Servus {$name}!\nplain = Schlicht\n", StringComparison.Ordinal) &&
            german.Contains("settings {\n  display {\n    title = Anzeige\n  }\n}", StringComparison.Ordinal),
            "The .rmf2 edit did not keep the file or add the nested key: " + german);
        Require(TranslationCompiler.CompileProject(project, TranslationInterchangeSourceWriter.Apply(sources, edits)).Success, "The edited sources do not compile.");

        TranslationSource[] direct = [Source("translations/en/hello.mf2", "Hello\n"), Source("translations/en/bye.mf2", "Bye\n"), Source("translations/de/hello.mf2", "Hallo\n")];
        Rmf2ProjectCompilation directCompilation = TranslationCompiler.CompileMf2Project(project, direct);
        Require(directCompilation.Success, "The direct fixture did not compile.");
        IReadOnlyList<TranslationSourceEdit> directEdits = TranslationInterchangeSourceWriter.Plan(Environment.CurrentDirectory, project, direct, directCompilation, "de",
            [new("hello", "Servus"), new("bye", "Tschüss")]);
        Require(directEdits.Select(item => item.Path + "=" + Encoding.UTF8.GetString(item.Bytes) + (item.OriginalBytes is null ? "(new)" : "")).SequenceEqual(
            ["translations/de/bye.mf2=Tschüss\n(new)", "translations/de/hello.mf2=Servus\n"]), "The direct .mf2 edits are wrong.");
    }

    [Test, DisplayName("deeply nested <mrk> and <group> are refused as a document over the depth limit, not a crash")]
    public void DeepNestingIsRefused()
    {
        Rmf2ProjectCompilation compilation = Compile(English, German);
        string xml = Text(TranslationInterchange.ExportXliff21(compilation).Documents.Single().Bytes);
        string markers = SetTarget(xml, "plain", string.Concat(Enumerable.Repeat("<mrk id=\"m\">", 20_000)) + "Schlicht" + string.Concat(Enumerable.Repeat("</mrk>", 20_000)));
        RequireLimit(markers, "20,000 nested <mrk>");
        int file = xml.IndexOf("<unit ", StringComparison.Ordinal), end = xml.LastIndexOf("</file>", StringComparison.Ordinal);
        string groups = xml[..file] + string.Concat(Enumerable.Repeat("<group id=\"g\">", 100_000)) + xml[file..end] + string.Concat(Enumerable.Repeat("</group>", 100_000)) + xml[end..];
        RequireLimit(groups, "100,000 nested <group>");

        // Nesting below the limit is walked without recursion and keeps document order.
        string shallow = xml[..file] + string.Concat(Enumerable.Repeat("<group id=\"g\">", 40)) + xml[file..end] + string.Concat(Enumerable.Repeat("</group>", 40)) + xml[end..];
        shallow = SetTarget(shallow, "plain", string.Concat(Enumerable.Repeat("<mrk id=\"m\">", 10)) + "Sch<mrk id=\"n\">li</mrk>cht" + string.Concat(Enumerable.Repeat("</mrk>", 10)));
        TranslationXliffImportResult imported = TranslationInterchange.ImportXliff21(Encoding.UTF8.GetBytes(shallow), compilation);
        Require(Message(imported, "plain") == "Schlicht" && imported.Units.Select(unit => unit.Key).SequenceEqual(compilation.Project!.CanonicalMessages.Select(message => message.Key).Order(StringComparer.Ordinal)),
            "Nested groups or markers below the limit were not read in order: " + string.Join(", ", imported.Units.Select(unit => unit.Key)));

        static void RequireLimit(string document, string what)
        {
            try { _ = TranslationInterchange.ImportXliff21(Encoding.UTF8.GetBytes(document)); }
            catch (TranslationInterchangeException exception) when (exception.Code == "XLIFF21-LIMIT" && exception.Message.Contains("64", StringComparison.Ordinal)) { return; }
            throw new InvalidOperationException(what + " was not refused with XLIFF21-LIMIT.");
        }
    }

    [Test, DisplayName("a target <ph equiv> may move and respell a placeholder but not change its formatting or replace it")]
    public void TargetPlaceholdersCannotChangeExpressions()
    {
        Rmf2ProjectCompilation compilation = Compile(English, German);
        string xml = Text(TranslationInterchange.ExportXliff21(compilation).Documents.Single().Bytes);
        string edited = SetTarget(xml, "items", "<ph id=\"9\" equiv=\"{$count :number style=percent}\"/> Artikel");
        edited = SetTarget(edited, "greet", "Hallo <ph id=\"7\" equiv=\"{|Mallory|}\"/>!");
        foreach (Rmf2ProjectCompilation? live in new[] { compilation, null })
        {
            TranslationXliffImportResult imported = live is null
                ? TranslationInterchange.ImportXliff21(Encoding.UTF8.GetBytes(edited))
                : TranslationInterchange.ImportXliff21(Encoding.UTF8.GetBytes(edited), live);
            Require(Outcome(imported, "items") is { Outcome: TranslationXliffUnitOutcome.Refused, Code: "XLIFF21-PLACEHOLDER-CHANGED" } items &&
                items.Message!.Contains("{$count :number style=percent}", StringComparison.Ordinal) && items.Message.Contains("{$count :integer}", StringComparison.Ordinal),
                "A target that changed the placeholder's formatting was not refused with its reason: " + Outcome(imported, "items").Message);
            Require(Outcome(imported, "greet") is { Outcome: TranslationXliffUnitOutcome.Refused, Code: "XLIFF21-PLACEHOLDER-CHANGED" },
                "A literal that replaced a placeholder was not refused.");
            Require(Message(imported, "items") is null && Message(imported, "greet") is null, "A refused unit was applied.");
        }

        // The same expression spelled differently, from equiv or originalData, is accepted and written as the source spells it.
        string respelled = SetTarget(xml, "items", "<ph id=\"9\" equiv=\"{ $count   :integer }\"/> Artikel, <ph id=\"1\"/> insgesamt");
        TranslationXliffImportResult accepted = TranslationInterchange.ImportXliff21(Encoding.UTF8.GetBytes(respelled), compilation);
        Require(Message(accepted, "items") == "{$count :integer} Artikel, {$count :integer} insgesamt", "A respelled placeholder was not accepted in the source's spelling: " + Message(accepted, "items"));

        // An expression of the live translation is accepted too, even when the source formats it differently.
        Rmf2ProjectCompilation translated = Compile(English, German + "\nitems = {$count :number} Stück\n");
        string own = SetTarget(Text(TranslationInterchange.ExportXliff21(translated).Documents.Single().Bytes), "items", "<ph id=\"9\" equiv=\"{$count :number}\"/> Teile");
        Require(Message(TranslationInterchange.ImportXliff21(Encoding.UTF8.GetBytes(own), translated), "items") == "{$count :number} Teile",
            "An expression of the live translation was refused.");
    }

    [Test, DisplayName("a stateless target that copies the source is imported as needs-review; an explicit state is kept")]
    public void CopiedSourceIsNeedsReview()
    {
        Rmf2ProjectCompilation compilation = Compile(English, German);
        string xml = Text(TranslationInterchange.ExportXliff21(compilation).Documents.Single().Bytes);
        string copied = SetTarget(SetTarget(xml, "later", "Later"), "items", "<ph id=\"1\"/> items");
        TranslationXliffImportResult imported = TranslationInterchange.ImportXliff21(Encoding.UTF8.GetBytes(copied), compilation);
        foreach (string key in new[] { "later", "items" })
        {
            Require(Outcome(imported, key) is { Outcome: TranslationXliffUnitOutcome.Applied, State: TranslationReviewState.NeedsReview } &&
                imported.Review.Entries.Single(entry => entry.Key == key).State == TranslationReviewState.NeedsReview,
                "A stateless copy of the source was not imported as needs-review: " + key);
            Require(imported.Report.Losses.Any(loss => loss.Code == "XLIFF21-TARGET-COPIES-SOURCE" && loss.Location == "/" + key), "The copy was not reported: " + key);
        }
        TranslationXliffImportResult stated = TranslationInterchange.ImportXliff21(Encoding.UTF8.GetBytes(SetState(SetState(copied, "later", "translated"), "items", "final")), compilation);
        Require(Outcome(stated, "later").State == TranslationReviewState.Translated && Outcome(stated, "items").State == TranslationReviewState.Approved &&
            stated.Report.Losses.All(loss => loss.Code != "XLIFF21-TARGET-COPIES-SOURCE"), "An identical translation with an explicit state did not keep it.");
        TranslationXliffImportResult different = TranslationInterchange.ImportXliff21(Encoding.UTF8.GetBytes(SetTarget(xml, "later", "Später")), compilation);
        Require(Outcome(different, "later").State == TranslationReviewState.Translated, "A stateless translation was not imported as translated.");
    }

    [Test, DisplayName("the contract check refuses every invalid unit, so a library result holds only valid messages")]
    public void ContractCheckReachesAFixedPoint()
    {
        // Without a live catalog the file's source is the contract; a source whose placeholders conflict (one input
        // read as two types) does not compile, so its unit is invalid, whatever the other units contain.
        static string Unit(string key, string source, string target) =>
            "<unit id=\"" + key + "\"><segment state=\"translated\"><source>" + source + "</source><target>" + target + "</target></segment></unit>";
        string xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?><xliff xmlns=\"urn:oasis:names:tc:xliff:document:2.0\" version=\"2.1\" srcLang=\"en\" trgLang=\"de\"><file id=\"app\">" +
            Unit("alpha", "<ph id=\"1\" equiv=\"{$n :integer}\"/> of <ph id=\"2\" equiv=\"{$n :datetime}\"/>", "<ph id=\"1\"/> von <ph id=\"2\"/>") +
            Unit("beta", "Plain", "Schlicht") +
            Unit("gamma", "<ph id=\"1\" equiv=\"{$when :datetime}\"/> <ph id=\"2\" equiv=\"{$when :string}\"/>", "am <ph id=\"1\"/> <ph id=\"2\"/>") +
            Unit("delta", "Hello <ph id=\"1\" equiv=\"{$name}\"/>", "Hallo <ph id=\"1\"/>") +
            "</file></xliff>";
        TranslationXliffImportResult imported = TranslationInterchange.ImportXliff21(Encoding.UTF8.GetBytes(xml));
        Require(imported.Units.Where(unit => unit.Outcome == TranslationXliffUnitOutcome.Refused).Select(unit => unit.Key + "=" + unit.Code).SequenceEqual(["alpha=XLIFF21-TARGET-INVALID", "gamma=XLIFF21-TARGET-INVALID"]),
            "The invalid units were not all refused: " + string.Join(", ", imported.Units.Select(unit => unit.Key + "=" + unit.Outcome + "/" + unit.Code)));
        Require(imported.Messages.Select(message => message.MessageId).SequenceEqual(["beta", "delta"]), "An invalid message is in the library result.");
    }

    [Test, DisplayName("source fingerprints ignore spacing inside expressions and selectors, but not a changed placeholder or selector")]
    public void FingerprintsUseCanonicalSource()
    {
        const string match = ".input {$count :integer}\n.match $count\none {{One item}}\n* {{{$count} items}}";
        string[] Fingerprint(string items, string count = match)
        {
            string english = English.Replace("items = {$count :integer} items", "items = " + items, StringComparison.Ordinal) + "\ncount =\n" + string.Join("\n", count.Split('\n').Select(line => "  " + line)) + "\n";
            IReadOnlyDictionary<string, string> fingerprints = TranslationInterchange.ComputeSourceFingerprints(Compile(english, German));
            return [fingerprints["items"], fingerprints["count"]];
        }
        string[] baseline = Fingerprint("{$count :integer} items");
        string[] spaced = Fingerprint("{ $count   :integer } items", ".input   { $count  :integer }\n.match   $count\n  one   {{One item}}\n  *   {{{ $count } items}}");
        Require(spaced.SequenceEqual(baseline), "Spacing inside expressions or between selector parts changed a fingerprint.");
        Require(Fingerprint("{$count :number} items")[0] != baseline[0], "A changed function did not change the fingerprint.");
        Require(Fingerprint("{$count :integer} things")[0] != baseline[0], "Changed text did not change the fingerprint.");
        Require(Fingerprint("{$count :integer}  items")[0] != baseline[0], "Changed pattern spacing did not change the fingerprint.");
        Require(Fingerprint("{$count :integer} items", match.Replace("one {{One item}}", "one {{Single item}}", StringComparison.Ordinal))[1] != baseline[1],
            "A changed variant did not change the fingerprint.");
        Require(Fingerprint("{$count :integer} items", match.Replace(".input {$count :integer}", ".input {$count :number}", StringComparison.Ordinal))[1] != baseline[1],
            "A changed selector function did not change the fingerprint.");
    }

    [Test, DisplayName("the source writer adds a key to an existing group with 4-space indentation")]
    public void SourceWriterCopiesGroupIndentation()
    {
        TranslationSource project = Source("translations/runic.json", Project);
        string english = "cart {\n    empty = Empty\n    items = Items\n}\nother = Other\n";
        TranslationSource[] sources = [Source("translations/en.rmf2", english), Source("translations/de.rmf2", "cart {\n    empty = Leer\n}\n")];
        Rmf2ProjectCompilation compilation = TranslationCompiler.CompileProject(project, sources);
        Require(compilation.Success, "The writer fixture did not compile.");
        IReadOnlyList<TranslationSourceEdit> edits = TranslationInterchangeSourceWriter.Plan(Environment.CurrentDirectory, project, sources, compilation, "de",
            [new("cart_items", "Artikel"), new("other", "Andere")]);
        Require(Encoding.UTF8.GetString(edits.Single().Bytes) == "cart {\n    empty = Leer\n    items = Artikel\n}\nother = Andere\n",
            "The key was not added with the group's indentation: " + Encoding.UTF8.GetString(edits.Single().Bytes));
        Require(TranslationCompiler.CompileProject(project, TranslationInterchangeSourceWriter.Apply(sources, edits)).Success, "The edited sources do not compile.");
    }

    private static void ValidateSchema(byte[] document)
    {
        string schemas = Path.Combine(RepositoryRoot(), "tests", "dotnet", "Runic.Translations.Tooling.Tests", "Fixtures", "xliff-2.1");
        var set = new XmlSchemaSet { XmlResolver = new XmlUrlResolver() };
        set.Add("http://www.w3.org/XML/1998/namespace", Path.Combine(schemas, "informativeCopiesOf3rdPartySchemas", "w3c", "xml.xsd"));
        using (XmlReader core = XmlReader.Create(Path.Combine(schemas, "xliff_core_2.0.xsd"), new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore }))
            set.Add(TranslationInterchangeNamespace, core);
        set.Compile();
        var errors = new List<string>();
        var settings = new XmlReaderSettings { ValidationType = ValidationType.Schema, Schemas = set, ValidationFlags = XmlSchemaValidationFlags.ReportValidationWarnings };
        settings.ValidationEventHandler += (_, args) => { if (args.Severity == XmlSeverityType.Error) errors.Add(args.Message); };
        using var stream = new MemoryStream(document);
        using (XmlReader reader = XmlReader.Create(stream, settings)) while (reader.Read()) { }
        Require(errors.Count == 0, "An XLIFF document does not validate against the XLIFF 2.1 core schema: " + string.Join("; ", errors));
    }

    private const string TranslationInterchangeNamespace = "urn:oasis:names:tc:xliff:document:2.0";

    internal static string RepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Runic.Translations.slnx"))) return directory.FullName;
        throw new InvalidOperationException("The repository root was not found.");
    }

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
        XNamespace ns = TranslationInterchangeNamespace;
        XElement segment = document.Descendants(ns + "unit").Single(unit => (string?)unit.Attribute("id") == key).Element(ns + "segment")!;
        segment.Element(ns + "target")?.Remove();
        XElement parsed = XElement.Parse("<target xmlns=\"" + TranslationInterchangeNamespace + "\">" + target + "</target>", LoadOptions.PreserveWhitespace);
        segment.Element(ns + "source")!.AddAfterSelf(parsed);
        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static string SetState(string xml, string key, string state)
    {
        XDocument document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        XNamespace ns = TranslationInterchangeNamespace;
        document.Descendants(ns + "unit").Single(unit => (string?)unit.Attribute("id") == key).Element(ns + "segment")!.SetAttributeValue("state", state);
        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static string Segment(string xml, string key)
    {
        XNamespace ns = TranslationInterchangeNamespace;
        return XDocument.Parse(xml).Descendants(ns + "unit").Single(unit => (string?)unit.Attribute("id") == key).Element(ns + "segment")!.ToString(SaveOptions.DisableFormatting);
    }

    private static string? Message(TranslationXliffImportResult result, string key) =>
        result.Messages.SingleOrDefault(message => message.MessageId == key) is { } message ? Encoding.UTF8.GetString(message.Bytes.Span).TrimEnd('\n') : null;

    private static TranslationXliffUnitResult Outcome(TranslationXliffImportResult result, string key) => result.Units.Single(unit => unit.Key == key);
    private static string Text(ReadOnlyMemory<byte> bytes) => Encoding.UTF8.GetString(bytes.Span);
    private static string Base64(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    private static TranslationSource Source(string path, string text) => new(path, Encoding.UTF8.GetBytes(text));
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
