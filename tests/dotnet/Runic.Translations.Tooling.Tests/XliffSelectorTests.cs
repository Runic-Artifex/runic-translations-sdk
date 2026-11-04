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

// Selector messages (plurals, selects, declarations) through XLIFF as one unit: the header is a protected <ph>, every
// variant a <pc> whose start code is its keys, and translators edit only the variant texts (D055.3).
internal sealed class XliffSelectorTests
{
    private const string Xliff = "urn:oasis:names:tc:xliff:document:2.0";
    private const string Items = "items =\n  .input {$n :integer}\n  .match $n\n  one {{One item}}\n  * {{{$n} items}}";
    private const string Header = ".input {$n :integer}\n.match $n";

    private static string ProjectJson(string locales, string validation = "") =>
        "{\"schemaVersion\":1,\"catalog\":\"app\",\"code\":{\"namespace\":\"App\",\"className\":\"Text\"},\"baseLocale\":\"en\",\"locales\":[\"en\"" + locales + "]," +
        "\"validation\":{\"translationCompleteness\":\"allow\"" + validation + "}}";

    private static Rmf2ProjectCompilation Compile(string english, string? translated = null, string locale = "de", string validation = "")
    {
        var sources = new List<TranslationSource> { new("translations/en.rmf2", Encoding.UTF8.GetBytes(english)) };
        if (translated is not null) sources.Add(new("translations/" + locale + ".rmf2", Encoding.UTF8.GetBytes(translated)));
        Rmf2ProjectCompilation compilation = TranslationCompiler.CompileProject(
            new TranslationSource("translations/runic.json", Encoding.UTF8.GetBytes(ProjectJson(",{\"tag\":\"" + locale + "\",\"fallback\":\"en\"}", validation))), sources);
        Require(compilation.Success, "The fixture did not compile: " + string.Join("; ", compilation.Diagnostics.Select(d => d.Id + " " + d.Message)));
        return compilation;
    }

    private static string Export(Rmf2ProjectCompilation compilation) =>
        Encoding.UTF8.GetString(TranslationInterchange.ExportXliff21(compilation).Documents.Single().Bytes.Span);

    private static XElement Unit(string xml, string key) =>
        XDocument.Parse(xml, LoadOptions.PreserveWhitespace).Descendants(XName.Get("unit", Xliff)).Single(unit => (string?)unit.Attribute("id") == key);

    // Replaces a unit's target and segment state.
    private static string SetTarget(string xml, string key, string target, string? state = "translated")
    {
        XDocument document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        XNamespace ns = Xliff;
        XElement segment = document.Descendants(ns + "unit").Single(unit => (string?)unit.Attribute("id") == key).Element(ns + "segment")!;
        segment.Element(ns + "target")?.Remove();
        segment.Element(ns + "source")!.AddAfterSelf(XElement.Parse("<target xmlns=\"" + Xliff + "\">" + target + "</target>", LoadOptions.PreserveWhitespace));
        if (state is null) segment.Attribute("state")?.Remove(); else segment.SetAttributeValue("state", state);
        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static TranslationXliffImportResult Import(string xml, Rmf2ProjectCompilation compilation) =>
        TranslationInterchange.ImportXliff21(Encoding.UTF8.GetBytes(xml), compilation);

    private static string? Message(TranslationXliffImportResult result, string key) =>
        result.Messages.SingleOrDefault(message => message.MessageId == key) is { } message ? Encoding.UTF8.GetString(message.Bytes.Span).TrimEnd('\n') : null;

    private static TranslationXliffUnitResult Outcome(TranslationXliffImportResult result, string key) => result.Units.Single(unit => unit.Key == key);

    private const string GermanItems = "<ph id=\"h\"/><pc id=\"v1\">Ein Element</pc><pc id=\"v2\"><ph id=\"1\"/> Elemente</pc>";

    [Test, DisplayName("a plural message exports as a header <ph> and one <pc> per variant, losslessly and schema-valid")]
    public void ExportsSelectorsAsProtectedCodes()
    {
        Rmf2ProjectCompilation compilation = Compile(Items, "items =\n  .input {$n :integer}\n  .match $n\n  one {{Ein Element}}\n  * {{{$n} Elemente}}");
        TranslationXliffExportResult export = TranslationInterchange.ExportXliff21(compilation);
        Require(export.Report.IsLossless, "A plural message was reported as lossy: " + string.Join("; ", export.Report.Losses.Select(loss => loss.Code)));
        string xml = Encoding.UTF8.GetString(export.Documents.Single().Bytes.Span);
        ValidateSchema(Encoding.UTF8.GetBytes(xml));
        Require(xml.Contains("<unit id=\"items\">", StringComparison.Ordinal), "The plural unit is not translatable: " + xml);
        Require(xml.Contains("<source><ph id=\"h\" canCopy=\"no\" canDelete=\"no\" equiv=\".input {$n :integer}&#xA;.match $n\" disp=\".match $n\" />" +
            "<pc id=\"v1\" canCopy=\"no\" canOverlap=\"no\" equivStart=\"one {{\" equivEnd=\"}}\" dispStart=\"one\" dispEnd=\"\">One item</pc>" +
            "<pc id=\"v2\" canCopy=\"no\" canOverlap=\"no\" equivStart=\"* {{\" equivEnd=\"}}\" dispStart=\"*\" dispEnd=\"\"><ph id=\"1\" equiv=\"{$n}\" disp=\"n\" /> items</pc></source>", StringComparison.Ordinal),
            "The source codes differ: " + xml);
        Require(xml.Contains("<target><ph id=\"h\" canCopy=\"no\" canDelete=\"no\" equiv=\".input {$n :integer}&#xA;.match $n\" disp=\".match $n\" />" +
            "<pc id=\"v1\" canCopy=\"no\" canOverlap=\"no\" equivStart=\"one {{\" equivEnd=\"}}\" dispStart=\"one\" dispEnd=\"\">Ein Element</pc>", StringComparison.Ordinal),
            "The target does not reuse the source ids: " + xml);

        // The unchanged document re-imports to the same message, through the source ids alone too.
        TranslationXliffImportResult same = Import(xml, compilation);
        Require(Message(same, "items") == Header + "\none {{Ein Element}}\n* {{{$n} Elemente}}", "The export did not round-trip: " + Message(same, "items"));
        TranslationXliffImportResult byId = Import(SetTarget(xml, "items", "<ph id=\"h\"/><pc id=\"v1\">Eins</pc>\n<pc id=\"v2\"><ph id=\"1\"/> Stück</pc>"), compilation);
        Require(Message(byId, "items") == Header + "\none {{Eins}}\n* {{{$n} Stück}}", "A target with ids only did not import: " + Message(byId, "items") + " " + Outcome(byId, "items").Message);
    }

    [Test, DisplayName("a select message and a message with declarations only are exchanged too; markup stays skipped")]
    public void SelectsAndDeclarations()
    {
        const string english = "status =\n  .input {$s :string}\n  .match $s\n  shipped {{Shipped}}\n  * {{Preparing}}\n" +
            "total =\n  .local $sum = {$amount :number minimumFractionDigits=2}\n  {{Total: {$sum}}}\nhelp = Read the {#strong}help{/strong}.";
        Rmf2ProjectCompilation compilation = Compile(english);
        string xml = Export(compilation);
        Require(Unit(xml, "status").Attribute("translate") is null && Unit(xml, "total").Attribute("translate") is null, "A select or declaration unit is not translatable.");
        Require((string?)Unit(xml, "help").Attribute("translate") == "no", "A markup unit became translatable.");
        string edited = SetTarget(xml, "status", "<ph id=\"h\"/><pc id=\"v1\">Versandt</pc><pc id=\"v2\">In Vorbereitung</pc>");
        edited = SetTarget(edited, "total", "<ph id=\"h\"/><pc id=\"v1\">Summe: <ph id=\"1\"/></pc>");
        edited = SetTarget(edited, "help", "Lies die Hilfe.");
        TranslationXliffImportResult imported = Import(edited, compilation);
        Require(Message(imported, "status") == ".input {$s :string}\n.match $s\nshipped {{Versandt}}\n* {{In Vorbereitung}}", "The select did not import: " + Message(imported, "status"));
        Require(Message(imported, "total") == ".local $sum = {$amount :number minimumFractionDigits=2}\n{{Summe: {$sum}}}", "The declaration message did not import: " + Message(imported, "total") + " " + Outcome(imported, "total").Message);
        Require(Outcome(imported, "help").Code is "XLIFF21-NOT-TRANSLATABLE" or "XLIFF21-STRUCTURED-SKIPPED", "A markup unit was not skipped: " + Outcome(imported, "help").Code);
    }

    [Test, DisplayName("a locale with other plural categories gets a prefilled target in state initial with its variants")]
    public void PrefillsTheLocalesCategories()
    {
        Rmf2ProjectCompilation czech = Compile(Items, locale: "cs");
        string xml = Export(czech);
        ValidateSchema(Encoding.UTF8.GetBytes(xml));
        XElement segment = Unit(xml, "items").Element(XName.Get("segment", Xliff))!;
        Require((string?)segment.Attribute("state") == "initial", "The prefilled segment is not initial.");
        string[] keys = segment.Element(XName.Get("target", Xliff))!.Elements(XName.Get("pc", Xliff)).Select(pc => (string)pc.Attribute("equivStart")!).ToArray();
        Require(keys.SequenceEqual(["one {{", "few {{", "many {{", "* {{"]), "The Czech variants differ: " + string.Join(", ", keys));
        Require(segment.Element(XName.Get("target", Xliff))!.Elements(XName.Get("pc", Xliff)).Select(pc => (string)pc.Attribute("id")!).SequenceEqual(["v1", "tv1", "tv2", "v2"]),
            "The added variants do not have target ids.");

        // Returned untouched, the prefill is skipped; returned translated without a state, it is a copy for review.
        Require(Outcome(Import(xml, czech), "items").Code == "XLIFF21-UNTRANSLATED", "The untouched prefill was imported.");
        string copy = new XDocument(XDocument.Parse(xml)).ToString(SaveOptions.DisableFormatting).Replace(" state=\"initial\"", "", StringComparison.Ordinal);
        Require(Outcome(Import(copy, czech), "items").State == TranslationReviewState.NeedsReview, "A stateless copy of the prefill was imported as translated.");

        string translated = SetTarget(xml, "items", "<ph id=\"h\"/><pc id=\"v1\">Jedna položka</pc><pc id=\"tv1\" equivStart=\"few {{\" equivEnd=\"}}\"><ph id=\"1\"/> položky</pc>" +
            "<pc id=\"tv2\" equivStart=\"many {{\" equivEnd=\"}}\"><ph id=\"1\"/> položky</pc><pc id=\"v2\"><ph id=\"1\"/> položek</pc>");
        TranslationXliffImportResult imported = Import(translated, czech);
        Require(Message(imported, "items") == Header + "\none {{Jedna položka}}\nfew {{{$n} položky}}\nmany {{{$n} položky}}\n* {{{$n} položek}}", "The Czech plural did not import: " + Message(imported, "items"));
        Require(imported.Report.Losses.All(loss => loss.Code != "XLIFF21-PLURAL-CATEGORIES"), "A complete Czech plural was reported.");

        // A locale that selects fewer categories drops the unreachable ones: Japanese has only other.
        string japanese = Export(Compile(Items, locale: "ja"));
        Require(Unit(japanese, "items").Descendants(XName.Get("target", Xliff)).Single().Elements(XName.Get("pc", Xliff)).Select(pc => (string)pc.Attribute("equivStart")!).SequenceEqual(["* {{"]),
            "The Japanese prefill kept 'one'.");
        // German selects the same categories: no prefill, as for any untranslated unit.
        Require(Unit(Export(Compile(Items)), "items").Descendants(XName.Get("target", Xliff)).Any() == false, "A German unit was prefilled.");
    }

    [Test, DisplayName("plural categories on import follow the locale's pluralCategories policy")]
    public void PluralCategoriesFollowThePolicy()
    {
        const string onlyOne = "<ph id=\"h\"/><pc id=\"v1\">Jedna položka</pc><pc id=\"v2\"><ph id=\"1\"/> položek</pc>";
        Rmf2ProjectCompilation warning = Compile(Items, locale: "cs");
        TranslationXliffImportResult applied = Import(SetTarget(Export(warning), "items", onlyOne), warning);
        Require(Outcome(applied, "items").Outcome == TranslationXliffUnitOutcome.Applied, "A Czech plural without few and many was not applied under the warning policy.");
        TranslationInterchangeLoss notice = applied.Report.Losses.Single(loss => loss.Code == "XLIFF21-PLURAL-CATEGORIES");
        Require(notice.Location == "/items" && notice.Message.Contains("RTR0095", StringComparison.Ordinal) && notice.Message.Contains("few, many", StringComparison.Ordinal), "The notice is wrong: " + notice.Message);

        Rmf2ProjectCompilation error = Compile(Items, locale: "cs", validation: ",\"pluralCategories\":\"error\"");
        TranslationXliffImportResult refused = Import(SetTarget(Export(error), "items", onlyOne), error);
        Require(Outcome(refused, "items") is { Outcome: TranslationXliffUnitOutcome.Refused, Code: "XLIFF21-PLURAL-CATEGORIES" }, "The error policy did not refuse: " + Outcome(refused, "items").Code);

        // A key the locale never selects.
        Rmf2ProjectCompilation german = Compile(Items);
        TranslationXliffImportResult few = Import(SetTarget(Export(german), "items", GermanItems.Replace("<pc id=\"v2\">", "<pc id=\"tv1\" equivStart=\"few {{\">Wenige</pc><pc id=\"v2\">", StringComparison.Ordinal)), german);
        Require(few.Report.Losses.Any(loss => loss.Code == "XLIFF21-PLURAL-CATEGORIES" && loss.Message.Contains("RTR0094", StringComparison.Ordinal)), "An unreachable key was not reported.");
    }

    [Test, DisplayName("translators may add exact variants but not change declarations, selectors or placeholders, or leave a variant empty")]
    public void ProtectsTheStructure()
    {
        Rmf2ProjectCompilation compilation = Compile(Items, "items =\n  .input {$n :integer}\n  .match $n\n  one {{Ein Element}}\n  * {{{$n} Elemente}}");
        string xml = Export(compilation);

        TranslationXliffImportResult added = Import(SetTarget(xml, "items", "<ph id=\"h\"/><pc id=\"tv1\" equivStart=\"0 {{\" equivEnd=\"}}\">Keine Elemente</pc><pc id=\"v1\">Ein Element</pc><pc id=\"v2\"><ph id=\"1\"/> Elemente</pc>"), compilation);
        Require(Message(added, "items") == Header + "\n0 {{Keine Elemente}}\none {{Ein Element}}\n* {{{$n} Elemente}}", "An added exact variant did not import: " + Message(added, "items") + " " + Outcome(added, "items").Message);

        TranslationXliffImportResult header = Import(SetTarget(xml, "items", "<ph id=\"th\" equiv=\".input {$n :number}&#10;.match $n\"/><pc id=\"v1\">Ein Element</pc><pc id=\"v2\"><ph id=\"1\"/> Elemente</pc>"), compilation);
        Require(Outcome(header, "items").Code == "XLIFF21-SELECTOR-CHANGED", "A changed header was accepted: " + Outcome(header, "items").Code);

        TranslationXliffImportResult placeholder = Import(SetTarget(xml, "items", "<ph id=\"h\"/><pc id=\"v1\">Ein Element</pc><pc id=\"v2\"><ph id=\"t1\" equiv=\"{$n :number}\"/> Elemente</pc>"), compilation);
        Require(Outcome(placeholder, "items").Code == "XLIFF21-PLACEHOLDER-CHANGED", "A changed placeholder in a variant was accepted: " + Outcome(placeholder, "items").Code);

        TranslationXliffImportResult empty = Import(SetTarget(xml, "items", "<ph id=\"h\"/><pc id=\"v1\"></pc><pc id=\"v2\"><ph id=\"1\"/> Elemente</pc>"), compilation);
        Require(Outcome(empty, "items").Code == "XLIFF21-EMPTY-VARIANT", "An empty variant was accepted: " + Outcome(empty, "items").Code);

        TranslationXliffImportResult outside = Import(SetTarget(xml, "items", "<ph id=\"h\"/>Text<pc id=\"v1\">Ein Element</pc><pc id=\"v2\"><ph id=\"1\"/> Elemente</pc>"), compilation);
        Require(Outcome(outside, "items").Code == "XLIFF21-INLINE", "Text outside the variants was accepted: " + Outcome(outside, "items").Code);

        TranslationXliffImportResult noCatchAll = Import(SetTarget(xml, "items", "<ph id=\"h\"/><pc id=\"v1\">Ein Element</pc>"), compilation);
        Require(Outcome(noCatchAll, "items").Code == "XLIFF21-TARGET-INVALID", "A plural without * was accepted: " + Outcome(noCatchAll, "items").Code);

        TranslationXliffImportResult input = Import(SetTarget(xml, "items", "<ph id=\"h\"/><pc id=\"v1\">Ein <ph id=\"t1\" equiv=\"{$name}\"/></pc><pc id=\"v2\"><ph id=\"1\"/> Elemente</pc>"), compilation);
        Require(Outcome(input, "items").Code == "XLIFF21-PLACEHOLDER", "A new input in a variant was accepted: " + Outcome(input, "items").Code);

        // A plain translation of a plural source, as an existing Japanese one, is exported and imported as plain text.
        Rmf2ProjectCompilation japanese = Compile(Items, "items = {$n} 個", locale: "ja");
        string ja = Export(japanese);
        Require(Unit(ja, "items").Descendants(XName.Get("target", Xliff)).Single().Elements(XName.Get("pc", Xliff)).Any() == false, "A plain translation was exported with variants.");
        Require(Message(Import(SetTarget(ja, "items", "<ph id=\"1\"/> 件"), japanese), "items") == "{$n} 件", "A plain translation of a plural did not import.");
    }

    [Test, DisplayName("@maxLength on a selector message restricts every variant")]
    public void MaxLengthPerVariant()
    {
        string xml = Export(Compile("@maxLength 20\n" + Items));
        ValidateSchema(Encoding.UTF8.GetBytes(xml));
        XElement unit = Unit(xml, "items");
        XNamespace slr = "urn:oasis:names:tc:xliff:sizerestriction:2.0";
        Require(unit.Attribute(slr + "sizeRestriction") is null, "The selector unit is restricted as a whole.");
        Require(unit.Descendants(XName.Get("pc", Xliff)).All(pc => (string?)pc.Attribute(slr + "sizeRestriction") == "20"), "A variant has no size restriction.");
    }

    [Test, DisplayName("imported selector messages are written into .rmf2 and compile")]
    public void SourceWriterWritesSelectors()
    {
        TranslationSource project = new("translations/runic.json", Encoding.UTF8.GetBytes(ProjectJson(",{\"tag\":\"de\",\"fallback\":\"en\"}")));
        TranslationSource[] sources =
        [
            new("translations/en.rmf2", Encoding.UTF8.GetBytes(Items + "\nother = Other\n")),
            new("translations/de.rmf2", Encoding.UTF8.GetBytes("other = Andere\n")),
        ];
        Rmf2ProjectCompilation compilation = TranslationCompiler.CompileProject(project, sources);
        TranslationXliffImportResult imported = Import(SetTarget(Export(compilation), "items", GermanItems), compilation);
        string message = Message(imported, "items")!;
        IReadOnlyList<TranslationSourceEdit> edits = TranslationInterchangeSourceWriter.Plan(Environment.CurrentDirectory, project, sources, compilation, "de", [new("items", message)]);
        IReadOnlyList<TranslationSource> applied = TranslationInterchangeSourceWriter.Apply(sources, edits);
        Rmf2ProjectCompilation after = TranslationCompiler.CompileProject(project, applied);
        Require(after.Success, "The written selector does not compile: " + string.Join("; ", after.Diagnostics.Select(d => d.Message)) + "\n" +
            Encoding.UTF8.GetString(applied.Single(source => source.Path.EndsWith("de.rmf2", StringComparison.Ordinal)).GetUtf8Bytes()));
        // A second export of the written source shows the translation as the target, unchanged.
        Require(Message(Import(Export(after), after), "items") == message, "The written selector did not round-trip.");
    }

    private static void ValidateSchema(byte[] document)
    {
        string schemas = Path.Combine(RepositoryRoot(), "tests", "dotnet", "Runic.Translations.Tooling.Tests", "Fixtures", "xliff-2.1");
        var set = new XmlSchemaSet { XmlResolver = new XmlUrlResolver() };
        set.Add("http://www.w3.org/XML/1998/namespace", Path.Combine(schemas, "informativeCopiesOf3rdPartySchemas", "w3c", "xml.xsd"));
        using (XmlReader core = XmlReader.Create(Path.Combine(schemas, "xliff_core_2.0.xsd"), new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore }))
            set.Add(Xliff, core);
        set.Add("urn:oasis:names:tc:xliff:sizerestriction:2.0", Path.Combine(schemas, "size_restriction.xsd"));
        set.Compile();
        var errors = new List<string>();
        var settings = new XmlReaderSettings { ValidationType = ValidationType.Schema, Schemas = set, ValidationFlags = XmlSchemaValidationFlags.ReportValidationWarnings };
        settings.ValidationEventHandler += (_, args) => { if (args.Severity == XmlSeverityType.Error) errors.Add(args.Message); };
        using var stream = new MemoryStream(document);
        using (XmlReader reader = XmlReader.Create(stream, settings)) while (reader.Read()) { }
        Require(errors.Count == 0, "The XLIFF document does not validate: " + string.Join("; ", errors));
    }

    private static string RepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null && !File.Exists(Path.Combine(directory, "Runic.Translations.slnx"))) directory = Path.GetDirectoryName(directory);
        return directory ?? throw new InvalidOperationException("The repository root was not found.");
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
