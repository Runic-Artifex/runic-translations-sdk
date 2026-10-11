using Runic.CommandLine;

namespace Runic.Translations.CommandLine.Tests;

internal static class Commands
{
    [Command("greet", Description = "Greet a person", DescriptionKey = "commands.greet")]
    public static string Greet(
        [Argument(Description = "Name of the person to greet", DescriptionKey = "arguments.name")] string name,
        [Option("--times", Description = "How often to greet", DescriptionKey = "options.times", Minimum = 1, Maximum = 3)] int times = 1) =>
        string.Join(' ', System.Linq.Enumerable.Repeat("Hello " + name, times));
}
