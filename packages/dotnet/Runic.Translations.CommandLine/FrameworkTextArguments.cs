using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace Runic.Translations.CommandLine;

// The argument names of every Runic.CommandLine framework key, in the order the framework passes them.
// This copies CommandTextKeys.All so the adapter works with every Runic.CommandLine release that has
// ICommandTextResolver; keep it in step with docs/guides/command-line/text-keys.md in runic-cli-sdk.
internal static class FrameworkTextArguments
{
    private static readonly string[] None = [];
    private static readonly string[] Option = ["option"];
    private static readonly string[] Parameter = ["parameter"];
    private static readonly string[] Prompt = ["prompt"];

    internal static FrozenDictionary<string, string[]> ByKey { get; } = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["help.usage"] = None,
        ["help.command-placeholder"] = None,
        ["help.options-placeholder"] = None,
        ["help.default-command"] = None,
        ["help.completion"] = None,
        ["help.commands"] = None,
        ["help.arguments"] = None,
        ["help.options"] = None,
        ["help.show-help"] = None,
        ["help.show-version"] = None,
        ["help.output-format"] = None,
        ["help.json-output-alias"] = Option,
        ["help.json-payload-output-alias"] = None,
        ["help.examples"] = None,
        ["help.required"] = None,
        ["help.default"] = None,
        ["help.choices"] = None,
        ["help.environment"] = None,
        ["help.existing"] = None,
        ["help.path-kind.file"] = None,
        ["help.path-kind.directory"] = None,
        ["help.minimum"] = None,
        ["help.maximum"] = None,
        ["help.requires"] = None,
        ["help.conflicts"] = None,
        ["completion.invalid-shell"] = None,
        ["prompts.select-hint"] = None,
        ["prompts.multi-select-hint"] = None,
        ["prompts.yes"] = None,
        ["prompts.no"] = None,
        ["prompts.choose-one"] = ["count"],
        ["prompts.choose-many"] = ["count"],
        ["prompts.answer-yes-no"] = ["yes", "no"],
        ["prompts.required"] = None,
        ["prompts.cancelled"] = None,
        ["prompts.done"] = None,
        ["faults.RCLI4000"] = None,
        // The built-in catalogs leave faults.RCLI4001 to its more specific diagnostics.prompt-* text.
        ["faults.RCLI4001"] = None,
        ["faults.RCLI5000"] = None,
        ["hints.RCLI5000"] = None,
        ["diagnostics.unknown-option"] = Option,
        ["diagnostics.unknown-option-suggestion"] = ["option", "suggestion"],
        ["diagnostics.unknown-command"] = ["command"],
        ["diagnostics.unknown-command-suggestion"] = ["command", "suggestion"],
        ["diagnostics.missing-option-value"] = Option,
        ["diagnostics.unexpected-option-value"] = Option,
        ["diagnostics.missing-argument"] = ["argument"],
        ["diagnostics.unexpected-argument"] = None,
        ["diagnostics.duplicate-option"] = Option,
        ["diagnostics.unsupported-short-bundle"] = None,
        ["diagnostics.invalid-output-mode"] = Option,
        ["diagnostics.conflicting-output-selector"] = ["option", "conflict"],
        ["diagnostics.transport-output-option-collision"] = None,
        ["diagnostics.missing-required-option"] = ["option", "command"],
        ["diagnostics.unexpected-root-help-argument"] = None,
        ["diagnostics.invalid-environment-value"] = ["option", "variable"],
        ["diagnostics.invalid-choice"] = ["parameter", "choices"],
        ["diagnostics.invalid-choice-hidden"] = Parameter,
        ["diagnostics.invalid-integer"] = Parameter,
        ["diagnostics.invalid-number"] = Parameter,
        ["diagnostics.invalid-guid"] = Parameter,
        ["diagnostics.invalid-boolean"] = Parameter,
        ["diagnostics.invalid-value"] = Parameter,
        ["diagnostics.validation-failed"] = Parameter,
        ["diagnostics.missing-value"] = Parameter,
        ["diagnostics.out-of-range"] = ["parameter", "minimum", "maximum"],
        ["diagnostics.below-minimum"] = ["parameter", "minimum"],
        ["diagnostics.above-maximum"] = ["parameter", "maximum"],
        ["diagnostics.option-requires"] = ["option", "required"],
        ["diagnostics.option-conflict"] = ["option", "conflict"],
        ["diagnostics.invalid-file-path"] = Parameter,
        ["diagnostics.invalid-directory-path"] = Parameter,
        ["diagnostics.file-not-found"] = Parameter,
        ["diagnostics.directory-not-found"] = Parameter,
        ["diagnostics.environment-value-source"] = ["parameter", "variable"],
        ["diagnostics.prompt-requires-terminal"] = Prompt,
        ["diagnostics.prompt-json-output"] = Prompt,
        ["diagnostics.prompt-disabled"] = Prompt,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    internal static string[] For(string key) => ByKey.TryGetValue(key, out string[]? names) ? names : None;
}
