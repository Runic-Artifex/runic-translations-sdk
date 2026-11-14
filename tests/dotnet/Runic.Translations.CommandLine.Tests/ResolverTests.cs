using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Runic.CommandLine;
using Runic.CommandLine.Generated;
using Runic.Translations.CommandLine.Tests.Translations;
using Runic.Translations.CommandLine.Translations;
using TUnit.Core;

namespace Runic.Translations.CommandLine.Tests;

internal sealed partial class ResolverTests
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
        ["greet", "Ada", "--json", "--format", "json"],
        ["ask"],
        ["ask", "--json"],
    ];

    [Test, DisplayName("message names drop hyphens and capitalize the next letter")]
    public void MessageNames()
    {
        Assert.Equal("help.showHelp", TranslationCommandTextResolver.GetMessageName("help.show-help"));
        Assert.Equal("help.pathKind.file", TranslationCommandTextResolver.GetMessageName("help.path-kind.file"));
        Assert.Equal("diagnostics.unknownOptionSuggestion", TranslationCommandTextResolver.GetMessageName("diagnostics.unknown-option-suggestion"));
        Assert.Equal("faults.RCLI4000", TranslationCommandTextResolver.GetMessageName("faults.RCLI4000"));
        Assert.Equal("commands.greet", TranslationCommandTextResolver.GetMessageName("commands.greet"));
        Assert.Throws<ArgumentNullException>(() => TranslationCommandTextResolver.GetMessageName(null!));
    }

    // The key list of the Runic.CommandLine package this suite restores (translations/text-keys.json), next to the
    // catalog sources the adapter compiles; the project passes its path.
    private static readonly string TextKeysPath = typeof(ResolverTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(static attribute => attribute.Key == "RunicCommandLineTextKeys").Value!;

    private static (string Key, string[] Arguments, string? Message)[] PackedKeys()
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(TextKeysPath));
        Assert.Equal("runic-command-line", document.RootElement.GetProperty("catalog").GetString(), "text-keys.json catalog");
        return document.RootElement.GetProperty("keys").EnumerateArray().Select(static item => (
            item.GetProperty("key").GetString()!,
            item.GetProperty("arguments").EnumerateArray().Select(static argument => argument.GetString()!).ToArray(),
            item.GetProperty("message").ValueKind == JsonValueKind.Null ? null : item.GetProperty("message").GetString())).ToArray();
    }

    // Keys the catalog leaves to the framework's own text ("message": null in text-keys.json): Runic.CommandLine uses
    // faults.RCLI4001 only when the diagnostics.prompt-* text resolves to its English text, so a built-in message would
    // replace that more specific text in English. The application catalog may still define it.
    private static string[] WithoutBuiltInText => PackedKeys().Where(static key => key.Message is null).Select(static key => key.Key).ToArray();

    private static IEnumerable<CommandTextKey> TranslatedKeys => CommandTextKeys.All.Where(static key => !WithoutBuiltInText.Contains(key.Key));

    [Test, DisplayName("the compiled catalog covers text-keys.json of the referenced Command Line, key for key")]
    public void CatalogCoversCommandLineKeys()
    {
        (string Key, string[] Arguments, string? Message)[] packed = PackedKeys();
        // text-keys.json is written from CommandTextKeys.All of the same build, so the two lists agree.
        Assert.Equal(string.Join("|", CommandTextKeys.All.Select(static key => key.ToString())),
            string.Join("|", packed.Select(static key => key.Arguments.Length == 0 ? key.Key : key.Key + "(" + string.Join(", ", key.Arguments) + ")")),
            "text-keys.json against CommandTextKeys.All");
        // The documented exception; Command Line may name more keys without a catalog message in text-keys.json.
        Assert.True(WithoutBuiltInText.Contains("faults.RCLI4001"), "faults.RCLI4001 has a catalog message: " + string.Join("|", WithoutBuiltInText));

        CompiledTranslationDefinition[] definitions = CommandLineTextCatalogData.CreateDefinition().Definitions.ToArray();
        string[] expected = packed.Where(static key => key.Message is not null).Select(static key => key.Message!.Replace('.', '_'))
            .Order(StringComparer.Ordinal).ToArray();
        // No key without its message, and no message for a key that Command Line removed.
        Assert.Equal(string.Join("|", expected), string.Join("|", definitions.Select(static item => item.Name).Order(StringComparer.Ordinal)), "compiled messages");
        foreach ((string key, string[] arguments, string? message) in packed.Where(static key => key.Message is not null))
        {
            Assert.Equal(TranslationCommandTextResolver.GetMessageName(key), message, key + " message name");
            CompiledTranslationDefinition definition = definitions.Single(item => item.Name == message!.Replace('.', '_'));
            foreach (TranslationPlaceholderDescriptor input in definition.Placeholders.ToArray())
                Assert.True(arguments.Contains(input.Name), key + " declares input " + input.Name + " that the framework does not supply.");
        }
    }

    [Test, DisplayName("built-in catalogs translate every framework key in English and German")]
    public void BuiltInCoverage()
    {
        var resolver = new TranslationCommandTextResolver();
        string[] identical = ["help.minimum", "help.maximum"];
        foreach (string key in WithoutBuiltInText)
        {
            Assert.Equal(null, resolver.Resolve(key, CultureInfo.GetCultureInfo("en"), []));
            Assert.Equal(null, resolver.Resolve(key, CultureInfo.GetCultureInfo("de"), []));
        }

        foreach (CommandTextKey key in TranslatedKeys)
        {
            string[] arguments = key.Arguments.Select(static name => "<" + name + ">").ToArray();
            string? english = resolver.Resolve(key.Key, CultureInfo.GetCultureInfo("en"), arguments);
            string? german = resolver.Resolve(key.Key, CultureInfo.GetCultureInfo("de"), arguments);
            Assert.True(english is { Length: > 0 }, "No English text for " + key.Key);
            Assert.True(german is { Length: > 0 }, "No German text for " + key.Key);
            Assert.True(identical.Contains(key.Key) != (english != german), key.Key + " German text: " + german);
        }
    }

    [Test, DisplayName("built-in English matches the framework's own English output")]
    public async Task EnglishMatchesFramework()
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

    [Test, DisplayName("German invocations show no English framework text")]
    public async Task GermanHasNoEnglish()
    {
        var resolver = new TranslationCommandTextResolver();
        // Every English framework sentence and label of more than one word.
        string[] english = TranslatedKeys
            .Select(item => resolver.Resolve(item.Key, CultureInfo.GetCultureInfo("en"), item.Arguments.Select(static name => "\u0001").ToArray())!)
            .SelectMany(static text => text.Split('\u0001'))
            .Select(static fragment => fragment.Trim(' ', '.', '(', ')', '\'', '?', ':'))
            .Where(static fragment => fragment.Contains(' ', StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal).ToArray();
        Assert.True(english.Length > 40, "Too few English fragments: " + english.Length);
        int checkedFragments = 0;
        foreach (string[] invocation in Invocations)
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
        // #168: the usage errors end with the German help hint (hints.usage, new in Command Line 0.10), not English.
        Result unknown = await Run(resolver, "de", "greet", "Ada", "--bogus");
        Assert.True(unknown.Error.Contains("Die Option '--bogus' ist unbekannt.", StringComparison.Ordinal), unknown.Error);
        Assert.True(unknown.Error.Contains("Weitere Informationen erhalten Sie mit \"greeter greet --help\".", StringComparison.Ordinal), unknown.Error);
        Assert.False(unknown.Error.Contains("for more information", StringComparison.Ordinal), unknown.Error);
        Result unknownCommand = await Run(resolver, "de", "frobnicate");
        Assert.True(unknownCommand.Error.Contains("Der Befehl 'frobnicate' ist unbekannt.", StringComparison.Ordinal) &&
            unknownCommand.Error.Contains("Weitere Informationen erhalten Sie mit", StringComparison.Ordinal), unknownCommand.Error);
        Result missingArgument = await Run(resolver, "de", "greet");
        Assert.True(missingArgument.Error.Contains("Weitere Informationen erhalten Sie mit \"greeter greet --help\".", StringComparison.Ordinal), missingArgument.Error);
        Assert.True(help.Output.Contains("Entspricht --format json", StringComparison.Ordinal), help.Output);
        Result conflict = await Run(resolver, "de", "greet", "Ada", "--json", "--format", "json");
        Assert.True(conflict.Output.Contains("--format und --json wählen beide das Ausgabeformat.", StringComparison.Ordinal), conflict.Output);
        Result prompt = await Run(resolver, "de", "ask");
        // Since Command Line 0.10 the human output has no code prefix and ends with the usage line and the help hint.
        Assert.Equal("Name benötigt eine Antwort, aber der Befehl läuft nicht in einem interaktiven Terminal. Geben Sie den Wert in der Befehlszeile an.\n" +
            "Aufruf: greeter ask [Optionen]\nWeitere Informationen erhalten Sie mit \"greeter ask --help\".\n",
            prompt.Error, "ask error");
        Result json = await Run(resolver, "de", "ask", "--json");
        Assert.True(json.Output.Contains("Name benötigt eine Antwort, aber bei JSON-Ausgabe wird nie nachgefragt.", StringComparison.Ordinal), json.Output);
    }

    [Test, DisplayName("the application catalog comes first and binds arguments by name or position")]
    public async Task ApplicationCatalogFirst()
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
        Assert.True(missing.Error.Contains("Das Argument <name> fehlt.", StringComparison.Ordinal), missing.Error);

        // An application diagnostic with positional string and integer inputs.
        Assert.Equal("disk hat sein Kontingent von 5 überschritten.", resolver.Resolve("diagnostics.quota-exceeded", german, ["disk", "5"]));
        Assert.Equal(null, resolver.Resolve("diagnostics.quota-exceeded", german, ["disk", "five"]));
        Assert.Equal(null, resolver.Resolve("diagnostics.quota-exceeded", german, ["disk"]));
        // The built-in text follows the snapshot locale, not the culture.
        Assert.Equal("Optionen", resolver.Resolve("help.options", CultureInfo.GetCultureInfo("en"), []));
    }

    [Test, DisplayName("a manager-backed resolver follows locale switches")]
    public async Task ManagerFollowsLocale()
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

    [Test, DisplayName("unsupported cultures and unknown keys fall back")]
    public void Fallbacks()
    {
        var resolver = new TranslationCommandTextResolver();
        Assert.Equal("Usage", resolver.Resolve("help.usage", CultureInfo.GetCultureInfo("fr"), []));
        Assert.Equal("Usage", resolver.Resolve("help.usage", CultureInfo.InvariantCulture, []));
        Assert.Equal("Aufruf", resolver.Resolve("help.usage", CultureInfo.GetCultureInfo("de-AT"), []));
        Assert.Equal(null, resolver.Resolve("commands.greet", CultureInfo.GetCultureInfo("de"), []));
        // A code that is no framework key (an application's own fault) has no built-in text.
        Assert.Equal(null, resolver.Resolve("faults.RCLI9999", CultureInfo.GetCultureInfo("de"), []));
        // A framework key with fewer arguments than its message declares falls back to the framework's English.
        Assert.Equal(null, resolver.Resolve("diagnostics.invalid-integer", CultureInfo.GetCultureInfo("de"), []));
        // Keys that do not use an argument resolve without it.
        Assert.Equal("Es wurden zu viele Argumente angegeben.", resolver.Resolve("diagnostics.unexpected-argument", CultureInfo.GetCultureInfo("de"), []));
        Assert.Equal("Der Befehl 'shwo' ist unbekannt. Meinten Sie 'show'?",
            resolver.Resolve("diagnostics.unknown-command-suggestion", CultureInfo.GetCultureInfo("de"), ["shwo", "show"]));
    }

    private static async Task<Result> Run(ICommandTextResolver? resolver, string culture, params string[] args)
    {
        var console = new MemoryConsole();
        int exit = await new CommandApp(GeneratedCommandCatalog.Create(static builder => builder.JsonOutputAlias("--json")))
        {
            Name = "greeter",
            Console = console,
            Culture = CultureInfo.GetCultureInfo(culture),
            TextResolver = resolver,
        }.RunAsync(args);
        // JSON frames carry a fresh request id on every run.
        return new Result(exit, RequestId().Replace(console.StandardOutput, "\"requestId\":\"*\""), console.StandardError);
    }

    [GeneratedRegex("\"requestId\":\"[0-9a-f]+\"")]
    private static partial Regex RequestId();

    private readonly record struct Result(int Exit, string Output, string Error);
}
