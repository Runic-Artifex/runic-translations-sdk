using System.Collections.Generic;
using Runic.CommandLine;

namespace Runic.Translations.Tool;

/// <summary><c>runic-translations xliff export|import</c>: XLIFF 2.1 interchange with translators and TMS tools.</summary>
[CommandGroup(Group, Description = "Exchange translations with translators and TMS tools as XLIFF 2.1.")]
internal static class XliffCommandModule
{
    internal const string Group = "xliff";
    private const string ProjectDescription = "Translations directory or its runic.json. Defaults to ./runic.json, then ./translations/runic.json.";
    private const string ExitCodes = "Exit codes: 0 success, also when single units were skipped or refused (unless --fail-on refused); 1 command failure, such as an inaccessible file; 2 invalid invocation, translation diagnostics, a document that cannot be imported, or a refused unit with --fail-on refused; 3 internal failure; 130 cancelled.";

    [Command("xliff export", Description = "Write one XLIFF 2.1 document per translated locale.",
        LongDescription = "Writes <catalog>.<locale>.xliff for every locale except the base locale, or only the --locale ones. Placeholders are <ph> elements with their MF2 expression. Messages with selectors, declarations or markup are exported as translate=\"no\". Review state comes from <catalog>.review.json beside runic.json, or --review; a stale approval is exported as needs-review and reported.\n\n" + ExitCodes,
        Examples = ["runic-translations xliff export --output xliff", "runic-translations xliff export --project src/App/translations --output xliff --locale de"])]
    [CommandResult("runic.translations.tool/1", typeof(TranslationsToolCommandJsonContext))]
    internal static CommandOutcome<TranslationsToolCommandResult> Export(
        [FromServices] ITranslationsToolCommandOperations operations,
        [Option("--project", Description = ProjectDescription, ValueName = "directory")] string? project,
        [Option("--output", Required = true, Description = "Directory to write the XLIFF documents to.", ValueName = "directory")] string output,
        [Option("--locale", AllowMultipleValues = true, ValueName = "locale", Description = "Export only this locale; repeatable.")] IReadOnlyList<string> locales,
        [Option("--review", ValueName = "path", Description = "Review file to read instead of <catalog>.review.json beside runic.json.")] string? review) =>
        operations.Execute(new("xliff export", Project: project, Output: output, Locales: locales) { Review = review });

    [Command("xliff import", Description = "Import a translated XLIFF document into the .rmf2 or .mf2 sources.",
        LongDescription = "Applies each translated unit to the target locale's sources; untranslated, empty and selector units are skipped and invalid units refused, each with a reason, and the rest is applied. A unit whose source changed since export is applied as needs-review, or refused with --source-changed refuse. Updates <catalog>.review.json when it exists or --review is given. In CI, --fail-on refused makes any refused unit fail the command, and then nothing is written.\n\n" + ExitCodes,
        Examples = ["runic-translations xliff import --source xliff/app.de.xliff --dry-run", "runic-translations xliff import --project translations --source app.de.xliff --source-changed refuse", "runic-translations xliff import --source app.de.xliff --fail-on refused"])]
    [CommandResult("runic.translations.tool/1", typeof(TranslationsToolCommandJsonContext))]
    internal static CommandOutcome<TranslationsToolCommandResult> Import(
        [FromServices] ITranslationsToolCommandOperations operations,
        [Option("--project", Description = ProjectDescription, ValueName = "directory")] string? project,
        [Option("--source", Required = true, ValueName = "path", Description = "The XLIFF document to import.")] string source,
        [Option("--source-changed", ValueName = "policy", Description = "needs-review (default) applies a unit whose source changed since export as needs-review; refuse refuses it.")] string? sourceChanged,
        [Option("--review", Description = "Write review state to <catalog>.review.json even when it does not exist yet.")] bool review,
        [Option("--fail-on", ValueName = "condition", Description = "refused: fail (exit 2) and write nothing when any unit is refused. By default refused units are warnings and the rest is applied.")] string? failOn,
        [Option("--dry-run", Description = "Report the outcome without writing files.")] bool dryRun) =>
        operations.Execute(new("xliff import", Project: project, DryRun: dryRun) { Source = source, SourceChanged = sourceChanged, WriteReview = review, FailOn = failOn });
}

/// <summary><c>runic-translations review mark</c>: the SDK-owned review file, <c>&lt;catalog&gt;.review.json</c>.</summary>
[CommandGroup(Group, Description = "Record the review state of translations in <catalog>.review.json.")]
internal static class ReviewCommandModule
{
    internal const string Group = "review";

    [Command("review mark", Description = "Set the review state of translations against the current source.",
        LongDescription = "Writes <catalog>.review.json beside runic.json (created when missing). Each entry records the per-unit fingerprint of the current source, so validate warns (RTR0090) when the source changes later.\n\nExit codes: 0 success; 1 command failure, such as an inaccessible file; 2 invalid invocation or translation diagnostics; 3 internal failure; 130 cancelled.",
        Examples = ["runic-translations review mark --key greeting --locale de --state approved", "runic-translations review mark --key title --key subtitle --locale fr --state needs-review --note \"Check the length.\""])]
    [CommandResult("runic.translations.tool/1", typeof(TranslationsToolCommandJsonContext))]
    internal static CommandOutcome<TranslationsToolCommandResult> Mark(
        [FromServices] ITranslationsToolCommandOperations operations,
        [Option("--project", Description = "Translations directory or its runic.json. Defaults to ./runic.json, then ./translations/runic.json.", ValueName = "directory")] string? project,
        [Option("--key", Required = true, AllowMultipleValues = true, ValueName = "key", Description = "Message key; repeatable.")] IReadOnlyList<string> keys,
        [Option("--locale", Required = true, ValueName = "locale", Description = "A translated locale of the catalog.")] string locale,
        [Option("--state", Required = true, ValueName = "state", Description = "draft, translated, needs-review or approved.")] string state,
        [Option("--note", ValueName = "text", Description = "A review note to store with the entries.")] string? note) =>
        operations.Execute(new("review mark", Project: project, Locales: [locale]) { Keys = keys, State = state, Note = note });
}
