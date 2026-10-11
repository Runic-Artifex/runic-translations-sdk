using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Runic.CommandLine;
using Runic.CommandLine.Generated;
using Runic.Translations.CommandLine.Tests.Translations;
using Runic.Translations.CommandLine.Translations;

namespace Runic.Translations.CommandLine.Tests;

internal static class ResolverTests
{
    // Invocations that reach help, parse errors, value errors and the unknown-command path.
    private static readonly string[][] Invocations =
    [
        ["--help"],
        ["greet", "--help"],
        ["greet"],
        ["greet", "Ada", "--bogus"],
        ["greet", "Ada", "--times"],
        ["greet", "Ada", "--times", "9"],
        ["greet", "Ada", "--times", "x"],
        ["greet", "Ada", "--times", "1", "--times", "2"],
        ["greet", "Ada", "Bob"],
        ["frobnicate"],
    ];

    public static void Register(TestRunner runner)
    {
        runner.Add("message names drop hyphens and capitalize the next letter", MessageNames);
        runner.Add("the argument table matches CommandTextKeys.All when Runic.CommandLine provides it", TableMatchesCommandLine);
        runner.Add("built-in catalogs translate every framework key in English and German", BuiltInCoverage);
        runner.Add("built-in English matches the framework's own English output", EnglishMatchesFramework);
        runner.Add("German invocations show no English framework text", GermanHasNoEnglish);
        runner.Add("the application catalog comes first and binds arguments by name or position", ApplicationCatalogFirst);
        runner.Add("a manager-backed resolver follows locale switches", ManagerFollowsLocale);
        runner.Add("unsupported cultures and unknown keys fall back", Fallbacks);
    }

    private static void MessageNames()
    {
        Assert.Equal("help.showHelp", TranslationCommandTextResolver.GetMessageName("help.show-help"));
        Assert.Equal("help.pathKind.file", TranslationCommandTextResolver.GetMessageName("help.path-kind.file"));
        Assert.Equal("diagnostics.unknownOptionSuggestion", TranslationCommandTextResolver.GetMessageName("diagnostics.unknown-option-suggestion"));
        Assert.Equal("faults.RCLI4000", TranslationCommandTextResolver.GetMessageName("faults.RCLI4000"));
        Assert.Equal("commands.greet", TranslationCommandTextResolver.GetMessageName("commands.greet"));
        Assert.Throws<ArgumentNullException>(() => TranslationCommandTextResolver.GetMessageName(null!));
    }

    // CommandTextKeys is new after Runic.CommandLine 0.6.0-preview.3, which this repository builds against.
    private static readonly Type? TextKeys = typeof(CommandApp).Assembly.GetType("Runic.CommandLine.CommandTextKeys");

    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "The test runs on the JIT and reads a public property.")]
    private static void TableMatchesCommandLine()
    {
        if (TextKeys is null)
        {
            Console.WriteLine("  (Runic.CommandLine " + typeof(CommandApp).Assembly.GetName().Version + " has no CommandTextKeys; skipped)");
            return;
        }

        var all = (System.Collections.IEnumerable)TextKeys.GetProperty("All")!.GetValue(null)!;
        var keys = new List<string>();
        foreach (object item in all)
        {
            string key = (string)item.GetType().GetProperty("Key")!.GetValue(item)!;
            var arguments = (IReadOnlyList<string>)item.GetType().GetProperty("Arguments")!.GetValue(item)!;
            Assert.True(FrameworkTextArguments.ByKey.TryGetValue(key, out string[]? names), "The adapter does not know " + key);
            Assert.Equal(string.Join(",", arguments), string.Join(",", names!), key + " arguments");
            keys.Add(key);
        }

        Assert.Equal(FrameworkTextArguments.ByKey.Count, keys.Count, "framework key count");
    }

    private static void BuiltInCoverage()
    {
        // The catalogs hold exactly one message per framework key, with inputs the framework supplies.
        CompiledTranslationDefinition[] definitions = CommandLineTextCatalogData.CreateDefinition().Definitions.ToArray();
        string[] expected = FrameworkTextArguments.ByKey.Keys.Select(static key => TranslationCommandTextResolver.GetMessageName(key).Replace('.', '_'))
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(string.Join("|", expected), string.Join("|", definitions.Select(static item => item.Name).Order(StringComparer.Ordinal)), "built-in messages");
        foreach ((string key, string[] names) in FrameworkTextArguments.ByKey)
        {
            CompiledTranslationDefinition definition = definitions.Single(item => item.Name == TranslationCommandTextResolver.GetMessageName(key).Replace('.', '_'));
            foreach (TranslationPlaceholderDescriptor input in definition.Placeholders.ToArray())
                Assert.True(names.Contains(input.Name), key + " declares input " + input.Name + " that the framework does not supply.");
        }

        var resolver = new TranslationCommandTextResolver();
        string[] identical = ["help.minimum", "help.maximum"];
        foreach ((string key, string[] names) in FrameworkTextArguments.ByKey)
        {
            string[] arguments = names.Select(static name => "<" + name + ">").ToArray();
            string? english = resolver.Resolve(key, CultureInfo.GetCultureInfo("en"), arguments);
            string? german = resolver.Resolve(key, CultureInfo.GetCultureInfo("de"), arguments);
            Assert.True(english is { Length: > 0 }, "No English text for " + key);
            Assert.True(german is { Length: > 0 }, "No German text for " + key);
            Assert.True(identical.Contains(key) != (english != german), key + " German text: " + german);
        }
    }

    private static async Task EnglishMatchesFramework()
    {
        foreach (string[] invocation in Invocations)
        {
            Result framework = await Run(null, "en", invocation);
            Result adapter = await Run(new TranslationCommandTextResolver(), "en", invocation);
            string name = string.Join(' ', invocation);
            Assert.Equal(framework.Exit, adapter.Exit, name + " exit");
            Assert.Equal(framework.Output, adapter.Output, name + " output");
            Assert.Equal(framework.Error, adapter.Error, name + " error");
        }
    }

    private static async Task GermanHasNoEnglish()
    {
        var resolver = new TranslationCommandTextResolver();
        // Every English framework sentence and label of more than one word.
        string[] english = FrameworkTextArguments.ByKey
            .Select(item => resolver.Resolve(item.Key, CultureInfo.GetCultureInfo("en"), item.Value.Select(static name => "\u0001").ToArray())!)
            .SelectMany(static text => text.Split('\u0001'))
            .Select(static fragment => fragment.Trim(' ', '.', '(', ')', '\'', '?', ':'))
            .Where(static fragment => fragment.Contains(' ', StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal).ToArray();
        Assert.True(english.Length > 40, "Too few English fragments: " + english.Length);
        int checkedFragments = 0;
        // Runic.CommandLine 0.6.0-preview.3 reports value errors as faults.RCLI2xxx without arguments; releases with
        // CommandTextKeys ask for diagnostics.invalid-integer and diagnostics.out-of-range instead.
        foreach (string[] invocation in Invocations.Where(static item => TextKeys is not null || (!item.Contains("9") && !item.Contains("x"))))
        {
            Result german = await Run(resolver, "de", invocation);
            string text = german.Output + german.Error;
            Assert.True(text.Length > 0, string.Join(' ', invocation) + " wrote nothing.");
            foreach (string fragment in english)
            {
                checkedFragments++;
                Assert.False(text.Contains(fragment, StringComparison.Ordinal), string.Join(' ', invocation) + " shows English '" + fragment + "':\n" + text);
            }
        }

        Assert.True(checkedFragments > 0, "Nothing was checked.");
        Result help = await Run(resolver, "de", "greet", "--help");
        Assert.True(help.Output.Contains("Aufruf", StringComparison.Ordinal) && help.Output.Contains("Optionen", StringComparison.Ordinal) &&
            help.Output.Contains("Hilfe anzeigen", StringComparison.Ordinal), help.Output);
        Result unknown = await Run(resolver, "de", "greet", "Ada", "--bogus");
        Assert.True(unknown.Error.Contains("Unbekannte Option. (--bogus)", StringComparison.Ordinal), unknown.Error);
    }

    private static async Task ApplicationCatalogFirst()
    {
        ITranslationSnapshot snapshot = await TestTextCatalog.CreateProvider().GetSnapshotAsync("de");
        var resolver = new TranslationCommandTextResolver(snapshot);
        CultureInfo german = CultureInfo.GetCultureInfo("de");

        Result help = await Run(resolver, "de", "greet", "--help");
        Assert.True(help.Output.Contains("Grüßt eine Person", StringComparison.Ordinal), help.Output);
        Assert.True(help.Output.Contains("Wie oft gegrüßt wird", StringComparison.Ordinal), help.Output);
        // The application overrides help.usage; other labels come from the built-in catalog.
        Assert.True(help.Output.Contains("Übersicht", StringComparison.Ordinal) && !help.Output.Contains("Aufruf", StringComparison.Ordinal), help.Output);
        Assert.True(help.Output.Contains("Optionen", StringComparison.Ordinal), help.Output);

        // {$arg0} binds the first argument.
        Result unknown = await Run(resolver, "de", "greet", "Ada", "--bogus");
        Assert.True(unknown.Error.Contains("Keine solche Option: --bogus", StringComparison.Ordinal), unknown.Error);
        // The application message declares an input the framework does not supply, so the built-in text is used.
        Result missing = await Run(resolver, "de", "greet");
        Assert.True(missing.Error.Contains("Ein erforderliches Argument fehlt.", StringComparison.Ordinal), missing.Error);

        // An application diagnostic with positional string and integer inputs.
        Assert.Equal("disk hat sein Kontingent von 5 überschritten.", resolver.Resolve("diagnostics.quota-exceeded", german, ["disk", "5"]));
        Assert.Equal(null, resolver.Resolve("diagnostics.quota-exceeded", german, ["disk", "five"]));
        Assert.Equal(null, resolver.Resolve("diagnostics.quota-exceeded", german, ["disk"]));
        // The built-in text follows the snapshot locale, not the culture.
        Assert.Equal("Optionen", resolver.Resolve("help.options", CultureInfo.GetCultureInfo("en"), []));
    }

    private static async Task ManagerFollowsLocale()
    {
        ITranslationManager manager = await TestTextCatalog.CreateManagerAsync("en");
        var resolver = new TranslationCommandTextResolver(manager);
        CultureInfo culture = CultureInfo.InvariantCulture;
        Assert.Equal("Synopsis", resolver.Resolve("help.usage", culture, []));
        Assert.Equal("Options", resolver.Resolve("help.options", culture, []));
        await manager.SetLocaleAsync("de");
        Assert.Equal("Übersicht", resolver.Resolve("help.usage", culture, []));
        Assert.Equal("Optionen", resolver.Resolve("help.options", culture, []));
        Assert.Throws<ArgumentNullException>(() => _ = new TranslationCommandTextResolver((ITranslationManager)null!));
        Assert.Throws<ArgumentNullException>(() => _ = new TranslationCommandTextResolver((ITranslationSnapshot)null!));
    }

    private static void Fallbacks()
    {
        var resolver = new TranslationCommandTextResolver();
        Assert.Equal("Usage", resolver.Resolve("help.usage", CultureInfo.GetCultureInfo("fr"), []));
        Assert.Equal("Usage", resolver.Resolve("help.usage", CultureInfo.InvariantCulture, []));
        Assert.Equal("Aufruf", resolver.Resolve("help.usage", CultureInfo.GetCultureInfo("de-AT"), []));
        Assert.Equal(null, resolver.Resolve("commands.greet", CultureInfo.GetCultureInfo("de"), []));
        Assert.Equal(null, resolver.Resolve("faults.RCLI6001", CultureInfo.GetCultureInfo("de"), []));
        // A framework key with fewer arguments than its message declares falls back to the framework's English.
        Assert.Equal(null, resolver.Resolve("diagnostics.invalid-integer", CultureInfo.GetCultureInfo("de"), []));
        // Keys that do not use an argument resolve without it.
        Assert.Equal("Unbekannter Befehl.", resolver.Resolve("diagnostics.unknown-command", CultureInfo.GetCultureInfo("de"), []));
        Assert.Equal("Unbekannter Befehl. Meinten Sie 'show'?",
            resolver.Resolve("diagnostics.unknown-command-suggestion", CultureInfo.GetCultureInfo("de"), ["shwo", "show"]));
    }

    private static async Task<Result> Run(ICommandTextResolver? resolver, string culture, params string[] args)
    {
        var console = new MemoryConsole();
        int exit = await new CommandApp(GeneratedCommandCatalog.Create())
        {
            Name = "greeter",
            Console = console,
            Culture = CultureInfo.GetCultureInfo(culture),
            TextResolver = resolver,
        }.RunAsync(args);
        return new Result(exit, console.StandardOutput, console.StandardError);
    }

    private readonly record struct Result(int Exit, string Output, string Error);
}
