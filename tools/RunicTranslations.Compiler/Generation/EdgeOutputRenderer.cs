using System;
using System.Collections.Generic;
using System.Text;

namespace RunicTranslations.Compiler.Generation;

internal static class EdgeOutputRenderer
{
    internal static TextResourceGeneratedOutput RenderLocale(CompiledTextCatalog catalog, string localeTag)
    {
        CompiledTextLocale? locale = null;
        for (int i = 0; i < catalog.Locales.Count; i++)
        {
            if (string.Equals(catalog.Locales[i].Tag, localeTag, StringComparison.Ordinal))
            {
                locale = catalog.Locales[i];
                break;
            }
        }
        if (locale is null)
            throw new ArgumentException("Locale '" + localeTag + "' is not a declared canonical locale of catalog '" + catalog.Id + "'.", nameof(localeTag));

        int artifactVersion = catalog.MessageGrammarVersion == 1
            ? TextResourceOutputRenderer.LocaleArtifactVersion
            : TextResourceOutputRenderer.LocaleArtifactV2Version;
        var json = new StringBuilder();
        json.Append("{\"artifactVersion\":").Append(artifactVersion)
            .Append(",\"messageGrammarVersion\":").Append(catalog.MessageGrammarVersion)
            .Append(",\"catalog\":").Append(GenerationSupport.JsonString(catalog.Id))
            .Append(",\"locale\":").Append(GenerationSupport.JsonString(locale.Tag))
            .Append(",\"contractFingerprint\":").Append(GenerationSupport.JsonString(catalog.Fingerprint))
            .Append(",\"messages\":{");
        IReadOnlyList<CompiledTextResource> resources = GenerationSupport.OrderedResources(locale.ResolvedResources);
        for (int i = 0; i < resources.Count; i++)
        {
            if (i > 0) json.Append(',');
            CompiledTextResource resource = resources[i];
            json.Append(GenerationSupport.JsonString(resource.Key)).Append(':');
            if (artifactVersion == TextResourceOutputRenderer.LocaleArtifactVersion)
            {
                json.Append("{\"pattern\":").Append(GenerationSupport.JsonString(resource.Pattern)).Append(",\"arguments\":");
                WriteJsonArguments(json, resource.Placeholders);
            }
            else
            {
                WriteMessageAst(json, resource);
            }
            json.Append('}');
        }
        json.Append("}}");
        return new TextResourceGeneratedOutput(
            TextResourceGeneratedOutputKind.LocaleJson,
            catalog.Id + "." + locale.Tag + ".locale-v" + artifactVersion + ".json",
            "application/json",
            json.ToString());
    }

    private static void WriteMessageAst(StringBuilder json, CompiledTextResource resource)
    {
        json.Append("{\"astVersion\":2,\"inputs\":{");
        IReadOnlyList<CompiledTextPlaceholder> placeholders = GenerationSupport.OrderedPlaceholders(resource.Placeholders);
        for (int index = 0; index < placeholders.Count; index++)
        {
            if (index != 0) json.Append(',');
            json.Append(GenerationSupport.JsonString(placeholders[index].Name)).Append(":{\"type\":")
                .Append(GenerationSupport.JsonString(GenerationSupport.JsonArgumentType(placeholders[index].Type)))
                .Append(",\"format\":").Append(GenerationSupport.JsonString(placeholders[index].Format)).Append('}');
        }
        json.Append("},\"selectors\":[");
        for (int index = 0; index < resource.Message.Selectors.Count; index++)
        {
            if (index != 0) json.Append(',');
            CompiledMessageSelector selector = resource.Message.Selectors[index];
            json.Append("{\"name\":").Append(GenerationSupport.JsonString(selector.Name))
                .Append(",\"input\":").Append(GenerationSupport.JsonString(selector.Input))
                .Append(",\"function\":").Append(GenerationSupport.JsonString(selector.Function)).Append('}');
        }
        json.Append("],\"variants\":[");
        if (resource.Message.IsVariant)
        {
            for (int index = 0; index < resource.Message.Variants.Count; index++)
            {
                if (index != 0) json.Append(',');
                CompiledMessageVariant variant = resource.Message.Variants[index];
                json.Append("{\"matches\":{");
                for (int selectorIndex = 0; selectorIndex < resource.Message.Selectors.Count; selectorIndex++)
                {
                    if (selectorIndex != 0) json.Append(',');
                    string name = resource.Message.Selectors[selectorIndex].Name;
                    json.Append(GenerationSupport.JsonString(name)).Append(':').Append(GenerationSupport.JsonString(variant.Matches[name]));
                }
                json.Append("},\"nodes\":");
                WriteNodes(json, variant.Pattern.Nodes);
                json.Append('}');
            }
        }
        else
        {
            json.Append("{\"matches\":{},\"nodes\":");
            WriteNodes(json, resource.Message.Nodes);
            json.Append('}');
        }
        json.Append(']');
    }

    private static void WriteNodes(StringBuilder json, IReadOnlyList<CompiledMessageNode> nodes)
    {
        json.Append('[');
        for (int index = 0; index < nodes.Count; index++)
        {
            if (index != 0) json.Append(',');
            CompiledMessageNode node = nodes[index];
            if (node is CompiledMessageText text)
                json.Append("{\"kind\":\"text\",\"value\":").Append(GenerationSupport.JsonString(text.Value)).Append('}');
            else if (node is CompiledMessageInput input)
                json.Append("{\"kind\":\"input\",\"input\":").Append(GenerationSupport.JsonString(input.Name)).Append('}');
            else if (node is CompiledMessageFormat format)
            {
                json.Append("{\"kind\":\"format\",\"input\":").Append(GenerationSupport.JsonString(format.Input))
                    .Append(",\"function\":").Append(GenerationSupport.JsonString(format.Function))
                    .Append(",\"format\":").Append(GenerationSupport.JsonString(format.Format));
                if (format.Unit is not null) json.Append(",\"unit\":").Append(GenerationSupport.JsonString(format.Unit));
                if (format.Numeric is not null) json.Append(",\"numeric\":").Append(GenerationSupport.JsonString(format.Numeric));
                json.Append('}');
            }
            else if (node is CompiledMessageMarkup markup)
            {
                json.Append("{\"kind\":\"markup\",\"name\":").Append(GenerationSupport.JsonString(markup.Name)).Append(",\"attributes\":{");
                int attributeIndex = 0;
                foreach (KeyValuePair<string, string> attribute in markup.Attributes)
                {
                    if (attributeIndex++ != 0) json.Append(',');
                    json.Append(GenerationSupport.JsonString(attribute.Key)).Append(':').Append(GenerationSupport.JsonString(attribute.Value));
                }
                json.Append("},\"children\":");
                WriteNodes(json, markup.Children);
                json.Append('}');
            }
        }
        json.Append(']');
    }

    internal static TextResourceGeneratedOutput RenderTemplateManifest(CompiledTextCatalog catalog)
    {
        var json = new StringBuilder();
        json.Append("{\"manifestVersion\":").Append(TextResourceOutputRenderer.TemplateManifestVersion)
            .Append(",\"messageGrammarVersion\":").Append(catalog.MessageGrammarVersion)
            .Append(",\"catalog\":").Append(GenerationSupport.JsonString(catalog.Id))
            .Append(",\"contractFingerprint\":").Append(GenerationSupport.JsonString(catalog.Fingerprint))
            .Append(",\"messages\":{");
        IReadOnlyList<CompiledTextResource> resources = GenerationSupport.OrderedResources(catalog.CanonicalResources);
        for (int i = 0; i < resources.Count; i++)
        {
            if (i > 0) json.Append(',');
            CompiledTextResource resource = resources[i];
            json.Append(GenerationSupport.JsonString(resource.Key)).Append(":{\"description\":")
                .Append(resource.Description is null ? "null" : GenerationSupport.JsonString(resource.Description))
                .Append(",\"since\":").Append(resource.Since is null ? "null" : GenerationSupport.JsonString(resource.Since))
                .Append(",\"deprecated\":").Append(resource.DeprecatedReason is null ? "null" : GenerationSupport.JsonString(resource.DeprecatedReason))
                .Append(",\"tags\":");
            WriteJsonStrings(json, resource.Tags);
            json.Append(",\"arguments\":");
            WriteJsonArguments(json, resource.Placeholders);
            json.Append('}');
        }
        json.Append("}}");
        return new TextResourceGeneratedOutput(
            TextResourceGeneratedOutputKind.TemplateManifestJson,
            catalog.Id + ".template-manifest-v1.json",
            "application/json",
            json.ToString());
    }

    internal static TextResourceGeneratedOutput RenderTypeScriptContract(CompiledTextCatalog catalog)
    {
        var writer = new GenerationWriter();
        IReadOnlyList<CompiledTextResource> resources = GenerationSupport.OrderedResources(catalog.CanonicalResources);
        string keyType = catalog.ClassName + "Key";
        string argumentsType = catalog.ClassName + "Arguments";
        string keyConstant = LowercaseFirst(catalog.ClassName) + "Keys";
        writer.Line("// <auto-generated />");
        writer.Line("// Translations TypeScript edge contract version " + TextResourceOutputRenderer.TypeScriptContractVersion + ".");
        writer.Blank();
        writer.Line("export declare const textResourceContractVersion: " + TextResourceOutputRenderer.TypeScriptContractVersion + ";");
        writer.Line("export declare const textResourceCatalog: " + GenerationSupport.JsonString(catalog.Id) + ";");
        writer.Line("export declare const textResourceContractFingerprint: " + GenerationSupport.JsonString(catalog.Fingerprint) + ";");
        writer.Blank();
        writer.Line("export type " + keyType + " =");
        writer.Indent();
        for (int i = 0; i < resources.Count; i++)
            writer.Line("| " + GenerationSupport.JsonString(resources[i].Key) + (i + 1 == resources.Count ? ";" : string.Empty));
        if (resources.Count == 0) writer.Line("never;");
        writer.Unindent();
        writer.Blank();
        writer.Line("export declare const " + keyConstant + ": {");
        writer.Indent();
        WriteTypeScriptMembers(writer, GenerationSupport.BuildTree(catalog), string.Empty);
        writer.Unindent();
        writer.Line("};");
        writer.Blank();
        writer.Line("export interface " + argumentsType + " {");
        writer.Indent();
        for (int i = 0; i < resources.Count; i++)
        {
            CompiledTextResource resource = resources[i];
            WriteTypeScriptDocumentation(writer, resource);
            IReadOnlyList<CompiledTextPlaceholder> placeholders = GenerationSupport.OrderedPlaceholders(resource.Placeholders);
            if (placeholders.Count == 0)
            {
                writer.Line(GenerationSupport.JsonString(resource.Key) + ": Readonly<Record<never, never>>;");
                continue;
            }
            writer.Line(GenerationSupport.JsonString(resource.Key) + ": Readonly<{");
            writer.Indent();
            for (int placeholderIndex = 0; placeholderIndex < placeholders.Count; placeholderIndex++)
            {
                CompiledTextPlaceholder placeholder = placeholders[placeholderIndex];
                writer.Line(GenerationSupport.JsonString(placeholder.Name) + ": " + GenerationSupport.TypeScriptType(placeholder.Type) + ";");
            }
            writer.Unindent();
            writer.Line("}>;");
        }
        writer.Unindent();
        writer.Line("}");
        writer.Blank();
        writer.Line("export type TextResourceArguments<K extends " + keyType + "> = " + argumentsType + "[K];");
        return new TextResourceGeneratedOutput(
            TextResourceGeneratedOutputKind.TypeScriptContract,
            catalog.Id + ".translations-v1.d.ts",
            "text/typescript",
            writer.ToString());
    }

    internal static TextResourceGeneratedOutput RenderAssetManifest(
        CompiledTextCatalog catalog,
        IEnumerable<TextResourceGeneratedOutput> selectedOutputs)
    {
        ArgumentNullException.ThrowIfNull(selectedOutputs);

        var assets = new List<TextResourceGeneratedOutput>();
        foreach (TextResourceGeneratedOutput output in selectedOutputs)
        {
            if (output is null)
                throw new ArgumentException("Selected outputs must not contain null.", nameof(selectedOutputs));
            if (output.Kind is TextResourceGeneratedOutputKind.LocaleJson or
                TextResourceGeneratedOutputKind.TemplateManifestJson or
                TextResourceGeneratedOutputKind.TypeScriptContract)
            {
                assets.Add(output);
            }
        }

        assets.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath));
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var json = new StringBuilder();
        json.Append("{\"assetManifestVersion\":").Append(TextResourceOutputRenderer.AssetManifestVersion)
            .Append(",\"catalog\":").Append(GenerationSupport.JsonString(catalog.Id))
            .Append(",\"assets\":[");
        for (int index = 0; index < assets.Count; index++)
        {
            TextResourceGeneratedOutput asset = assets[index];
            if (!paths.Add(asset.RelativePath))
                throw new ArgumentException("Selected outputs contain duplicate relative paths.", nameof(selectedOutputs));
            if (index > 0) json.Append(',');
            json.Append("{\"path\":").Append(GenerationSupport.JsonString(asset.RelativePath))
                .Append(",\"sha256\":").Append(GenerationSupport.JsonString(BareSha256(asset)))
                .Append(",\"byteLength\":").Append(asset.GetUtf8Bytes().Length)
                .Append(",\"mediaType\":").Append(GenerationSupport.JsonString(asset.MediaType))
                .Append(",\"locale\":");
            if (asset.Kind == TextResourceGeneratedOutputKind.LocaleJson)
                json.Append(GenerationSupport.JsonString(LocaleFor(catalog, asset.RelativePath)));
            else
                json.Append("null");
            json.Append('}');
        }

        json.Append("]}");
        return new TextResourceGeneratedOutput(
            TextResourceGeneratedOutputKind.AssetManifestJson,
            catalog.Id + ".asset-manifest-v1.json",
            "application/json",
            json.ToString());
    }

    private static string BareSha256(TextResourceGeneratedOutput output)
    {
        const string Prefix = "sha256:";
        if (!output.Sha256.StartsWith(Prefix, StringComparison.Ordinal) || output.Sha256.Length != Prefix.Length + 64)
            throw new ArgumentException("Selected output has an invalid SHA-256 value.", nameof(output));
        return output.Sha256.Substring(Prefix.Length);
    }

    private static string LocaleFor(CompiledTextCatalog catalog, string relativePath)
    {
        for (int index = 0; index < catalog.Locales.Count; index++)
        {
            string locale = catalog.Locales[index].Tag;
            if (string.Equals(relativePath, catalog.Id + "." + locale + ".locale-v1.json", StringComparison.Ordinal))
                return locale;
        }

        throw new ArgumentException("Locale output path is not canonical for the supplied catalog.", nameof(relativePath));
    }

    private static void WriteJsonArguments(StringBuilder json, IReadOnlyList<CompiledTextPlaceholder> placeholders)
    {
        IReadOnlyList<CompiledTextPlaceholder> ordered = GenerationSupport.OrderedPlaceholders(placeholders);
        json.Append('[');
        for (int i = 0; i < ordered.Count; i++)
        {
            if (i > 0) json.Append(',');
            CompiledTextPlaceholder placeholder = ordered[i];
            json.Append("{\"name\":").Append(GenerationSupport.JsonString(placeholder.Name))
                .Append(",\"type\":").Append(GenerationSupport.JsonString(GenerationSupport.JsonArgumentType(placeholder.Type)))
                .Append(",\"format\":").Append(GenerationSupport.JsonString(placeholder.Format)).Append('}');
        }
        json.Append(']');
    }

    private static void WriteJsonStrings(StringBuilder json, IReadOnlyList<string> values)
    {
        var ordered = new List<string>(values.Count);
        for (int i = 0; i < values.Count; i++) ordered.Add(values[i]);
        ordered.Sort(StringComparer.Ordinal);
        json.Append('[');
        for (int i = 0; i < ordered.Count; i++)
        {
            if (i > 0) json.Append(',');
            json.Append(GenerationSupport.JsonString(ordered[i]));
        }
        json.Append(']');
    }

    private static void WriteTypeScriptMembers(GenerationWriter writer, ResourceTreeNode node, string dottedPrefix)
    {
        foreach (KeyValuePair<string, ResourceTreeNode> child in node.Children)
        {
            string dotted = dottedPrefix.Length == 0 ? child.Key : dottedPrefix + "." + child.Key;
            if (child.Value.Resource is not null)
                writer.Line("readonly " + GenerationSupport.JsonString(child.Key) + ": " + GenerationSupport.JsonString(dotted) + ";");
            else
            {
                writer.Line("readonly " + GenerationSupport.JsonString(child.Key) + ": {");
                writer.Indent();
                WriteTypeScriptMembers(writer, child.Value, dotted);
                writer.Unindent();
                writer.Line("};");
            }
        }
    }

    private static void WriteTypeScriptDocumentation(GenerationWriter writer, CompiledTextResource resource)
    {
        if (resource.Description is null && resource.DeprecatedReason is null) return;
        writer.Line("/**");
        if (resource.Description is not null) writer.Line(" * " + SafeTypeScriptDocumentation(resource.Description));
        if (resource.DeprecatedReason is not null) writer.Line(" * @deprecated " + SafeTypeScriptDocumentation(resource.DeprecatedReason));
        writer.Line(" */");
    }

    private static string SafeTypeScriptDocumentation(string value)
    {
        string normalized = value.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Replace("*/", "* /");
        var result = new StringBuilder(normalized.Length);
        for (int i = 0; i < normalized.Length; i++)
        {
            char character = normalized[i];
            bool unpairedHigh = char.IsHighSurrogate(character) &&
                (i + 1 >= normalized.Length || !char.IsLowSurrogate(normalized[i + 1]));
            bool unpairedLow = char.IsLowSurrogate(character) &&
                (i == 0 || !char.IsHighSurrogate(normalized[i - 1]));
            if ((character < ' ' && character != '\t') || unpairedHigh || unpairedLow)
                result.Append(character < ' ' ? ' ' : '\uFFFD');
            else
                result.Append(character);
        }
        return result.ToString();
    }

    private static string LowercaseFirst(string value)
    {
        if (value.Length == 0 || (value[0] < 'A' || value[0] > 'Z')) return value;
        return char.ToLowerInvariant(value[0]) + value.Substring(1);
    }
}
