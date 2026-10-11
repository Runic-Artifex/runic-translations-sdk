using System.Globalization;
using LocalizedCli.Translations;
using Runic.CommandLine;
using Runic.CommandLine.Generated;
using Runic.Translations;
using Runic.Translations.CommandLine;

CultureInfo culture = CultureInfo.GetCultureInfo(Environment.GetEnvironmentVariable("RCLI_EXAMPLE_CULTURE") ?? "en");
ITranslationManager manager = await CliTextCatalog.CreateManagerAsync(culture.Name);
Commands.Text = new CliText(manager);
return await new CommandApp(GeneratedCommandCatalog.Create())
{
    Name = "localized",
    Culture = culture,
    // Finds DescriptionKey values and framework keys in CliText by name; framework keys the catalog
    // does not define use the built-in English or German text. No key mapping.
    TextResolver = new TranslationCommandTextResolver(manager),
}.RunAsync(args);

internal static class Commands
{
    internal static CliText Text { get; set; } = null!;

    [Command("greet", Description = "Greet a person", DescriptionKey = "commands.greet")]
    public static string Greet(
        [Argument(Description = "Name of the person to greet", DescriptionKey = "arguments.name")] string name) =>
        Text.Messages.app_greeting(name: name);
}
