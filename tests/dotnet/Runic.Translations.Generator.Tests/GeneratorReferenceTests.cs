using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using TUnit.Core;

namespace Runic.Translations.Generator.Tests;

/// <summary>Generated <c>Class.References</c> factories (L3-1) and documentation comments (L4-7).</summary>
internal sealed class GeneratorReferenceTests
{
    private const string Project = """
        {
          "schemaVersion": 1,
          "catalog": "notes",
          "code": { "namespace": "Example.Notes", "className": "NotesText"FALLBACK },
          "baseLocale": "en",
          "locales": [ "en", "de" ]
        }
        """;
    private const string English = """
        # Shown on the toolbar.
        title = Notes

        # Shown when a note title is too long.
        @param $max - The longest allowed title, in characters.
        title_too_long = The title is longer than {$max :integer} characters.

        saved = {$name} saved {$size :number} on {$day :date} at {$time :time} ({$when :datetime}, {$id :runic:uuid}, {$ok :runic:boolean}, {$tags :runic:list})
        help = Read the {#link ref=guide}guide{/link}.
        References = Clash
        pair = {$first} and {$second}
        """;
    private const string German = """
        title = Notizen
        title_too_long = Der Titel ist länger als {$max :integer} Zeichen.
        saved = {$name} hat {$size :number} am {$day :date} um {$time :time} gespeichert ({$when :datetime}, {$id :runic:uuid}, {$ok :runic:boolean}, {$tags :runic:list})
        help = Lies die {#link ref=guide}Anleitung{/link}.
        References = Konflikt
        pair = {$first} und {$second}
        """;

    private const string Probe = """
        namespace Example.Consumer;
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Text.Json;
        using Example.Notes;
        using Runic.Translations;

        public static class Probe
        {
            public static string Run()
            {
                TranslationReference reference = NotesText.References.title_too_long(max: 80);
                Require(reference.Catalog == NotesTextCatalog.CatalogId && reference.ContractFingerprint == NotesTextCatalog.ContractFingerprint, "catalog");
                Require(reference.Key == "title_too_long" && reference.Arguments["max"].Type == TextArgumentType.Int && reference.Arguments["max"].Value == "80", "arguments");
                TranslationReference saved = NotesText.References.saved(day: new DateOnly(2026, 11, 4), id: Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"),
                    name: "Ada", ok: true, size: -0.0m, tags: new[] { "a", "b" }, time: new TimeOnly(9, 30, 15, 250), when: new DateTimeOffset(2026, 11, 4, 10, 0, 1, 500, TimeSpan.FromHours(1)));
                string json = JsonSerializer.Serialize(saved, TranslationReferenceJsonContext.Default.TranslationReference);
                TranslationReference parsed = TranslationReference.Parse(json);
                string wire = string.Join(",", new[] { "day", "id", "name", "ok", "size", "time", "when" }.Select(name => name + "=" + parsed.Arguments[name].Value)) +
                    ",tags=" + string.Join("+", parsed.Arguments["tags"].Items!);
                ITranslationManager manager = NotesTextCatalog.CreateManager("de");
                var resolved = TranslationReferenceResolver.Resolve(manager.Current, NotesTextCatalog.ContractFingerprint, reference);
                var stale = TranslationReferenceResolver.Resolve(manager.Current, NotesTextCatalog.ContractFingerprint,
                    new TranslationReference(reference.Catalog, reference.ContractFingerprint, "removed_key", reference.Arguments, reference.FallbackText));
                return string.Join("\n", reference.FallbackText, NotesText.References.title().FallbackText, NotesText.References.r_5265666572656e636573().Key,
                    wire, resolved.Text, stale.Outcome + ": " + stale.Text);
            }

            private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        }
        """;

    [Test, DisplayName("reference factories carry typed canonical arguments, the fingerprint and the base locale's text")]
    public void ReferenceFactories()
    {
        GeneratorRun run = GeneratorTestHost.RunWithConsumer(Probe, Inputs(""));
        AssertClean(run);
        Assert.Equal(string.Join("\n",
            "The title is longer than 80 characters.",
            "Notes",
            "References",
            "day=2026-11-04,id=0f8fad5b-d9cb-469f-a165-70867728950e,name=Ada,ok=true,size=0,time=09:30:15,when=2026-11-04T09:00:01Z,tags=a+b",
            "Der Titel ist länger als 80 Zeichen.",
            "Fallback: The title is longer than 80 characters."), Execute(run), "reference execution");
        string accessors = Source(run, "NotesText.Accessors.g.cs");
        // Structured messages cannot resolve as text, so they get no factory.
        Assert.True(!accessors.Contains("TranslationReference @help(", StringComparison.Ordinal), "a structured message has a factory");
    }

    [Test, DisplayName("code.referenceFallbackText false leaves reference factories without fallback text")]
    public void ReferenceFallbackOptOut()
    {
        const string probe = """
            namespace Example.Consumer;
            using Example.Notes;
            public static class Probe
            {
                public static string Run() => (NotesText.References.title_too_long(max: 3).FallbackText ?? "none") + "|" + NotesText.References.title().Key;
            }
            """;
        GeneratorRun run = GeneratorTestHost.RunWithConsumer(probe, Inputs(", \"referenceFallbackText\": false"));
        AssertClean(run);
        Assert.Equal("none|title", Execute(run), "opt-out execution");
        Assert.True(!Source(run, "NotesText.Accessors.g.cs").Contains("__referenceSnapshot", StringComparison.Ordinal), "opt-out still formats fallback text");
    }

    // The fallback text repeats the arguments' text. Arguments that are valid alone must not fail because of it.
    [Test, DisplayName("reference factories leave out fallback text that would break a transport limit")]
    public void ReferenceFallbackLimits()
    {
        const string probe = """
            namespace Example.Consumer;
            using System;
            using Example.Notes;
            using Runic.Translations;
            public static class Probe
            {
                public static string Run()
                {
                    // 32,768 characters of arguments and as many of fallback text: over MaximumTotalTextLength (65,536).
                    TranslationReference text = NotesText.References.pair(first: new string('a', 16384), second: new string('b', 16384));
                    // 44,000 characters in all, but control characters are six bytes each in JSON: over MaximumEncodedLength.
                    TranslationReference encoded = NotesText.References.pair(first: new string('\u0001', 11000), second: new string('\u0002', 11000));
                    TranslationReference small = NotesText.References.pair(first: "a", second: "b");
                    TranslationReference parsed = TranslationReference.Parse(System.Text.Json.JsonSerializer.Serialize(text, TranslationReferenceJsonContext.Default.TranslationReference));
                    bool tooLong = false;
                    try { NotesText.References.pair(first: new string('a', 16385), second: "b"); } catch (ArgumentOutOfRangeException) { tooLong = true; }
                    return string.Join("|", text.FallbackText ?? "none", text.Arguments["first"].Value.Length, encoded.FallbackText ?? "none",
                        encoded.Arguments["second"].Value.Length, small.FallbackText, parsed.Arguments["second"].Value.Length, tooLong);
                }
            }
            """;
        GeneratorRun run = GeneratorTestHost.RunWithConsumer(probe, Inputs(""));
        AssertClean(run);
        Assert.Equal("none|16384|none|11000|a and b|16384|True", Execute(run), "limit execution");
    }

    // Key ToString is reserved, so its factory keeps the encoded name r_546f537472696e67, which is also a valid key.
    [Test, DisplayName("a reference factory whose readable name is another factory's encoded name keeps its encoded name")]
    public void ReferenceNameClash()
    {
        const string probe = """
            namespace Example.Consumer;
            using Example.Notes;
            public static class Probe
            {
                public static string Run() => NotesText.References.r_546f537472696e67().Key + "|" + NotesText.References.r_725f35343666353337343732363936653637().Key +
                    "|" + NotesText.References.plain().Key;
            }
            """;
        TestInput[] inputs =
        [
            new("C:/repo/translations/runic.json", "Project", Project.Replace("FALLBACK", "", StringComparison.Ordinal)),
            new("C:/repo/translations/en.rmf2", "Rmf2", "ToString = One\nr_546f537472696e67 = Two\nplain = Three\n"),
            new("C:/repo/translations/de.rmf2", "Rmf2", "ToString = Eins\nr_546f537472696e67 = Zwei\nplain = Drei\n"),
        ];
        GeneratorRun run = GeneratorTestHost.RunWithConsumer(probe, inputs);
        string[] warnings = run.SingleResult.Diagnostics.Select(static item => item.Id + ": " + item.GetMessage(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        Assert.True(warnings.Any(static item => item.StartsWith("RTR0069: Reference factory name 'r_546f537472696e67' is the encoded name of message 'ToString'", StringComparison.Ordinal)),
            "no clash warning: " + string.Join("\n", warnings));
        Diagnostic[] problems = run.Compilation.GetDiagnostics().Where(static item => item.Severity >= DiagnosticSeverity.Warning).ToArray();
        Assert.Equal(0, problems.Length, string.Join("\n", problems.Select(static item => item.ToString())));
        Assert.Equal("ToString|r_546f537472696e67|plain", Execute(run), "clash execution");
    }

    [Test, DisplayName("generated members document the base text, the description and @param descriptions")]
    public void DocumentationComments()
    {
        GeneratorRun run = GeneratorTestHost.Run(Inputs(""));
        Assert.Equal(0, run.SingleResult.Diagnostics.Length, string.Join("\n", run.SingleResult.Diagnostics));
        string readable = Source(run, "NotesText.Readable.g.cs"), accessors = Source(run, "NotesText.Accessors.g.cs");
        const string summary = "/// <summary>The title is longer than {$max :integer} characters. — Shown when a note title is too long.</summary>";
        foreach (string source in new[] { readable, accessors })
        {
            Assert.True(source.Contains(summary, StringComparison.Ordinal), summary);
            Assert.True(source.Contains("/// <remarks>Formats <c>title_too_long</c>.</remarks>", StringComparison.Ordinal), "key remarks");
            Assert.True(source.Contains("/// <summary>Notes — Shown on the toolbar.</summary>", StringComparison.Ordinal), "title summary");
        }
        Assert.True(readable.Contains("/// <param name=\"max\">The longest allowed title, in characters.</param>", StringComparison.Ordinal), "readable @param");
        Assert.True(accessors.Contains("/// <param name=\"max\">The longest allowed title, in characters.</param>", StringComparison.Ordinal), "reference @param");
        Assert.True(readable.Contains("/// <param name=\"name\">Input <c>name</c>.</param>", StringComparison.Ordinal), "undocumented input");
    }

    private static TestInput[] Inputs(string fallback) =>
    [
        new("C:/repo/translations/runic.json", "Project", Project.Replace("FALLBACK", fallback, StringComparison.Ordinal)),
        new("C:/repo/translations/en.rmf2", "Rmf2", English),
        new("C:/repo/translations/de.rmf2", "Rmf2", German),
    ];

    private static string Source(GeneratorRun run, string hint) =>
        run.SingleResult.GeneratedSources.Single(source => source.HintName.EndsWith(hint, StringComparison.Ordinal)).SourceText.ToString();

    private static void AssertClean(GeneratorRun run)
    {
        Assert.Equal(0, run.SingleResult.Diagnostics.Length, string.Join("\n", run.SingleResult.Diagnostics));
        Diagnostic[] problems = run.Compilation.GetDiagnostics().Where(static item => item.Severity >= DiagnosticSeverity.Warning).ToArray();
        Assert.Equal(0, problems.Length, string.Join("\n", problems.Select(static item => item.ToString())));
    }

    private static string Execute(GeneratorRun run)
    {
        using var stream = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = run.Compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        Assembly assembly = Assembly.Load(stream.ToArray());
        MethodInfo method = assembly.GetType("Example.Consumer.Probe", throwOnError: true)!.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;
        return (string)method.Invoke(null, null)!;
    }
}
