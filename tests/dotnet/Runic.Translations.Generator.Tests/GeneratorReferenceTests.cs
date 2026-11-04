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
        """;
    private const string German = """
        title = Notizen
        title_too_long = Der Titel ist länger als {$max :integer} Zeichen.
        saved = {$name} hat {$size :number} am {$day :date} um {$time :time} gespeichert ({$when :datetime}, {$id :runic:uuid}, {$ok :runic:boolean}, {$tags :runic:list})
        help = Lies die {#link ref=guide}Anleitung{/link}.
        References = Konflikt
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
        Assert.True(!Source(run, "NotesText.Accessors.g.cs").Contains("__ReferenceFallbackText", StringComparison.Ordinal), "opt-out still formats fallback text");
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
