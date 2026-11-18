using System.Text.Json;
using Runic.Translations.Compiler;
using static Runic.Translations.Editor.WorkspaceFiles;
using static Runic.Translations.Editor.WorkspaceStateReader;

namespace Runic.Translations.Editor;

/// <summary>The message entries of a document (key, text, composer projection, context, limits) for the bridge contract.</summary>
internal sealed class EntryProjector
{
    private readonly WorkspaceFiles _files;

    internal EntryProjector(WorkspaceFiles files) => _files = files;

    internal EditorMessageEntry[] ReadEntries(string path, string content, WorkspaceState state) =>
        ReadEntries(path, content, state.Compilation, BaseResources(state));

    internal static EditorMessageProjection EditorProjection(Rmf2AuthoringProjection projection) =>
        new(projection.Revision, projection.Supported, projection.Reason,
            projection.Inputs.Select(input => new EditorMessageInput(input.Name, input.Type, input.Function, input.Declared)).ToArray(),
            projection.Selectors, projection.SelectorFunctions, projection.Variants.Select(variant => new EditorMessageVariant(variant.Id, variant.Keys, variant.Pattern, variant.StartByte, variant.LengthBytes)).ToArray(),
            projection.PluralCategories, projection.SelectorPluralCategories, projection.CldrVersion, EditorMessageDrafts.IsEmpty(projection));

    internal EditorMessageEntry[] ReadEntries(string path, string content, Rmf2ProjectCompilation compilation,
        IReadOnlyDictionary<string, Rmf2TranslationV5> baseResources)
    {
        var resources = compilation.Project?.Locales.SelectMany(locale => locale.DirectResources).ToArray() ?? [];
        EditorMessageEntry Entry(IReadOnlyList<string> logicalPath, string message, int start, int length, Rmf2ResourceNode? node)
        {
            string key = Rmf2MessageKeys.Canonical(logicalPath);
            var linked = resources.FirstOrDefault(resource => resource.Key == key && resource.SourceLocation.Path == path);
            var source = Source(path, message);
            string locale = linked?.ContentLocale ?? _files.Rmf2Catalog().Locale(path);
            var projection = Rmf2AuthoringService.Project(source, locale, linked?.Message);
            var semantic = Rmf2AuthoringService.Semantic(source, linked?.Message);
            var examples = new List<IReadOnlyDictionary<string, string>>();
            foreach (string property in node?.Properties ?? [])
            {
                if (!property.StartsWith("example ", StringComparison.Ordinal)) continue;
                try
                {
                    using var json = JsonDocument.Parse(property[8..]);
                    if (json.RootElement.ValueKind == JsonValueKind.Object)
                        examples.Add(json.RootElement.EnumerateObject().ToDictionary(item => item.Name,
                            item => item.Value.ValueKind == JsonValueKind.String ? item.Value.GetString()! : item.Value.GetRawText(), StringComparer.Ordinal));
                }
                catch (JsonException) { /* Compiler diagnostics retain invalid metadata; no invented sample is emitted. */ }
            }
            var context = new EditorMessageContext(node?.Comments ?? [],
                (node?.Properties ?? []).Where(property => property.StartsWith("tag ", StringComparison.Ordinal) || property.StartsWith("tags ", StringComparison.Ordinal))
                    .SelectMany(property => property[(property.IndexOf(' ') + 1)..].Split([' ', ','], StringSplitOptions.RemoveEmptyEntries)).Distinct(StringComparer.Ordinal).ToArray(), examples);
            int? maxLength = TranslationCompiler.EffectiveMaxLengthV5(
                baseResources.TryGetValue(key, out var baseResource) ? TranslationCompiler.MaxLengthV5(baseResource.Properties) : null,
                TranslationCompiler.MaxLengthV5(node?.Properties ?? linked?.Properties ?? []));
            return new(key, message, start, length, EditorProjection(projection), context,
                new EditorMessageSemantic(semantic.Text, semantic.Placeholders, semantic.Slots, semantic.Supported, semantic.HasBoundaryWhitespace),
                logicalPath.ToArray(), maxLength);
        }
        if (path.EndsWith(".rmf2", StringComparison.OrdinalIgnoreCase))
        {
            var workspace = _files.Rmf2Catalog();
            return Rmf2ResourceReader.Read(Source(path, content)).Nodes.Where(node => !node.IsGroup)
                .Select(node => Entry(workspace.LogicalPath(path, node), node.Message!, node.MessageByteMap[0], node.MessageByteMap[^1] - node.MessageByteMap[0], node)).ToArray();
        }
        if (path.EndsWith(".mf2", StringComparison.OrdinalIgnoreCase))
        {
            // A direct message document is one message; its path is the compiled contract's, else the file name.
            string? key = resources.FirstOrDefault(resource => resource.SourceLocation.Path == path)?.Key;
            IReadOnlyList<string>? logical = key is not null && compilation.Project is { } project && Rmf2MessageKeys.Paths(project).TryGetValue(key, out var found) ? found : null;
            return [Entry(logical ?? [key ?? Path.GetFileNameWithoutExtension(path)], content, 0, StrictUtf8.GetByteCount(content), null)];
        }
        return [];
    }
}
