using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Runic.CommandLine;
using Runic.CommandLine.Generated;
using Runic.Translations.CommandLine.Tests.Translations;
using TUnit.Core;

namespace Runic.Translations.CommandLine.Tests;

// The help and usage texts of Command Line 0.13 in German through the adapter: LongDescriptionKey (cli#97), arguments
// and related options named as users see them (cli#98), no default for flags (cli#99) and the JSON binding (D066.2).
internal sealed class HelpTextTests
{
    private static async Task<(int Exit, string Output, string Error)> Run(ICommandTextResolver? resolver, CommandSyntaxDialect syntax, params string[] args)
    {
        var console = new MemoryConsole();
        int exit = await new CommandApp(GeneratedCommandCatalog.Create(builder => builder.Syntax(syntax)))
        {
            Name = "greeter",
            Console = console,
            Culture = CultureInfo.GetCultureInfo("de"),
            TextResolver = resolver,
        }.RunAsync(args);
        return (exit, console.StandardOutput, console.StandardError);
    }

    [Test, DisplayName("the missing-argument error names the argument by its placeholder, in German")]
    public async Task MissingArgumentPlaceholder()
    {
        var resolver = new TranslationCommandTextResolver();
        (int exit, string output, string error) = await Run(resolver, CommandSyntaxDialect.WindowsSlash, "/link", "one");
        Assert.Equal(CommandExitCodes.Usage, exit, error);
        Assert.Equal("", output, "output");
        Assert.Equal(
            "Das Argument <Container-Pfad> fehlt.\n" +
            "Aufruf: greeter /link <Richtlinie> <Container-Pfad> [Optionen]\n" +
            "Weitere Informationen erhalten Sie mit \"greeter /link /?\".\n",
            error, "slash error");

        (exit, _, error) = await Run(resolver, CommandSyntaxDialect.Portable, "link", "one");
        Assert.Equal(CommandExitCodes.Usage, exit, error);
        Assert.True(error.StartsWith("Das Argument <Container-Pfad> fehlt.\n", StringComparison.Ordinal), error);
        Assert.Equal("Das Argument <Container-Pfad> fehlt.",
            resolver.Resolve(CommandTextKeys.DiagnosticMissingArgument, CultureInfo.GetCultureInfo("de"), ["<Container-Pfad>"]));

        // Help lists the arguments under the same placeholders.
        (exit, output, _) = await Run(resolver, CommandSyntaxDialect.WindowsSlash, "/link", "/?");
        Assert.Equal(0, exit, output);
        Assert.True(output.Contains("Argumente:\n  <Richtlinie>      [erforderlich]\n  <Container-Pfad>  Path of the container [erforderlich]\n", StringComparison.Ordinal), output);
    }

    [Test, DisplayName("requires and conflicts name options in the dialect's spelling, and flags show no default")]
    public async Task RelatedOptionsAndFlags()
    {
        var resolver = new TranslationCommandTextResolver();
        (_, string alias, _) = await Run(resolver, CommandSyntaxDialect.WindowsSlash, "/alias", "/?");
        Assert.True(alias.Contains("Remove the entries for <AltName> [schließt aus: /l]\n", StringComparison.Ordinal), alias);
        // The flag has no [Standard: False] note.
        Assert.True(alias.Contains("List the aliases\n", StringComparison.Ordinal), alias);
        Assert.False(alias.Contains("False", StringComparison.OrdinalIgnoreCase), alias);
        (_, string wait, _) = await Run(resolver, CommandSyntaxDialect.WindowsSlash, "/wait", "/?");
        Assert.True(wait.Contains("Upper limit [Standard: 10000] [erfordert: /factor]\n", StringComparison.Ordinal), wait);
        Assert.True(wait.Contains("Backoff factor [Standard: 1.5]\n", StringComparison.Ordinal), wait);

        (_, alias, _) = await Run(resolver, CommandSyntaxDialect.Portable, "alias", "--help");
        Assert.True(alias.Contains("Remove the entries for <AltName> [schließt aus: -l]\n", StringComparison.Ordinal), alias);
        (_, wait, _) = await Run(resolver, CommandSyntaxDialect.Portable, "wait", "--help");
        Assert.True(wait.Contains("Upper limit [Standard: 10000] [erfordert: --factor]\n", StringComparison.Ordinal), wait);

        // The usage error of the same conflict names the options the same way.
        (int exit, _, string error) = await Run(resolver, CommandSyntaxDialect.WindowsSlash, "/alias", "/l", "/r", "x");
        Assert.Equal(CommandExitCodes.Usage, exit, error);
        Assert.True(error.StartsWith("/r kann nicht mit /l kombiniert werden.\n", StringComparison.Ordinal), error);
    }

    [Test, DisplayName("JSON diagnostics carry the German message, the placeholder and the binding ID")]
    public async Task JsonBinding()
    {
        (int exit, string output, _) = await Run(new TranslationCommandTextResolver(), CommandSyntaxDialect.Portable, "link", "one", "--format", "json");
        Assert.Equal(CommandExitCodes.Usage, exit, output);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement diagnostic = document.RootElement.GetProperty("diagnostics").EnumerateArray().Single();
        Assert.Equal("RCLI1005", diagnostic.GetProperty("code").GetString(), output);
        Assert.Equal("Das Argument <Container-Pfad> fehlt.", diagnostic.GetProperty("message").GetString(), output);
        Assert.Equal("<Container-Pfad>", diagnostic.GetProperty("arguments").EnumerateArray().Single().GetString(), output);
        // The stable ID of the argument: no value name, dialect or culture changes it.
        Assert.Equal("container-path", diagnostic.GetProperty("binding").GetString(), output);
    }

    [Test, DisplayName("LongDescriptionKey resolves through the application catalog, in German with its line breaks")]
    public async Task LongDescriptionKey()
    {
        ITranslationSnapshot snapshot = await TestTextCatalog.CreateProvider().GetSnapshotAsync("de");
        var resolver = new TranslationCommandTextResolver(snapshot);
        Assert.Equal("Starten Sie den Dienst danach neu.\n\n  /a   erster Eintrag\n  /b   zweiter Eintrag",
            resolver.Resolve("commands.store-show-details", CultureInfo.GetCultureInfo("de"), []));

        (int exit, string output, _) = await Run(resolver, CommandSyntaxDialect.Portable, "store", "show", "--help");
        Assert.Equal(0, exit, output);
        Assert.True(output.Contains("\nZeigt den Speicher an\n\nStarten Sie den Dienst danach neu.\n\n  /a   erster Eintrag\n  /b   zweiter Eintrag\n", StringComparison.Ordinal), output);
        Assert.False(output.Contains("Restart the service", StringComparison.Ordinal), output);
        (exit, output, _) = await Run(resolver, CommandSyntaxDialect.Portable, "store", "--help");
        Assert.Equal(0, exit, output);
        Assert.True(output.Contains("\nDer Speicher hat einen Eintrag pro Container.\n", StringComparison.Ordinal), output);

        // ValidateDescriptionKeys finds both keys of the store group and command through the adapter, in both locales.
        foreach (string locale in new[] { "en", "de" })
        {
            var context = new CommandTextContext(CultureInfo.GetCultureInfo(locale),
                new TranslationCommandTextResolver(await TestTextCatalog.CreateProvider().GetSnapshotAsync(locale)));
            Assert.Equal("", string.Join("\n", context.ValidateDescriptionKeys(GeneratedCommandCatalog.Create())
                .Where(static issue => issue.Location.StartsWith("store", StringComparison.Ordinal)).Select(static issue => issue.Code + " " + issue.Message)),
                locale + " description keys");
        }

        // An unresolved LongDescriptionKey is reported (RCLI0023) as the resolver sees it.
        var builtInOnly = new CommandTextContext(CultureInfo.GetCultureInfo("de"), new TranslationCommandTextResolver());
        Assert.True(builtInOnly.ValidateDescriptionKeys(GeneratedCommandCatalog.Create())
            .Any(static issue => issue.Code == "RCLI0023" && issue.Message.Contains("'commands.store-show-details'", StringComparison.Ordinal)), "RCLI0023 for the long description key");

        // Without the application catalog the literal is the fallback.
        (_, output, _) = await Run(new TranslationCommandTextResolver(), CommandSyntaxDialect.Portable, "store", "show", "--help");
        Assert.True(output.Contains("\nRestart the service afterwards.\n", StringComparison.Ordinal), output);
    }
}
