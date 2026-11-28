using Runic.Translations.Authoring;
using Runic.Translations.Compiler;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using Runic.Translations.Internal;
using Runic.Translations.Tooling;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI.Primitives.Concurrency;
using Runic.Application.Testing;
using Runic.Application.Views;
using Runic.Application.Views.ReactiveUI;
using Runic.CommandLine;
using Runic.Platform;
using Runic.Navigation;
using System.Xml.Linq;

namespace Runic.Translations.Editor;

internal static partial class EditorSmokeTest
{
    private static async Task AuthoringJourneyAsync(string project)
    {
        await TranslationProjectWriter.CreateAsync(TranslationProjectScaffolder.Render(
            new TranslationProjectCreationRequest(project, "authoring-smoke", "en", "Smoke.Translations", "SmokeText",
                [new TranslationProjectLocale("de", "en")]))).ConfigureAwait(false);
        const string source = "# Cart count context 🦊\n@tag checkout\n@example {\"count\":1}\n@example {\"count\":2}\ncart =\n  .input {$count :integer}\n  .match $count\n  one {{One item}}\n  * {{Many items}}\n";
        const string target = "# Warenkorbkontext\n@tag checkout\n@example {\"count\":1}\n@example {\"count\":2}\ncart =\n  .input {$count :integer}\n  .match $count\n  one {{Ein Artikel}}\n  * {{Viele Artikel}}\n";
        await File.WriteAllTextAsync(Path.Combine(project, "en.rmf2"), source).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(project, "de.rmf2"), target).ConfigureAwait(false);
        using var session = new EditorSession(project);
        var snapshot = await session.LoadAsync().ConfigureAwait(false);
        Require(snapshot.Success, "Authoring fixture did not compile.");
        var document = snapshot.Documents.Single(document => document.Path == "de.rmf2");
        var entry = document.Entries!.Single(entry => entry.Key == "cart");
        Require(entry.Authoring?.Supported == true && entry.Authoring.Variants.Count == 2, "Existing plural wasn't projected.");
        Require(entry.Context?.Comments.Count == 1 && entry.Context.Comments[0] == "Warenkorbkontext" && entry.Context.Tags.Contains("checkout") && entry.Context.Examples.Count == 2,
            "Context/tags/examples were dropped.");
        Require(entry.Semantic?.Text.Count == 2 && entry.Semantic.Text[0] == "Ein Artikel" && entry.Semantic.Text[1] == "Viele Artikel" && entry.Semantic.Placeholders.Count == 1 && entry.Semantic.Placeholders[0] == "count:int64", "Semantic QA projection wasn't typed branch text.");
        var draft = await session.ApplyAuthoringOperationAsync(document.Path, document.Content, entry.Key,
            entry.Authoring!.Revision, new EditorMessageOperation("set-pattern", "0", "Genau ein Artikel")).ConfigureAwait(false);
        Require(draft.Success, "Branch edit didn't compile.");
        Require(draft.Content.StartsWith("# Warenkorbkontext\n@tag checkout\n@example {\"count\":1}\n@example {\"count\":2}\n", StringComparison.Ordinal), "Untouched metadata wasn't retained byte-for-byte.");
        var changed = draft.Entries.Single();
        Require(changed.Context?.Examples.Count == 2, "Draft examples were dropped.");
        var one = await session.PreviewMessageAsync(document.Path, draft.Content, "de", "cart", JsonSerializer.Serialize(changed.Context!.Examples[0], EditorSmokeJsonContext.Default.IReadOnlyDictionaryStringString)).ConfigureAwait(false);
        var many = await session.PreviewMessageAsync(document.Path, draft.Content, "de", "cart", JsonSerializer.Serialize(changed.Context.Examples[1], EditorSmokeJsonContext.Default.IReadOnlyDictionaryStringString)).ConfigureAwait(false);
        Require(one.RenderedJson?.Contains("Genau ein Artikel", StringComparison.Ordinal) == true && many.RenderedJson?.Contains("Viele Artikel", StringComparison.Ordinal) == true,
            "Selected canonical examples didn't choose the expected plural branches.");
        var stale = await session.ApplyAuthoringOperationAsync(document.Path, draft.Content, "cart", entry.Authoring.Revision,
            new EditorMessageOperation("set-pattern", "0", "Stale")).ConfigureAwait(false);
        Require(!stale.Success && stale.Content == draft.Content && stale.Diagnostics.Any(diagnostic => diagnostic.Id == "EDITOR-AUTHORING"), "Stale authoring edit was accepted.");
        // Refusals report a localized editor notice, as the document transform does: the compiler's reason as its detail.
        EditorDiagnostic staleRefusal = stale.Diagnostics.Single(diagnostic => diagnostic.Id == "EDITOR-AUTHORING");
        Require(staleRefusal.Message.Length == 0 && staleRefusal.Notice is { Code: "ui_backend_external_error", Detail.Length: > 0 },
            "A stale authoring edit wasn't refused with a localized notice: " + staleRefusal.Notice?.Code + " " + staleRefusal.Message);
        var vanished = await session.ApplyAuthoringOperationAsync(document.Path, draft.Content, "gone", entry.Authoring.Revision,
            new EditorMessageOperation("set-pattern", "0", "Weg")).ConfigureAwait(false);
        Require(!vanished.Success && vanished.Diagnostics.Single().Notice?.Code == "ui_backend_messageMissing" && vanished.Diagnostics.Single().Message.Length == 0,
            "An operation on a missing message wasn't refused with ui_backend_messageMissing: " + vanished.Diagnostics.Single().Notice?.Code);
        Require((await session.SaveAsync(document.Path, draft.Content, document.Revision).ConfigureAwait(false)).Ok, "Authoring source wasn't saved.");
        var reloaded = (await session.LoadAsync().ConfigureAwait(false)).Documents.Single(document => document.Path == "de.rmf2");
        Require(reloaded.Entries!.Single().Authoring?.Variants[0].Pattern == "Genau ein Artikel", "Branch edit didn't survive save/reload.");
        // Input renames update attached caller examples and @param metadata.
        var rename = await session.ApplyAuthoringOperationAsync("en.rmf2", source, "cart", snapshot.Documents.Single(document => document.Path == "en.rmf2").Entries!.Single().Authoring!.Revision,
            new EditorMessageOperation("rename-input", Name: "count", NewName: "amount")).ConfigureAwait(false);
        Require(rename.Content.Contains("\"amount\":1", StringComparison.Ordinal) && !rename.Content.Contains("\"count\":1", StringComparison.Ordinal), "Input rename didn't update example contracts.");
        const string broken = "# 🦊 incomplete\nempty =\n";
        var validation = await session.ValidateAsync("de.rmf2", broken).ConfigureAwait(false);
        var diagnostic = validation.Diagnostics.Single(diagnostic => diagnostic.QuickFixes?.Count == 1);
        Require(diagnostic.Span?.StartUtf16 == broken.IndexOf("empty", StringComparison.Ordinal), "Diagnostic selection wasn't physical UTF16.");
        var fix = diagnostic.QuickFixes!.Single();
        var repaired = await session.ApplyDiagnosticFixAsync("de.rmf2", broken, fix).ConfigureAwait(false);
        Require(repaired.Content == "# 🦊 incomplete\nempty = {{}}\n" && !repaired.Diagnostics.Any(diagnostic => diagnostic.Id == "EDITOR-FIX"), "Safe repair didn't preserve unrelated content.");
        var staleFix = await session.ApplyDiagnosticFixAsync("de.rmf2", broken + "other = New\n", fix).ConfigureAwait(false);
        Require(staleFix.Diagnostics.Any(diagnostic => diagnostic.Id == "EDITOR-FIX"), "Stale repair wasn't refused as an operation result.");
        EditorDiagnostic fixRefusal = staleFix.Diagnostics.Single(diagnostic => diagnostic.Id == "EDITOR-FIX");
        Require(fixRefusal.Message.Length == 0 && fixRefusal.Notice is { Code: "ui_backend_external_error", Detail.Length: > 0 },
            "A stale repair wasn't refused with a localized notice: " + fixRefusal.Notice?.Code + " " + fixRefusal.Message);
    }

    // Issue #225, P2/P5/P21: a missing translation starts as a standalone draft shaped like the base message with the
    // target locale's plural variants; composer operations apply to the draft text; the frontend inserts it once it has
    // text. The preview names the previewed message's own error, and entries carry their effective @maxLength.
    private static async Task MessageDraftJourneyAsync(string project)
    {
        Directory.CreateDirectory(project);
        await File.WriteAllTextAsync(Path.Combine(project, "runic.json"),
            "{\"schemaVersion\":1,\"catalog\":\"drafts\",\"code\":{\"namespace\":\"Smoke.Translations\",\"className\":\"SmokeText\"},\"baseLocale\":\"en\",\"locales\":[\"en\",\"de\",\"pl\"]}\n").ConfigureAwait(false);
        const string english = "@maxLength 20\nsave = Save\nfiles =\n  .input {$count :integer}\n  .match $count\n  one {{One file}}\n  * {{{$count} files}}\n" +
            "greeting =\n  .input {$name :string}\n  {{Hello {$name}}}\n";
        const string german = "@maxLength 10\nsave = Speichern\ngreeting =\n  .input {$name :string}\n  {{Hallo {$name}}}\n";
        const string polish = "save = Zapisz\n";
        await File.WriteAllTextAsync(Path.Combine(project, "en.rmf2"), english).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(project, "de.rmf2"), german).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(project, "pl.rmf2"), polish).ConfigureAwait(false);
        using var session = new EditorSession(project);
        var loaded = await session.LoadAsync().ConfigureAwait(false);
        string Describe(IEnumerable<EditorDiagnostic> diagnostics) => string.Join("; ", diagnostics.Select(item => item.Id + " " + item.Message));

        // P21: the effective @maxLength, the smaller of the base's and the translation's own.
        int? MaxLength(string path, string key) => loaded.Documents.Single(document => document.Path == path).Entries!.Single(entry => entry.Key == key).MaxLength;
        Require(MaxLength("en.rmf2", "save") == 20 && MaxLength("de.rmf2", "save") == 10 && MaxLength("pl.rmf2", "save") == 20 && MaxLength("en.rmf2", "files") is null,
            "Entries didn't carry their effective @maxLength.");

        // A missing plain message: the empty simple pattern, supported by the composer, empty, with the base's limit.
        var plain = await session.CreateMessageDraftAsync("save", "fr").ConfigureAwait(false);
        Require(!plain.Success && plain.Diagnostics.Single().Id == "EDITOR-DRAFT", "A draft for an undeclared locale wasn't refused.");
        var unknown = await session.CreateMessageDraftAsync("nothing", "pl").ConfigureAwait(false);
        Require(!unknown.Success && unknown.Diagnostics.Single().Notice?.Code == "ui_backend_preview_key_missing", "A draft of an unknown key wasn't refused.");
        await File.WriteAllTextAsync(Path.Combine(project, "pl.rmf2"), "").ConfigureAwait(false);
        plain = await session.CreateMessageDraftAsync("save", "pl").ConfigureAwait(false);
        Require(plain.Success && plain.Text.Length == 0 && plain.Projection is { Supported: true, Empty: true, Variants.Count: 1 } && plain.MaxLength == 20,
            "A missing plain message didn't start as an empty supported draft: " + JsonSerializer.Serialize(plain, EditorJsonContext.Default.EditorMessageDraft));
        var typed = await session.ApplyMessageDraftOperationAsync("save", "pl", plain.Text, plain.Projection!.Revision,
            new EditorMessageOperation("set-pattern", "0", "Zapisz")).ConfigureAwait(false);
        Require(typed.Success && typed.Text == "Zapisz" && typed.Projection?.Empty == false, "Typing into a plain draft didn't give its text: " + typed.Text);

        // Declarations without a matcher: the declarations and an empty quoted pattern.
        var greeting = await session.CreateMessageDraftAsync("greeting", "pl").ConfigureAwait(false);
        Require(greeting.Success && greeting.Text == ".input {$name :string}\n{{}}" && greeting.Projection is { Supported: true, Empty: true } &&
            greeting.Projection.Inputs.Single().Name == "name", "A declared message's draft wasn't its declarations and an empty pattern: " + greeting.Text);

        // A missing plural message in a locale with more categories than the base (en: one/other; pl integers: one/few/many).
        var files = await session.CreateMessageDraftAsync("files", "pl").ConfigureAwait(false);
        Require(files.Success && files.Text == ".input {$count :integer}\n.match $count\none {{}}\nfew {{}}\nmany {{}}\n* {{}}",
            "The plural draft didn't take pl's categories: " + files.Text + Describe(files.Diagnostics));
        Require(files.Projection is { Supported: true, Empty: true } projection && projection.Selectors.SequenceEqual(["count"]) &&
            projection.Variants.Select(variant => variant.Keys.Single()).SequenceEqual(["one", "few", "many", "*"]) && projection.SelectorPluralCategories.Single().Contains("few"),
            "The plural draft's projection wasn't supported for the composer.");
        var few = await session.ApplyMessageDraftOperationAsync("files", "pl", files.Text, files.Projection!.Revision,
            new EditorMessageOperation("set-pattern", "1", "{$count} pliki")).ConfigureAwait(false);
        Require(few.Success && few.Text.Contains("few {{{$count} pliki}}", StringComparison.Ordinal) && few.Projection?.Empty == false &&
            few.Projection.Variants[1].Pattern == "{$count} pliki", "An operation on the draft didn't change its text: " + few.Text + Describe(few.Diagnostics));
        var stale = await session.ApplyMessageDraftOperationAsync("files", "pl", few.Text, files.Projection.Revision,
            new EditorMessageOperation("set-pattern", "0", "Stale")).ConfigureAwait(false);
        Require(!stale.Success && stale.Text == few.Text && stale.Diagnostics.Single().Id == "EDITOR-AUTHORING", "A stale draft operation wasn't refused.");
        var refused = await session.ApplyMessageDraftOperationAsync("files", "pl", few.Text, few.Projection!.Revision,
            new EditorMessageOperation("remove-variant", "3")).ConfigureAwait(false);
        Require(!refused.Success && refused.Text == few.Text, "Removing the catch-all of a draft wasn't refused.");
        // Nothing was written; the frontend inserts the typed draft through the document transform.
        Require(await File.ReadAllTextAsync(Path.Combine(project, "pl.rmf2")).ConfigureAwait(false) == "", "A draft wrote to the document.");
        var inserted = await session.TransformDocumentAsync("pl.rmf2", "", "files", few.Text).ConfigureAwait(false);
        Require(inserted.Entries.Single(entry => entry.Key == "files").Authoring is { Supported: true, Variants.Count: 4, Empty: false },
            "The inserted draft wasn't a supported message entry: " + inserted.Content + Describe(inserted.Diagnostics));

        // P5: the preview names the previewed message's syntax error, not the first diagnostic of the catalog.
        const string broken = "save = Speichern\ngreeting =\n  .input {$name :string}\n  {{Hallo {$name}\n";
        var preview = await session.PreviewMessageAsync("de.rmf2", broken, "de", "greeting").ConfigureAwait(false);
        Require(!preview.Success && preview.PrimaryDiagnostic is { Severity: "error" } primary && primary.Id != "RTR0010" && primary.Line >= 2 &&
            preview.Diagnostics.Any(item => item.Id == "RTR0010") && !ReferenceEquals(preview.Diagnostics[0], primary),
            "The preview's primary diagnostic wasn't the message's syntax error: " + preview.PrimaryDiagnostic?.Id + " of " + Describe(preview.Diagnostics));
        var valid = await session.PreviewMessageAsync("de.rmf2", german, "de", "save").ConfigureAwait(false);
        Require(valid.PrimaryDiagnostic is null || valid.PrimaryDiagnostic.Key == "save", "A preview chose a diagnostic of another message: " + valid.PrimaryDiagnostic?.Id);
    }

    private static async Task MountedContextJourneyAsync(string root)
    {
        string project = Path.Combine(root, "translations"), feature = Path.Combine(root, "feature");
        Directory.CreateDirectory(project); Directory.CreateDirectory(feature);
        await File.WriteAllTextAsync(Path.Combine(project, "runic.json"),
            "{\"schemaVersion\":1,\"catalog\":\"context\",\"code\":{\"namespace\":\"Smoke.Translations\",\"className\":\"SmokeText\"},\"baseLocale\":\"en\",\"locales\":[\"en\"],\"sourceRoots\":[{\"path\":\"../feature\",\"namespace\":[\"shop\"]}]}\n").ConfigureAwait(false);
        const string text = "# Checkout context 🦊\n@tag checkout\n@example {\"count\":1}\ncart =\n  .input {$count :integer}\n  .match $count\n  one {{One item}}\n  * {{Many items}}\n";
        await File.WriteAllTextAsync(Path.Combine(feature, "en.rmf2"), text).ConfigureAwait(false);
        using var session = new EditorSession(root);
        var loaded = await session.LoadAsync().ConfigureAwait(false);
        var document = loaded.Documents.Single(document => document.Path == "feature/en.rmf2");
        var entry = document.Entries!.Single();
        Require(loaded.Success && entry.Key == "shop_cart" && entry.Context?.Comments.Single() == "Checkout context 🦊" && entry.Context.Examples.Single()["count"] == "1", "Mounted context/examples weren't exposed under compiler identity.");
        Require(entry.Path?.SequenceEqual(["shop", "cart"]) == true, "A mounted message didn't carry its logical path: " + string.Join(".", entry.Path ?? []));
        var draft = await session.ApplyAuthoringOperationAsync(document.Path, document.Content, entry.Key,
            entry.Authoring!.Revision, new("set-pattern", "0", "Exactly one item")).ConfigureAwait(false);
        var changed = draft.Entries.Single();
        Require(changed.Context?.Tags.Single() == "checkout" && changed.Context.Examples.Single()["count"] == "1", "Unsaved mounted context/examples weren't retained.");
        var preview = await session.PreviewMessageAsync(document.Path, draft.Content, "en", entry.Key,
            JsonSerializer.Serialize(changed.Context!.Examples.Single(), EditorSmokeJsonContext.Default.IReadOnlyDictionaryStringString)).ConfigureAwait(false);
        Require(preview.RenderedJson?.Contains("Exactly one item", StringComparison.Ordinal) == true, "Mounted selected example wasn't previewed canonically.");
    }

    // L5-23, L5-24, L5-25: the preview formats for a requested locale and in a preview time zone, renders pseudo-locales
    // through the SDK generator (also when runic.json does not configure them), and the locale picker's CLDR data.
    private static async Task PreviewFormattingJourneyAsync(string project)
    {
        Directory.CreateDirectory(project);
        await File.WriteAllTextAsync(Path.Combine(project, "runic.json"),
            "{\"schemaVersion\":1,\"catalog\":\"formatting\",\"code\":{\"namespace\":\"Smoke.Translations\",\"className\":\"SmokeText\"},\"baseLocale\":\"en\",\"locales\":[\"en\",\"de\"],\"pseudoLocales\":[\"ar-XB\"]}\n").ConfigureAwait(false);
        const string english = "price = Total {$amount :number} at {$when :datetime style=short}\n";
        await File.WriteAllTextAsync(Path.Combine(project, "en.rmf2"), english).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(project, "de.rmf2"), "price = Summe {$amount :number} am {$when :datetime style=short}\n").ConfigureAwait(false);
        using var session = new EditorSession(project);
        var loaded = await session.LoadAsync().ConfigureAwait(false);
        Require(loaded.Success && loaded.Catalog?.PseudoLocales?.SequenceEqual(["ar-XB"]) == true, "Configured pseudo-locales weren't listed.");
        var german = loaded.Documents.Single(document => document.Path == "de.rmf2");
        const string samples = "{\"amount\":\"1234.5\",\"when\":\"2026-01-15T12:00:00Z\"}";
        var swiss = await session.PreviewMessageAsync(german.Path, german.Content, "de", "price", samples, "de-CH", "Asia/Tokyo").ConfigureAwait(false);
        Require(swiss.RenderedJson is { } swissJson && JsonNode.Parse(swissJson)!["runs"]![0]!["text"]!.GetValue<string>() == "Summe 1'234.5 am 15.01.2026 21:00" &&
            swissJson.Contains("\"formattingLocale\":\"de-CH\"", StringComparison.Ordinal) && swissJson.Contains("\"resolution\":\"parent\"", StringComparison.Ordinal),
            "The preview did not format for de-CH in the preview time zone: " + swiss.RenderedJson);
        var zone = await session.PreviewMessageAsync(german.Path, german.Content, "de", "price", samples, null, "Mars/Olympus").ConfigureAwait(false);
        Require(!zone.Success && zone.Diagnostics.Single().Id == "PREVIEW", "An unknown preview time zone wasn't reported.");
        foreach (string pseudo in new[] { "en-XA", "ar-XB" })
        {
            var generated = await session.PreviewMessageAsync(german.Path, german.Content, pseudo, "price", samples, null, "utc").ConfigureAwait(false);
            // en-XA accents the text; ar-XB wraps it in a right-to-left override (U+202E), both bracketed.
            string text = generated.Success && generated.RenderedJson is { } json ? JsonNode.Parse(json)!["runs"]![0]!["text"]!.GetValue<string>() : "";
            Require(text.StartsWith('[') && (pseudo == "ar-XB" ? text.Contains('\u202E') : !text.Contains("Total", StringComparison.Ordinal)),
                $"The {pseudo} preview wasn't the generated pseudo-locale: " + generated.RenderedJson);
        }
        // runic.json as the compiler reads it: with a UTF-8 BOM (as Visual Studio saves it) the pseudo-locales still
        // preview; a duplicate member is the compiler's diagnostic, not an exception.
        string manifestPath = Path.Combine(project, "runic.json");
        string manifestText = await File.ReadAllTextAsync(manifestPath).ConfigureAwait(false);
        await File.WriteAllBytesAsync(manifestPath, [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(manifestText)]).ConfigureAwait(false);
        foreach (string pseudo in new[] { "en-XA", "ar-XB" })
        {
            var bom = await session.PreviewMessageAsync(german.Path, german.Content, pseudo, "price", samples, null, "utc").ConfigureAwait(false);
            Require(bom.Success && bom.RenderedJson?.Contains('[') == true, $"The {pseudo} preview failed on a runic.json with a BOM: " + string.Join("; ", bom.Diagnostics.Select(item => item.Id + " " + item.Message)));
        }
        Require((await session.LoadAsync().ConfigureAwait(false)).Catalog?.PseudoLocales?.SequenceEqual(["ar-XB"]) == true, "A runic.json with a BOM lost its pseudo-locales.");
        await File.WriteAllTextAsync(manifestPath, manifestText.Replace("\"catalog\":\"formatting\"", "\"catalog\":\"formatting\",\"catalog\":\"formatting\"", StringComparison.Ordinal)).ConfigureAwait(false);
        var duplicate = await session.PreviewMessageAsync(german.Path, german.Content, "en-XA", "price", samples, null, "utc").ConfigureAwait(false);
        Require(!duplicate.Success && duplicate.Diagnostics.Any(item => item.Message.Contains("Duplicate JSON property 'catalog'", StringComparison.Ordinal)) &&
            duplicate.Diagnostics.All(item => item.Id != "PREVIEW"), "A duplicate runic.json member wasn't the compiler's diagnostic: " + string.Join("; ", duplicate.Diagnostics.Select(item => item.Id + " " + item.Message)));
        await File.WriteAllTextAsync(manifestPath, manifestText).ConfigureAwait(false);

        // The picker suggests the script-level locale runtime negotiation produces (zh-TW → zh-Hant), and lists which
        // requested tags reach the typed and the suggested locale under the catalog's unsupportedLocale policy.
        EditorLocaleDescription chinese = EditorLocales.Describe("zh-TW"), russian = EditorLocales.Describe("ru"), invalid = EditorLocales.Describe("not a tag");
        Require(chinese.Valid && chinese.Canonical == "zh-TW" && chinese.DataId == "zh-Hant-TW" && chinese.Suggested == "zh-Hant" && chinese.Cardinal.SequenceEqual(["other"]), "zh-TW wasn't described through likely subtags.");
        Require(EditorLocales.Describe("sr-ME").Suggested == "sr-Latn" && EditorLocales.Describe("de-CH").Suggested is null && EditorLocales.Describe("zh-Hant-HK").Suggested is null,
            "The script-level suggestion differs.");
        EditorLocaleDescription negotiated = await session.DescribeLocaleAsync("zh-TW").ConfigureAwait(false);
        Require(negotiated is { UnsupportedLocale: "parentsThenDefault", Matches.Tags: ["zh-TW", ..], SuggestedMatches: { } parents } &&
            parents.Tags.Contains("zh-TW") && parents.Tags.Contains("zh-HK") && parents.Tags.Contains("zh-Hant") && !parents.Tags.Contains("zh"),
            "The requested tags under parentsThenDefault differ: " + JsonSerializer.Serialize(negotiated.SuggestedMatches?.Tags, EditorSmokeJsonContext.Default.IReadOnlyListString));
        await File.WriteAllTextAsync(manifestPath, manifestText.Replace("\"pseudoLocales\"", "\"runtime\":{\"unsupportedLocale\":\"exact\"},\"pseudoLocales\"", StringComparison.Ordinal)).ConfigureAwait(false);
        EditorLocaleDescription exact = await session.DescribeLocaleAsync("zh-TW").ConfigureAwait(false);
        Require(exact is { UnsupportedLocale: "exact", Matches.Tags: ["zh-TW"], SuggestedMatches.Tags: ["zh-Hant"] },
            "Under exact, a locale must serve only its own tag: " + JsonSerializer.Serialize(exact.Matches?.Tags, EditorSmokeJsonContext.Default.IReadOnlyListString) + " " + JsonSerializer.Serialize(exact.SuggestedMatches?.Tags, EditorSmokeJsonContext.Default.IReadOnlyListString));
        await File.WriteAllTextAsync(manifestPath, manifestText).ConfigureAwait(false);
        Require(russian.Cardinal.SequenceEqual(["one", "few", "many", "other"]) && russian.Direction == "ltr" && EditorLocales.Describe("fa").Direction == "rtl",
            "Plural categories or direction came from somewhere other than the SDK's CLDR data.");
        Require(!invalid.Valid && invalid.Message?.Code == "ui_backend_locale_invalid", "An invalid tag wasn't refused.");
        Require(EditorLocales.Catalog.Locales.Contains("zh-Hant") && EditorLocales.Catalog.Locales.Contains("de-CH") && !EditorLocales.Catalog.Locales.Contains("root"),
            "The CLDR locale list is incomplete.");
    }

    // L5-24: adding and removing a locale goes through Tooling's planner (runic-translations locale add|remove):
    // runic.json keeps its formatting, the copied starter messages get the plural categories the new locale needs, and
    // a removed locale's review entries leave the review file.
    private static async Task LocaleChangeJourneyAsync(string project)
    {
        Directory.CreateDirectory(project);
        const string manifest = "{\n  \"schemaVersion\": 1,\n  \"catalog\": \"locales\",\n  \"code\": { \"namespace\": \"Smoke.Translations\", \"className\": \"SmokeText\" },\n  \"baseLocale\": \"en\",\n  \"locales\": [\"en\", \"de\"]\n}\n";
        await File.WriteAllTextAsync(Path.Combine(project, "runic.json"), manifest).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(project, "en.rmf2"), "files =\n  .input {$count :integer}\n  .match $count\n  one {{One file}}\n  * {{{$count} files}}\n").ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(project, "de.rmf2"), "files =\n  .input {$count :integer}\n  .match $count\n  one {{Eine Datei}}\n  * {{{$count} Dateien}}\n").ConfigureAwait(false);
        using var session = new EditorSession(project);
        Require((await session.LoadAsync().ConfigureAwait(false)).Success, "The locale fixture did not load.");

        var add = new EditorMutationRequest("add-locale", "ru", null, null, "en", null, null, null);
        EditorMutationPreview preview = session.PreviewMutation(add);
        Require(preview.Ok && preview.Files.Any(file => file.Path == "ru.rmf2" && file.Kind == "create"), "Adding ru wasn't planned: " + preview.Message?.Code);
        EditorOperationResult added = await session.ApplyMutationAsync(add with { ConfirmationToken = preview.ConfirmationToken }).ConfigureAwait(false);
        Require(added.Ok, "Adding ru failed: " + added.Message?.Code + " " + added.Message?.Detail);
        string updated = await File.ReadAllTextAsync(Path.Combine(project, "runic.json")).ConfigureAwait(false);
        Require(updated == manifest.Replace("[\"en\", \"de\"]", "[\"en\", \"de\", \"ru\"]", StringComparison.Ordinal), "runic.json lost its formatting: " + updated);
        string russian = await File.ReadAllTextAsync(Path.Combine(project, "ru.rmf2")).ConfigureAwait(false);
        Require(russian.Contains("few {{{$count} files}}", StringComparison.Ordinal) && russian.Contains("many {{{$count} files}}", StringComparison.Ordinal) && russian.Contains("one {{One file}}", StringComparison.Ordinal),
            "The copied starter didn't get ru's plural categories: " + russian);
        // The starter copies are drafts: ru's coverage starts at 0 %, not 100 %; saving an edited one records it translated.
        Require(added.Snapshot?.Status?.Locales.SingleOrDefault(locale => locale.Locale == "ru") is { Translated: 0, Missing: 1, Coverage: 0 },
            "A new language's starter copies counted as translated: " + JsonSerializer.Serialize(added.Snapshot?.Status?.Locales.Select(locale => locale.Locale + " " + locale.Coverage), EditorSmokeJsonContext.Default.IEnumerableString));
        Require((await File.ReadAllTextAsync(Path.Combine(project, "locales.review.json")).ConfigureAwait(false)).Contains("\"draft\"", StringComparison.Ordinal), "The starter copies weren't recorded as drafts.");
        EditorDocument starter = added.Snapshot!.Documents.Single(document => document.Path == "ru.rmf2");
        EditorOperationResult translated = await session.SaveAsync(starter.Path, starter.Content.Replace("One file", "Один файл", StringComparison.Ordinal), starter.Revision).ConfigureAwait(false);
        Require(translated.Ok && translated.Message is null, "Saving the edited starter failed: " + translated.Message);
        string promoted = await File.ReadAllTextAsync(Path.Combine(project, "locales.review.json")).ConfigureAwait(false);
        Require(promoted.Contains("\"translated\"", StringComparison.Ordinal) && !promoted.Contains("\"draft\"", StringComparison.Ordinal), "The edited starter wasn't recorded translated: " + promoted);
        Require((await session.LoadAsync().ConfigureAwait(false)).Status?.Locales.SingleOrDefault(locale => locale.Locale == "ru") is { Translated: 1, Coverage: 100 }, "The translated starter didn't count.");

        await File.WriteAllTextAsync(Path.Combine(project, "locales.review.json"), "{\"$schema\":\"runic.translations.interchange-review/2\",\"catalog\":\"locales\",\"entries\":[{\"key\":\"files\",\"locale\":\"ru\",\"state\":\"approved\"},{\"key\":\"files\",\"locale\":\"de\",\"state\":\"approved\"}]}\n").ConfigureAwait(false);
        var remove = new EditorMutationRequest("remove-locale", "ru", null, null, null, null, null, null);
        EditorMutationPreview removal = session.PreviewMutation(remove);
        Require(removal.Ok, "Removing ru wasn't planned: " + removal.Message?.Code + " " + removal.Message?.Detail);
        EditorOperationResult removed = await session.ApplyMutationAsync(remove with { ConfirmationToken = removal.ConfirmationToken }).ConfigureAwait(false);
        Require(removed.Ok && !File.Exists(Path.Combine(project, "ru.rmf2")), "Removing ru failed: " + removed.Message?.Code);
        Require(await File.ReadAllTextAsync(Path.Combine(project, "runic.json")).ConfigureAwait(false) == manifest, "Removing ru didn't restore runic.json byte for byte.");
        string review = await File.ReadAllTextAsync(Path.Combine(project, "locales.review.json")).ConfigureAwait(false);
        Require(!review.Contains("\"ru\"", StringComparison.Ordinal) && review.Contains("\"de\"", StringComparison.Ordinal), "The removed locale's review entries stayed: " + review);

        // A corrupt review file refuses the removal with a notice that names it, instead of a raw failure.
        await File.WriteAllTextAsync(Path.Combine(project, "locales.review.json"), "{ not json").ConfigureAwait(false);
        EditorMutationPreview corrupt = session.PreviewMutation(remove with { Locale = "de" });
        Require(!corrupt.Ok && corrupt.Message is { Code: "ui_backend_locale_review_invalid", Detail: not null } notice &&
            notice.Args.Any(argument => argument is { Name: "path", Value: "locales.review.json" }), "A corrupt review file wasn't a clear notice: " + corrupt.Message);
        File.Delete(Path.Combine(project, "locales.review.json"));

        // L5-22: the snapshot carries runic-translations status, also while a translation is missing (RTR0010 errors).
        await File.WriteAllTextAsync(Path.Combine(project, "de.rmf2"), "").ConfigureAwait(false);
        WorkspaceSnapshot incomplete = await session.LoadAsync().ConfigureAwait(false);
        TranslationsStatusLocale? german = incomplete.Status?.Locales.SingleOrDefault(locale => locale.Locale == "de");
        Require(!incomplete.Success && german is { Missing: 1, Coverage: 0, Completeness: "error" } && incomplete.Status!.Messages == 1,
            "The editor's status model doesn't match runic-translations status for an incomplete catalog.");
    }

    // P7: a missing key of an existing group is inserted into that group, not written to a second block at the end.
    private static async Task NestedMissingKeyJourneyAsync(string project)
    {
        Directory.CreateDirectory(project);
        const string manifest = "{\n  \"schemaVersion\": 1,\n  \"catalog\": \"nested\",\n  \"code\": { \"namespace\": \"Smoke.Translations\", \"className\": \"SmokeText\" },\n  \"baseLocale\": \"en\",\n  \"locales\": [\"en\", \"de\"]\n}\n";
        await File.WriteAllTextAsync(Path.Combine(project, "runic.json"), manifest).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(project, "en.rmf2"), "notes {\n  selected = Selected\n  empty = Empty\n  deleteConfirm = Delete?\n}\n").ConfigureAwait(false);
        // The German file must load, so it keeps every base key; the edit starts from the same group without deleteConfirm.
        await File.WriteAllTextAsync(Path.Combine(project, "de.rmf2"), "notes {\n  selected = Ausgewählt\n  empty = Leer\n  deleteConfirm = Löschen?\n}\n").ConfigureAwait(false);
        using var session = new EditorSession(project);
        WorkspaceSnapshot loaded = await session.LoadAsync().ConfigureAwait(false);
        Require(loaded.Success, "The nested-key fixture did not load: " + string.Join("; ", loaded.Diagnostics.Select(item => item.Id + " " + item.Message)));
        EditorDocument german = loaded.Documents.Single(document => document.Path.EndsWith("de.rmf2", StringComparison.OrdinalIgnoreCase));
        string withoutKey = german.Content.Replace("  deleteConfirm = Löschen?\n", "", StringComparison.Ordinal);
        Require(withoutKey != german.Content, "The fixture's German group has no deleteConfirm line to remove.");
        EditorDocumentDraft draft = await session.TransformDocumentAsync(german.Path, withoutKey, "notes_deleteConfirm", "Löschen bestätigen?").ConfigureAwait(false);
        Require(draft.Success && draft.Entries.Any(entry => entry.Key == "notes_deleteConfirm"),
            "The missing nested key was not added to the German draft: " + draft.Content);
        Require(draft.Entries.Single(entry => entry.Key == "notes_deleteConfirm").Path?.SequenceEqual(["notes", "deleteConfirm"]) == true,
            "A nested message didn't carry its logical path.");
        string[] lines = draft.Content.Split('\n');
        Require(lines.Count(line => line == "notes {") == 1 && lines.Count(line => line == "}") == 1,
            "The missing nested key was written to a second group block: " + draft.Content);
        Require(lines.Contains("  deleteConfirm = Löschen bestätigen?"),
            "The missing nested key was not indented inside its existing group: " + draft.Content);
        EditorOperationResult saved = await session.SaveAsync(german.Path, draft.Content, german.Revision).ConfigureAwait(false);
        Require(saved.Ok && await File.ReadAllTextAsync(Path.Combine(project, "de.rmf2")).ConfigureAwait(false) == draft.Content,
            "The German document with the inserted nested key was not saved as drafted: " + saved.Message?.Code);
    }

    // L5-26: migration from .resx and i18next through Tooling's importer: a dry run lists every message and the files
    // it writes; applying it is a one-use confirmation, refused once a file changed.
    private static async Task CatalogImportJourneyAsync(string root)
    {
        string project = Path.Combine(root, "translations"), resx = Path.Combine(root, "resx"), i18next = Path.Combine(root, "locales");
        Directory.CreateDirectory(project); Directory.CreateDirectory(resx); Directory.CreateDirectory(Path.Combine(i18next, "en")); Directory.CreateDirectory(Path.Combine(i18next, "de"));
        await File.WriteAllTextAsync(Path.Combine(project, "runic.json"),
            "{\"schemaVersion\":1,\"catalog\":\"migrated\",\"code\":{\"namespace\":\"Smoke.Translations\",\"className\":\"SmokeText\"},\"baseLocale\":\"en\",\"locales\":[\"en\",\"de\"]}\n").ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(project, "en.rmf2"), "existing = Existing\n").ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(project, "de.rmf2"), "existing = Vorhanden\n").ConfigureAwait(false);
        static string Resx(params (string Name, string Value)[] items) => "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<root>\n" +
            string.Concat(items.Select(item => $"  <data name=\"{item.Name}\" xml:space=\"preserve\">\n    <value>{item.Value}</value>\n  </data>\n")) + "</root>\n";
        await File.WriteAllTextAsync(Path.Combine(resx, "Strings.resx"), Resx(("Greeting", "Hello, {0}!"), ("Existing", "Different"))).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(resx, "Strings.de.resx"), Resx(("Greeting", "Hallo, {0}!"))).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(i18next, "en", "common.json"), "{\"ok\":\"OK\",\"cancel\":\"Cancel\"}").ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(i18next, "de", "common.json"), "{\"ok\":\"OK\",\"cancel\":\"Abbrechen\"}").ConfigureAwait(false);

        using var session = new EditorSession(project);
        Require((await session.LoadAsync().ConfigureAwait(false)).Success, "The import fixture did not load.");
        EditorCatalogImportPlan plan = await session.PreviewCatalogImportAsync(new EditorCatalogImportRequest("resx", "../resx/Strings.resx")).ConfigureAwait(false);
        Require(plan.Ok && plan.ConfirmationToken is not null && plan.Files.Contains("en.rmf2") &&
            plan.Locales.Single(locale => locale.Locale == "en") is { Added: 2 } && plan.Locales.Single(locale => locale.Locale == "de") is { Added: 1 } &&
            plan.Entries.Any(entry => entry is { Key: "Greeting", Locale: "de", Outcome: "added" }),
            "The resx dry run isn't the importer's plan: " + JsonSerializer.Serialize(plan, EditorJsonContext.Default.EditorCatalogImportPlan));
        Require(!File.ReadAllText(Path.Combine(project, "en.rmf2")).Contains("greeting", StringComparison.Ordinal), "The dry run wrote a file.");
        EditorOperationResult applied = await session.ApplyCatalogImportAsync(plan.ConfirmationToken!).ConfigureAwait(false);
        string importedGerman = string.Concat(Directory.GetFiles(project, "de.rmf2", SearchOption.AllDirectories).Select(File.ReadAllText));
        Require(applied.Ok && importedGerman.Contains("Hallo, ", StringComparison.Ordinal) &&
            File.ReadAllText(Path.Combine(project, "en.rmf2")).Contains("existing = Existing", StringComparison.Ordinal),
            "The resx import wasn't applied as planned: " + applied.Message?.Code + " " + string.Join(", ", plan.Files) + " " + importedGerman);
        Require(!(await session.ApplyCatalogImportAsync(plan.ConfirmationToken!).ConfigureAwait(false)).Ok, "A confirmation token applied twice.");

        EditorCatalogImportPlan i18n = await session.PreviewCatalogImportAsync(new EditorCatalogImportRequest("i18next", i18next)).ConfigureAwait(false);
        Require(i18n.Ok && i18n.ConfirmationToken is not null, "The i18next dry run failed: " + i18n.Message?.Code);
        await File.AppendAllTextAsync(Path.Combine(project, "en.rmf2"), "late = Changed after the dry run\n").ConfigureAwait(false);
        Require(!(await session.ApplyCatalogImportAsync(i18n.ConfirmationToken!).ConfigureAwait(false)).Ok, "An import applied over a file that changed since its dry run.");
        EditorCatalogImportPlan missing = await session.PreviewCatalogImportAsync(new EditorCatalogImportRequest("resx", "../resx/Missing.resx")).ConfigureAwait(false);
        Require(!missing.Ok && missing.ConfirmationToken is null, "A missing source was planned.");
    }

    private static async Task MalformedManifestJourneyAsync(string root)
    {
        // A hand-edited or half-saved runic.json must produce diagnostics, not
        // exceptions, in load, mutation planning and the external-change scan,
        // and the editor must recover once the manifest is fixed.
        string project = Path.Combine(root, "translations"), feature = Path.Combine(root, "feature");
        Directory.CreateDirectory(project); Directory.CreateDirectory(feature);
        const string valid = "{\"schemaVersion\":1,\"catalog\":\"manifest\",\"code\":{\"namespace\":\"Smoke.Translations\",\"className\":\"ManifestText\"},\"baseLocale\":\"en\",\"sourceRoots\":[{\"path\":\"../feature\",\"namespace\":[\"shop\"]}]}\n";
        string manifest = Path.Combine(project, "runic.json");
        await File.WriteAllTextAsync(manifest, valid).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(feature, "en.rmf2"), "title = Shop\n").ConfigureAwait(false);
        using var session = new EditorSession(root);
        WorkspaceSnapshot initial = await session.LoadAsync().ConfigureAwait(false);
        Require(initial.Success && initial.Documents.Any(document => document.Path == "feature/en.rmf2"), "The manifest fixture did not load.");
        await DrainExternalChangesAsync(session, null).ConfigureAwait(false);

        string Mount(string path) => valid.Replace("\"path\":\"../feature\"", "\"path\":\"" + path + "\"", StringComparison.Ordinal);
        string noMount = TranslationManifestReader.SourceRootWithoutPath;
        // Message is the manifest diagnostic the load must report; Mutation is
        // the reason create-key planning must give, when it gets that far.
        var cases = new (string Name, string Content, string Message, string? Mutation)[] {
            ("array root", "[\"not\",\"an\",\"object\"]\n", "Runic project root must be an object.", null),
            ("missing path", valid.Replace("\"path\":\"../feature\",", "", StringComparison.Ordinal), "Missing required member 'path'.", noMount),
            ("whitespace path", Mount("   "), noMount, noMount),
            ("non-object mount", valid.Replace("[{\"path\":\"../feature\",\"namespace\":[\"shop\"]}]", "[42]", StringComparison.Ordinal), "Invalid source mount.", noMount),
            ("non-array sourceRoots", valid.Replace("[{\"path\":\"../feature\",\"namespace\":[\"shop\"]}]", "{\"path\":\"../feature\",\"namespace\":[\"shop\"]}", StringComparison.Ordinal),
                "sourceRoots requires a nonempty array of mounts.", TranslationManifestReader.SourceRootsNotArray),
            ("half-saved JSON", valid[..(valid.Length / 2)], "Expected a JSON value.", null),
            // A path still being typed names a directory that does not exist yet.
            ("missing source root", Mount("../featur"), EditorWorkspace.SourceRootMissing("featur"),
                EditorWorkspace.SourceRootMissing("featur")),
            ("escaping source root", Mount("../../.."), EditorWorkspace.SourceRootOutsideWorkspace("../.."),
                EditorWorkspace.SourceRootOutsideWorkspace("../..")),
            ("missing baseLocale", valid.Replace("\"baseLocale\":\"en\",", "", StringComparison.Ordinal), "Missing required member 'baseLocale'.", TranslationManifestReader.MissingBaseLocale),
        };
        foreach (var malformed in cases)
        {
            await File.WriteAllTextAsync(manifest, malformed.Content).ConfigureAwait(false);
            // The scan must report the manifest change and then settle; a scan
            // that throws would fail again on every subsequent check. Scan before
            // loading: a load resets the external-change baseline.
            await DrainExternalChangesAsync(session, "translations/runic.json").ConfigureAwait(false);
            await session.CheckExternalChangesAsync().ConfigureAwait(false);
            WorkspaceSnapshot broken = await session.LoadAsync().ConfigureAwait(false);
            EditorDiagnostic[] manifestErrors = broken.Diagnostics
                .Where(diagnostic => diagnostic.Path == "translations/runic.json" && diagnostic.Severity == "error").ToArray();
            Require(!broken.Success && manifestErrors.Length != 0, $"A malformed manifest ({malformed.Name}) did not produce a manifest diagnostic.");
            Require(manifestErrors.Any(diagnostic => diagnostic.Message == malformed.Message),
                $"The {malformed.Name} manifest diagnostic was not '{malformed.Message}': {string.Join(" | ", manifestErrors.Select(diagnostic => diagnostic.Message))}");
            EditorMutationPreview mutation = session.PreviewMutation(new EditorMutationRequest("create-key", null, null, null, null, null, "shop.subtitle", "Subtitle"));
            Require(!mutation.Ok, $"Mutation planning accepted a malformed manifest ({malformed.Name}).");
            Require(malformed.Mutation is null || mutation.Message?.Detail == malformed.Mutation,
                $"Mutation planning for the {malformed.Name} manifest did not refuse with '{malformed.Mutation}': {mutation.Message?.Code} {mutation.Message?.Detail}");
        }

        await File.WriteAllTextAsync(manifest, valid).ConfigureAwait(false);
        await DrainExternalChangesAsync(session, "translations/runic.json").ConfigureAwait(false);
        WorkspaceSnapshot recovered = await session.LoadAsync().ConfigureAwait(false);
        Require(recovered.Success && recovered.Documents.Any(document => document.Path == "feature/en.rmf2"), "The editor did not recover after the manifest was fixed.");
    }

    // A preview of a structural change reads the workspace but acknowledges nothing: an outside edit made just before it
    // is still reported by the next external-change check. Only a reload or an applied change moves the baseline.
    private static async Task PreviewKeepsExternalChangesJourneyAsync(string project)
    {
        Directory.CreateDirectory(project);
        await File.WriteAllTextAsync(Path.Combine(project, "runic.json"),
            "{\"schemaVersion\":1,\"catalog\":\"preview-external\",\"code\":{\"namespace\":\"Smoke.Translations\",\"className\":\"PreviewText\"},\"baseLocale\":\"en\",\"locales\":[\"en\",\"de\"]}\n").ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(project, "en.rmf2"), "notes {\n  pinned = Pinned\n}\n").ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(project, "de.rmf2"), "notes {\n  pinned = Angeheftet\n}\n").ConfigureAwait(false);
        using var session = new EditorSession(project);
        Require((await session.LoadAsync().ConfigureAwait(false)).Success, "The preview fixture did not load.");
        await DrainExternalChangesAsync(session, null).ConfigureAwait(false);

        await File.WriteAllTextAsync(Path.Combine(project, "de.rmf2"), "notes {\n  pinned = Von außen geändert\n}\n").ConfigureAwait(false);
        EditorMutationPreview preview = session.PreviewMutation(new EditorMutationRequest("create-key", null, null, null, null, null, "notes.archived", "Archived"));
        Require(preview.Ok, "The create-key preview failed: " + preview.Message?.Code + " " + preview.Message?.Detail);
        await DrainExternalChangesAsync(session, "de.rmf2").ConfigureAwait(false);
        Require(await File.ReadAllTextAsync(Path.Combine(project, "en.rmf2")).ConfigureAwait(false) == "notes {\n  pinned = Pinned\n}\n", "The preview wrote to the workspace.");
    }

    private static async Task DrainExternalChangesAsync(EditorSession session, string? expectedPath)
    {
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        bool observed = expectedPath is null;
        while (deadline.Elapsed < TimeSpan.FromSeconds(10))
        {
            EditorExternalChanges changes = await session.CheckExternalChangesAsync().ConfigureAwait(false);
            observed |= changes.Paths.Contains(expectedPath);
            if (observed && changes.Paths.Count == 0) return;
            await Task.Delay(100).ConfigureAwait(false);
        }
        Require(observed, $"The external-change scan never reported '{expectedPath}'.");
    }

    // I-165: `export --output` on the command line writes a rooted path where
    // it points and a relative path under the workspace, as before. The UI
    // bridge keeps every export inside the workspace.
    private static async Task InterchangeOutputJourneyAsync(string container, IEditorCommandOperations operations)
    {
        string project = Path.Combine(container, "workspace");
        string outside = Path.Combine(container, "outside");
        await TranslationProjectWriter.CreateAsync(TranslationProjectScaffolder.Render(
            new TranslationProjectCreationRequest(project, "output-smoke", "en", "Smoke.Translations", "SmokeText",
                [new TranslationProjectLocale("de", "en")]))).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(project, "en.rmf2"), "application {\n  title = Title\n}\n").ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(project, "de.rmf2"), "application {\n  title = Titel\n}\n").ConfigureAwait(false);
        EditorCommandRequest Export(string format, string output) => new(
            "export", [project], null, false, false, false, CommandOutputMode.Json, Format: format, Output: output);

        string xliffDirectory = Path.Combine(outside, "xliff");
        CommandOutcome<EditorCommandResult> xliff = await operations.ExecuteAsync(Export("xliff", xliffDirectory)).ConfigureAwait(false);
        string xliffFile = Path.Combine(xliffDirectory, "output-smoke.de.xliff");
        Require(xliff.Succeeded && File.Exists(xliffFile) &&
            xliff.Value?.XliffExport?.Documents.Single().Path == xliffFile &&
            !Directory.Exists(Path.Combine(project, xliffDirectory.TrimStart('/', '\\'))),
            "export --interchange xliff did not write to the rooted --output directory.");

        string reviewFile = Path.Combine(outside, "review", "catalog.review.json");
        CommandOutcome<EditorCommandResult> review = await operations.ExecuteAsync(Export("review", reviewFile)).ConfigureAwait(false);
        Require(review.Succeeded && File.Exists(reviewFile) && review.Value?.ReviewExport?.Path == reviewFile,
            "export --interchange review did not write to the rooted --output path.");

        CommandOutcome<EditorCommandResult> relative = await operations.ExecuteAsync(Export("xliff", "exports/xliff")).ConfigureAwait(false);
        Require(relative.Succeeded && File.Exists(Path.Combine(project, "exports", "xliff", "output-smoke.de.xliff")) &&
            relative.Value?.XliffExport?.Documents.Single().Path == "exports/xliff/output-smoke.de.xliff",
            "export --output with a relative path did not resolve under the workspace.");

        string bridgeTarget = Path.Combine(outside, "bridge");
        using var session = new EditorSession(project);
        EditorXliffExportResult bridge = await session.ExportXliffAsync(bridgeTarget).ConfigureAwait(false);
        Require(!Directory.Exists(bridgeTarget) && (!bridge.Ok || bridge.Documents.All(document => !Path.IsPathRooted(document.Path))),
            "The UI export wrote outside the workspace.");
    }

    // Version 1 editor approvals move into the SDK review file (<catalog>.review.json beside runic.json) on first open:
    // an approval whose recorded base value still matches stays approved, the rest become needs-review, with one notice.
    // Afterwards "stale" is the SDK per-unit fingerprint (RTR0090): editing another key leaves an approval current,
    // editing its own source makes it stale, and marking it again records the current source.
    private static async Task ReviewStampJourneyAsync(string container)
    {
        // Values from the pre-0.9 review-model.ts sourceFingerprint, run with bun.
        Require(EditorWorkspace.EditorSourceFingerprint("Hello {$name}!") == "fnv1a64:6766928709d1ff53" &&
            EditorWorkspace.EditorSourceFingerprint("Zeile\n\"zwei\"\t\\ \u0001 😀 \ud800 Ä") == "fnv1a64:f1a8bb5b9e6f39e5",
            "The editor's former review fingerprint differs from the frontend's.");
        string project = Path.Combine(container, "workspace");
        await TranslationProjectWriter.CreateAsync(TranslationProjectScaffolder.Render(
            new TranslationProjectCreationRequest(project, "stamp-smoke", "en", "Smoke.Translations", "SmokeText",
                [new TranslationProjectLocale("de", "en")]))).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(project, "en.rmf2"), "title = Title\nother = Other\n").ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(project, "de.rmf2"), "title = Titel\nother = Andere\n").ConfigureAwait(false);
        string sidecar = Path.Combine(project, ".runic-translations", "stamp-smoke.editor-state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(sidecar)!);
        await File.WriteAllTextAsync(sidecar, "{\"$schema\":\"runic.translations.editor-state/1\",\"catalog\":\"stamp-smoke\",\"messages\":{" +
            "\"title\":{\"de\":{\"state\":\"approved\",\"note\":\"Checked\",\"sourceFingerprint\":\"" + EditorWorkspace.EditorSourceFingerprint("Title") + "\"}}," +
            "\"other\":{\"de\":{\"state\":\"approved\",\"sourceFingerprint\":\"" + EditorWorkspace.EditorSourceFingerprint("Previous") + "\"}}," +
            "\"gone\":{\"de\":{\"state\":\"approved\"}}}," +
            "\"terminology\":[{\"source\":\"Title\",\"preferred\":\"Titel\"}]}").ConfigureAwait(false);
        using var session = new EditorSession(project);
        WorkspaceSnapshot opened = await session.LoadAsync().ConfigureAwait(false);
        EditorReviewSnapshot review = opened.Review ?? throw new InvalidOperationException("The migrated workspace has no review snapshot.");
        Require(review.Notice is { Code: "ui_backend_review_migrated" } notice && notice.Args.Single(arg => arg.Name == "count").Value == "2" &&
            notice.Args.Single(arg => arg.Name == "demoted").Value == "1" && notice.Args.Single(arg => arg.Name == "dropped").Value == "1" &&
            notice.Args.Single(arg => arg.Name == "kept").Value == "0" &&
            notice.Args.Single(arg => arg.Name == "backup").Value == ".runic-translations/stamp-smoke.editor-state.v1.json" && review.Path == "stamp-smoke.review.json",
            "The move of editor approvals into the review file was not announced once with its counts: " + review.Notice);
        Require((await File.ReadAllTextAsync(sidecar[..^".json".Length] + ".v1.json").ConfigureAwait(false)).Contains("editor-state/1", StringComparison.Ordinal),
            "The version 1 editor state was not kept as a backup.");
        Require(review.Entries.Single(entry => entry.Key == "title") is { State: "approved", Note: "Checked", Stale: false } &&
            review.Entries.Single(entry => entry.Key == "other") is { State: "needs-review", Stale: false } && review.Terminology.Single().Preferred == "Titel",
            "The current approval was not kept or the stale one was not demoted.");
        TranslationInterchangeReview file = TranslationReviewFile.Read(Path.Combine(project, "stamp-smoke.review.json"), "stamp-smoke")
            ?? throw new InvalidOperationException("The review file was not written.");
        Require(file.Entries.Count == 2 && file.Entries.All(entry => entry.Note is null && entry.SourceFingerprint?.StartsWith("sha256:", StringComparison.Ordinal) == true),
            "The review file entries do not carry the SDK fingerprint, or the editor note moved into the review file.");
        string rewritten = await File.ReadAllTextAsync(sidecar).ConfigureAwait(false);
        Require(rewritten.Contains("editor-state/2", StringComparison.Ordinal) && !rewritten.Contains("\"state\"", StringComparison.Ordinal) && rewritten.Contains("Checked", StringComparison.Ordinal),
            "The editor state was not rewritten as version 2 with the note kept: " + rewritten);
        Require((await session.LoadAsync().ConfigureAwait(false)).Review?.Notice is null, "The migration notice was repeated.");

        async Task<string> SegmentAsync()
        {
            EditorXliffExportResult export = await session.ExportXliffAsync("xliff").ConfigureAwait(false);
            Require(export.Ok, "The review-stamp export failed.");
            XDocument document = XDocument.Load(Path.Combine(project, export.Documents.Single().Path));
            return document.Descendants().Single(element => element.Name.LocalName == "unit" && element.Attribute("id")?.Value == "title")
                .Elements().Single(element => element.Name.LocalName == "segment").ToString(SaveOptions.DisableFormatting);
        }
        Require((await SegmentAsync().ConfigureAwait(false)).Contains("state=\"final\"", StringComparison.Ordinal), "A current approval was not exported as final.");
        EditorReviewFileResult copy = await session.ExportReviewJsonAsync(null).ConfigureAwait(false);
        Require(copy.Ok && (await File.ReadAllTextAsync(Path.Combine(project, copy.Path!)).ConfigureAwait(false)).Contains("\"Checked\"", StringComparison.Ordinal),
            "The exported review data lost the editor's note.");
        await File.WriteAllTextAsync(Path.Combine(project, "en.rmf2"), "title = Title\nother = Other text\n").ConfigureAwait(false);
        Require((await SegmentAsync().ConfigureAwait(false)).Contains("state=\"final\"", StringComparison.Ordinal), "Editing another key voided an approval.");
        await File.WriteAllTextAsync(Path.Combine(project, "en.rmf2"), "title = Main title\nother = Other text\n").ConfigureAwait(false);
        Require((await SegmentAsync().ConfigureAwait(false)).Contains("subState=\"runic:needs-review\"", StringComparison.Ordinal),
            "An approval made before the source changed was exported as current.");
        WorkspaceSnapshot changed = await session.LoadAsync().ConfigureAwait(false);
        Require(changed.Review!.Entries.Single(entry => entry.Key == "title").Stale &&
            changed.Diagnostics.Any(diagnostic => diagnostic is { Id: "RTR0090", Key: "title", Locale: "de" } && diagnostic.MessagePath?.SequenceEqual(["title"]) == true),
            "A stale approval was not marked stale or reported as RTR0090 on its row.");

        // Marking it again records the source the editor loaded (the snapshot's fingerprint), and the note moves onto the
        // review entry, so an edited or cleared note is what XLIFF exports.
        EditorReviewSnapshot before = changed.Review!;
        string loaded = before.Fingerprints!["title"];
        EditorReviewOperationResult saved = await session.SaveReviewAsync(new EditorReviewSaveRequest(before.Revision,
            before.Entries.Select(entry => entry.Key == "title" ? entry with { SourceFingerprint = loaded } : entry).ToArray(), before.Terminology)).ConfigureAwait(false);
        Require(saved.Ok && saved.Review!.Entries.Single(entry => entry.Key == "title") is { State: "approved", Stale: false, Note: "Checked" } && saved.Review.Notice is null,
            "Marking a stale approval again did not record the loaded source: " + saved.Message);
        Require(TranslationReviewFile.Read(Path.Combine(project, "stamp-smoke.review.json"), "stamp-smoke")!.Entries.Single(entry => entry.Key == "title").Note == "Checked",
            "The note did not move onto the review entry.");
        Require(!(await session.LoadAsync().ConfigureAwait(false)).Diagnostics.Any(diagnostic => diagnostic is { Id: "RTR0090", Key: "title" }), "RTR0090 remained after the review.");
        Require((await session.UndoAsync().ConfigureAwait(false)).Ok &&
            (await session.LoadAsync().ConfigureAwait(false)).Review!.Entries.Single(entry => entry.Key == "title").Stale,
            "Undo did not restore the earlier review file.");
        Require(saved.Review!.Revision != before.Revision, "Saving did not change the review revision.");

        // The base changes on disk after the editor loaded it: an approval made against the loaded text is saved for that
        // text, so it shows as stale instead of approving text the reviewer never saw.
        WorkspaceSnapshot current = await session.LoadAsync().ConfigureAwait(false);
        EditorReviewSnapshot seen = current.Review!;
        await File.WriteAllTextAsync(Path.Combine(project, "en.rmf2"), "title = Main title, again\nother = Other text\n").ConfigureAwait(false);
        EditorReviewOperationResult unseen = await session.SaveReviewAsync(new EditorReviewSaveRequest(seen.Revision,
            seen.Entries.Select(entry => entry.Key == "title" ? entry with { State = "approved", SourceFingerprint = seen.Fingerprints!["title"], Note = null } : entry).ToArray(),
            seen.Terminology)).ConfigureAwait(false);
        Require(unseen.Ok && unseen.Review!.Notice is { Code: "ui_backend_review_saved_stale" } && unseen.Review.Entries.Single(entry => entry.Key == "title") is { Stale: true, Note: null },
            "An approval of base text that changed on disk since it was loaded was not saved as stale, or the cleared note came back: " + unseen.Message);
    }

    // A catalog being translated (a locale misses keys, RTR0010 is an error by default) still has review data: version 1
    // approvals move, notes alone create no review entry, a state is recorded with its fingerprint, the RTR0095 quick fix
    // is offered, and a review file another writer changes is reported and merged with unsaved editor changes.
    private static async Task IncompleteCatalogReviewJourneyAsync(string container)
    {
        string project = Path.Combine(container, "workspace");
        await TranslationProjectWriter.CreateAsync(TranslationProjectScaffolder.Render(
            new TranslationProjectCreationRequest(project, "incomplete-smoke", "en", "Smoke.Translations", "SmokeText",
                [new TranslationProjectLocale("cs", "en")]))).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(project, "en.rmf2"),
            "title = Title\nlater = Later\nother = Other\nitems =\n  .input {$n :integer}\n  .match $n\n  one {{One}}\n  * {{Many}}\n").ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(project, "cs.rmf2"), "title = Titulek\n").ConfigureAwait(false);
        string sidecar = Path.Combine(project, ".runic-translations", "incomplete-smoke.editor-state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(sidecar)!);
        await File.WriteAllTextAsync(sidecar, "{\"$schema\":\"runic.translations.editor-state/1\",\"catalog\":\"incomplete-smoke\",\"messages\":{" +
            "\"title\":{\"cs\":{\"state\":\"approved\",\"sourceFingerprint\":\"" + EditorWorkspace.EditorSourceFingerprint("Title") + "\"}}},\"terminology\":[]}").ConfigureAwait(false);
        using var session = new EditorSession(project);
        WorkspaceSnapshot opened = await session.LoadAsync().ConfigureAwait(false);
        Require(!opened.Success && opened.Diagnostics.Any(diagnostic => diagnostic is { Id: "RTR0010", Severity: "error" }), "The fixture compiled without an RTR0010 error.");
        EditorReviewSnapshot review = opened.Review!;
        Require(review is { MigrationPending: false, Error: null, Fingerprints.Count: 4, Notice.Code: "ui_backend_review_migrated" } &&
            review.Entries.Single() is { Key: "title", Locale: "cs", State: "approved", Stale: false },
            "Version 1 approvals did not move while the catalog has errors: " + review.Error);

        // Notes and samples save on an incomplete catalog without creating a review entry; a state is recorded with its fingerprint.
        var noteOnly = new EditorReviewEntry("later", "cs", "draft", "Ask the author", null, new Dictionary<string, string> { ["n"] = "3" }, Implicit: true);
        var approved = new EditorReviewEntry("title", "cs", "approved", null, review.Fingerprints!["title"], new Dictionary<string, string>());
        EditorReviewOperationResult saved = await session.SaveReviewAsync(new EditorReviewSaveRequest(review.Revision, [noteOnly, approved],
            [new EditorTerminologyEntry("Title", "Titulek", "cs", null)], review.Entries, review.Terminology)).ConfigureAwait(false);
        Require(saved.Ok, "Review data was not saved on an incomplete catalog: " + saved.Message);
        string reviewPath = Path.Combine(project, "incomplete-smoke.review.json");
        TranslationInterchangeReview file = TranslationReviewFile.Read(reviewPath, "incomplete-smoke")!;
        Require(file.Entries.Single() is { Key: "title", State: TranslationReviewState.Approved } entry && entry.SourceFingerprint == review.Fingerprints["title"],
            "A note alone created a review entry, or the state was not recorded with its fingerprint.");
        Require(saved.Review!.Entries.Single(entry => entry.Key == "later") is { Implicit: true, Note: "Ask the author" } && saved.Review.Terminology.Count == 1,
            "The note, samples or terminology were not kept in the editor state.");

        // The RTR0095 quick fix is offered although the catalog does not compile.
        await File.WriteAllTextAsync(Path.Combine(project, "cs.rmf2"), "title = Titulek\nitems =\n  .input {$n :integer}\n  .match $n\n  * {{Mnoho}}\n  one {{Jedna}}\n").ConfigureAwait(false);
        WorkspaceSnapshot plural = await session.LoadAsync().ConfigureAwait(false);
        Require(!plural.Success && plural.Diagnostics.Any(diagnostic => diagnostic is { Id: "RTR0095", Key: "items", Locale: "cs", QuickFixes.Count: 1 }),
            "The RTR0095 quick fix was not offered while the catalog has errors.");

        // Another writer (runic-translations review mark) records a state: the external-change check reports the new
        // review data, and a save started from the older data keeps both sides' changes.
        EditorReviewSnapshot start = plural.Review!;
        TranslationReviewFile.Update(reviewPath, "incomplete-smoke", current => TranslationReviewFile.Mark(current, "incomplete-smoke", ["other"], "cs",
            TranslationReviewState.NeedsReview, "From the CLI", start.Fingerprints!));
        EditorExternalChanges external = new(false, [], []);
        for (int attempt = 0; attempt < 50 && external.Review is null; attempt++)
        {
            await Task.Delay(100).ConfigureAwait(false);
            external = await session.CheckExternalChangesAsync().ConfigureAwait(false);
        }
        Require(external.Review?.Entries.Any(entry => entry is { Key: "other", State: "needs-review", Note: "From the CLI" }) == true,
            "The external-change check did not report the review file another writer changed.");
        EditorReviewEntry[] mine = [.. start.Entries.Select(entry => entry.Key == "later" ? entry with { Note = "Ask again" } : entry)];
        EditorReviewOperationResult merged = await session.SaveReviewAsync(new EditorReviewSaveRequest(start.Revision, mine, start.Terminology, start.Entries, start.Terminology)).ConfigureAwait(false);
        Require(merged.Ok && merged.Review!.Entries.Single(entry => entry.Key == "other") is { State: "needs-review" } &&
            merged.Review.Entries.Single(entry => entry.Key == "later") is { Note: "Ask again" },
            "A save from older review data did not merge with the other writer's change: " + merged.Message);
        // The same entry changed on both sides is refused, naming it.
        EditorReviewSnapshot latest = merged.Review!;
        TranslationReviewFile.Update(reviewPath, "incomplete-smoke", current => TranslationReviewFile.Mark(current, "incomplete-smoke", ["other"], "cs",
            TranslationReviewState.Approved, null, latest.Fingerprints!));
        EditorReviewOperationResult conflict = await session.SaveReviewAsync(new EditorReviewSaveRequest(latest.Revision,
            [.. latest.Entries.Select(entry => entry.Key == "other" ? entry with { State = "translated", SourceFingerprint = latest.Fingerprints!["other"] } : entry)],
            latest.Terminology, latest.Entries, latest.Terminology)).ConfigureAwait(false);
        Require(!conflict.Ok && conflict.Message is { Code: "ui_backend_review_conflict" } message && message.Args.Single().Value == "other (cs)",
            "An entry changed on both sides was not refused: " + conflict.Message);
    }

    // An XLIFF import goes through the Tooling plan: a key the import adds to an existing group copies the group's
    // 4-space indentation, the approval lands in the review file with the per-unit fingerprint of the source the file
    // showed, and a unit whose source changed since export is imported as needs-review instead of refusing the file.
    private static async Task ImportedApprovalJourneyAsync(string container)
    {
        string project = Path.Combine(container, "workspace");
        await TranslationProjectWriter.CreateAsync(TranslationProjectScaffolder.Render(
            new TranslationProjectCreationRequest(project, "import-smoke", "en", "Smoke.Translations", "SmokeText",
                [new TranslationProjectLocale("de", "en")]))).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(project, "en.rmf2"), "title = Title\ncart {\n    empty = Empty\n    items = Items\n}\n").ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(project, "de.rmf2"), "cart {\n    empty = Leer\n}\n").ConfigureAwait(false);
        // The German translation starts incomplete, so the import adds keys.
        JsonObject manifest = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(project, "runic.json")).ConfigureAwait(false))!.AsObject();
        manifest["validation"] = new JsonObject { ["translationCompleteness"] = "allow" };
        await File.WriteAllTextAsync(Path.Combine(project, "runic.json"), manifest.ToJsonString()).ConfigureAwait(false);
        using var session = new EditorSession(project);
        EditorXliffExportResult export = await session.ExportXliffAsync("xliff").ConfigureAwait(false);
        Require(export.Ok, "The import-smoke export failed: " + export.Message?.Code + " " + export.Message?.Detail);
        string relative = export.Documents.Single().Path;
        XDocument xliff = XDocument.Load(Path.Combine(project, relative));
        foreach ((string key, string text, string state) in new[] { ("title", "Titel", "final"), ("cart_items", "Artikel", "translated") })
        {
            XElement segment = xliff.Descendants().Single(element => element.Name.LocalName == "unit" && element.Attribute("id")?.Value == key)
                .Elements().Single(element => element.Name.LocalName == "segment");
            segment.SetAttributeValue("state", state);
            segment.Elements().SingleOrDefault(element => element.Name.LocalName == "target")?.Remove();
            XElement source = segment.Elements().Single(element => element.Name.LocalName == "source");
            source.AddAfterSelf(new XElement(source.Name.Namespace + "target", text));
        }
        xliff.Save(Path.Combine(project, relative), SaveOptions.DisableFormatting);
        EditorXliffImportPlan plan = await session.PreviewXliffImportAsync(relative).ConfigureAwait(false);
        Require(plan.Ok && plan.ConfirmationToken is not null,
            "The import-smoke XLIFF import was not planned: " + string.Join("; ", plan.Refusals.Select(refusal => refusal.Code)));
        Require((await session.ApplyXliffImportAsync(plan.ConfirmationToken!).ConfigureAwait(false)).Ok, "The import-smoke XLIFF import was not applied.");
        string german = await File.ReadAllTextAsync(Path.Combine(project, "de.rmf2")).ConfigureAwait(false);
        Require(german.StartsWith("cart {\n    empty = Leer\n    items = Artikel\n}\n", StringComparison.Ordinal) && german.Contains("title = Titel\n", StringComparison.Ordinal),
            "The editor import did not copy the group's indentation: " + german);
        EditorReviewEntry imported = (await session.LoadAsync().ConfigureAwait(false)).Review!.Entries.Single(entry => entry.Key == "title" && entry.Locale == "de");
        Require(imported is { State: "approved", Stale: false } && File.Exists(Path.Combine(project, "import-smoke.review.json")),
            "The imported approval was not recorded in the review file for the current source.");

        async Task<string> SegmentAsync()
        {
            EditorXliffExportResult again = await session.ExportXliffAsync("xliff").ConfigureAwait(false);
            Require(again.Ok, "The import-smoke re-export failed.");
            return XDocument.Load(Path.Combine(project, again.Documents.Single().Path)).Descendants()
                .Single(element => element.Name.LocalName == "unit" && element.Attribute("id")?.Value == "title")
                .Elements().Single(element => element.Name.LocalName == "segment").ToString(SaveOptions.DisableFormatting);
        }
        Require((await SegmentAsync().ConfigureAwait(false)).Contains("state=\"final\"", StringComparison.Ordinal), "A current imported approval was not exported as final.");
        await File.WriteAllTextAsync(Path.Combine(project, "en.rmf2"), "title = Main title\ncart {\n    empty = Empty\n    items = Items\n}\n").ConfigureAwait(false);
        Require((await SegmentAsync().ConfigureAwait(false)).Contains("subState=\"runic:needs-review\"", StringComparison.Ordinal),
            "An imported approval whose base text changed was exported as current.");

        // The exported file still shows "Title"; after the source changed, its unit is imported as needs-review.
        EditorXliffImportPlan changedSource = await session.PreviewXliffImportAsync(relative).ConfigureAwait(false);
        Require(changedSource.Ok && changedSource.Refusals.Count == 0 &&
            changedSource.Changes.Any(change => change is { Key: "title", Kind: "state-change", StateAfter: "needs-review" }),
            "A unit whose source changed since export refused the import or was not demoted: " + string.Join("; ", changedSource.Refusals.Select(refusal => refusal.Code)));
    }

    // #225 P20: a review state the editor records keeps its base text beside the review file
    // (<catalog>.review-sources.json), in the form the source panel shows; once the source changes, the stale entry's
    // recorded text reaches the frontend (EditorReviewSnapshot.RecordedSources) to be compared with the current one, an
    // entry an older tool recorded without its text has none, and confirming records the new text.
    private static async Task StaleSourceJourneyAsync(string container)
    {
        string project = Path.Combine(container, "workspace");
        await TranslationProjectWriter.CreateAsync(TranslationProjectScaffolder.Render(
            new TranslationProjectCreationRequest(project, "stale-source-smoke", "en", "Smoke.Translations", "SmokeText",
                [new TranslationProjectLocale("de", "en")]))).ConfigureAwait(false);
        const string English = "actions {\n  save = Save all changes\n}\nwelcome =\n  .input {$name :string}\n  {{Welcome back, {$name}}}\n" +
            "files =\n  .input {$count :integer}\n  .match $count\n  one {{One file}}\n  * {{{$count} files}}\nother = Other\n";
        await File.WriteAllTextAsync(Path.Combine(project, "en.rmf2"), English).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(project, "de.rmf2"), "actions {\n  save = Alle Änderungen speichern\n}\nwelcome =\n  .input {$name :string}\n  {{Willkommen zurück, {$name}}}\n" +
            "files =\n  .input {$count :integer}\n  .match $count\n  one {{Eine Datei}}\n  * {{{$count} Dateien}}\nother = Andere\n").ConfigureAwait(false);
        using var session = new EditorSession(project);
        WorkspaceSnapshot opened = await session.LoadAsync().ConfigureAwait(false);
        EditorReviewSnapshot review = opened.Review!;
        Require(opened.Success && review.Fingerprints is not null && review.RecordedSources is null, "The stale-source fixture did not load without review data.");
        IReadOnlyDictionary<string, string> fingerprints = review.Fingerprints!;
        EditorReviewEntry Approve(string key, EditorReviewSnapshot from) => new(key, "de", "approved", null, from.Fingerprints![key], new Dictionary<string, string>());
        EditorReviewOperationResult saved = await session.SaveReviewAsync(new EditorReviewSaveRequest(review.Revision,
            [Approve("actions_save", review), Approve("welcome", review), Approve("files", review)], review.Terminology, review.Entries, review.Terminology)).ConfigureAwait(false);
        Require(saved.Ok, "The approvals were not saved: " + saved.Message?.Code);
        string reviewPath = Path.Combine(project, "stale-source-smoke.review.json");
        string sourcesPath = Path.Combine(project, "stale-source-smoke.review-sources.json");
        string reviewText = await File.ReadAllTextAsync(reviewPath).ConfigureAwait(false);
        Require(reviewText.Contains("runic.translations.interchange-review/2", StringComparison.Ordinal) && !reviewText.Contains("\"source\"", StringComparison.Ordinal),
            "The review file changed its format: " + reviewText);
        IReadOnlyDictionary<string, string> recorded = TranslationReviewSources.Read(reviewPath, "stale-source-smoke");
        Require(recorded.Count == 3 && TranslationReviewSources.Text(recorded, "actions_save", fingerprints["actions_save"]) == "Save all changes",
            "The approvals' base texts were not recorded beside the review file.");
        // The recorded text is the base message as the source panel shows it, also for a multi-line message.
        IReadOnlyList<EditorMessageEntry> baseEntries = opened.Documents.Single(document => document.Path == "en.rmf2").Entries ?? [];
        foreach (string key in new[] { "welcome", "files" })
            Require(TranslationReviewSources.Text(recorded, key, fingerprints[key]) == baseEntries.Single(entry => entry.Key == key).Content,
                $"The recorded text of {key} is not the base message's text as the editor shows it.");

        // An entry recorded without its text, as a 0.14 tool writes it.
        TranslationReviewFile.Update(reviewPath, "stale-source-smoke", current => TranslationReviewFile.Mark(current, "stale-source-smoke", ["other"], "de",
            TranslationReviewState.Approved, null, fingerprints));
        // The source changes: both entries are stale, and only the recorded one has its text.
        await File.WriteAllTextAsync(Path.Combine(project, "en.rmf2"), English.Replace("Save all changes", "Save the changes", StringComparison.Ordinal)
            .Replace("other = Other", "other = Others", StringComparison.Ordinal)).ConfigureAwait(false);
        EditorReviewSnapshot stale = (await session.LoadAsync().ConfigureAwait(false)).Review!;
        Require(stale.Entries.Where(static entry => entry.Stale).Select(static entry => entry.Key).Order(StringComparer.Ordinal).SequenceEqual(["actions_save", "other"]),
            "The changed sources did not make their entries stale.");
        Require(stale.RecordedSources is { Count: 1 } sources && sources["actions_save"].Single() is { } text &&
            text.Key == fingerprints["actions_save"] && text.Value == "Save all changes",
            "The stale entry's recorded text was not offered for the comparison, or an entry without one got one.");

        // Confirming the translation records it for the current source, with the current text.
        EditorReviewOperationResult confirmed = await session.SaveReviewAsync(new EditorReviewSaveRequest(stale.Revision,
            [.. stale.Entries.Select(entry => entry.Key == "actions_save" ? Approve("actions_save", stale) : entry)], stale.Terminology, stale.Entries, stale.Terminology)).ConfigureAwait(false);
        Require(confirmed.Ok && confirmed.Review!.Entries.Single(entry => entry.Key == "actions_save") is { Stale: false } &&
            confirmed.Review.RecordedSources is null,
            "Confirming did not record the current source: " + confirmed.Message?.Code);
        Require(TranslationReviewSources.Text(TranslationReviewSources.Read(reviewPath, "stale-source-smoke"), "actions_save", stale.Fingerprints!["actions_save"]) == "Save the changes",
            "Confirming did not record the current text.");
        Require(File.Exists(sourcesPath), "The companion file is missing.");
    }

    // Issue #280: a link above the workspace (macOS /tmp and /var, a linked ~/Development, a bundle's linked
    // ExampleWorkspace), or the opened folder being a link itself, does not refuse it: the workspace works from its
    // physical folder. A link inside the workspace, which a repository can plant, is still refused, and validate says
    // why in words instead of the notice code.
    private static async Task LinkedWorkspaceJourneyAsync(string container, IEditorCommandOperations commandLine)
    {
        string development = Path.Combine(container, "disk", "development");
        string project = Path.Combine(development, "translations");
        await TranslationProjectWriter.CreateAsync(TranslationProjectScaffolder.Render(
            new TranslationProjectCreationRequest(project, "linked-smoke", "en", "Smoke.Translations", "SmokeText",
                [new TranslationProjectLocale("de", "en")]))).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(project, "en.rmf2"), "application {\n  title = Title\n}\n").ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(project, "de.rmf2"), "application {\n  title = Titel\n}\n").ConfigureAwait(false);
        string linkedDevelopment = Path.Combine(container, "linked-development");
        string linkedProject = Path.Combine(container, "linked-translations");
        try
        {
            Directory.CreateSymbolicLink(linkedDevelopment, development);
            Directory.CreateSymbolicLink(linkedProject, project);
        }
        catch (Exception exception) when (OperatingSystem.IsWindows() && exception is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine("SKIP: linked workspaces; this Windows account cannot create symbolic links.");
            return;
        }
        // The container itself may be below a link (macOS $TMPDIR in /var), so compare with the physical folder.
        string physical = WorkspaceFiles.PhysicalPath(project);
        Require(!physical.StartsWith(Path.GetFullPath(linkedDevelopment), StringComparison.Ordinal), "The physical path kept a link.");

        // Through a linked parent folder, and with the opened folder itself a link: load, edit and save.
        int edits = 0;
        foreach (string opened in new[] { Path.Combine(linkedDevelopment, "translations"), linkedProject })
        {
            using var session = new EditorSession(opened);
            WorkspaceSnapshot loaded = await session.LoadAsync().ConfigureAwait(false);
            Require(loaded.Success && loaded.Root == physical && loaded.Documents.Any(document => document.Path == "de.rmf2"),
                $"The workspace opened through the link '{opened}' did not load from its physical folder.");
            EditorDocument german = loaded.Documents.Single(document => document.Path == "de.rmf2");
            string text = $"Titel {++edits}";
            EditorDocumentDraft draft = await session.TransformDocumentAsync(german.Path, german.Content, "application_title", text).ConfigureAwait(false);
            EditorOperationResult saved = await session.SaveAsync(german.Path, draft.Content, german.Revision).ConfigureAwait(false);
            Require(draft.Success && saved.Ok && File.ReadAllText(Path.Combine(project, "de.rmf2")).Contains(text, StringComparison.Ordinal),
                $"The workspace opened through the link '{opened}' did not save to its physical folder.");
        }
        CommandOutcome<EditorCommandResult> linkedValidation = await commandLine.ExecuteAsync(new EditorCommandRequest(
            "validate", [Path.Combine(linkedDevelopment, "translations")], null, false, false, false, CommandOutputMode.Json)).ConfigureAwait(false);
        Require(linkedValidation.Succeeded, "validate refused a workspace below a linked folder.");

        // A relative link whose target crosses another link resolves to the same physical folder; a cycle fails.
        string chained = Path.Combine(container, "chained");
        Directory.CreateSymbolicLink(chained, Path.Combine("linked-development", "translations"));
        Require(WorkspaceFiles.PhysicalPath(Path.Combine(chained, "de.rmf2")) == Path.Combine(physical, "de.rmf2"),
            "A relative link across another link did not resolve to the physical folder.");
        string loopA = Path.Combine(container, "loop-a");
        string loopB = Path.Combine(container, "loop-b");
        Directory.CreateSymbolicLink(loopA, loopB);
        Directory.CreateSymbolicLink(loopB, loopA);
        bool loopRefused = false;
        try { new EditorWorkspace(Path.Combine(loopA, "translations")).Dispose(); }
        catch (Exception exception) when (exception is IOException or EditorUserException) { loopRefused = true; }
        Require(loopRefused, "A cycle of links opened as a workspace.");

        // A link inside the workspace is still refused, opened through a linked parent or not.
        string outside = Path.Combine(container, "outside");
        Directory.CreateDirectory(outside);
        string planted = Path.Combine(project, "planted");
        Directory.CreateSymbolicLink(planted, outside);
        using (var session = new EditorSession(Path.Combine(linkedDevelopment, "translations")))
        {
            string? refusal = null;
            try { await session.LoadAsync().ConfigureAwait(false); }
            catch (EditorUserException exception) { refusal = exception.Notice.Code; }
            Require(refusal == "ui_backend_path_link", "A link inside a workspace below a linked folder was not refused: " + refusal);
            EditorOperationResult escaped = await session.SaveAsync("planted/de.rmf2", "application {\n  title = Titel\n}\n",
                EditorWorkspace.NewMf2DocumentRevision).ConfigureAwait(false);
            Require(!escaped.Ok && escaped.Message?.Code == "ui_backend_path_link" && !File.Exists(Path.Combine(outside, "de.rmf2")),
                "A save through a link inside the workspace was not refused.");
        }
        TextWriter standardError = Console.Error;
        var captured = new StringWriter();
        CommandOutcome<EditorCommandResult> refused;
        Console.SetError(captured);
        try
        {
            refused = await commandLine.ExecuteAsync(new EditorCommandRequest(
                "validate", [project], null, false, false, false, CommandOutputMode.Json)).ConfigureAwait(false);
        }
        finally
        {
            Console.SetError(standardError);
        }
        Require(!refused.Succeeded && captured.ToString().Contains("Validation could not start: Translation sources cannot be symbolic links.", StringComparison.Ordinal) &&
            !captured.ToString().Contains("ui_backend_path_link", StringComparison.Ordinal),
            "validate did not report the refusal's text: " + captured);
        Require(EditorNoticeText.Render(EditorNotice.Create("ui_backend_workspace_missing", ("path", "/nowhere"))).Contains("/nowhere", StringComparison.Ordinal) &&
            EditorNoticeText.Render(EditorNotice.External("disk full")).Contains("disk full", StringComparison.Ordinal) &&
            EditorNoticeText.Render(new EditorNotice("not_a_notice", [])) == "not_a_notice",
            "A notice's arguments or an unknown code did not render.");
        Directory.Delete(planted);
    }

    // commandLine is the command-line operations (Program.cs), which a frontend
    // bootstrap build does not compile.
    public static async Task<int> RunAsync(string workspacePath, IEditorCommandOperations commandLine)
    {
        string container = Path.Combine(Path.GetTempPath(), $"runic-editor-smoke-{Guid.NewGuid():N}");
        try
        {
            await WorkspacePickerJourneyAsync(Path.Combine(container, "picker")).ConfigureAwait(false);
            await WindowFolderDialogJourneyAsync(Path.Combine(container, "window-dialog")).ConfigureAwait(false);
            DesktopEntryJourney(Path.Combine(container, "desktop-entry"));
            await AuthoringJourneyAsync(Path.Combine(container, "authoring")).ConfigureAwait(false);
            await MessageDraftJourneyAsync(Path.Combine(container, "message-drafts")).ConfigureAwait(false);
            await MountedContextJourneyAsync(Path.Combine(container, "mounted-context")).ConfigureAwait(false);
            await PreviewFormattingJourneyAsync(Path.Combine(container, "preview-formatting")).ConfigureAwait(false);
            await LocaleChangeJourneyAsync(Path.Combine(container, "locale-change")).ConfigureAwait(false);
            await NestedMissingKeyJourneyAsync(Path.Combine(container, "nested-missing-key")).ConfigureAwait(false);
            await CatalogImportJourneyAsync(Path.Combine(container, "catalog-import")).ConfigureAwait(false);
            await MalformedManifestJourneyAsync(Path.Combine(container, "malformed-manifest")).ConfigureAwait(false);
            await PreviewKeepsExternalChangesJourneyAsync(Path.Combine(container, "preview-external")).ConfigureAwait(false);
            await InterchangeOutputJourneyAsync(Path.Combine(container, "interchange-output"), commandLine).ConfigureAwait(false);
            await LinkedWorkspaceJourneyAsync(Path.Combine(container, "linked-workspace"), commandLine).ConfigureAwait(false);
            await ReviewStampJourneyAsync(Path.Combine(container, "review-stamp")).ConfigureAwait(false);
            await IncompleteCatalogReviewJourneyAsync(Path.Combine(container, "incomplete-review")).ConfigureAwait(false);
            await ImportedApprovalJourneyAsync(Path.Combine(container, "imported-approval")).ConfigureAwait(false);
            await StaleSourceJourneyAsync(Path.Combine(container, "stale-source")).ConfigureAwait(false);
            await LibraryCatalogJourneyAsync(Path.Combine(container, "library-catalogs")).ConfigureAwait(false);
            await SpellingJourneyAsync(Path.Combine(container, "spelling")).ConfigureAwait(false);
            await CloseGuardJourneyAsync(Path.Combine(container, "close-guard")).ConfigureAwait(false);
            WindowPlacementJourney(Path.Combine(container, "window-placement"));
            string project = Path.Combine(container, "translations");
            // Smoke tests exercise an intentionally minimal workspace. They must
            // never mutate the packaged example or a caller-provided directory.
            await TranslationProjectWriter.CreateAsync(TranslationProjectScaffolder.Render(
                new TranslationProjectCreationRequest(project, "editor-smoke", "en", "Smoke.Translations", "SmokeText",
                    [new TranslationProjectLocale("de", "en")]))).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(project, "en.rmf2"), "application {\n  title = Title\n}\n").ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(project, "de.rmf2"), "application {\n  title = Titel\n}\n").ConfigureAwait(false);

            using var session = new EditorSession(project);
            WorkspaceSnapshot initial = await session.LoadAsync().ConfigureAwait(false);
            Require(initial.Success, "The RMF2 project did not load.");
            EditorDocument german = initial.Documents.Single(document => document.Path.EndsWith("de.rmf2", StringComparison.OrdinalIgnoreCase));
            EditorDocumentDraft draft = await session.TransformDocumentAsync(german.Path, german.Content, "application_title", "Titel 🦊").ConfigureAwait(false);
            Require(draft.Success, "The RMF2 message edit did not compile.");
            Require((await session.SaveAsync(german.Path, draft.Content, german.Revision).ConfigureAwait(false)).Ok, "The RMF2 document was not saved.");
            var mutation = new EditorMutationRequest("create-key", null, null, null, null, null, "application.subtitle", "Untertitel");
            EditorMutationPreview preview = session.PreviewMutation(mutation);
            Require(preview.Ok, $"The RMF2 mutation could not be prepared: {preview.Message?.Detail ?? preview.Message?.Code ?? "unknown error"}");
            EditorOperationResult applied = await session.ApplyMutationAsync(mutation with { ConfirmationToken = preview.ConfirmationToken }).ConfigureAwait(false);
            Require(applied.Ok && applied.Snapshot?.Documents.Any(document =>
                string.Equals(document.Locale, initial.Catalog!.DefaultLocale, StringComparison.OrdinalIgnoreCase) &&
                document.Entries?.Any(entry => entry.Key == "application_subtitle" && entry.Content == "Untertitel") == true) == true,
                "The RMF2 create-key mutation did not produce the expected resource.");

            // Exercise each generated feature owner with a real compiler-backed
            // session. The hosted browser test covers routed document editing.
            // The ViewModel runs in a window scope of the SDK's window test host, as OpenWindowAsync opens one.
            var windowServices = new ServiceCollection();
            windowServices.AddRunicReactiveModelContext();
            windowServices.AddRunicWindowService();
            windowServices.AddRunicViews();
            await using var windowProvider = windowServices.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            await using var windowScope = windowProvider.CreateAsyncScope();
            var windowLocale = windowScope.ServiceProvider.GetRequiredService<IRunicWindowLocale>();
            await using var windowPlatform = new RunicTestPlatform();
            using var windowSpelling = SmokeSpelling(Path.Combine(container, "window-spelling"), []);
            using (var editor = new EditorViewModel(new EditorSession(project),
                windowScope.ServiceProvider.GetRequiredService<IRunicModelContext>(),
                windowScope.ServiceProvider.GetRequiredService<ISequencer>(), windowLocale,
                windowScope.ServiceProvider.GetRequiredService<IRunicWindowContext>(), windowPlatform.FileDialogs, windowSpelling))
            using (var window = RunicWindowTestHost.Create(windowScope.ServiceProvider, editor))
            {
                // The page's language switch sets the window's locale, which reaches the page as a locale push; a
                // malformed tag leaves it unchanged.
                const string pageConnection = "editor-page";
                HelloWithTranslatedText(window.Transport, pageConnection);
                await Signal.ToTask(editor.SetUiLocaleCommand.Execute("de"), CancellationToken.None).ConfigureAwait(false);
                Require(windowLocale.Locale == "de" && editor.UiLocale == "de" && window.Content.Locale == "de",
                    "The interface language did not set the window's locale.");
                Require(await TakeLocalePushAsync(window.Transport, pageConnection).ConfigureAwait(false) == "de",
                    "The window did not push the interface language to the page.");
                await Signal.ToTask(editor.SetUiLocaleCommand.Execute("not a tag"), CancellationToken.None).ConfigureAwait(false);
                Require(windowLocale.Locale == "de" && window.Content.Locale == "de",
                    "A malformed interface language changed the window's locale.");
                await Signal.ToTask(editor.SetUiLocaleCommand.Execute("en"), CancellationToken.None).ConfigureAwait(false);
                Require(await TakeLocalePushAsync(window.Transport, pageConnection).ConfigureAwait(false) == "en",
                    "A malformed interface language was pushed, or the next switch was not.");
                // A reloaded page connects again and is told the window's locale after its hello.
                HelloWithTranslatedText(window.Transport, pageConnection + "-reloaded");
                Require(await TakeLocalePushAsync(window.Transport, pageConnection + "-reloaded").ConfigureAwait(false) == "en",
                    "A reloaded page was not told the window's locale.");
                await ExecuteRouteAsync(editor.Workspace.LoadCommand, () => editor.Workspace.LoadResultJson, "{}");
                WorkspaceSnapshot loaded = editor.Workspace.LastLoad
                    ?? throw new InvalidOperationException("The workspace ViewModel did not publish a typed snapshot.");
                Require(loaded.Success && editor.Documents.Count > 0,
                    "The workspace ViewModel did not publish its typed snapshot and routed documents.");
                EditorDocument routedGerman = loaded.Documents.Single(document => document.Path == german.Path);
                await ExecuteRouteAsync(editor.DocumentTools.TransformDocumentCommand,
                    () => editor.DocumentTools.TransformDocumentResultJson,
                    EditorJsonText.Write(writer =>
                    {
                        writer.WriteStartObject();
                        writer.WriteString("path", routedGerman.Path);
                        writer.WriteString("content", routedGerman.Content);
                        writer.WriteString("key", "application_title");
                        writer.WriteString("value", "ViewModel edit");
                        writer.WriteEndObject();
                    }));
                Require(editor.DocumentTools.LastTransformDocument?.Success == true,
                    "The document tools ViewModel did not publish its typed draft.");
                var review = new EditorReviewSaveRequest(loaded.Review?.Revision,
                    loaded.Review?.Entries ?? [], loaded.Review?.Terminology ?? []);
                await ExecuteRouteAsync(editor.Review.SaveReviewCommand, () => editor.Review.SaveReviewResultJson,
                    JsonSerializer.Serialize(review, EditorJsonContext.Default.EditorReviewSaveRequest));
                Require(editor.Review.LastSaveReview?.Ok == true,
                    "The review ViewModel did not save through the compiler-backed session.");
                await ExecuteRouteAsync(editor.Interchange.ExportReviewJsonCommand,
                    () => editor.Interchange.ExportReviewJsonResultJson, "{}");
                Require(editor.Interchange.LastExportReviewJson?.Ok == true,
                    "The interchange ViewModel did not export review state.");
                await ExecuteRouteAsync(editor.Diagnostics.AboutCommand, () => editor.Diagnostics.AboutResultJson, "{}");
                // The spelling operations through their generated routes (D074.10).
                await ExecuteRouteAsync(editor.Spelling.SpellingStatusCommand, () => editor.Spelling.SpellingStatusResultJson,
                    "{\"locales\":[\"en\"],\"automaticDownload\":false}");
                Require(editor.Spelling.SpellingStatusResultJson.Contains("\"state\":\"ready\"", StringComparison.Ordinal),
                    "The routed spelling status did not report English: " + editor.Spelling.SpellingStatusResultJson);
                await ExecuteRouteAsync(editor.Spelling.CheckSpellingCommand, () => editor.Spelling.CheckSpellingResultJson,
                    "{\"locale\":\"en\",\"texts\":[{\"id\":\"t\",\"text\":\"Teh {$name}\"}]}");
                Require(editor.Spelling.CheckSpellingResultJson.Contains("\"word\":\"Teh\"", StringComparison.Ordinal),
                    "The routed spelling check did not mark the word: " + editor.Spelling.CheckSpellingResultJson);
                await ExecuteRouteAsync(editor.Spelling.SuggestSpellingCommand, () => editor.Spelling.SuggestSpellingResultJson,
                    "{\"locale\":\"en\",\"word\":\"Teh\"}");
                await ExecuteRouteAsync(editor.Spelling.DownloadSpellingDictionaryCommand, () => editor.Spelling.DownloadSpellingDictionaryResultJson,
                    "{\"locale\":\"en\"}");
                await ExecuteRouteAsync(editor.Spelling.AddSpellingWordCommand, () => editor.Spelling.AddSpellingWordResultJson,
                    "{\"word\":\"Runic\"}");
                Require(editor.Diagnostics.LastAbout?.Product is not null,
                    "The diagnostics ViewModel did not publish its typed product description.");
                await ExecuteRouteAsync(editor.LocalState.LoadLocalStateCommand,
                    () => editor.LocalState.LoadLocalStateResultJson, "{}");
                Require(editor.LocalState.LastLoadLocalState is not null,
                    "The local state ViewModel did not publish its typed snapshot.");
                var newProject = new EditorProjectCreationRequest(Path.Combine(container, "preview-project"),
                    "preview-project", "en", [], "Smoke.Translations", "SmokeText", false);
                await ExecuteRouteAsync(editor.Project.PreviewProjectCommand,
                    () => editor.Project.PreviewProjectResultJson,
                    JsonSerializer.Serialize(newProject, EditorJsonContext.Default.EditorProjectCreationRequest));
                Require(editor.Project.LastPreviewProject?.Ok == true,
                    "The project ViewModel did not publish its typed plan.");
            }

            string mountedRoot = Path.Combine(container, "mounted-direct");
            string mountedProject = Path.Combine(mountedRoot, "translations");
            string mountedFeature = Path.Combine(mountedRoot, "feature");
            Directory.CreateDirectory(mountedProject);
            Directory.CreateDirectory(Path.Combine(mountedFeature, "en"));
            await File.WriteAllTextAsync(Path.Combine(mountedProject, "runic.json"),
                "{\"schemaVersion\":1,\"catalog\":\"mounted-direct\",\"code\":{\"namespace\":\"Smoke.Translations\",\"className\":\"SmokeText\"},\"baseLocale\":\"en\",\"locales\":[{\"tag\":\"en\"},{\"tag\":\"de\",\"fallback\":\"en\"}],\"validation\":{\"translationCompleteness\":\"allow\"},\"sourceRoots\":[{\"path\":\"../feature\",\"namespace\":[\"shop\"]}]}\n").ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(mountedFeature, "en", "greeting.mf2"), "Hello\n").ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(mountedFeature, "en", "farewell.mf2"), "Goodbye\n").ConfigureAwait(false);

            using var mountedSession = new EditorSession(mountedRoot);
            WorkspaceSnapshot mounted = await mountedSession.LoadAsync().ConfigureAwait(false);
            EditorDocument[] mountedGreeting = mounted.Documents.Where(document => document.Path.EndsWith("greeting.mf2", StringComparison.OrdinalIgnoreCase)).ToArray();
            Require(mounted.Success && mountedGreeting.Length == 1 && mountedGreeting[0].Locale == "en" &&
                mountedGreeting[0].Entries?.Single().Key == "shop_greeting",
                "The editor did not preserve compiler identity for external mounted direct MF2 resources.");
            EditorDocumentDraft mountedGerman = await mountedSession.TransformDocumentAsync(
                "feature/de/greeting.mf2", "Hallo\n", "shop_greeting", "Hallo").ConfigureAwait(false);
            Require(mountedGerman.Success && mountedGerman.Entries.Single().Key == "shop_greeting",
                "The editor did not resolve a synthesized direct document against its external source root.");
            EditorOperationResult mountedSaved = await mountedSession.SaveAsync(
                "feature/de/greeting.mf2", mountedGerman.Content, EditorWorkspace.NewMf2DocumentRevision).ConfigureAwait(false);
            WorkspaceSnapshot mountedReloaded = await mountedSession.LoadAsync().ConfigureAwait(false);
            Require(mountedSaved.Ok && mountedReloaded.Documents.Single(document => document.Path == "feature/de/greeting.mf2").Locale == "de",
                "The editor did not save the synthesized locale beside its canonical mounted source.");

            EditorXliffExportResult xliffExport = await mountedSession.ExportXliffAsync("xliff").ConfigureAwait(false);
            Require(xliffExport.Ok && xliffExport.Documents.Count == 1,
                "The editor did not export the mounted direct MF2 project.");
            string xliffPath = Path.Combine(mountedRoot, xliffExport.Documents.Single().Path);
            XDocument xliff = XDocument.Load(xliffPath);
            XElement greetingUnit = xliff.Descendants().Single(element =>
                element.Name.LocalName == "unit" && element.Attribute("id")?.Value == "shop_greeting");
            greetingUnit.Descendants().Single(element => element.Name.LocalName == "target").Value = "Guten Tag";
            XElement farewellUnit = xliff.Descendants().Single(element =>
                element.Name.LocalName == "unit" && element.Attribute("id")?.Value == "shop_farewell");
            XElement farewellSource = farewellUnit.Descendants().Single(element => element.Name.LocalName == "source");
            farewellSource.AddAfterSelf(new XElement(farewellSource.Name.Namespace + "target", "Auf Wiedersehen"));
            xliff.Save(xliffPath, SaveOptions.DisableFormatting);
            // An import planned while the manifest's source layout is unusable
            // (a whitespace mount path compiles with no sources) is refused as a
            // failed compilation, not planned against an empty project.
            string mountedManifest = Path.Combine(mountedProject, "runic.json");
            string mountedValid = await File.ReadAllTextAsync(mountedManifest).ConfigureAwait(false);
            await File.WriteAllTextAsync(mountedManifest, mountedValid.Replace("\"path\":\"../feature\"", "\"path\":\"   \"", StringComparison.Ordinal)).ConfigureAwait(false);
            EditorXliffImportPlan brokenImport = await mountedSession.PreviewXliffImportAsync(xliffExport.Documents.Single().Path).ConfigureAwait(false);
            Require(!brokenImport.Ok && brokenImport.ConfirmationToken is null && brokenImport.Refusals.Any(refusal => refusal.Code == "EDITOR-COMPILE"),
                "The editor planned an XLIFF import against a manifest with an unusable source root.");
            await File.WriteAllTextAsync(mountedManifest, mountedValid).ConfigureAwait(false);
            EditorXliffImportPlan xliffPreview = await mountedSession.PreviewXliffImportAsync(xliffExport.Documents.Single().Path).ConfigureAwait(false);
            Require(xliffPreview.Ok && xliffPreview.ChangedCount == 1 && xliffPreview.AddedCount == 1 && xliffPreview.ConfirmationToken is not null,
                "The editor did not plan the mounted direct MF2 XLIFF import.");
            EditorOperationResult xliffApplied = await mountedSession.ApplyXliffImportAsync(xliffPreview.ConfirmationToken!).ConfigureAwait(false);
            Require(xliffApplied.Ok && File.ReadAllText(Path.Combine(mountedFeature, "de", "greeting.mf2")).TrimEnd() == "Guten Tag" &&
                File.ReadAllText(Path.Combine(mountedFeature, "de", "farewell.mf2")).TrimEnd() == "Auf Wiedersehen" &&
                !File.Exists(Path.Combine(mountedProject, "de", "shop_greeting.mf2")) &&
                !File.Exists(Path.Combine(mountedProject, "de", "shop_farewell.mf2")),
                "The mounted direct MF2 XLIFF import wrote outside the canonical source root or changed the physical filename.");

            var rename = new EditorMutationRequest("rename-key", null, null, null, null, "shop_greeting", "shop.salutation", null);
            EditorMutationPreview renamePreview = mountedSession.PreviewMutation(rename);
            Require(renamePreview.Ok && renamePreview.Files.Any(file => file.Path == "feature/de/salutation.mf2"),
                "The editor did not plan mounted direct MF2 mutation beside the matched source root.");
            EditorOperationResult renamed = await mountedSession.ApplyMutationAsync(rename with { ConfirmationToken = renamePreview.ConfirmationToken }).ConfigureAwait(false);
            EditorDocument[] renamedSalutation = renamed.Snapshot?.Documents.Where(document => document.Path.EndsWith("salutation.mf2", StringComparison.OrdinalIgnoreCase)).ToArray() ?? [];
            Require(renamed.Ok && renamedSalutation.Length == 2 && renamedSalutation
                .All(document => document.Locale is "en" or "de" && document.Entries?.Single().Key == "shop_salutation"),
                "The editor did not apply compiler-consistent mounted direct MF2 mutation.");

            Console.WriteLine("PASS: editor feature ViewModels and grouped/mounted RMF2 v5 authoring journeys.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAIL: {exception}");
            return 1;
        }
        finally
        {
            try { if (Directory.Exists(container)) Directory.Delete(container, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    // Issue #225, P19: the editor window asks its page before it closes, through the user's close (the close button,
    // Alt+F4, the window manager) and the page's own Close window command alike. The page holds the unsaved work and
    // answers at once; a page that does not answer keeps the window open.
    private static async Task CloseGuardJourneyAsync(string project)
    {
        await TranslationProjectWriter.CreateAsync(TranslationProjectScaffolder.Render(
            new TranslationProjectCreationRequest(project, "close-guard", "en", "Smoke.Translations", "SmokeText",
                [new TranslationProjectLocale("de", "en")]))).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(project, "en.rmf2"), "title = Title\n").ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(project, "de.rmf2"), "title = Titel\n").ConfigureAwait(false);

        // No page has mounted its handler (it is loading, or gone): nothing unsaved can be in it, so the window closes.
        await using (var services = EditorServices.Create(project))
        await using (var window = await RunicWindowTestHost.CreateWindowAsync<EditorWindow>(services).ConfigureAwait(false))
        {
            Require(await window.RequestUserCloseAsync().ConfigureAwait(false) && !window.IsOpen,
                "The editor window did not close without a page to ask.");
        }

        await using (var services = EditorServices.Create(project))
        await using (var window = await RunicWindowTestHost.CreateWindowAsync<EditorWindow>(services).ConfigureAwait(false))
        {
            var editor = window.Root<EditorViewModel>();
            using var page = await editor.MountPresentationAsync().ConfigureAwait(false);
            int asked = 0;
            bool clean = false;
            // The page answers whether it may close now: with unsaved work it keeps the window open and shows its own
            // Save, Discard and Cancel question; Cancel leaves it so.
            page.Handle(vm => vm.ConfirmClose, (EditorCloseQuestion _) => { asked++; return clean; });
            Require(!await window.RequestUserCloseAsync().ConfigureAwait(false) && window.IsOpen && asked == 1,
                "The user's close did not ask the page, or closed the window with unsaved work.");
            // The application's own close (the page's Close window command, RequestCloseAsync) asks the same guard.
            (await editor.ExecuteAsync(vm => vm.CloseWindowCommand).ConfigureAwait(false)).ThrowIfFailed();
            Require(window.IsOpen && asked == 2, "The Close window command did not ask the page, or closed the window.");
            // Save or Discard leaves nothing unsaved; the page then closes the window, and the guard's question is
            // answered at once.
            clean = true;
            Require(await window.RequestUserCloseAsync().ConfigureAwait(false) && !window.IsOpen && asked == 3,
                "The editor window did not close once the page had nothing unsaved.");
        }

        // A page that does not answer in time keeps the window open; the user can close it again.
        await using (var services = EditorServices.Create(project, TimeSpan.FromMilliseconds(300)))
        await using (var window = await RunicWindowTestHost.CreateWindowAsync<EditorWindow>(services,
            hostOptions: new RunicWindowTestHostOptions { TimeProvider = TimeProvider.System }).ConfigureAwait(false))
        {
            using var page = await window.Root<EditorViewModel>().MountPresentationAsync().ConfigureAwait(false);
            bool answers = false;
            await page.HandleAsync<EditorCloseQuestion, bool>(vm => vm.ConfirmClose, async (_, cancellationToken) =>
            {
                if (!answers) await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                return true;
            }).ConfigureAwait(false);
            Require(!await window.RequestUserCloseAsync().ConfigureAwait(false) && window.IsOpen,
                "A page that did not answer in time let the editor window close.");
            answers = true;
            Require(await window.RequestUserCloseAsync().ConfigureAwait(false) && !window.IsOpen,
                "The editor window did not close once the page answered.");
        }
    }

    // Issue #225, P24: the window's placement round-trips through the native editor state under one key, next to the
    // page's entries, and a stored value this version cannot read is ignored.
    private static void WindowPlacementJourney(string directory)
    {
        string? previous = EditorLocalStateStore.PathOverride;
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "editor-state-v1.json");
        EditorLocalStateStore.PathOverride = path;
        try
        {
            Require(EditorWindowPlacements.Read() is null, "A placement was read without a native editor state.");
            EditorLocalStateStore.Save([new EditorLocalStateEntry("runic-translations.theme-mode", "dark")]);
            var maximized = new Runic.Desktop.DesktopWindowPlacement { Width = 1280, Height = 800, X = 40, Y = 60, Maximized = true };
            Require(EditorWindowPlacements.Remember(maximized), "The window placement was not stored.");
            EditorLocalStateSnapshot stored = EditorLocalStateStore.Load();
            Require(stored.Entries.Count == 2 && stored.Entries.Any(entry => entry.Key == "runic-translations.theme-mode" && entry.Value == "dark"),
                "Storing the window placement changed the page's entries.");
            Require(EditorWindowPlacements.Read() is { Width: 1280, Height: 800, X: 40, Y: 60, Maximized: true, FullScreen: false },
                "The window placement did not round-trip through the native editor state.");
            // GTK 4 on Wayland reports no position: the placement keeps the size and maximized state only.
            Require(EditorWindowPlacements.Remember(new Runic.Desktop.DesktopWindowPlacement { Width = 1440, Height = 900 }) &&
                EditorWindowPlacements.Read() is { Width: 1440, Height: 900, X: null, Y: null, Maximized: false },
                "A placement without a position did not round-trip.");
            // The page writes its whole projection back, the placement included (EditorLocalStateStore.Save validates it).
            EditorLocalStateStore.Save(EditorLocalStateStore.Load().Entries);
            Require(EditorWindowPlacements.Read() is { Width: 1440 }, "The page's write did not keep the window placement.");
            // A window that never reported a size has nothing to restore.
            Require(!EditorWindowPlacements.Remember(new Runic.Desktop.DesktopWindowPlacement { Width = 0, Height = 0 }) &&
                EditorWindowPlacements.Read() is { Width: 1440 }, "An empty placement replaced the stored one.");
            // Values another version wrote, or that are invalid, are ignored: the window opens at its usual size.
            foreach (string invalid in new[]
            {
                "not json",
                "[]",
                "{\"version\":2,\"width\":800,\"height\":600,\"maximized\":false,\"fullScreen\":false}",
                "{\"width\":800,\"height\":600,\"maximized\":false,\"fullScreen\":false}",
                "{\"version\":1,\"width\":-5,\"height\":600,\"maximized\":false,\"fullScreen\":false}",
                "{\"version\":1,\"width\":800,\"height\":600,\"x\":10,\"maximized\":false,\"fullScreen\":false}",
                "{\"version\":1,\"width\":800,\"height\":600,\"maximized\":\"yes\",\"fullScreen\":false}",
                "{\"version\":1,\"width\":800,\"height\":600,\"maximized\":false,\"fullScreen\":false,\"origin\":\"Middle\"}",
            })
            {
                EditorLocalStateStore.Set(EditorWindowPlacements.EditorWindowKey, invalid);
                Require(EditorWindowPlacements.Read() is null, $"An invalid stored placement was used: {invalid}");
            }
            // An unreadable record is left for the page, which quarantines it and says so; the placement is not stored.
            File.WriteAllText(path, "{ broken");
            Require(EditorWindowPlacements.Read() is null && !EditorWindowPlacements.Remember(maximized) &&
                File.ReadAllText(path) == "{ broken", "The window placement replaced an unreadable native editor state.");
        }
        finally
        {
            EditorLocalStateStore.PathOverride = previous;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static async Task ExecuteRouteAsync(ReactiveCommand<string, RxVoid> command,
        Func<string> response, string argumentJson)
    {
        string requestId = Guid.NewGuid().ToString("N");
        string request = "{\"requestId\":" + JsonSerializer.Serialize(requestId, EditorJsonContext.Default.String)
            + ",\"argument\":" + argumentJson + "}";
        await Signal.ToTask(command.Execute(request), CancellationToken.None).ConfigureAwait(false);
        using var result = JsonDocument.Parse(response());
        Require(result.RootElement.GetProperty("requestId").GetString() == requestId,
            "The routed feature result was not correlated with its command.");
        Require(result.RootElement.TryGetProperty("result", out _),
            "The routed feature did not publish a result.");
    }

    // A page connection that, like @runic-artifex/views 0.16, follows the window's locale (translation-text/1).
    private static void HelloWithTranslatedText(InMemoryBridgeTransport transport, string connection)
    {
        string reply = transport.Request(
            $$"""{"kind":"hello","protocol":4,"session":"session-{{connection}}","client":{"name":"editor-smoke","version":"1.0.0"},"features":["translation-text/1"]}""",
            connection, $"client-{connection}");
        Require(reply.StartsWith("{\"ok\":true", StringComparison.Ordinal), $"The window refused the page's hello: {reply}");
    }

    // The locale of the next locale push to the connection; other pushes are dropped.
    private static async Task<string?> TakeLocalePushAsync(InMemoryBridgeTransport transport, string connection)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            foreach (RunicBridgeMessage message in transport.TakeMessages())
            {
                if (message.ConnectionKey != connection || message.Kind != "locale") continue;
                using var push = JsonDocument.Parse(message.Json);
                return push.RootElement.GetProperty("locale").GetString();
            }
            await Task.Delay(10).ConfigureAwait(false);
        }
        throw new InvalidOperationException("The window did not push a locale to the page.");
    }

    // The workspace folder dialog is the window's (IFileDialogs, owned by the editor window); only a window without a
    // native owner, as in serve, falls back to the platform's dialogs without one. Checked with scripted dialogs.
    private static async Task WorkspacePickerJourneyAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        string chosen = Path.GetFullPath(directory);
        await using var window = new RunicTestPlatform();
        await using var fallback = new RunicTestPlatform();
        int fallbacks = 0;
        using var picker = new EditorWorkspacePicker(window.FileDialogs, () => { fallbacks++; return fallback.FileDialogs; });

        window.FileDialogs.EnqueueOpenDirectory(chosen);
        Require(await picker.PickAsync(CancellationToken.None).ConfigureAwait(false) is { Ok: true, Cancelled: false, Message: null } selected &&
            selected.Directory == chosen, "The window's folder dialog did not choose the workspace.");
        Require(window.FileDialogs.Calls is [{ Kind: RunicFileDialogKind.OpenDirectory, DirectoryOptions.OwnerPolicy: OwnerPolicy.PreferOwner }] &&
            fallbacks == 0, "The workspace was not chosen through the window's own folder dialog.");

        window.FileDialogs.EnqueueDismissed();
        Require(await picker.PickAsync(CancellationToken.None).ConfigureAwait(false) is { Ok: false, Cancelled: true, Message: null },
            "A dismissed folder dialog was not reported as cancelled.");

        window.FileDialogs.EnqueueFailed(PlatformFailureCode.ResourceBusy);
        Require(await picker.PickAsync(CancellationToken.None).ConfigureAwait(false) is
            { Ok: false, Cancelled: false, Message: { Code: "ui_backend_picker_failed" } busy } && busy.Args.Single() is { Name: "reason", Value: "ResourceBusy" },
            "A folder dialog that failed was not reported with its reason.");
        EditorWorkspacePickerResult failed = EditorWorkspacePicker.Outcome(new PickerResult<IDirectoryLease>.Failed(
            PlatformFailureCode.PermissionDenied, PlatformDiagnostic.FromErrno(13)));
        Require(failed is { Ok: false, Cancelled: false, Message: { Code: "ui_backend_picker_failed" } notice } &&
            notice.Args.Single() is { Name: "reason", Value: "PermissionDenied" } && notice.Detail?.StartsWith("errno 13", StringComparison.Ordinal) == true,
            $"A folder the picker could not open was not reported with its reason: {failed.Message}");

        // A window whose dialogs cannot show (no FileChooser portal, no session bus) has no other dialog to try: the page
        // asks for the path instead.
        window.FileDialogs.EnqueueUnavailable(PlatformUnavailableReason.BackendUnavailable);
        Require(await picker.PickAsync(CancellationToken.None).ConfigureAwait(false) is
            { Ok: false, Cancelled: false, Message.Code: "ui_backend_picker_unavailable" } && fallbacks == 0,
            "A window without folder dialogs was not reported as unavailable, or fell back to dialogs without an owner.");

        // serve: the window has no native owner, so the dialog shows without one.
        window.FileDialogs.EnqueueUnavailable(PlatformUnavailableReason.OwnerUnavailable);
        fallback.FileDialogs.EnqueueOpenDirectory(chosen);
        Require(await picker.PickAsync(CancellationToken.None).ConfigureAwait(false) is { Ok: true } served && served.Directory == chosen &&
            fallbacks == 1 && fallback.FileDialogs.Calls is [{ Kind: RunicFileDialogKind.OpenDirectory, DirectoryOptions.OwnerPolicy: OwnerPolicy.PreferOwner }],
            "A window without a native owner did not fall back to the folder dialog without an owner.");
        using var withoutFallback = new EditorWorkspacePicker(window.FileDialogs, static () => null);
        window.FileDialogs.EnqueueUnavailable(PlatformUnavailableReason.OwnerUnavailable);
        Require(await withoutFallback.PickAsync(CancellationToken.None).ConfigureAwait(false) is
            { Ok: false, Cancelled: false, Message.Code: "ui_backend_picker_unavailable" },
            "Without dialogs without an owner, a window without one was not reported as unavailable.");
    }

    // desktop-entry install|uninstall writes the Linux desktop entry and hicolor icon under the data directory it is
    // given, replaces only its own entry, and removes what it wrote.
    private static void DesktopEntryJourney(string dataHome)
    {
        const string id = "eu.runicartifex.TranslationsEditor";
        string icon = Path.Combine(dataHome, "source", "icon.png");
        Directory.CreateDirectory(Path.GetDirectoryName(icon)!);
        // The smallest square PNG header the installer reads its size from: 64×64.
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R', 0, 0, 0, 64, 0, 0, 0, 64, 8, 6, 0, 0, 0];
        File.WriteAllBytes(icon, png);
        var entry = new EditorDesktopEntry(id, dataHome);
        string program = Path.Combine(dataHome, "Editor Build", "Runic.Translations.Editor");
        IReadOnlyList<string> written = entry.Install([program], "Runic Translations Editor", "An editor.", icon);
        string desktop = Path.Combine(dataHome, "applications", id + ".desktop");
        string installedIcon = Path.Combine(dataHome, "icons", "hicolor", "64x64", "apps", id + ".png");
        Require(written.SequenceEqual([desktop, installedIcon]) && File.ReadAllBytes(installedIcon).SequenceEqual(png),
            "The desktop entry and icon were not written where the desktop looks for them.");
        string[] lines = File.ReadAllLines(desktop);
        Require(lines[0] == "[Desktop Entry]" && lines.Contains("Type=Application") && lines.Contains("Name=Runic Translations Editor") &&
            lines.Contains($"Exec=\"{program}\" %f") && lines.Contains($"TryExec={program}") && lines.Contains($"Icon={id}") &&
            lines.Contains($"StartupWMClass={id}") && lines.Contains(EditorDesktopEntry.MarkerKey + "=true"),
            "The desktop entry does not name the editor, its icon and its app ID: " + string.Join(" | ", lines));
        Require(EditorDesktopEntry.QuoteArgument("/opt/a$b%c") == "\"/opt/a\\$b%%c\"" && EditorDesktopEntry.EscapeString("\\") == "\\\\",
            "Exec arguments are not quoted by the Desktop Entry Specification.");

        // A second install replaces the editor's own files, also when the icon changed size.
        png[19] = 128; png[23] = 128;
        File.WriteAllBytes(icon, png);
        entry.Install([program], "Runic Translations Editor", "An editor.", icon);
        Require(!File.Exists(installedIcon) && File.Exists(Path.Combine(dataHome, "icons", "hicolor", "128x128", "apps", id + ".png")),
            "Installing again left the previous icon size behind.");
        Require(entry.Uninstall().Count == 2 && !File.Exists(desktop) && entry.Uninstall().Count == 0,
            "Uninstalling did not remove exactly what the install wrote.");

        // Another desktop entry of that name, such as a package's, is neither replaced nor removed.
        Directory.CreateDirectory(Path.GetDirectoryName(desktop)!);
        File.WriteAllText(desktop, "[Desktop Entry]\nType=Application\nName=Packaged\nExec=editor\n");
        static bool Refused(Action action)
        {
            try { action(); return false; }
            catch (EditorDesktopEntryException) { return true; }
        }
        Require(Refused(() => entry.Install([program], "n", "c", icon)) && Refused(() => entry.Uninstall()) &&
            File.ReadAllText(desktop).Contains("Name=Packaged", StringComparison.Ordinal),
            "A desktop entry the editor did not write was replaced or removed.");
        Require(EditorDesktopEntry.PngSize("not a png"u8) is null, "A file that is not a PNG was installed as the icon.");
    }

    // The editor's window scope (EditorServices) gives the workspace picker the window's dialogs.
    private static async Task WindowFolderDialogJourneyAsync(string project)
    {
        await TranslationProjectWriter.CreateAsync(TranslationProjectScaffolder.Render(
            new TranslationProjectCreationRequest(project, "window-dialog", "en", "Smoke.Translations", "SmokeText",
                [new TranslationProjectLocale("de", "en")]))).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(project, "en.rmf2"), "title = Title\n").ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(project, "de.rmf2"), "title = Titel\n").ConfigureAwait(false);
        string chosen = Path.GetFullPath(project);
        await using var services = EditorServices.Create(project, configure: static services => services.AddRunicTestPlatform());
        await using var window = await RunicWindowTestHost.CreateWindowAsync<EditorWindow>(services).ConfigureAwait(false);
        RunicTestPlatform platform = window.Services.GetRequiredService<RunicTestPlatform>();
        platform.FileDialogs.EnqueueOpenDirectory(chosen);
        var editor = (EditorViewModel)window.ViewModel;
        await ExecuteRouteAsync(editor.Workspace.PickWorkspaceCommand, () => editor.Workspace.PickWorkspaceResultJson, "{}");
        Require(editor.Workspace.LastPickWorkspace is { Ok: true } picked && picked.Directory == chosen &&
            platform.FileDialogs.Calls is [{ Kind: RunicFileDialogKind.OpenDirectory }],
            $"Browse did not use the editor window's folder dialog: {editor.Workspace.PickWorkspaceResultJson}");
    }
}

// The smoke test's own JSON, source-generated so that it also runs in a NativeAOT build of the editor, as its packages are.
[JsonSerializable(typeof(IReadOnlyDictionary<string, string>))]
[JsonSerializable(typeof(IReadOnlyList<string>))]
[JsonSerializable(typeof(IEnumerable<string>))]
internal sealed partial class EditorSmokeJsonContext : JsonSerializerContext;
