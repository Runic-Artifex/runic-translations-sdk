using System.Threading;
using System.Threading.Tasks;
using Runic.CommandLine;

namespace Runic.Translations.CommandLine.Tests;

internal static class Commands
{
    [Command("greet", Description = "Greet a person", DescriptionKey = "commands.greet")]
    public static string Greet(
        [Argument(Description = "Name of the person to greet", DescriptionKey = "arguments.name")] string name,
        [Option("--times", Description = "How often to greet", DescriptionKey = "options.times", Minimum = 1, Maximum = 3)] int times = 1) =>
        string.Join(' ', System.Linq.Enumerable.Repeat("Hello " + name, times));

    // A prompt without a default: the tests' console is not interactive, so it fails with RCLI4001.
    [Command("ask", Description = "Ask for a name", DescriptionKey = "commands.ask")]
    public static async Task<string> Ask(CommandExecutionContext context, CancellationToken cancellationToken) =>
        "Hello " + await new CommandPrompts(context).TextAsync("Name", cancellationToken: cancellationToken).ConfigureAwait(false);
}

// Command Line 0.13 help and usage texts (cli#97, #98, #99). Hidden, so the root help of the other cases is unchanged.
[CommandGroup("store", Hidden = true, Description = "Manage the store", DescriptionKey = "groups.store",
    LongDescription = "The store keeps one entry per container.", LongDescriptionKey = "groups.store-details")]
internal static class HelpTextCommands
{
    // Extended help through LongDescriptionKey, with a paragraph break and preformatted lines.
    [Command("store show", Hidden = true, Description = "Show the store", DescriptionKey = "commands.store-show",
        LongDescription = "Restart the service afterwards.", LongDescriptionKey = "commands.store-show-details")]
    public static string Show() => "ok";

    // Arguments named by their value names in help and in the missing-argument error.
    [Command("link", Hidden = true, Description = "Link a policy to a container")]
    public static string Link(
        [Argument(ValueName = "Richtlinie")] string policyName,
        [Argument(ValueName = "Container-Pfad", Description = "Path of the container")] string containerPath) => policyName + " " + containerPath;

    // A conflict between a value option and a flag; the flag shows no default.
    [Command("alias", Hidden = true, Description = "List or remove aliases")]
    public static string Alias(
        [Option("-l", Description = "List the aliases")] bool list = false,
        [Option("-r", Description = "Remove the entries for <AltName>", ValueName = "AltName", ConflictsWith = ["list"])] string? remove = null) =>
        list ? "list" : remove ?? "";

    // A value option that requires another; both keep their defaults.
    [Command("wait", Hidden = true, Description = "Wait with backoff")]
    public static string Wait(
        [Option("--factor", Description = "Backoff factor")] double factor = 1.5,
        [Option("--limit", Description = "Upper limit", Requires = ["factor"])] int limit = 10000) =>
        factor.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + limit.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
