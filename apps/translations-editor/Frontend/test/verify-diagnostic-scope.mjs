// The validation panel's scoping (#225 P6, P12): diagnostics are placed in their message (the backend's row, else the
// entry their span starts in) with a line inside that message; the selected message's diagnostics come first, errors
// before warnings; every other error stays visible (any error blocks saving); the other warnings are collapsed.
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { plugin } from "bun";
import { compile, compileModule } from "svelte/compiler";
import ts from "typescript";

globalThis.__runicTestUiText = {
  text: (key, values) => values === undefined ? `[${key}]` : `[${key} ${Object.entries(values).map(([name, value]) => `${name}=${value}`).join(" ")}]`,
};
plugin({
  name: "svelte-server-components",
  setup(build) {
    build.module("virtual:runic-translations/editor", () => ({ exports: { m: {} }, loader: "object" }));
    build.onLoad({ filter: /\/src\/lib\/ui-text\.ts$/ }, async ({ path }) => ({
      contents: (await readFile(path, "utf8")).replace("return useUiText();", "return globalThis.__runicTestUiText;"),
      loader: "ts",
    }));
    build.onLoad({ filter: /\.svelte\.(?:js|ts)$/ }, async ({ path }) => {
      const source = await readFile(path, "utf8");
      const javascript = path.endsWith(".ts") ? ts.transpileModule(source, {
        compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022, verbatimModuleSyntax: false }, fileName: path,
      }).outputText : source;
      return { contents: compileModule(javascript, { filename: path, generate: "server", dev: false }).js.code, loader: "js" };
    });
    build.onLoad({ filter: /\.svelte$/ }, async ({ path }) => {
      const compiled = compile(await readFile(path, "utf8"), { filename: path, generate: "server", dev: false });
      return { contents: compiled.js.code, loader: "js" };
    });
  },
});

const { errorsFirst, groupDiagnostics, messageDisplayName, placeDiagnostics } = await import("../src/lib/diagnostic-scope.ts");
const { render } = await import("svelte/server");
const { default: ValidationPanel } = await import("../src/lib/ValidationPanel.svelte");

const bytes = (text) => new TextEncoder().encode(text).length;
// de.rmf2: two messages; the second spans three lines and has non-ASCII text before it, so byte and UTF-16 offsets differ.
const prefix = "# Größe 🦊\nnotes {\n  title = Notizen\n  deleteConfirm =\n    .input {$count :integer}\n    .match $count\n    * {{Löschen {$count :nosuch}}}\n}\n";
const titleStart = bytes(prefix.slice(0, prefix.indexOf("Notizen")));
const confirmStart = bytes(prefix.slice(0, prefix.indexOf(".input")));
const confirmLength = bytes(prefix.slice(prefix.indexOf(".input"), prefix.indexOf("}}}") + 3));
const nosuch = bytes(prefix.slice(0, prefix.indexOf(":nosuch")));
const entry = (key, path, start, length) => ({ key, path, content: "", valueStartByte: start, valueLengthBytes: length });
const deDocument = {
  path: "translations/de.rmf2", content: prefix, revision: "r", isManifest: false, isMalformed: false, locale: "de",
  entries: [entry("notes_title", ["notes", "title"], titleStart, bytes("Notizen")), entry("notes_deleteConfirm", ["notes", "deleteConfirm"], confirmStart, confirmLength)],
};
const manifest = { path: "runic.json", content: "{}", revision: "m", isManifest: true, isMalformed: false };
const sources = { documents: [deDocument, manifest] };
const diagnostic = (id, severity, extra = {}) => ({
  id, severity, message: id, path: "translations/de.rmf2", line: 1, column: 1, endLine: 1, endColumn: 2, ...extra,
});
const span = (startByte) => ({ startByte, lengthBytes: 1, startUtf16: 0, lengthUtf16: 1, startLine: 0, startCharacter: 0, endLine: 0, endCharacter: 1 });

const diagnostics = [
  diagnostic("RTR0010", "warning", { key: "notes_title", locale: "fr", messagePath: ["notes", "title"], path: "translations/fr.rmf2", line: 3, column: 3 }),
  // A syntax error inside deleteConfirm's text: no row from the backend; its span places it, on the message's 3rd line.
  diagnostic("RTR0201", "error", { line: 7, column: 22, span: span(nosuch) }),
  diagnostic("RTR0096", "warning", { key: "notes_deleteConfirm", locale: "de", messagePath: ["notes", "deleteConfirm"], line: 4, column: 3, span: span(confirmStart) }),
  diagnostic("RTR0001", "warning", { path: "runic.json" }),
  diagnostic("RTR0002", "error", { path: "runic.json" }),
  diagnostic("RTR0094", "warning", { key: "notes_title", locale: "de", line: 3, column: 11, span: span(titleStart) }),
  // An error in another message, without a span.
  diagnostic("RTR0070", "error", { key: "notes_title", locale: "de", messagePath: ["notes", "title"], line: 3, column: 11 }),
];
const placed = placeDiagnostics(diagnostics, sources);
assert.deepEqual(placed.map(({ key, locale, message, messageLine }) => [key, locale, message, messageLine]), [
  ["notes_title", "fr", "notes.title", undefined],
  ["notes_deleteConfirm", "de", "notes.deleteConfirm", 3],
  ["notes_deleteConfirm", "de", "notes.deleteConfirm", 1],
  [undefined, undefined, undefined, undefined],
  [undefined, undefined, undefined, undefined],
  // No messagePath from the backend: the entry's path names it.
  ["notes_title", "de", "notes.title", 1],
  ["notes_title", "de", "notes.title", undefined],
], "Each diagnostic is placed in its message, with the line inside the message's text.");

// Stale entries (the draft changed after they were parsed) give no span placement and no message line.
const stale = placeDiagnostics(diagnostics, { documents: [deDocument], drafts: { "translations/de.rmf2": `${prefix}x = y\n` } });
assert.deepEqual([stale[1].key, stale[2].key, stale[2].messageLine], [undefined, "notes_deleteConfirm", undefined]);
// A draft's parsed entries are used while they were read from the draft's content.
const draft = `\n${prefix}`;
const shifted = placeDiagnostics([diagnostic("RTR0201", "error", { span: span(nosuch + 1) })], {
  documents: [deDocument], drafts: { [deDocument.path]: draft },
  parsed: { [deDocument.path]: { content: draft, entries: deDocument.entries.map((item) => ({ ...item, valueStartByte: item.valueStartByte + 1 })) } },
});
assert.deepEqual([shifted[0].key, shifted[0].messageLine], ["notes_deleteConfirm", 3]);
assert.deepEqual(placeDiagnostics(diagnostics.slice(0, 1), undefined).map((item) => item.message), ["notes.title"], "Without documents the backend's path still names the message.");

// Grouping: the selected message in the selected locale first; every other error is pinned; only warnings are collapsed.
const groups = groupDiagnostics(placed, { key: "notes_deleteConfirm", locale: "de" });
assert.deepEqual(groups.message.map((item) => item.diagnostic.id), ["RTR0201", "RTR0096"], "The selected message's error comes before its warning.");
assert.deepEqual(groups.blocking.map((item) => item.diagnostic.id), ["RTR0002", "RTR0070"], "Errors of other messages and about no message are pinned.");
assert.deepEqual(groups.other.map((item) => item.diagnostic.id), ["RTR0010", "RTR0001", "RTR0094"]);
const french = groupDiagnostics(placed, { key: "notes_title", locale: "fr" });
assert.deepEqual(french.message.map((item) => item.diagnostic.id), ["RTR0010"], "Another locale's diagnostics of the same message are not this message's.");
assert.deepEqual(french.blocking.map((item) => item.diagnostic.id), ["RTR0201", "RTR0002", "RTR0070"]);
assert.deepEqual(french.other.map((item) => item.diagnostic.id), ["RTR0096", "RTR0001", "RTR0094"], "Only warnings are collapsed.");
const noErrors = groupDiagnostics(placed.filter((item) => item.diagnostic.severity === "warning"), { key: "notes_title", locale: "fr" });
assert.deepEqual([noErrors.blocking, noErrors.other.length], [[], 3]);
assert.deepEqual(groupDiagnostics(placed, undefined).message, []);
assert.deepEqual(errorsFirst(diagnostics, (item) => item).map((item) => item.id), ["RTR0201", "RTR0002", "RTR0070", "RTR0010", "RTR0096", "RTR0001", "RTR0094"]);
assert.equal(messageDisplayName("notes_deleteConfirm", sources), "notes.deleteConfirm");
assert.equal(messageDisplayName("unknown_key", sources), "unknown_key");

// The rendered panel.
const html = (props) => render(ValidationPanel, { props }).body.replace(/<!--[^>]*-->/g, "");
const text = (markup) => markup.replace(/<[^>]+>/g, " ").replace(/\s+/g, " ").trim();
const base = {
  busy: false, diagnostics, clientError: undefined, errorCount: 3, warningCount: 4,
  validLabel: "valid", invalidLabel: "invalid", diagnosticsLabel: "Diagnostics", schemaVersion: 1, sources, onselect() {},
};
const scoped = html({ ...base, selection: { key: "notes_deleteConfirm", locale: "de" } });
const section = (name) => new RegExp(`<section[^>]*data-testid="diagnostics-${name}"[^>]*>([\\s\\S]*?)</section>`).exec(scoped)?.[1] ?? "";
const message = section("message");
assert.match(scoped, /<section[^>]*aria-labelledby="([^"]+)-message"[^>]*data-testid="diagnostics-message"/, "The message's diagnostics are a labelled region.");
assert.match(message, /<h3[^>]*>\s*\[ui_validation_message_heading message=notes\.deleteConfirm locale=de\]\s*<\/h3>/);
assert.deepEqual([...message.matchAll(/data-severity="(\w+)"/g)].map(([, severity]) => severity), ["error", "warning"], "Errors come before warnings.");
assert.ok(text(message).indexOf("RTR0201") < text(message).indexOf("RTR0096"));
assert.match(text(message), /RTR0201 RTR0201 \[ui_validation_place_line line=3\] translations\/de\.rmf2:7:22/, "A line inside the message, the file position second.");
assert.match(message, /title="\[ui_validation_file_position path=translations\/de\.rmf2 line=7 column=22\]"/, "The file position is the tooltip.");
assert.match(scoped, /<section[^>]*aria-labelledby="[^"]+-blocking"[^>]*data-testid="diagnostics-blocking"/);
assert.match(text(section("blocking")), /^\[ui_validation_blocking_heading\] RTR0002 RTR0002 runic\.json:1:1 RTR0070 RTR0070 \[ui_validation_place_message message=notes\.title locale=de\] translations\/de\.rmf2:3:11$/,
  "Errors about no message and in other messages are visible without expanding, each naming its message or file.");
assert.deepEqual([...section("blocking").matchAll(/data-severity="(\w+)"/g)].map(([, severity]) => severity), ["error", "error"]);
const other = section("other");
assert.match(other, /<button[^>]*aria-expanded="false"[^>]*aria-controls="([^"]+)-other"[^>]*>[\s\S]*\[ui_validation_other_warnings_heading count=3\]/, "The other warnings are a collapsed disclosure button.");
assert.match(other, /<div[^>]*id="[^"]+-other"[^>]*hidden/, "Other warnings are collapsed.");
// D074.1: a disclosure is neither paint nor a state; it is ink inside the error-coloured panel.
assert.match(other, /<button[^>]*class="[^"]*\btext-foreground\b[^"]*"[^>]*data-testid="diagnostics-other-toggle"/, "The other-warnings disclosure is not ink.");
assert.doesNotMatch(/<button[^>]*data-testid="diagnostics-other-toggle"[^>]*>/.exec(other)?.[0] ?? "", /(?:^|[\s"])text-(?:destructive|warning|paint)\b/, "The other-warnings disclosure is coloured as a state.");
assert.doesNotMatch(other, /RTR0010|RTR0094/, "Collapsed issues are not rendered.");
assert.match(text(scoped), /Diagnostics · \[ui_count_errors count=3\] · \[ui_count_warnings count=4\] · \[ui_validation_message_counts errors=\[ui_count_errors count=1\] warnings=\[ui_count_warnings count=1\]\]/,
  "The summary keeps the project counts and adds this message's.");

// Without a selection (the repair dialog) every diagnostic is listed, errors first, each naming its message.
const flat = html(base);
assert.doesNotMatch(flat, /data-testid="diagnostics-(?:message|other)"/);
assert.deepEqual([...flat.matchAll(/data-severity="(\w+)"/g)].map(([, severity]) => severity), ["error", "error", "error", "warning", "warning", "warning", "warning"]);
assert.match(text(flat), /RTR0010 \[ui_validation_place_message message=notes\.title locale=fr\] translations\/fr\.rmf2:3:3/);
assert.match(text(flat), /RTR0201 \[ui_validation_place_message_line message=notes\.deleteConfirm locale=de line=3\]/);
assert.doesNotMatch(text(flat), /ui_validation_message_counts/);

// Without errors outside the selected message, the pinned section is not rendered.
const warningsOnly = html({ ...base, diagnostics: diagnostics.filter((item) => item.severity === "warning"), errorCount: 0,
  selection: { key: "notes_deleteConfirm", locale: "de" } });
assert.doesNotMatch(warningsOnly, /data-testid="diagnostics-blocking"/);
assert.match(warningsOnly, /\[ui_validation_other_warnings_heading count=3\]/);

const source = await readFile(new URL("../src/lib/ValidationPanel.svelte", import.meta.url), "utf8");
const warnings = compile(source, { generate: "server" }).warnings.filter((warning) => warning.code.startsWith("a11y_"));
assert.deepEqual(warnings, [], "ValidationPanel.svelte has compiler-reported accessibility violations.");
console.log("PASS: the validation panel lists the selected message's diagnostics first, errors before warnings, with message lines, every other error pinned, and the other warnings collapsed.");
