using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Runic.Translations.Compiler;

namespace Runic.Translations.Tooling;

/// <summary>
/// Reads XLIFF 2.0/2.1 documents of the Runic profile, versions 2 and 3, and what translation management systems
/// write from them. Document-level problems (not XLIFF, another catalog, another execution contract) throw
/// <see cref="TranslationInterchangeException"/>; everything else is decided per unit.
/// </summary>
internal static class TranslationXliffReader
{
    private static readonly XNamespace Xliff = TranslationInterchange.XliffNamespace;

    internal static TranslationXliffImportResult Import(ReadOnlyMemory<byte> source, TranslationInterchangeProjection? live, TranslationXliffImportOptions? options)
    {
        if (source.Length == 0) throw new TranslationInterchangeException("XLIFF21-EMPTY", "An XLIFF document is required.");
        if (source.Length > TranslationInterchange.MaximumXliffBytes) throw new TranslationInterchangeException("XLIFF21-LIMIT", "The XLIFF document exceeds the byte limit.");
        XDocument document;
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = true,
                IgnoreProcessingInstructions = true, MaxCharactersInDocument = TranslationInterchange.MaximumXliffBytes,
            };
            byte[] bytes = source.ToArray();
            // The element depth is checked in a streaming pass before the tree is built, so that no later step
            // sees a document nested deeper than the limit.
            using (var stream = new MemoryStream(bytes, writable: false))
            using (XmlReader scan = XmlReader.Create(stream, settings))
                while (scan.Read())
                    if (scan.NodeType == XmlNodeType.Element && scan.Depth >= TranslationInterchange.MaximumXliffDepth)
                        throw new TranslationInterchangeException("XLIFF21-LIMIT", "The XLIFF document nests elements deeper than " + TranslationInterchange.MaximumXliffDepth.ToString(CultureInfo.InvariantCulture) + " levels.");
            using (var stream = new MemoryStream(bytes, writable: false))
            using (XmlReader reader = XmlReader.Create(stream, settings))
                document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException exception) { throw new TranslationInterchangeException("XLIFF21-MALFORMED", "The XLIFF document is malformed.", exception); }

        XElement root = document.Root ?? throw new TranslationInterchangeException("XLIFF21-ROOT", "The XLIFF document has no root element.");
        if (root.Name != Xliff + "xliff") throw new TranslationInterchangeException("XLIFF21-ROOT", "The document is not XLIFF 2 (urn:oasis:names:tc:xliff:document:2.0).");
        if ((string?)root.Attribute("version") is not ("2.0" or "2.1")) throw new TranslationInterchangeException("XLIFF21-VERSION", "Only XLIFF versions 2.0 and 2.1 are supported.");
        string sourceLocale = (string?)root.Attribute("srcLang") ?? throw new TranslationInterchangeException("XLIFF21-ROOT", "The XLIFF document has no srcLang.");
        string targetLocale = (string?)root.Attribute("trgLang") ?? throw new TranslationInterchangeException("XLIFF21-ROOT", "The XLIFF document has no trgLang; it is not a translation.");
        XElement[] files = root.Elements(Xliff + "file").ToArray();
        if (files.Length != 1) throw new TranslationInterchangeException("XLIFF21-FILE", "The XLIFF document must contain exactly one file.");
        string catalog = (string?)files[0].Attribute("id") ?? throw new TranslationInterchangeException("XLIFF21-FILE", "The XLIFF file has no id; Runic exports the catalog identifier there.");

        if (live is not null)
        {
            if (!string.Equals(catalog, live.CatalogId, StringComparison.Ordinal))
                throw new TranslationInterchangeException("XLIFF21-CATALOG-MISMATCH", "The XLIFF file is for catalog '" + catalog + "', not '" + live.CatalogId + "'.");
            if (!string.Equals(sourceLocale, live.SourceLocale, StringComparison.Ordinal))
                throw new TranslationInterchangeException("XLIFF21-SOURCE-LOCALE", "The XLIFF source locale '" + sourceLocale + "' is not the base locale '" + live.SourceLocale + "'.");
            if (string.Equals(targetLocale, live.SourceLocale, StringComparison.Ordinal))
                throw new TranslationInterchangeException("XLIFF21-TARGET-LOCALE", "The XLIFF target locale is the base locale '" + targetLocale + "'.");
            if (!live.Locales.Any(locale => string.Equals(locale.Tag, targetLocale, StringComparison.Ordinal)))
                throw new TranslationInterchangeException("XLIFF21-TARGET-LOCALE", "The catalog does not declare the XLIFF target locale '" + targetLocale + "'.");
        }

        var parsed = new List<ParsedUnit>();
        CollectUnits(files[0], parsed);
        if (parsed.Count == 0) throw new TranslationInterchangeException("XLIFF21-FILE", "The XLIFF file contains no units.");
        if (parsed.Count > TranslationInterchange.MaximumUnits) throw new TranslationInterchangeException("XLIFF21-LIMIT", "The XLIFF document has too many units.");

        int profileVersion = ProfileVersion(parsed);
        var losses = new List<TranslationInterchangeLoss>();
        var context = new ImportContext(catalog, sourceLocale, targetLocale, profileVersion, live, options ?? new TranslationXliffImportOptions(), losses);
        if (profileVersion == 2) context.FormerTextProfileFingerprint = FormerTextProfileFingerprint(catalog, sourceLocale, parsed);

        var decisions = new List<UnitDecision>(parsed.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (ParsedUnit unit in parsed)
        {
            if (!seen.Add(unit.Id)) { decisions.Add(UnitDecision.Refuse(unit.Id, "XLIFF21-DUPLICATE-UNIT", "The unit identifier '" + unit.Id + "' appears more than once.")); continue; }
            decisions.Add(Decide(unit, context));
        }
        ValidateContracts(decisions, context);

        var messages = new List<TranslationMf2Document>();
        var reviews = new List<TranslationInterchangeReviewEntry>();
        var units = new List<TranslationXliffUnitResult>(decisions.Count);
        foreach (UnitDecision decision in decisions)
        {
            units.Add(new TranslationXliffUnitResult(decision.Key, decision.Outcome, decision.Code, decision.Message,
                decision.Outcome == TranslationXliffUnitOutcome.Applied ? decision.State : null,
                decision.SourceFingerprint, decision.SourceChanged));
            if (decision.Outcome != TranslationXliffUnitOutcome.Applied) continue;
            messages.Add(new TranslationMf2Document(decision.Key, Encoding.UTF8.GetBytes(decision.Pattern! + "\n")));
            if (decision.ExplicitState)
                reviews.Add(new TranslationInterchangeReviewEntry(decision.Key, targetLocale, decision.State, decision.Note, decision.SourceFingerprint));
        }
        var review = new TranslationInterchangeReview(catalog, reviews);
        TranslationInterchange.ValidateReview(review, catalog);
        return new TranslationXliffImportResult(catalog, sourceLocale, targetLocale,
            messages.OrderBy(static message => message.MessageId, StringComparer.Ordinal).ToArray(),
            review, new TranslationInterchangeReport(losses), units, profileVersion);
    }

    // ---- Parsing ----

    private enum InlineKind { Text, Placeholder, VariantStart, VariantEnd }

    // A text run, a <ph>, or the start or end of a <pc> (a selector variant: Equiv is its equivStart).
    private sealed record Inline(string? Text, string? Id, string? Equiv, string? DataRef, string? CopyOf)
    {
        internal InlineKind Kind { get; init; } = Text is not null ? InlineKind.Text : InlineKind.Placeholder;
        internal string? EquivEnd { get; init; }
        internal bool IsWhitespace => Kind == InlineKind.Text && string.IsNullOrWhiteSpace(Text);
    }

    // The resolved content of a source or target: plain pieces, or a selector view.
    private sealed record Content(IReadOnlyList<TranslationInterchangePiece>? Pieces, TranslationInterchangeSelector? Selector)
    {
        internal string Compose() => Selector is { } selector ? TranslationInterchangePattern.Compose(selector) : TranslationInterchangePattern.Compose(Pieces!);
        internal IEnumerable<IReadOnlyList<TranslationInterchangePiece>> Patterns => Selector?.Variants.Select(static variant => variant.Pieces) ?? [Pieces!];
        internal IReadOnlyList<TranslationInterchangePiece> AllPieces => Patterns.SelectMany(static pattern => pattern).ToArray();
        internal bool IsEmpty => Patterns.All(TranslationInterchangePattern.IsEmpty);
    }

    private sealed class ParsedUnit
    {
        internal required string Id { get; init; }
        internal bool NotTranslatable { get; set; }
        internal UnitNote? Metadata { get; set; }
        internal ReviewNote? Review { get; set; }
        internal List<string> ForeignNotes { get; } = new();
        internal List<Inline> Source { get; } = new();
        internal List<Inline>? Target { get; set; } = new();
        internal List<string?> States { get; } = new();
        internal List<string> SubStates { get; } = new();
        internal Dictionary<string, string> OriginalData { get; } = new(StringComparer.Ordinal);
        internal bool HasMarkers { get; set; }
        internal (string Code, string Message)? Problem { get; set; }
    }

    private sealed record UnitNote(string ExecutionProfile, int MessageGrammarVersion, string InterchangeProfile, int InterchangeProfileVersion, bool Structured, string? Description, string? Since, string? Deprecated, string[] Tags, IReadOnlyList<TranslationInterchangePlaceholder> Placeholders, string? SourceFingerprint);
    private sealed record ReviewNote(TranslationReviewState State, string? Note, string? SourceFingerprint);

    // Units in document order, through any depth of <group>; iterative, so the nesting never reaches the call stack.
    private static void CollectUnits(XElement container, List<ParsedUnit> units)
    {
        var pending = new Stack<IEnumerator<XElement>>();
        pending.Push(container.Elements().GetEnumerator());
        while (pending.Count != 0)
        {
            IEnumerator<XElement> current = pending.Peek();
            if (!current.MoveNext()) { pending.Pop().Dispose(); continue; }
            XElement element = current.Current;
            if (element.Name == Xliff + "group") { pending.Push(element.Elements().GetEnumerator()); continue; }
            if (element.Name != Xliff + "unit") continue; // notes, skeleton, module metadata
            if (units.Count > TranslationInterchange.MaximumUnits) return;
            units.Add(ParseUnit(element));
        }
    }

    private static ParsedUnit ParseUnit(XElement element)
    {
        var unit = new ParsedUnit { Id = (string?)element.Attribute("id") ?? string.Empty };
        unit.NotTranslatable = (string?)element.Attribute("translate") == "no";
        try
        {
            if (element.Element(Xliff + "notes") is { } notes) ParseNotes(notes, unit);
            if (element.Element(Xliff + "originalData") is { } original)
                foreach (XElement data in original.Elements(Xliff + "data"))
                    if ((string?)data.Attribute("id") is { } id) unit.OriginalData[id] = data.Value;
            int segments = 0;
            foreach (XElement part in element.Elements())
            {
                bool segment = part.Name == Xliff + "segment";
                if (!segment && part.Name != Xliff + "ignorable") continue;
                XElement source = part.Element(Xliff + "source") ?? throw new UnitProblem("XLIFF21-SOURCE", "A segment has no source.");
                XElement? target = part.Element(Xliff + "target");
                ReadInline(source, unit.Source, unit);
                if (segment)
                {
                    segments++;
                    unit.States.Add((string?)part.Attribute("state"));
                    if ((string?)part.Attribute("subState") is { } subState) unit.SubStates.Add(subState);
                    if (target is null) unit.Target = null;
                    else if (unit.Target is not null) ReadInline(target, unit.Target, unit);
                }
                else if (unit.Target is not null) ReadInline(target ?? source, unit.Target, unit);
            }
            if (segments == 0) throw new UnitProblem("XLIFF21-SEGMENT", "The unit has no segment.");
            int length = unit.Source.Sum(static inline => inline.Text?.Length ?? 0) + (unit.Target?.Sum(static inline => inline.Text?.Length ?? 0) ?? 0);
            if (length > TranslationInterchange.MaximumTextLength) throw new UnitProblem("XLIFF21-TEXT", "The unit text exceeds the length limit.");
        }
        catch (UnitProblem problem) { unit.Problem = (problem.Code, problem.Message); }
        catch (TranslationInterchangeException exception) when (exception.Code is "XLIFF21-METADATA" or "XLIFF21-REVIEW" or "XLIFF21-TAGS" or "XLIFF21-PLACEHOLDERS")
        { unit.Problem = (exception.Code, exception.Message); }
        return unit;
    }

    private static void ParseNotes(XElement notes, ParsedUnit unit)
    {
        foreach (XElement note in notes.Elements(Xliff + "note"))
        {
            string? category = (string?)note.Attribute("category");
            string value = note.Value;
            switch (category)
            {
                case "runic:unit":
                    if (unit.Metadata is not null) throw new UnitProblem("XLIFF21-METADATA", "The unit has two Runic unit notes.");
                    unit.Metadata = ParseUnitNote(value.Trim());
                    break;
                case "runic:review":
                    if (unit.Review is not null) throw new UnitProblem("XLIFF21-REVIEW", "The unit has two Runic review notes.");
                    unit.Review = ParseReviewNote(value.Trim());
                    break;
                case "description":
                    break; // Runic's readable description; the sources stay authoritative.
                default:
                    if (!string.IsNullOrWhiteSpace(value)) unit.ForeignNotes.Add(category ?? string.Empty);
                    break;
            }
        }
    }

    // Inline content in document order; <mrk> is unwrapped and <pc> is read as a variant start and end, iteratively,
    // so their nesting never reaches the call stack.
    private static void ReadInline(XElement container, List<Inline> output, ParsedUnit unit)
    {
        var pending = new Stack<(IEnumerator<XNode> Nodes, string? VariantId)>();
        pending.Push((container.Nodes().GetEnumerator(), null));
        while (pending.Count != 0)
        {
            (IEnumerator<XNode> current, string? variant) = pending.Peek();
            if (!current.MoveNext())
            {
                pending.Pop().Nodes.Dispose();
                if (variant is not null) output.Add(new Inline(null, variant, null, null, null) { Kind = InlineKind.VariantEnd });
                continue;
            }
            XNode node = current.Current;
            switch (node)
            {
                case XText text:
                    output.Add(new Inline(text.Value, null, null, null, null));
                    break;
                case XElement element when element.Name == Xliff + "ph":
                    output.Add(new Inline(null, (string?)element.Attribute("id"), (string?)element.Attribute("equiv"), (string?)element.Attribute("dataRef"), (string?)element.Attribute("copyOf")));
                    break;
                case XElement element when element.Name == Xliff + "pc":
                    // A selector variant: its start and end codes are the keys and the pattern quotes.
                    string id = (string?)element.Attribute("id") ?? string.Empty;
                    output.Add(new Inline(null, id, (string?)element.Attribute("equivStart"), (string?)element.Attribute("dataRefStart"), (string?)element.Attribute("copyOf"))
                    { Kind = InlineKind.VariantStart, EquivEnd = (string?)element.Attribute("equivEnd") });
                    pending.Push((element.Nodes().GetEnumerator(), id));
                    break;
                case XElement element when element.Name == Xliff + "cp":
                    string hex = (string?)element.Attribute("hex") ?? throw new UnitProblem("XLIFF21-INLINE", "A <cp> element has no hex value.");
                    if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code) || code is < 0 or > 0x10FFFF || code is >= 0xD800 and <= 0xDFFF)
                        throw new UnitProblem("XLIFF21-INLINE", "A <cp> element has an invalid code point.");
                    output.Add(new Inline(char.ConvertFromUtf32(code), null, null, null, null));
                    break;
                case XElement element when element.Name == Xliff + "mrk":
                    // An annotation (a term, a comment, a translate="no" span): its content is kept, the annotation is not.
                    unit.HasMarkers = true;
                    pending.Push((element.Nodes().GetEnumerator(), null));
                    break;
                case XElement element:
                    throw new UnitProblem("XLIFF21-INLINE", "The inline element <" + element.Name.LocalName + "> is not supported; only <ph>, <pc> (selector variants), <mrk> and <cp> are.");
            }
        }
    }

    private static int ProfileVersion(List<ParsedUnit> units)
    {
        var contracts = units.Where(static unit => unit.Metadata is not null).Select(static unit => unit.Metadata!)
            .Select(static note => (note.ExecutionProfile, note.MessageGrammarVersion, note.InterchangeProfile, note.InterchangeProfileVersion))
            .Distinct().ToArray();
        if (contracts.Length == 0) return TranslationInterchange.XliffProfileVersion;
        if (contracts.Length != 1) throw new TranslationInterchangeException("XLIFF21-CONTRACT", "Every imported XLIFF unit must declare the same execution and interchange contract.");
        var contract = contracts[0];
        if (contract.ExecutionProfile != Rmf2ProjectCompilation.CurrentExecutionProfile || contract.MessageGrammarVersion != Rmf2ProjectCompilation.CurrentMessageGrammarVersion)
            throw new TranslationInterchangeException("XLIFF21-CONTRACT", "The XLIFF document does not target the supported RMF2 execution contract.");
        if (contract.InterchangeProfile != TranslationInterchange.XliffProfile || contract.InterchangeProfileVersion is not (2 or TranslationInterchange.XliffProfileVersion))
            throw new TranslationInterchangeException("XLIFF21-PROFILE", "The XLIFF document does not use a supported closed interchange profile.");
        return contract.InterchangeProfileVersion;
    }

    // ---- Decisions ----

    private sealed class ImportContext(string catalog, string sourceLocale, string targetLocale, int profileVersion, TranslationInterchangeProjection? live, TranslationXliffImportOptions options, List<TranslationInterchangeLoss> losses)
    {
        internal string Catalog { get; } = catalog;
        internal string SourceLocale { get; } = sourceLocale;
        internal string TargetLocale { get; } = targetLocale;
        internal int ProfileVersion { get; } = profileVersion;
        internal TranslationInterchangeProjection? Live { get; } = live;
        internal TranslationXliffImportOptions Options { get; } = options;
        internal List<TranslationInterchangeLoss> Losses { get; } = losses;
        internal string? FormerTextProfileFingerprint { get; set; }
        internal Dictionary<string, TranslationInterchangeSourceUnit> LiveUnits { get; } =
            live?.CanonicalUnits.ToDictionary(static unit => unit.Key, StringComparer.Ordinal) ?? new(StringComparer.Ordinal);
        internal TranslationPolicy PluralCategories { get; } =
            live?.Locales.FirstOrDefault(locale => string.Equals(locale.Tag, targetLocale, StringComparison.Ordinal))?.PluralCategories ?? TranslationPolicy.Warning;
        internal Dictionary<string, TranslationInterchangeTargetUnit> LiveTargets { get; } =
            live?.Locales.FirstOrDefault(locale => string.Equals(locale.Tag, targetLocale, StringComparison.Ordinal))?.DirectResources
                .ToDictionary(static unit => unit.Key, StringComparer.Ordinal) ?? new(StringComparer.Ordinal);
    }

    private sealed class UnitDecision
    {
        internal required string Key { get; init; }
        internal TranslationXliffUnitOutcome Outcome { get; set; }
        internal string? Code { get; set; }
        internal string? Message { get; set; }
        internal string? Pattern { get; init; }
        internal string? SourcePattern { get; init; }
        internal IReadOnlyList<TranslationInterchangePlaceholder> Contract { get; init; } = [];
        internal TranslationReviewState State { get; init; }
        internal bool ExplicitState { get; init; }
        internal string? Note { get; init; }
        internal string? SourceFingerprint { get; init; }
        internal bool SourceChanged { get; init; }

        internal static UnitDecision Skip(string key, string code, string message) => new() { Key = key, Outcome = TranslationXliffUnitOutcome.Skipped, Code = code, Message = message };
        internal static UnitDecision Refuse(string key, string code, string message) => new() { Key = key, Outcome = TranslationXliffUnitOutcome.Refused, Code = code, Message = message };
    }

    private static UnitDecision Decide(ParsedUnit unit, ImportContext context)
    {
        string key = unit.Id;
        string location = "/" + key;
        if (!TranslationInterchange.IsKey(key)) return UnitDecision.Refuse(key, "XLIFF21-UNIT-ID", "The unit identifier '" + key + "' is not a Runic message key.");
        if (unit.Problem is { } problem) return UnitDecision.Refuse(key, problem.Code, problem.Message);
        foreach (string category in unit.ForeignNotes.Distinct(StringComparer.Ordinal))
            context.Losses.Add(new("XLIFF21-NOTE-IGNORED", location, category.Length == 0 ? "A note without a category was ignored; the sources do not store translator notes." : "A note of category '" + category + "' was ignored; the sources do not store translator notes.", false));
        if (unit.HasMarkers) context.Losses.Add(new("XLIFF21-MARKER-IGNORED", location, "<mrk> annotations were removed; their text was kept.", false));
        foreach (string subState in unit.SubStates.Distinct(StringComparer.Ordinal))
            if (subState != TranslationInterchange.NeedsReviewSubState)
                context.Losses.Add(new("XLIFF21-SUBSTATE-IGNORED", location, "The segment subState '" + subState + "' is not a Runic state and was ignored.", false));

        TranslationInterchangeSourceUnit? live = null;
        if (context.Live is not null && !context.LiveUnits.TryGetValue(key, out live))
            return UnitDecision.Refuse(key, "XLIFF21-UNKNOWN-KEY", "The catalog has no message '" + key + "'.");
        if (unit.Metadata?.Structured == true || live?.Structured == true)
            return UnitDecision.Skip(key, "XLIFF21-STRUCTURED-SKIPPED", "Messages with markup are not exchanged through XLIFF; translate them in the .rmf2 sources or the editor.");
        if (unit.NotTranslatable)
            return UnitDecision.Skip(key, "XLIFF21-NOT-TRANSLATABLE", "The unit is marked translate=\"no\".");

        // State: the least advanced segment wins; a segment without state is XLIFF's "initial".
        bool stateDeclared = unit.States.Any(static state => state is not null);
        string segmentState = "translated";
        if (stateDeclared)
        {
            string[] order = ["initial", "translated", "reviewed", "final"];
            int lowest = int.MaxValue;
            foreach (string? declared in unit.States)
            {
                int index = Array.IndexOf(order, declared ?? "initial");
                if (index < 0) return UnitDecision.Refuse(key, "XLIFF21-STATE", "The segment state '" + declared + "' is not an XLIFF 2 state.");
                lowest = Math.Min(lowest, index);
            }
            segmentState = order[lowest];
        }
        if (unit.Target is null) return UnitDecision.Skip(key, "XLIFF21-UNTRANSLATED", "The unit has no target.");
        if (segmentState == "initial") return UnitDecision.Skip(key, "XLIFF21-UNTRANSLATED", "The unit is in the initial state.");

        // Content of source and target; profile 2 carried MF2 syntax as plain text.
        Content sourceContent, targetContent;
        if (context.ProfileVersion == 2)
        {
            if (unit.Source.Any(static inline => inline.Text is null) || unit.Target.Any(static inline => inline.Text is null))
                return UnitDecision.Refuse(key, "XLIFF21-INLINE", "Profile 2 documents carry plain text only.");
            IReadOnlyList<TranslationInterchangePiece>? decomposedSource = TranslationInterchangePattern.Decompose(string.Concat(unit.Source.Select(static inline => inline.Text)));
            IReadOnlyList<TranslationInterchangePiece>? decomposedTarget = TranslationInterchangePattern.Decompose(string.Concat(unit.Target.Select(static inline => inline.Text)));
            if (decomposedSource is null || decomposedSource.Any(static piece => !piece.IsText) || decomposedTarget is null || decomposedTarget.Any(static piece => !piece.IsText))
                return UnitDecision.Refuse(key, "XLIFF21-STRUCTURED-IMPORT", "A structured Runic message cannot be imported from the profile 2 text profile.");
            sourceContent = new(decomposedSource, null); targetContent = new(decomposedTarget, null);
        }
        else
        {
            Dictionary<string, string> sourceIds = new(StringComparer.Ordinal);
            if (!TryResolveContent(unit.Source, unit, null, sourceIds, live, out Content? resolvedSource, out (string Code, string Message)? sourceProblem))
                return UnitDecision.Refuse(key, sourceProblem!.Value.Code, sourceProblem.Value.Message);
            if (!TryResolveContent(unit.Target, unit, sourceIds, null, null, out Content? resolvedTarget, out (string Code, string Message)? targetProblem))
                return UnitDecision.Refuse(key, targetProblem!.Value.Code, targetProblem.Value.Message);
            sourceContent = resolvedSource!; targetContent = resolvedTarget!;
            // An export from before selector units carried the message as plain MF2 text; reading that text as a
            // translation would escape its syntax, so the unit is skipped until the catalog is exported again.
            if (live?.Selector is not null && sourceContent.Selector is null)
                return UnitDecision.Skip(key, "XLIFF21-STRUCTURED-SKIPPED", "The unit holds a selector message as plain text, as exports before selector units wrote it; export the catalog again to translate it through XLIFF.");
        }
        string sourcePattern = sourceContent.Compose(), targetPattern = targetContent.Compose();

        bool translatedState = stateDeclared || unit.Review is not null;
        if (targetContent.IsEmpty && !translatedState)
            return UnitDecision.Skip(key, "XLIFF21-EMPTY-TARGET", "The target is empty and no state marks it as translated; the base text stays the fallback.");
        // Some tools fill an untranslated target with a copy of the source and write no state. Without a state that
        // says otherwise, such a copy is imported for review instead of as a finished translation. A selector target
        // whose every variant repeats a source variant (as the prefilled variants of an export do) is such a copy.
        bool copiedSource = !translatedState && CopiesSource(targetContent, sourceContent);

        TranslationInterchangeTargetUnit? liveTarget = context.LiveTargets.GetValueOrDefault(key);
        if (targetContent.Selector is { } targetSelector)
        {
            // Translators edit the variant texts; declarations and selectors stay those of the source or the translation.
            var headers = new HashSet<string>(StringComparer.Ordinal);
            foreach (TranslationInterchangeSelector? known in new[] { sourceContent.Selector, live?.Selector, liveTarget?.Selector })
                if (known is not null) headers.Add(TranslationInterchangePattern.CanonicalHeader(known.Header));
            if (!headers.Contains(TranslationInterchangePattern.CanonicalHeader(targetSelector.Header)))
                return UnitDecision.Refuse(key, "XLIFF21-SELECTOR-CHANGED", "The target changes the message's declarations or .match statement; a translation through XLIFF may change only the variant texts and keys.");
            var emptyAllowed = new HashSet<string?>(new[] { sourceContent.Selector, live?.Selector, liveTarget?.Selector }.OfType<TranslationInterchangeSelector>()
                .SelectMany(static selector => selector.Variants).Where(static variant => TranslationInterchangePattern.IsEmpty(variant.Pieces))
                .Select(static variant => TranslationInterchangePattern.CanonicalKeys(variant.Keys)));
            if (targetSelector.Variants.FirstOrDefault(variant => TranslationInterchangePattern.IsEmpty(variant.Pieces) && !emptyAllowed.Contains(TranslationInterchangePattern.CanonicalKeys(variant.Keys))) is { } empty)
                return UnitDecision.Refuse(key, "XLIFF21-EMPTY-VARIANT", "The target variant '" + (empty.Keys ?? "{{}}") + "' is empty; translate every variant, or remove the ones the language does not use.");
        }

        // The contract the target is checked against: the live caller contract, else the one the file records,
        // else the placeholders of the file's source. Locals that a selector header declares are not inputs.
        IReadOnlyList<string> sourceVariables = TranslationInterchangePattern.Variables(sourceContent.AllPieces).Except(Locals(sourcePattern), StringComparer.Ordinal).ToArray();
        IReadOnlyList<TranslationInterchangePlaceholder> contract = live?.Metadata.Placeholders ?? unit.Metadata?.Placeholders ?? [];
        var allowed = new HashSet<string>(live is null && unit.Metadata is null ? sourceVariables : contract.Select(static placeholder => placeholder.Name), StringComparer.Ordinal);
        if (live is null && unit.Metadata is not null && sourceVariables.Any(variable => !allowed.Contains(variable)))
            return UnitDecision.Refuse(key, "XLIFF21-PLACEHOLDER", "The source placeholders do not match the contract that the unit records.");
        string[] targetLocals = Locals(targetPattern);
        foreach (string variable in TranslationInterchangePattern.Variables(targetContent.AllPieces))
            if (!allowed.Contains(variable) && !targetLocals.Contains(variable, StringComparer.Ordinal))
                return UnitDecision.Refuse(key, "XLIFF21-PLACEHOLDER", "The target uses the placeholder $" + variable + ", which the message's caller contract does not have.");
        if (context.ProfileVersion >= 3)
        {
            Dictionary<string, string> known = KnownExpressions(key, live, sourceContent, context);
            if (targetContent.Selector is { } selector)
            {
                var variants = new List<TranslationInterchangeVariant>(selector.Variants.Count);
                foreach (TranslationInterchangeVariant variant in selector.Variants)
                {
                    if (!TryKeepExpressions(variant.Pieces, known, out IReadOnlyList<TranslationInterchangePiece> kept, out string? changedProblem))
                        return UnitDecision.Refuse(key, "XLIFF21-PLACEHOLDER-CHANGED", changedProblem!);
                    variants.Add(variant with { Pieces = kept });
                }
                targetContent = targetContent with { Selector = selector with { Variants = variants } };
            }
            else
            {
                if (!TryKeepExpressions(targetContent.Pieces!, known, out IReadOnlyList<TranslationInterchangePiece> kept, out string? changedProblem))
                    return UnitDecision.Refuse(key, "XLIFF21-PLACEHOLDER-CHANGED", changedProblem!);
                targetContent = targetContent with { Pieces = kept };
            }
            targetPattern = targetContent.Compose();
        }
        IReadOnlyList<TranslationInterchangePiece>? sourcePieces = sourceContent.Pieces;

        // The source the translator saw, and whether the live source changed since.
        IReadOnlyList<TranslationInterchangePlaceholder> recordedContract = unit.Metadata?.Placeholders ?? contract;
        string fileFingerprint = context.ProfileVersion >= 3 && unit.Metadata?.SourceFingerprint is { } recordedFingerprint
            ? recordedFingerprint
            : TranslationInterchangeFingerprint.UnitSource(key, sourcePattern, sourcePieces, recordedContract);
        bool sourceChanged = live is not null && !string.Equals(fileFingerprint, live.SourceFingerprint, StringComparison.Ordinal);
        if (sourceChanged && context.Options.SourceChangePolicy == TranslationSourceChangePolicy.Refuse)
            return UnitDecision.Refuse(key, "XLIFF21-SOURCE-CHANGED", "The source of '" + key + "' changed after export.");

        // Review state: a Runic review note that agrees with the segment state keeps its note and fingerprint;
        // otherwise the segment state is authoritative.
        TranslationReviewState state = MapSegmentState(segmentState, unit.SubStates.Contains(TranslationInterchange.NeedsReviewSubState), context.ProfileVersion);
        string? note = unit.Review?.Note;
        if (copiedSource)
        {
            context.Losses.Add(new("XLIFF21-TARGET-COPIES-SOURCE", location, "The target equals the source and no state marks it as translated; it is imported as needs-review.", false));
            state = TranslationReviewState.NeedsReview;
        }
        else if (!stateDeclared && unit.Review is null)
            context.Losses.Add(new("XLIFF21-STATE-MISSING", location, "The segment has a target but no state; it is imported as translated.", false));
        if (unit.Review is { } review)
        {
            if (stateDeclared && review.State != state)
                context.Losses.Add(new("XLIFF21-STATE-CHANGED", location, "The segment state '" + segmentState + "' replaces the Runic review state '" + TranslationReviewStates.ToWire(review.State) + "'.", false));
            else
            {
                state = review.State;
                if (state == TranslationReviewState.Approved)
                {
                    string? expected = context.ProfileVersion == 2 ? context.FormerTextProfileFingerprint : fileFingerprint;
                    if (!string.Equals(review.SourceFingerprint, expected, StringComparison.Ordinal))
                    {
                        context.Losses.Add(new("REVIEW-STALE", location, "The approval does not match the source it was exported with; it is imported as needs-review.", false));
                        state = TranslationReviewState.NeedsReview;
                    }
                }
            }
        }
        if (state == TranslationReviewState.Draft) return UnitDecision.Skip(key, "XLIFF21-UNTRANSLATED", "The unit's review state is draft.");
        if (sourceChanged)
        {
            context.Losses.Add(new("XLIFF21-SOURCE-CHANGED", location, "The source changed after export; the translation is imported as needs-review.", false));
            state = TranslationReviewState.NeedsReview;
        }
        return new UnitDecision
        {
            Key = key,
            Outcome = TranslationXliffUnitOutcome.Applied,
            Pattern = targetPattern,
            SourcePattern = live?.Text ?? sourcePattern,
            Contract = contract,
            State = state,
            // "translated" is what an existing translation is by default; only other states become review data.
            ExplicitState = unit.Review is not null || sourceChanged || copiedSource || (stateDeclared && state != TranslationReviewState.Translated),
            Note = note,
            SourceFingerprint = fileFingerprint,
            SourceChanged = sourceChanged,
        };
    }

    private static TranslationReviewState MapSegmentState(string state, bool needsReview, int profileVersion) => state switch
    {
        "initial" => TranslationReviewState.Draft,
        "translated" => needsReview ? TranslationReviewState.NeedsReview : TranslationReviewState.Translated,
        // Profile 2 wrote needs-review as "reviewed"; in XLIFF 2 it means the review is done.
        "reviewed" => profileVersion == 2 ? TranslationReviewState.NeedsReview : TranslationReviewState.Approved,
        _ => TranslationReviewState.Approved,
    };

    // The local variables a composed message declares; they may appear in its patterns besides the caller inputs.
    private static string[] Locals(string message)
    {
        Mf2SyntaxDocument syntax = Mf2SyntaxReader.Read(new TranslationSource("interchange/message.mf2", Encoding.UTF8.GetBytes(message)));
        return syntax.Declarations.Where(static declaration => declaration.Kind == "local").Select(static declaration => declaration.Name).ToArray();
    }

    // Whether a target only repeats the source: the same plain text, or a selector whose header is the source's and
    // whose every variant text is one of the source's variant texts.
    private static bool CopiesSource(Content target, Content source)
    {
        static string Canonical(IReadOnlyList<TranslationInterchangePiece> pieces) =>
            TranslationInterchangePattern.Compose(TranslationInterchangePattern.Canonical(pieces));
        if (target.Selector is null) return source.Selector is null && Canonical(target.Pieces!) == Canonical(source.Pieces!);
        if (source.Selector is null || TranslationInterchangePattern.CanonicalHeader(source.Selector.Header) != TranslationInterchangePattern.CanonicalHeader(target.Selector.Header)) return false;
        var texts = source.Patterns.Select(Canonical).ToHashSet(StringComparer.Ordinal);
        return target.Patterns.All(pattern => texts.Contains(Canonical(pattern)));
    }

    // The ids an export gives to the live source's codes: "h" and "v1".. for a selector, "1".. for its placeholders.
    private static Dictionary<string, string>? LiveIds(TranslationInterchangeSourceUnit? live)
    {
        if (live is null || (live.Pieces is null && live.Selector is null)) return null;
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        int number = 0, variants = 0;
        if (live.Selector is { } selector)
        {
            ids["h"] = selector.Header;
            foreach (TranslationInterchangeVariant variant in selector.Variants)
            {
                ids["v" + (++variants).ToString(CultureInfo.InvariantCulture)] = variant.Keys ?? string.Empty;
                foreach (TranslationInterchangePiece piece in variant.Pieces)
                    if (!piece.IsText) ids[(++number).ToString(CultureInfo.InvariantCulture)] = piece.Expression!;
            }
            return ids;
        }
        foreach (TranslationInterchangePiece piece in live.Pieces!)
            if (!piece.IsText) ids[(++number).ToString(CultureInfo.InvariantCulture)] = piece.Expression!;
        return ids;
    }

    // The content a <ph> or <pc> start stands for: the source code with the same id (or copyOf), else the element's
    // equiv, else its originalData, else (for a source without equiv) the live export's numbering.
    private static string? ResolveCode(Inline inline, ParsedUnit unit, Dictionary<string, string>? sourceIds, Dictionary<string, string>? liveIds)
    {
        if (sourceIds is not null && inline.Id is not null && sourceIds.TryGetValue(inline.Id, out string? byId)) return byId;
        if (sourceIds is not null && inline.CopyOf is not null && sourceIds.TryGetValue(inline.CopyOf, out string? copied)) return copied;
        if (inline.Equiv is not null)
        {
            if (inline.Kind != InlineKind.VariantStart) return inline.Equiv;
            // A variant's start code is its keys and "{{"; its end code, when present, is "}}".
            string start = inline.Equiv.TrimEnd();
            return start.EndsWith("{{", StringComparison.Ordinal) && inline.EquivEnd?.Trim() is null or "}}" ? start[..^2].Trim() : null;
        }
        if (inline.DataRef is not null && unit.OriginalData.TryGetValue(inline.DataRef, out string? data)) return data;
        if (sourceIds is null && inline.Id is not null && liveIds is not null && liveIds.TryGetValue(inline.Id, out string? numbered)) return numbered;
        return null;
    }

    // Resolves inline content to plain pieces, or to a selector view when it has variants (<pc>): one header <ph>
    // with the declarations and .match, then the variants, with only whitespace between them.
    private static bool TryResolveContent(List<Inline> inlines, ParsedUnit unit, Dictionary<string, string>? sourceIds, Dictionary<string, string>? record,
        TranslationInterchangeSourceUnit? live, out Content? content, out (string Code, string Message)? problem)
    {
        content = null;
        problem = null;
        Dictionary<string, string>? liveIds = LiveIds(live);
        if (!inlines.Any(static inline => inline.Kind is InlineKind.VariantStart or InlineKind.VariantEnd))
        {
            if (!TryResolvePieces(inlines, 0, inlines.Count, unit, sourceIds, record, liveIds, out IReadOnlyList<TranslationInterchangePiece>? pieces, out string? message))
            {
                problem = ("XLIFF21-PLACEHOLDER", message!);
                return false;
            }
            content = new(pieces, null);
            return true;
        }
        int index = 0;
        while (index < inlines.Count && inlines[index].IsWhitespace) index++;
        string? header = index < inlines.Count && inlines[index].Kind == InlineKind.Placeholder ? ResolveCode(inlines[index], unit, sourceIds, liveIds) : null;
        if (header is null || !header.TrimStart().StartsWith('.'))
        {
            problem = ("XLIFF21-INLINE", "A unit with variants (<pc>) starts with one <ph> that holds the message's declarations and .match statement.");
            return false;
        }
        if (record is not null && inlines[index].Id is { } headerId) record[headerId] = header;
        index++;
        var variants = new List<TranslationInterchangeVariant>();
        while (true)
        {
            while (index < inlines.Count && inlines[index].IsWhitespace) index++;
            if (index == inlines.Count) break;
            Inline start = inlines[index];
            if (start.Kind != InlineKind.VariantStart)
            {
                problem = ("XLIFF21-INLINE", "Text and placeholders of a selector unit belong inside its variants (<pc>).");
                return false;
            }
            string? keys = ResolveCode(start, unit, sourceIds, liveIds);
            int end = index + 1;
            while (end < inlines.Count && inlines[end].Kind is InlineKind.Text or InlineKind.Placeholder) end++;
            if (keys is null || end == inlines.Count || inlines[end].Kind != InlineKind.VariantEnd)
            {
                problem = ("XLIFF21-INLINE", keys is null
                    ? "The variant <pc id=\"" + start.Id + "\"> has no keys: it matches no source variant and has no equivStart such as \"one {{\"."
                    : "The variant <pc id=\"" + start.Id + "\"> contains another variant.");
                return false;
            }
            if (record is not null && start.Id is not null) record[start.Id] = keys;
            if (!TryResolvePieces(inlines, index + 1, end, unit, sourceIds, record, liveIds, out IReadOnlyList<TranslationInterchangePiece>? pieces, out string? message))
            {
                problem = ("XLIFF21-PLACEHOLDER", message!);
                return false;
            }
            variants.Add(new(keys.Length == 0 ? null : keys, pieces!));
            index = end + 1;
        }
        if (variants.Count == 0)
        {
            problem = ("XLIFF21-INLINE", "A selector unit has no variants.");
            return false;
        }
        content = new(null, new TranslationInterchangeSelector(header.TrimEnd(), header.TrimEnd().Split('\n')[^1].Trim(), variants, null));
        return true;
    }

    // Resolves text and <ph> elements to pieces of MF2 text and placeholder expressions.
    private static bool TryResolvePieces(List<Inline> inlines, int from, int to, ParsedUnit unit, Dictionary<string, string>? sourceIds, Dictionary<string, string>? record,
        Dictionary<string, string>? liveIds, out IReadOnlyList<TranslationInterchangePiece>? pieces, out string? problem)
    {
        var result = new List<TranslationInterchangePiece>();
        for (int index = from; index < to; index++)
        {
            Inline inline = inlines[index];
            if (inline.Text is not null)
            {
                if (result.Count != 0 && result[^1].IsText) result[^1] = TranslationInterchangePiece.FromText(result[^1].Text + inline.Text);
                else result.Add(TranslationInterchangePiece.FromText(inline.Text));
                continue;
            }
            string? expression = ResolveCode(inline, unit, sourceIds, liveIds);
            if (expression is null || !TranslationInterchangePattern.IsPlaceholderExpression(expression))
            {
                pieces = null;
                problem = "The placeholder <ph id=\"" + inline.Id + "\"> " + (expression is null ? "has no equiv and matches no source placeholder." : "is not one MF2 placeholder expression.");
                return false;
            }
            if (record is not null && inline.Id is not null) record[inline.Id] = expression;
            IReadOnlyList<TranslationInterchangePiece> parsed = TranslationInterchangePattern.Decompose(expression)!;
            result.Add(parsed[0]);
        }
        pieces = result;
        problem = null;
        return true;
    }

    // The placeholder expressions a target may use, by canonical spelling: those of the live source and the live
    // translation, or without a live catalog those of the file's source. The value is the spelling that is written.
    private static Dictionary<string, string> KnownExpressions(string key, TranslationInterchangeSourceUnit? live, Content fileSource, ImportContext context)
    {
        var known = new Dictionary<string, string>(StringComparer.Ordinal);
        void Add(IEnumerable<TranslationInterchangePiece>? pieces)
        {
            foreach (TranslationInterchangePiece piece in pieces ?? [])
                if (!piece.IsText) known.TryAdd(TranslationInterchangePattern.CanonicalExpression(piece.Expression!), piece.Expression!);
        }
        if (context.Live is null) { Add(fileSource.AllPieces); return known; }
        Add(live?.Pieces);
        Add(live?.Selector?.Pieces);
        if (context.LiveTargets.TryGetValue(key, out TranslationInterchangeTargetUnit? target))
            Add(target.Pieces ?? target.Selector?.Pieces ?? TranslationInterchangePattern.Decompose(target.Text));
        return known;
    }

    // A target <ph> carries its expression in equiv, which a tool or a person can edit. A translation may move and
    // repeat the message's placeholders but not change one (another function or option) or replace it with a literal.
    private static bool TryKeepExpressions(IReadOnlyList<TranslationInterchangePiece> target, Dictionary<string, string> known,
        out IReadOnlyList<TranslationInterchangePiece> pieces, out string? problem)
    {
        var result = new List<TranslationInterchangePiece>(target.Count);
        foreach (TranslationInterchangePiece piece in target)
        {
            if (piece.IsText) { result.Add(piece); continue; }
            if (!known.TryGetValue(TranslationInterchangePattern.CanonicalExpression(piece.Expression!), out string? spelling))
            {
                pieces = target;
                problem = "The target placeholder " + piece.Expression + " is not one of the message's placeholders" +
                    (known.Count == 0 ? string.Empty : " (" + string.Join(", ", known.Values) + ")") +
                    "; a translation cannot change a placeholder's function or options or replace it with text.";
                return false;
            }
            result.Add(piece with { Expression = spelling });
        }
        pieces = result;
        problem = null;
        return true;
    }

    // Compiles every applied unit's source and target as one synthetic project: the compiler checks that the target
    // is valid MF2 and introduces no input outside the source's caller contract. A unit with errors is refused.
    // Every round refuses at least one unit, so the loop reaches a fixed point: either the remaining units compile
    // together, or none remain. A failure that names no unit is narrowed by compiling the units one by one.
    private static void ValidateContracts(List<UnitDecision> decisions, ImportContext context)
    {
        while (true)
        {
            UnitDecision[] applied = decisions.Where(static decision => decision.Outcome == TranslationXliffUnitOutcome.Applied).ToArray();
            if (applied.Length == 0) return;
            Rmf2ProjectCompilation compilation = CompileUnits(applied, context);
            if (compilation.Success)
            {
                // Plural categories of the target locale that its policy reports as warnings are notices.
                string prefix = "interchange/" + context.TargetLocale + "/";
                foreach (TranslationDiagnostic diagnostic in compilation.Diagnostics)
                    if (diagnostic.Id is "RTR0094" or "RTR0095" && diagnostic.Location.Path.StartsWith(prefix, StringComparison.Ordinal))
                        context.Losses.Add(new("XLIFF21-PLURAL-CATEGORIES", "/" + Path.GetFileNameWithoutExtension(diagnostic.Location.Path), "[" + diagnostic.Id + "] " + diagnostic.Message, false));
                return;
            }
            Dictionary<string, (string Code, string Message)> failing = FailingUnits(compilation, applied, context);
            if (failing.Count == 0)
                foreach (UnitDecision decision in applied)
                {
                    Rmf2ProjectCompilation alone = CompileUnits([decision], context);
                    if (!alone.Success)
                        failing[decision.Key] = FailingUnits(alone, [decision], context).TryGetValue(decision.Key, out var reason)
                            ? reason : ("XLIFF21-TARGET-INVALID", "The target does not satisfy the message's caller contract.");
                }
            // Units that compile alone but not together: refuse the rest deterministically rather than apply them.
            bool isolated = failing.Count != 0;
            foreach (UnitDecision decision in applied)
            {
                bool named = failing.TryGetValue(decision.Key, out var reason);
                if (!named && isolated) continue;
                decision.Outcome = TranslationXliffUnitOutcome.Refused;
                decision.Code = named ? reason.Code : "XLIFF21-TARGET-INVALID";
                decision.Message = named ? reason.Message : "The imported units do not compile together; none of them was applied.";
            }
        }
    }

    private static Rmf2ProjectCompilation CompileUnits(IReadOnlyList<UnitDecision> applied, ImportContext context)
    {
        string project = "{\"schemaVersion\":1,\"catalog\":" + Quote(context.Catalog) + ",\"code\":{\"namespace\":\"Runic.Interchange\",\"className\":\"Text\"},\"baseLocale\":" + Quote(context.SourceLocale) +
            ",\"locales\":[{\"tag\":" + Quote(context.SourceLocale) + "},{\"tag\":" + Quote(context.TargetLocale) + ",\"fallback\":" + Quote(context.SourceLocale) + "}]" +
            // The target locale's plural policy decides whether category problems refuse a unit; the other quality
            // checks are left to validate.
            ",\"validation\":{\"pluralCategories\":" + Quote(PolicyName(context.PluralCategories)) + ",\"inputParity\":\"allow\",\"formattedBooleans\":\"allow\",\"maxLength\":\"allow\",\"emptyValues\":\"allow\"}}";
        var messages = new List<TranslationSource>(applied.Count * 2);
        foreach (UnitDecision decision in applied)
        {
            messages.Add(new TranslationSource("interchange/" + context.SourceLocale + "/" + decision.Key + ".mf2", Encoding.UTF8.GetBytes(decision.SourcePattern!)));
            messages.Add(new TranslationSource("interchange/" + context.TargetLocale + "/" + decision.Key + ".mf2", Encoding.UTF8.GetBytes(decision.Pattern!)));
        }
        return TranslationCompiler.CompileRmf2ProjectV5(new TranslationSource("interchange/runic.json", Encoding.UTF8.GetBytes(project)), messages);
    }

    private static string PolicyName(TranslationPolicy policy) => policy switch { TranslationPolicy.Allow => "allow", TranslationPolicy.Error => "error", _ => "warning" };

    // The first error of every unit that a diagnostic names; plural categories that the policy makes errors have their own code.
    private static Dictionary<string, (string Code, string Message)> FailingUnits(Rmf2ProjectCompilation compilation, IReadOnlyList<UnitDecision> applied, ImportContext context)
    {
        var keys = new HashSet<string>(applied.Select(static decision => decision.Key), StringComparer.Ordinal);
        var failing = new Dictionary<string, (string Code, string Message)>(StringComparer.Ordinal);
        foreach (TranslationDiagnostic diagnostic in compilation.Diagnostics.Where(static diagnostic => diagnostic.Severity == TranslationDiagnosticSeverity.Error))
        {
            string path = diagnostic.Location.Path;
            string? key = path.StartsWith("interchange/", StringComparison.Ordinal) && path.EndsWith(".mf2", StringComparison.Ordinal)
                ? Path.GetFileNameWithoutExtension(path) : null;
            if (key is null || !keys.Contains(key)) continue;
            bool plural = diagnostic.Id is "RTR0094" or "RTR0095" && path.StartsWith("interchange/" + context.TargetLocale + "/", StringComparison.Ordinal);
            failing.TryAdd(key, (plural ? "XLIFF21-PLURAL-CATEGORIES" : "XLIFF21-TARGET-INVALID", "[" + diagnostic.Id + "] " + diagnostic.Message));
        }
        return failing;
    }

    // ---- Notes ----

    private static UnitNote ParseUnitNote(string encoded)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(Convert.FromBase64String(encoded));
            JsonElement root = document.RootElement;
            TranslationInterchange.RequireObject(root, "XLIFF21-METADATA");
            try { TranslationInterchange.RequireAllowed(root, "executionProfile", "messageGrammarVersion", "interchangeProfile", "interchangeProfileVersion", "structured", "description", "since", "deprecated", "tags", "placeholders", "sourceFingerprint"); }
            catch (TranslationInterchangeException exception) { throw new TranslationInterchangeException("XLIFF21-METADATA", "Runic unit metadata is invalid.", exception); }
            return new UnitNote(
                TranslationInterchange.RequiredString(root, "executionProfile", "XLIFF21-METADATA"),
                TranslationInterchange.Required(root, "messageGrammarVersion", JsonValueKind.Number, "XLIFF21-METADATA").GetInt32(),
                TranslationInterchange.RequiredString(root, "interchangeProfile", "XLIFF21-METADATA"),
                TranslationInterchange.Required(root, "interchangeProfileVersion", JsonValueKind.Number, "XLIFF21-METADATA").GetInt32(),
                TranslationInterchange.RequiredBoolean(root, "structured", "XLIFF21-METADATA"),
                TranslationInterchange.OptionalString(root, "description", "XLIFF21-METADATA"),
                TranslationInterchange.OptionalString(root, "since", "XLIFF21-METADATA"),
                TranslationInterchange.OptionalString(root, "deprecated", "XLIFF21-METADATA"),
                ParseStrings(TranslationInterchange.RequiredString(root, "tags", "XLIFF21-METADATA")),
                ParsePlaceholders(TranslationInterchange.RequiredString(root, "placeholders", "XLIFF21-METADATA")),
                TranslationInterchange.OptionalString(root, "sourceFingerprint", "XLIFF21-METADATA"));
        }
        catch (Exception exception) when (exception is FormatException or JsonException or InvalidOperationException)
        { throw new TranslationInterchangeException("XLIFF21-METADATA", "Runic unit metadata is invalid.", exception); }
    }

    private static ReviewNote ParseReviewNote(string encoded)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(Convert.FromBase64String(encoded));
            JsonElement root = document.RootElement;
            TranslationInterchange.RequireObject(root, "XLIFF21-REVIEW");
            try { TranslationInterchange.RequireAllowed(root, "state", "note", "sourceFingerprint"); }
            catch (TranslationInterchangeException exception) { throw new TranslationInterchangeException("XLIFF21-REVIEW", "Runic review metadata is invalid.", exception); }
            return new ReviewNote(
                TranslationInterchange.ReviewState(TranslationInterchange.RequiredString(root, "state", "XLIFF21-REVIEW"), "XLIFF21-REVIEW"),
                TranslationInterchange.OptionalString(root, "note", "XLIFF21-REVIEW"),
                TranslationInterchange.OptionalString(root, "sourceFingerprint", "XLIFF21-REVIEW"));
        }
        catch (Exception exception) when (exception is FormatException or JsonException or InvalidOperationException)
        { throw new TranslationInterchangeException("XLIFF21-REVIEW", "Runic review metadata is invalid.", exception); }
    }

    private static string[] ParseStrings(string encoded)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(Convert.FromBase64String(encoded));
            if (document.RootElement.ValueKind != JsonValueKind.Array) throw new TranslationInterchangeException("XLIFF21-TAGS", "Runic tag metadata is invalid.");
            string[] result = document.RootElement.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString()! : throw new TranslationInterchangeException("XLIFF21-TAGS", "Runic tag metadata is invalid.")).ToArray();
            if (result.Length > 64 || result.Distinct(StringComparer.Ordinal).Count() != result.Length) throw new TranslationInterchangeException("XLIFF21-TAGS", "Runic tag metadata is invalid.");
            return result;
        }
        catch (Exception exception) when (exception is FormatException or JsonException) { throw new TranslationInterchangeException("XLIFF21-TAGS", "Runic tag metadata is invalid.", exception); }
    }

    private static List<TranslationInterchangePlaceholder> ParsePlaceholders(string encoded)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(Convert.FromBase64String(encoded));
            if (document.RootElement.ValueKind != JsonValueKind.Array) throw new TranslationInterchangeException("XLIFF21-PLACEHOLDERS", "Runic placeholder metadata is invalid.");
            var result = new List<TranslationInterchangePlaceholder>();
            foreach (JsonElement item in document.RootElement.EnumerateArray())
            {
                TranslationInterchange.RequireObject(item, "XLIFF21-PLACEHOLDERS");
                result.Add(new TranslationInterchangePlaceholder(
                    TranslationInterchange.RequiredString(item, "name", "XLIFF21-PLACEHOLDERS"),
                    TranslationInterchange.RequiredString(item, "type", "XLIFF21-PLACEHOLDERS"),
                    TranslationInterchange.RequiredString(item, "format", "XLIFF21-PLACEHOLDERS")));
            }
            if (result.Count > 32 || result.Select(static item => item.Name).Distinct(StringComparer.Ordinal).Count() != result.Count) throw new TranslationInterchangeException("XLIFF21-PLACEHOLDERS", "Runic placeholder metadata is invalid.");
            return result;
        }
        catch (Exception exception) when (exception is FormatException or JsonException) { throw new TranslationInterchangeException("XLIFF21-PLACEHOLDERS", "Runic placeholder metadata is invalid.", exception); }
    }

    // Profile 2 stamped approvals with one fingerprint over every unit of the document.
    private static string? FormerTextProfileFingerprint(string catalog, string sourceLocale, List<ParsedUnit> units)
    {
        if (units.Any(static unit => unit.Metadata is null || unit.Problem is not null || unit.Source.Any(static inline => inline.Text is null))) return null;
        UnitNote contract = units[0].Metadata!;
        return TranslationInterchangeFingerprint.TextProfile(catalog, sourceLocale, contract.ExecutionProfile, contract.MessageGrammarVersion,
            contract.InterchangeProfile, contract.InterchangeProfileVersion,
            units.GroupBy(static unit => unit.Id, StringComparer.Ordinal).Select(static group => group.First()).Select(static unit => new TranslationInterchangeSourceUnit(
                unit.Id,
                string.Concat(unit.Source.Select(static inline => inline.Text)),
                unit.Metadata!.Structured,
                new TranslationInterchangeUnitMetadata(unit.Metadata.Description, unit.Metadata.Since, unit.Metadata.Deprecated, unit.Metadata.Tags, unit.Metadata.Placeholders))));
    }

    private static string Quote(string value) { using var stream = new MemoryStream(); using (var writer = new Utf8JsonWriter(stream)) writer.WriteStringValue(value); return Encoding.UTF8.GetString(stream.ToArray()); }

    private sealed class UnitProblem(string code, string message) : Exception(message)
    {
        internal string Code { get; } = code;
    }
}
