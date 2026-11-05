using System.Collections.Generic;
using System.Linq;
using System.Text;
using TUnit.Core;

namespace Runic.Translations.Compiler.Tests;

/// <summary>The QA warnings RTR0094-RTR0098 and their <c>validation</c> policies, project-wide and per locale.</summary>
[Category("rmf2-semantic-v5")]
internal sealed class Rmf2QualityCheckTests
{
    private const string Plural = "items =\n  .input {$n :integer}\n  .match $n\n  one {{One item}}\n  * {{{$n} items}}";
    private const string DecimalPlural = "items =\n  .input {$n :number}\n  .match $n\n  one {{One item}}\n  * {{{$n} items}}";

    private static TranslationSource Source(string path, string text) => new(path, Encoding.UTF8.GetBytes(text));

    private static Rmf2ProjectCompilation Compile(string config, params (string Locale, string Text)[] files) =>
        TranslationCompiler.CompileRmf2ProjectV5(
            Source("translations/runic.json", "{\"schemaVersion\":1,\"catalog\":\"app\",\"code\":{\"namespace\":\"Example\",\"className\":\"AppText\"},\"baseLocale\":\"en\"" + config + "}"),
            files.Select(file => Source("translations/" + file.Locale + ".rmf2", file.Text)).ToArray());

    private static string All(Rmf2ProjectCompilation result) => string.Join("\n", result.Diagnostics.Select(d => d.Id + " " + d.Severity + " " + d.Location.Path + ":" + d.Location.Line + ":" + d.Location.Column + " " + d.Message));

    private static TranslationDiagnostic Single(Rmf2ProjectCompilation result, string id)
    {
        TranslationDiagnostic[] found = result.Diagnostics.Where(d => d.Id == id).ToArray();
        Assert.True(found.Length == 1, "Expected one " + id + ":\n" + All(result));
        return found[0];
    }

    private static void None(Rmf2ProjectCompilation result, params string[] ids) =>
        Assert.True(!result.Diagnostics.Any(d => ids.Contains(d.Id)), "Unexpected " + string.Join("/", ids) + ":\n" + All(result));

    [Test, DisplayName("RTR0094 reports a plural key the locale never selects, at the key")]
    public void UnreachableCategory()
    {
        var result = Compile(",\"locales\":[\"en\",\"de\"]",
            ("en", Plural),
            ("de", "items =\n  .input {$n :integer}\n  .match $n\n  one {{Ein Element}}\n  few {{Wenige}}\n  * {{{$n} Elemente}}"));
        Assert.True(result.Success, All(result));
        TranslationDiagnostic diagnostic = Single(result, "RTR0094");
        Assert.Equal(TranslationDiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("Variant key 'few' of 'items' is never selected in locale 'de', whose plural categories are one, other.", diagnostic.Message);
        Assert.Equal("translations/de.rmf2", diagnostic.Location.Path);
        Assert.Equal(5, diagnostic.Location.Line);
        Assert.Equal(3, diagnostic.Location.Column);
        None(result, "RTR0095");

        // Ordinal selection uses the ordinal categories: English has one, two, few, other.
        var ordinal = Compile("", ("en", "place =\n  .input {$n :integer select=ordinal}\n  .match $n\n  one {{{$n}st}}\n  two {{{$n}nd}}\n  few {{{$n}rd}}\n  many {{?}}\n  * {{{$n}th}}"));
        Assert.Equal("Variant key 'many' of 'place' is never selected in locale 'en', whose ordinal categories are one, two, few, other.", Single(ordinal, "RTR0094").Message);
        None(ordinal, "RTR0095");
    }

    [Test, DisplayName("RTR0095 reports categories a locale needs that fall to the catch-all")]
    public void MissingCategories()
    {
        var result = Compile(",\"locales\":[\"en\",\"cs\"]",
            ("en", Plural),
            ("cs", "items =\n  .input {$n :integer}\n  .match $n\n  one {{Jedna položka}}\n  * {{{$n} položek}}"));
        Assert.True(result.Success, All(result));
        TranslationDiagnostic diagnostic = Single(result, "RTR0095");
        // `:integer` never has fraction digits, and Czech `many` is for fractions only.
        Assert.Equal("'items' has no variant for the plural category few of locale 'cs'; it falls to the '*' variant.", diagnostic.Message);
        Assert.Equal("translations/cs.rmf2", diagnostic.Location.Path);
        Assert.Equal(1, diagnostic.Location.Line);
        // A decimal selector can reach `many` (1.5).
        Assert.Equal("'items' has no variant for the plural categories few, many of locale 'cs'; they fall to the '*' variant.",
            Single(Compile(",\"locales\":[\"en\",\"cs\"]", ("en", DecimalPlural),
                ("cs", "items =\n  .input {$n :number}\n  .match $n\n  one {{Jedna položka}}\n  * {{{$n} položek}}")), "RTR0095").Message);

        // Exact keys only: the author chose not to select by category.
        None(Compile(",\"locales\":[\"en\",\"cs\"]", ("en", Plural),
            ("cs", "items =\n  .input {$n :integer}\n  .match $n\n  0 {{Žádné}}\n  * {{{$n} položek}}")), "RTR0094", "RTR0095");
        // A complete Czech integer set and the English base report nothing.
        None(Compile(",\"locales\":[\"en\",\"cs\"]", ("en", Plural),
            ("cs", "items =\n  .input {$n :integer}\n  .match $n\n  one {{Jedna}}\n  few {{{$n} položky}}\n  * {{{$n} položek}}")), "RTR0094", "RTR0095");
        // `many` under `:integer` can never be selected in Czech.
        var unreachable = Compile(",\"locales\":[\"en\",\"cs\"]", ("en", Plural),
            ("cs", "items =\n  .input {$n :integer}\n  .match $n\n  one {{Jedna}}\n  few {{{$n} položky}}\n  many {{{$n} položky}}\n  * {{{$n} položek}}"));
        Assert.Equal("Variant key 'many' of 'items' is never selected in locale 'cs', whose plural categories for integers are one, few, other.", Single(unreachable, "RTR0094").Message);
        None(unreachable, "RTR0095");
        // The same set is complete for a decimal selector.
        None(Compile(",\"locales\":[\"en\",\"cs\"]", ("en", DecimalPlural),
            ("cs", "items =\n  .input {$n :number}\n  .match $n\n  one {{Jedna}}\n  few {{{$n} položky}}\n  many {{{$n} položky}}\n  * {{{$n} položek}}")), "RTR0094", "RTR0095");
        // French integers reach `many` (1000000), so it is still expected under `:integer`.
        Assert.Equal("'items' has no variant for the plural category many of locale 'fr'; it falls to the '*' variant.",
            Single(Compile(",\"locales\":[\"en\",\"fr\"]", ("en", Plural),
                ("fr", "items =\n  .input {$n :integer}\n  .match $n\n  one {{Un élément}}\n  * {{{$n} éléments}}")), "RTR0095").Message);
        // A string selector is not checked.
        None(Compile("", ("en", "state =\n  .input {$s :string}\n  .match $s\n  few {{Few}}\n  * {{Other}}")), "RTR0094", "RTR0095");
    }

    [Test, DisplayName("integer plural categories are evaluated from every generated CLDR rule")]
    public void IntegerCategories()
    {
        string rules = TranslationCapabilityRegistry.EsmPluralRulesSource;
        foreach (System.Text.RegularExpressions.Match rule in System.Text.RegularExpressions.Regex.Matches(rules, "const pluralRule\\d+ = \\(o\\) => \\{(.*?)\\};", System.Text.RegularExpressions.RegexOptions.Singleline))
            Assert.True(PluralRuleReachability.IntegerCategories(rule.Groups[1].Value) is { Count: > 0 }, "The evaluator does not accept " + rule.Value);
        string Of(string locale, bool ordinal = false) => string.Join(",", Rmf2AuthoringService.PluralCategories(locale, ordinal, integer: true));
        Assert.Equal("one,few,other", Of("cs"));
        Assert.Equal("one,few,many,other", string.Join(",", Rmf2AuthoringService.PluralCategories("cs", ordinal: false)));
        Assert.Equal("one,many,other", Of("fr"));
        Assert.Equal("one,many,other", Of("es-MX"));
        Assert.Equal("one,many,other", Of("it"));
        Assert.Equal("one,other", Of("de"));
        Assert.Equal("one,other", Of("da"));
        Assert.Equal("other", Of("ja"));
        Assert.Equal("one,two,few,other", Of("en", ordinal: true));
        Assert.Equal("many,other", Of("it", ordinal: true));
    }

    [Test, DisplayName("RTR0096 reports an input the base displays and no variant of the translation displays")]
    public void InputParity()
    {
        const string locales = ",\"locales\":[\"en\",\"de\"]";
        var dropped = Compile(locales, ("en", "greeting = Hello {$name}, you have {$count :integer} messages"), ("de", "greeting = Hallo, Sie haben Nachrichten"));
        Assert.True(dropped.Success, All(dropped));
        TranslationDiagnostic diagnostic = Single(dropped, "RTR0096");
        Assert.Equal("The 'de' translation of 'greeting' displays none of $count, $name, which base locale 'en' displays.", diagnostic.Message);
        Assert.Equal("translations/de.rmf2", diagnostic.Location.Path);

        // The union over variants counts, and a local displays the input it reads.
        None(Compile(locales, ("en", Plural), ("de", "items =\n  .input {$n :integer}\n  .match $n\n  one {{Ein Element}}\n  * {{{$n} Elemente}}")), "RTR0096");
        None(Compile(locales, ("en", "count = {$n :integer} items"), ("de", "count =\n  .local $shown = {$n :integer}\n  {{{$shown} Elemente}}")), "RTR0096");
        // An input the base only selects on is not displayed, so a translation need not display it.
        None(Compile(locales, ("en", "items =\n  .input {$n :integer}\n  .match $n\n  one {{One item}}\n  * {{Items}}"), ("de", "items = Elemente")), "RTR0096");
        Assert.Equal("The 'de' translation of 'count' displays no $n, which base locale 'en' displays.",
            Single(Compile(locales, ("en", "count = {$n :integer} items"), ("de", "count = Elemente")), "RTR0096").Message);
    }

    [Test, DisplayName("RTR0097 reports a boolean formatted as text")]
    public void FormattedBoolean()
    {
        var formatted = Compile("", ("en", "flag = Flag {$on :runic:boolean}"));
        Assert.True(formatted.Success, All(formatted));
        Assert.Equal("'flag' in locale 'en' formats the boolean $on as text, which reads 'true' or 'false' in every locale; select on it with .match and write the words in the variants.",
            Single(formatted, "RTR0097").Message);
        // Through a declaration without a function at the placeholder.
        Single(Compile("", ("en", "flag =\n  .input {$on :runic:boolean}\n  {{Flag {$on}}}")), "RTR0097");
        // Selecting on it is the recommended form.
        None(Compile("", ("en", "flag =\n  .input {$on :runic:boolean}\n  .match $on\n  true {{On}}\n  * {{Off}}")), "RTR0097");
    }

    [Test, DisplayName("RTR0098 checks @maxLength per locale; RTR0051 rejects an invalid value")]
    public void MaxLength()
    {
        const string locales = ",\"locales\":[\"en\",\"de\"]";
        var result = Compile(locales, ("en", "@maxLength 10\nsave = Save {$name}"), ("de", "save = Speichern unter {$name}"));
        Assert.True(result.Success, All(result));
        TranslationDiagnostic diagnostic = Single(result, "RTR0098");
        Assert.Equal("The 'de' text of 'save' is 16 characters long, more than its @maxLength of 10; placeholder values are not counted.", diagnostic.Message);
        Assert.Equal("translations/de.rmf2", diagnostic.Location.Path);
        // Code points after NFC: "é" written as e + combining accent counts once; the base fits.
        None(Compile("", ("en", "@maxLength 4\ncafe = Café")), "RTR0098");
        // The longest variant counts.
        Assert.True(Single(Compile("", ("en", "@maxLength 7\n" + Plural)), "RTR0098").Message.Contains("in its longest variant", System.StringComparison.Ordinal), "The variant wording is missing.");
        // A translation can lower the base limit, not raise it: a larger one is reported and the base limit applies.
        var raised = Compile(locales, ("en", "@maxLength 10\nsave = Save"), ("de", "@maxLength 20\nsave = Speichern unter"));
        Assert.True(raised.Success, All(raised));
        TranslationDiagnostic ignored = Single(raised, "RTR0051");
        Assert.Equal(TranslationDiagnosticSeverity.Warning, ignored.Severity);
        Assert.Equal("The 'de' @maxLength 20 of 'save' is larger than the limit of 10 in base locale 'en'; a translation can only lower it, so 10 applies.", ignored.Message);
        Assert.Equal("translations/de.rmf2", ignored.Location.Path);
        Assert.True(Single(raised, "RTR0098").Message.EndsWith("more than its @maxLength of 10; placeholder values are not counted.", System.StringComparison.Ordinal), All(raised));
        Assert.True(Single(Compile(locales, ("en", "@maxLength 20\nsave = Save"), ("de", "@maxLength 5\nsave = Speichern")), "RTR0098").Message.Contains("@maxLength of 5", System.StringComparison.Ordinal), "The lower translation limit does not apply.");
        // Without a base limit, the translation's own applies.
        Single(Compile(locales, ("en", "save = Save"), ("de", "@maxLength 5\nsave = Speichern")), "RTR0098");

        foreach (string invalid in new[] { "@maxLength\nx = X", "@maxLength 0\nx = X", "@maxLength -3\nx = X", "@maxLength ten\nx = X", "@maxLength 4\n@maxLength 5\nx = X" })
        {
            var bad = Compile("", ("en", invalid));
            Assert.True(!bad.Success && bad.Diagnostics.Any(d => d.Id == "RTR0051" && d.Severity == TranslationDiagnosticSeverity.Error), "Accepted " + invalid + ":\n" + All(bad));
        }
        // A known property: no "unknown metadata" warning.
        None(Compile("", ("en", "@maxLength 40\nx = X")), "RTR0051");
    }

    [Test, DisplayName("The QA policies are warnings by default and follow the project and locale validation")]
    public void Policies()
    {
        const string czech = "items =\n  .input {$n :integer}\n  .match $n\n  one {{Jedna}}\n  * {{Položek}}";
        var defaults = Compile(",\"locales\":[\"en\",\"cs\"]", ("en", Plural), ("cs", czech));
        Assert.True(defaults.Success, All(defaults));
        Assert.Equal(TranslationDiagnosticSeverity.Warning, Single(defaults, "RTR0095").Severity);
        Assert.Equal(TranslationDiagnosticSeverity.Warning, Single(defaults, "RTR0096").Severity);

        var errors = Compile(",\"locales\":[\"en\",\"cs\"],\"validation\":{\"pluralCategories\":\"error\",\"inputParity\":\"allow\"}", ("en", Plural), ("cs", czech));
        Assert.True(!errors.Success && Single(errors, "RTR0095").Severity == TranslationDiagnosticSeverity.Error, All(errors));
        None(errors, "RTR0096");

        // A locale entry's validation overrides the project's for that locale only.
        string perLocale = ",\"locales\":[\"en\",{\"tag\":\"cs\",\"fallback\":\"en\",\"validation\":{\"pluralCategories\":\"allow\",\"translationCompleteness\":\"warning\"}},\"de\"]," +
            "\"validation\":{\"pluralCategories\":\"error\"}";
        var overridden = Compile(perLocale, ("en", Plural + "\nother = Other"), ("cs", czech), ("de", "items =\n  .input {$n :integer}\n  .match $n\n  one {{Ein}}\n  * {{{$n}}}\nother = Andere"));
        Assert.True(overridden.Success, All(overridden));
        None(overridden, "RTR0095");
        Assert.Equal(TranslationDiagnosticSeverity.Warning, Single(overridden, "RTR0010").Severity);

        var completeness = Compile(",\"locales\":[\"en\",{\"tag\":\"cs\",\"fallback\":\"en\"},{\"tag\":\"de\",\"fallback\":\"en\",\"validation\":{\"translationCompleteness\":\"allow\"}}]",
            ("en", "a = A\nb = B"), ("cs", "a = A"), ("de", "a = A"));
        Assert.True(!completeness.Success, All(completeness));
        TranslationDiagnostic missing = Single(completeness, "RTR0010");
        Assert.True(missing.Message.Contains("'cs'", System.StringComparison.Ordinal) && missing.Severity == TranslationDiagnosticSeverity.Error, All(completeness));

        foreach (string invalid in new[] { "{\"tag\":\"cs\",\"fallback\":\"en\",\"validation\":{\"pluralCategories\":\"loud\"}}", "{\"tag\":\"cs\",\"fallback\":\"en\",\"validation\":{\"unknown\":\"error\"}}", "{\"tag\":\"cs\",\"fallback\":\"en\",\"validation\":[]}" })
        {
            var bad = Compile(",\"locales\":[\"en\"," + invalid + "]", ("en", "a = A"), ("cs", "a = A"));
            Assert.True(!bad.Success && bad.Diagnostics.Any(d => d.Id == "RTR0019"), "Accepted " + invalid + ":\n" + All(bad));
        }
    }
}
