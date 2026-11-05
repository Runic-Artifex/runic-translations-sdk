using System.Collections.Generic;
using Runic.CommandLine;

namespace Runic.Translations.Tool;

/// <summary><c>runic-translations import --from resx|i18next</c>: migrate an existing catalog into the project's .rmf2 sources.</summary>
internal static class MigrationCommandModule
{
    [Command("import", Description = "Import a .resx or i18next catalog into the project's .rmf2 sources.",
        LongDescription = "Reads a .resx file (with its Strings.<locale>.resx satellites) or i18next JSON (a <lng>.json file, or a directory of <lng>.json or <lng>/<namespace>.json files) and writes each message into <project>[/<group>]/<locale>.rmf2: keys become RMF2 names (nested i18next keys become groups), {0}, {name} and {{name}} become inputs, i18next plural suffixes (_one, _other, ...) become .match on $count, and .resx comments become descriptions. A message that already exists keeps its text and translations and is reported as kept; --overwrite replaces its text with the source's (it keeps its place and comments). Run --dry-run first. What cannot be mapped is reported per message and the rest is imported. PO files are not supported: convert them to XLIFF and use xliff import.\n\nExit codes: 0 success, also when single messages were skipped or refused; 1 command failure, such as an inaccessible file; 2 invalid invocation, a source that cannot be read, or imported messages that do not compile; 3 internal failure; 130 cancelled.",
        Examples = ["runic-translations import --from resx Properties/Strings.resx --dry-run", "runic-translations import --from i18next public/locales --project translations", "runic-translations import --from i18next public/locales --locale de --group legacy"])]
    [CommandResult("runic.translations.tool/1", typeof(TranslationsToolCommandJsonContext))]
    internal static CommandOutcome<TranslationsToolCommandResult> Import(
        [FromServices] ITranslationsToolCommandOperations operations,
        [Argument(Description = "The .resx file, or the i18next JSON file or locales directory.", ValueName = "path")] string path,
        [Option("--from", Required = true, ValueName = "format", Choices = ["resx", "i18next"], Description = "The source format: resx or i18next.")] string from,
        [Option("--project", Description = "Translations directory or its runic.json. Defaults to ./runic.json, then ./translations/runic.json.", ValueName = "directory")] string? project,
        [Option("--locale", AllowMultipleValues = true, ValueName = "locale", Description = "Import only this declared locale; repeatable. Names the locale of a file whose name does not.")] IReadOnlyList<string> locales,
        [Option("--group", ValueName = "name", Description = "RMF2 group for new messages, written to <group>/.")] string? group,
        [Option("--overwrite", Description = "Replace the text of messages that already exist; without it they are kept.")] bool overwrite,
        [Option("--dry-run", Description = "Report the outcome without writing files.")] bool dryRun) =>
        operations.Execute(new("import", Project: project, Locales: locales, DryRun: dryRun) { From = from, Source = path, Group = group, Overwrite = overwrite });
}
