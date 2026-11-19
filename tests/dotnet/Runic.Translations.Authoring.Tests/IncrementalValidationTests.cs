using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using Runic.Translations.Compiler;
using Runic.Translations.Compiler.Generation;
using TUnit.Core;

namespace Runic.Translations.Authoring.Tests;

// Incremental validation (docs/plans/incremental-validation.md) against full compiles of the same sources.
internal sealed class IncrementalValidationTests
{
    private static readonly string[] Locales = ["en", "de", "fr", "pl"];

    [Test, DisplayName("Incremental validation equals a full compile after random edit sequences")]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    [Arguments(8)]
    public void RandomEdits(int seed)
    {
        var random = new Random(seed);
        var catalog = new RandomCatalog(random);
        var session = new TranslationValidationSession();
        int compiled = 0, diagnosed = 0;
        string last = "";
        for (int step = 0; step < 60; step++)
        {
            string edit = step == 0 ? "initial" : catalog.Edit();
            TranslationSource project = catalog.Project();
            TranslationSource[] sources = catalog.Sources();
            TranslationValidationSnapshot snapshot = session.Validate(project, sources);
            Rmf2ProjectCompilation full = TranslationCompiler.CompileRmf2ProjectV5(project, catalog.Sources());
            string context = $"seed {seed}, step {step} ({edit})";
            AssertSame(full, snapshot.Compilation, context);
            if (full.Project is not null) compiled++;
            if (full.Diagnostics.Count != 0) diagnosed++;
            last = string.Join(" ", full.Diagnostics.Where(diagnostic => diagnostic.Severity == TranslationDiagnosticSeverity.Error).Select(diagnostic => diagnostic.Id).Distinct());
            Assert.Equal(3L * step + 1, snapshot.Revision, context + ": revision");
            // A relaxed and a base-locale-only compile (the review compiles) through the same session.
            foreach (var options in new[] { new TranslationCompilerOptions { RelaxedValidation = true }, new TranslationCompilerOptions { RelaxedValidation = true, BaseLocaleOnly = true } })
                AssertSame(TranslationCompiler.CompileRmf2ProjectV5(project, sources, options), session.Validate(project, sources, options, false).Compilation, context + (options.BaseLocaleOnly ? ", base locale only" : ", relaxed"));
        }
        Assert.True(compiled > 0 && diagnosed > 0, $"the sequence covers compiled catalogs ({compiled}) and diagnostics ({diagnosed}); errors of the last step: {last}");
    }

    [Test, DisplayName("An edit to one file recompiles that file's messages and reuses the others")]
    public void ReusesUnchangedFiles()
    {
        var catalog = new RandomCatalog(new Random(42), errors: false);
        var session = new TranslationValidationSession();
        TranslationValidationSnapshot first = session.Validate(catalog.Project(), catalog.Sources());
        Assert.Equal(first.Statistics.Files, first.Statistics.FilesRead, "the first validation reads every file");
        Assert.Equal(catalog.Sources().Length, first.ChangedPaths.Count, "every source is new in the first validation");

        TranslationValidationSnapshot same = session.Validate(catalog.Project(), catalog.Sources());
        Assert.Equal(0, same.Statistics.FilesRead, "unchanged sources are not read again");
        Assert.Equal(0, same.Statistics.MessagesCompiled, "unchanged messages are not compiled again");
        Assert.Equal(0, same.Statistics.PairsChecked, "unchanged translations are not checked again");
        Assert.Equal(0, same.ChangedPaths.Count, "nothing changed");

        string path = catalog.ChangeMessage(translation: true);
        TranslationValidationSnapshot edited = session.Validate(catalog.Project(), catalog.Sources());
        AssertSame(TranslationCompiler.CompileRmf2ProjectV5(catalog.Project(), catalog.Sources()), edited.Compilation, "after the edit");
        Assert.Equal(1, edited.Statistics.FilesRead, "only the edited file is read");
        Assert.True(edited.Statistics.MessagesCompiled <= catalog.MessagesIn(path), $"only the edited file's messages are compiled ({edited.Statistics.MessagesCompiled})");
        Assert.True(edited.Statistics.PairsChecked <= catalog.MessagesIn(path), $"only the edited file's translations are checked ({edited.Statistics.PairsChecked})");
        Assert.Equal(path, edited.ChangedPaths.Single(), "the changed path");
    }

    [Test, DisplayName("A cancelled incremental validation leaves the session valid")]
    public void Cancellation()
    {
        var catalog = new RandomCatalog(new Random(7));
        var session = new TranslationValidationSession();
        session.Validate(catalog.Project(), catalog.Sources());
        for (int step = 0; step < 10; step++)
        {
            catalog.Edit();
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            Assert.Throws<OperationCanceledException>(() => session.Validate(catalog.Project(), catalog.Sources(), cancelled.Token), "");
            AssertSame(TranslationCompiler.CompileRmf2ProjectV5(catalog.Project(), catalog.Sources()), session.Validate(catalog.Project(), catalog.Sources()).Compilation, $"after cancellation {step}");
        }
    }

    [Test, DisplayName("Overlapping incremental validations each equal a full compile")]
    public void Concurrent()
    {
        var catalog = new RandomCatalog(new Random(11));
        var session = new TranslationValidationSession();
        var states = new List<(TranslationSource Project, TranslationSource[] Sources)>();
        for (int step = 0; step < 12; step++) { catalog.Edit(); states.Add((catalog.Project(), catalog.Sources())); }
        var results = new Rmf2ProjectCompilation[states.Count];
        System.Threading.Tasks.Parallel.For(0, states.Count, index => results[index] = session.Validate(states[index].Project, states[index].Sources).Compilation);
        for (int index = 0; index < states.Count; index++)
            AssertSame(TranslationCompiler.CompileRmf2ProjectV5(states[index].Project, states[index].Sources), results[index], $"state {index}");
    }

    [Test, DisplayName("A workspace from a cache validates through its project's session")]
    public void WorkspaceUsesSession()
    {
        var catalog = new RandomCatalog(new Random(3));
        var cache = new Rmf2WorkspaceCache();
        for (int step = 0; step < 8; step++)
        {
            if (step > 0) catalog.Edit();
            Rmf2Workspace workspace = cache.Create("/", catalog.Project(), catalog.Sources());
            AssertSame(TranslationCompiler.CompileRmf2ProjectV5(catalog.Project(), catalog.Sources()), workspace.Validate(), $"step {step}");
            Assert.True(workspace.Session is not null, "the workspace has a session");
        }
    }

    // The compilations are equal: every diagnostic in order and, when they compiled, the project.
    private static void AssertSame(Rmf2ProjectCompilation expected, Rmf2ProjectCompilation actual, string context)
    {
        Assert.Equal(Describe(expected), Describe(actual), context);
    }

    private static string Describe(Rmf2ProjectCompilation compilation)
    {
        var text = new StringBuilder();
        foreach (TranslationDiagnostic diagnostic in compilation.Diagnostics)
        {
            TextSourceLocation at = diagnostic.Location;
            text.Append(CultureInfo.InvariantCulture, $"{diagnostic.Id} {diagnostic.Severity} {at.Path} {at.StartByte}+{at.LengthBytes} {at.Line}:{at.Column}-{at.EndLine}:{at.EndColumn} [{diagnostic.MessageKey}/{diagnostic.Locale}] {diagnostic.Message}\n");
        }
        if (compilation.Project is not { } project) return text.Append("no project\n").ToString();
        text.Append(CultureInfo.InvariantCulture, $"{project.CallerFingerprint} {project.SourceHash}\n{project.MarkupContract}\n");
        foreach (Rmf2MessageContractV5 contract in project.CanonicalMessages.Concat(project.ExtraMessages))
            text.Append(CultureInfo.InvariantCulture, $"{contract.Id} {contract.Key} {string.Join('.', contract.Path)} ({string.Join(",", contract.Inputs.Select(input => input.Name + ":" + input.Type))}) " +
                $"{string.Join(",", contract.Slots.Select(slot => slot.Key + "=" + slot.Value))} {contract.Structured} {string.Join(",", contract.MarkupNames)} {contract.Content} {string.Join("|", contract.Skeletons)}\n");
        foreach (Rmf2LocaleV5 locale in project.Locales)
        {
            text.Append(CultureInfo.InvariantCulture, $"{locale.Tag} -> {locale.FallbackTag}: {string.Join(",", locale.DirectResources.Select(resource => resource.Key))}\n");
            text.Append(Rmf2LocaleArtifactV5.Render(project, locale.Tag).Text).Append('\n');
        }
        return text.ToString();
    }

    // A small catalog of features, each one .rmf2 file per locale, edited at random.
    private sealed class RandomCatalog
    {
        private readonly Random _random;
        private readonly bool _errors;
        // feature -> locale -> the file's messages (name, entry text), in order.
        private readonly SortedDictionary<string, SortedDictionary<string, List<(string Name, string Text)>>> _features = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TranslationSource> _sources = new(StringComparer.Ordinal);
        private string _validation = "";
        // The kind of each key's message: the same in every locale unless an edit breaks one.
        private readonly Dictionary<string, int> _kinds = new(StringComparer.Ordinal);
        private int _next;

        internal RandomCatalog(Random random, bool errors = true)
        {
            _random = random;
            _errors = errors;
            for (int feature = 0; feature < 3; feature++) AddFeature();
        }

        internal TranslationSource Project() => new("p/runic.json", Encoding.UTF8.GetBytes(
            "{\"schemaVersion\":1,\"catalog\":\"random\",\"code\":{\"namespace\":\"Random.Translations\",\"className\":\"AppText\"}," +
            "\"baseLocale\":\"en\",\"locales\":[" + string.Join(",", Locales.Select(locale => "\"" + locale + "\"")) + "]" + _validation + "}"));

        // The sources: unchanged files keep their objects, as a host's do.
        internal TranslationSource[] Sources()
        {
            var result = new List<TranslationSource>();
            foreach (var feature in _features)
                foreach (var locale in feature.Value)
                {
                    string path = "p/" + feature.Key + "/" + locale.Key + ".rmf2";
                    byte[] bytes = Encoding.UTF8.GetBytes(Render(locale.Value));
                    if (!_sources.TryGetValue(path, out var source) || !source.GetUtf8Bytes().AsSpan().SequenceEqual(bytes))
                        _sources[path] = source = new TranslationSource(path, bytes);
                    result.Add(source);
                }
            return result.ToArray();
        }

        internal int MessagesIn(string path)
        {
            string[] parts = path.Split('/');
            return _features[parts[1]][parts[2][..^".rmf2".Length]].Count;
        }

        internal string Edit()
        {
            switch (_random.Next(15))
            {
                case 0: AddFeature(); return "add a feature";
                case 1 when _features.Count > 1: _features.Remove(_features.Keys.ElementAt(_random.Next(_features.Count))); return "remove a feature";
                case 10 or 11 or 12:
                {
                    // A feature written again without errors.
                    string key = _features.Keys.ElementAt(_random.Next(_features.Count));
                    _features[key] = NewFeature(clean: true);
                    return "repair a feature";
                }
                case 2: _validation = _validation.Length == 0 ? ",\"validation\":{\"translationCompleteness\":\"warning\",\"inputParity\":\"error\"}" : ""; return "edit runic.json";
                case 3:
                {
                    // A new key in the base locale, and in some translations.
                    var feature = RandomFeature();
                    string name = "n" + _next++;
                    foreach (var locale in feature) if (locale.Key == "en" || _random.Next(3) != 0) Insert(locale.Value, (name, Message(name, locale.Key)));
                    return "add a key";
                }
                case 4:
                {
                    var file = RandomFile();
                    if (file.Count > 0) file.RemoveAt(_random.Next(file.Count));
                    return "delete a message";
                }
                case 5:
                {
                    // A same-bytes copy of a file: the host made a new source object.
                    var path = _sources.Keys.ElementAt(_random.Next(_sources.Count));
                    _sources[path] = new TranslationSource(path, _sources[path].GetUtf8Bytes());
                    return "copy a file";
                }
                case 6 when _errors:
                {
                    var file = RandomFile();
                    if (file.Count > 0) { int index = _random.Next(file.Count); file[index] = (file[index].Name, file[index].Text + " {$broken"); }
                    return "break a message";
                }
                case 7:
                {
                    // The same message in every locale changes its inputs (the base contract changes).
                    var feature = RandomFeature();
                    var names = feature["en"].Select(item => item.Name).ToArray();
                    if (names.Length == 0) return "no message";
                    string name = names[_random.Next(names.Length)];
                    _kinds[name] = _random.Next(8);
                    foreach (var locale in feature)
                        for (int index = 0; index < locale.Value.Count; index++)
                            if (locale.Value[index].Name == name) locale.Value[index] = (name, Message(name, locale.Key));
                    return "change a key in every locale";
                }
                case 8: ChangeMessage(translation: false); return "change a base message";
                default: ChangeMessage(translation: true); return "change a translation";
            }
        }

        // Changes one message of one file; returns the file's path.
        internal string ChangeMessage(bool translation)
        {
            var feature = _features.ElementAt(_random.Next(_features.Count));
            string locale = translation ? Locales[1 + _random.Next(Locales.Length - 1)] : "en";
            var file = feature.Value[locale];
            if (file.Count == 0) Insert(file, ("x" + _next++, Message("x" + _next, locale)));
            else { int index = _random.Next(file.Count); file[index] = (file[index].Name, Message(file[index].Name, locale)); }
            return "p/" + feature.Key + "/" + locale + ".rmf2";
        }

        private void AddFeature() => _features["f" + _next++] = NewFeature(clean: !_errors || _random.Next(2) == 0);

        private SortedDictionary<string, List<(string Name, string Text)>> NewFeature(bool clean)
        {
            var feature = new SortedDictionary<string, List<(string Name, string Text)>>(StringComparer.Ordinal);
            int messages = 4 + _random.Next(10);
            foreach (string locale in Locales) feature[locale] = new();
            for (int index = 0; index < messages; index++)
            {
                string name = "m" + _next++;
                _kinds[name] = _random.Next(8);
                foreach (string locale in Locales)
                    if (locale == "en" || clean || _random.Next(30) != 0) feature[locale].Add((name, Message(name, locale, clean)));
                if (!clean && _random.Next(30) == 0) feature[Locales[1 + _random.Next(Locales.Length - 1)]].Add(("extra" + _next++, "Extra"));
            }
            return feature;
        }

        private SortedDictionary<string, List<(string Name, string Text)>> RandomFeature() => _features.ElementAt(_random.Next(_features.Count)).Value;

        private List<(string Name, string Text)> RandomFile()
        {
            var feature = RandomFeature();
            return feature.ElementAt(_random.Next(feature.Count)).Value;
        }

        private void Insert(List<(string Name, string Text)> file, (string Name, string Text) item) => file.Insert(_random.Next(file.Count + 1), item);

        // One random message body of an entry, as RMF2 entry text after "name =".
        private string Message(string name, string locale, bool clean = false)
        {
            if (!_kinds.TryGetValue(name, out int kind)) _kinds[name] = kind = _random.Next(8);
            if (_errors && !clean && _random.Next(10) == 0) kind = 8 + _random.Next(4);
            string text = locale + " " + name + " " + _random.Next(1000).ToString(CultureInfo.InvariantCulture);
            return kind switch
            {
                0 or 1 or 2 => text,
                3 => text + " {$name}",
                4 => "\n    .input {$count :integer}\n    .match $count\n    one {{{$count} " + text + "}}\n    * {{{$count} " + text + " items}}",
                5 => text + ": {$amount :number}",
                6 => "ü 😀 " + text + " {|literal\\|x|}",
                7 => "\n    {{" + text + "}}",
                8 => text + " {$flag :boolean}",
                9 => "{#b}" + text + "{/b}",
                10 => "\n    .input {$count :integer}\n    .match $count\n    zero {{none}}\n    * {{" + text + "}}",
                _ => text + " {$name :bogus}",
            };
        }

        private string Render(List<(string Name, string Text)> messages)
        {
            var text = new StringBuilder("s0 {\n");
            foreach (var (name, body) in messages)
            {
                int hash = name.Sum(character => character) % 7;
                if (hash == 0) text.Append("  # A comment for ").Append(name).Append('\n');
                if (hash == 1 && body.Contains("{$name}", StringComparison.Ordinal)) text.Append("  @param $name - the user's name\n");
                if (hash == 2) text.Append("  @maxLength 12\n");
                text.Append("  ").Append(name).Append(" =").Append(body.StartsWith('\n') ? "" : " ").Append(body).Append('\n');
            }
            return text.Append("}\n").ToString();
        }
    }
}
