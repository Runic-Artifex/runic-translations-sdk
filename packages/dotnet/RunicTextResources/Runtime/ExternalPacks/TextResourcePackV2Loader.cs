using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace RunicTextResources;

internal static class TextResourcePackV2Loader
{
    internal static VerifiedExternalTextResourcePack Parse(
        ReadOnlyMemory<byte> content,
        TextResourcePackContract contract,
        TextResourcePackLimits limits,
        CancellationToken cancellationToken)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(content, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = limits.MaximumDepth,
            });
            Dictionary<string, JsonElement> root = Members(document.RootElement,
                ["artifactVersion", "messageGrammarVersion", "catalog", "locale", "contractFingerprint", "messages"]);
            if (Integer(root["artifactVersion"]) != 2) throw Error("The external pack artifact version is unsupported.", TextResourcePackFailureReason.ArtifactVersionMismatch);
            if (Integer(root["messageGrammarVersion"]) != 2 || contract.MessageGrammarVersion != 2)
                throw Error("The external pack message grammar version is unsupported.", TextResourcePackFailureReason.MessageGrammarVersionMismatch);
            string catalog = String(root["catalog"]);
            string locale = String(root["locale"]);
            string fingerprint = String(root["contractFingerprint"]);
            if (!string.Equals(catalog, contract.Catalog, StringComparison.Ordinal)) throw Error("The external pack catalog does not match the generated contract.", TextResourcePackFailureReason.CatalogMismatch);
            if (!string.Equals(locale, contract.Locale, StringComparison.Ordinal)) throw Error("The external pack locale does not match the generated contract.", TextResourcePackFailureReason.LocaleMismatch);
            if (!string.Equals(fingerprint, contract.ContractFingerprint, StringComparison.Ordinal)) throw Error("The external pack fingerprint does not match the generated contract.", TextResourcePackFailureReason.ContractFingerprintMismatch);
            if (root["messages"].ValueKind != JsonValueKind.Object) throw Error("The external pack messages value must be an object.");

            var messages = new List<VerifiedTextResourcePackMessage>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in root["messages"].EnumerateObject())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!keys.Add(property.Name)) throw Error("The external pack contains duplicate message key '" + property.Name + "'.");
                if (messages.Count >= limits.MaximumMessages) throw Limit("The external pack exceeds the configured message limit.");
                if (!contract.TryGetMessage(property.Name, out TextResourcePackMessageContract messageContract))
                    throw Error("The external pack contains unknown message key '" + property.Name + "'.", TextResourcePackFailureReason.UnknownKey);
                messages.Add(ReadMessage(property.Value, messageContract, limits));
            }
            messages.Sort(static (left, right) => string.CompareOrdinal(left.Key.Name, right.Key.Name));
            return new VerifiedExternalTextResourcePack(catalog, locale, fingerprint, messages.ToArray());
        }
        catch (TextResourcePackException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw Error("The external pack is incomplete or contains malformed JSON near byte " + exception.BytePositionInLine + ".");
        }
    }

    private static VerifiedTextResourcePackMessage ReadMessage(
        JsonElement value,
        TextResourcePackMessageContract contract,
        TextResourcePackLimits limits)
    {
        Dictionary<string, JsonElement> message = Members(value, ["astVersion", "inputs", "selectors", "variants"]);
        if (Integer(message["astVersion"]) != 2) throw Error("Message '" + contract.Key.Name + "' has an unsupported AST version.");
        ReadInputs(message["inputs"], contract);
        CompiledTextMessageSelector[] selectors = ReadSelectors(message["selectors"], contract);
        CompiledTextMessageVariant[] variants = ReadVariants(message["variants"], contract, selectors, limits);
        CompiledTextMessage compiled;
        try { compiled = new CompiledTextMessage(Array.Empty<CompiledTextMessageNode>(), selectors, variants); }
        catch (ArgumentException) { throw Error("Message '" + contract.Key.Name + "' contains an invalid normalized AST.", TextResourcePackFailureReason.MalformedPattern); }
        TextResourcePlaceholderDescriptor[] descriptors = Descriptors(contract.Arguments);
        if (!CompiledTextMessageRuntime.MatchesContract(compiled, descriptors))
            throw Error("Message '" + contract.Key.Name + "' does not match its generated argument contract.", TextResourcePackFailureReason.ArgumentContractMismatch);
        string compatibility = CompatibilityPattern(variants[^1].NodeArray);
        return new VerifiedTextResourcePackMessage(contract.Key, compatibility, compiled);
    }

    private static void ReadInputs(JsonElement value, TextResourcePackMessageContract contract)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Error("A message input contract must be an object.");
        var actual = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (JsonProperty property in value.EnumerateObject())
            if (!actual.TryAdd(property.Name, property.Value)) throw Error("A message contains a duplicate input declaration.");
        if (actual.Count != contract.Arguments.Count) throw ContractMismatch(contract.Key.Name);
        for (int index = 0; index < contract.Arguments.Count; index++)
        {
            TextResourcePackArgumentContract expected = contract.Arguments[index];
            if (!actual.TryGetValue(expected.Name, out JsonElement descriptor)) throw ContractMismatch(contract.Key.Name);
            Dictionary<string, JsonElement> fields = Members(descriptor, ["type", "format"]);
            if (String(fields["type"]) != TypeName(expected.Type) || String(fields["format"]) != FormatName(expected.Format))
                throw ContractMismatch(contract.Key.Name);
        }
    }

    private static CompiledTextMessageSelector[] ReadSelectors(JsonElement value, TextResourcePackMessageContract contract)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > 16) throw Error("A message selector list is invalid.");
        var selectors = new List<CompiledTextMessageSelector>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement item in value.EnumerateArray())
        {
            Dictionary<string, JsonElement> fields = Members(item, ["name", "input", "function"]);
            string name = String(fields["name"]);
            string input = String(fields["input"]);
            string function = String(fields["function"]);
            TextResourcePackArgumentContract argument = FindArgument(contract, input);
            if (!TextResourcePackValidation.IsIdentifier(name) || !names.Add(name)) throw Error("A message selector name is invalid or duplicated.");
            CompiledTextMessageSelectorKind kind = function switch
            {
                "literal" => CompiledTextMessageSelectorKind.Literal,
                "plural" => CompiledTextMessageSelectorKind.CardinalPlural,
                "ordinal" => CompiledTextMessageSelectorKind.OrdinalPlural,
                _ => throw Error("A message selector function is unsupported."),
            };
            if (kind is CompiledTextMessageSelectorKind.CardinalPlural or CompiledTextMessageSelectorKind.OrdinalPlural &&
                argument.Type is not (TextArgumentType.Int or TextArgumentType.Number)) throw ContractMismatch(contract.Key.Name);
            selectors.Add(new CompiledTextMessageSelector(name, input, kind));
        }
        return selectors.ToArray();
    }

    private static CompiledTextMessageVariant[] ReadVariants(JsonElement value, TextResourcePackMessageContract contract,
        CompiledTextMessageSelector[] selectors, TextResourcePackLimits limits)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() is < 1 or > 256) throw Error("A message variant list is invalid.");
        var variants = new List<CompiledTextMessageVariant>();
        var signatures = new HashSet<string>(StringComparer.Ordinal);
        bool catchAll = false;
        foreach (JsonElement item in value.EnumerateArray())
        {
            Dictionary<string, JsonElement> fields = Members(item, ["matches", "nodes"]);
            if (fields["matches"].ValueKind != JsonValueKind.Object) throw Error("A variant match must be an object.");
            var matchesByName = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (JsonProperty match in fields["matches"].EnumerateObject())
            {
                if (!matchesByName.TryAdd(match.Name, String(match.Value))) throw Error("A variant contains a duplicate selector match.");
            }
            if (matchesByName.Count != selectors.Length) throw Error("A variant must match every selector.");
            var matches = new string[selectors.Length];
            bool all = true;
            for (int index = 0; index < selectors.Length; index++)
            {
                if (!matchesByName.TryGetValue(selectors[index].Name, out string? match) || match.Length == 0) throw Error("A variant match is invalid.");
                matches[index] = match;
                all &= match == "*";
            }
            string signature = string.Join("\u001f", matches);
            if (!signatures.Add(signature)) throw Error("A message contains a duplicate variant match.");
            catchAll |= all;
            int nodeCount = 0;
            int textBytes = 0;
            CompiledTextMessageNode[] nodes = ReadNodes(fields["nodes"], contract, limits, 0, ref nodeCount, ref textBytes);
            variants.Add(new CompiledTextMessageVariant(matches, nodes));
        }
        if (!catchAll) throw Error("A normalized message requires a catch-all variant.");
        return variants.ToArray();
    }

    private static CompiledTextMessageNode[] ReadNodes(JsonElement value, TextResourcePackMessageContract contract,
        TextResourcePackLimits limits, int depth, ref int nodeCount, ref int textBytes)
    {
        if (value.ValueKind != JsonValueKind.Array || depth > 16) throw Error("A message node list is invalid.");
        var nodes = new List<CompiledTextMessageNode>();
        foreach (JsonElement item in value.EnumerateArray())
        {
            if (++nodeCount > 4096) throw Limit("A message exceeds the normalized node limit.");
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("kind", out JsonElement kindElement)) throw Error("A message node is malformed.");
            string kind = String(kindElement);
            if (kind == "text")
            {
                Dictionary<string, JsonElement> fields = Members(item, ["kind", "value"]);
                string text = String(fields["value"]);
                textBytes += Encoding.UTF8.GetByteCount(text);
                if (textBytes > limits.MaximumPatternBytes) throw Limit("A message exceeds the configured pattern limit.");
                nodes.Add(new CompiledTextMessageNode(CompiledTextMessageNodeKind.Text, text));
            }
            else if (kind == "input")
            {
                Dictionary<string, JsonElement> fields = Members(item, ["kind", "input"]);
                string input = String(fields["input"]);
                FindArgument(contract, input);
                nodes.Add(new CompiledTextMessageNode(CompiledTextMessageNodeKind.Input, input));
            }
            else if (kind == "format")
            {
                var allowed = new HashSet<string>(StringComparer.Ordinal) { "kind", "input", "function", "format", "unit", "numeric" };
                Dictionary<string, JsonElement> fields = MembersSubset(item, allowed, ["kind", "input", "function", "format"]);
                string input = String(fields["input"]);
                string function = String(fields["function"]);
                string formatName = String(fields["format"]);
                TextResourcePackArgumentContract argument = FindArgument(contract, input);
                if (function == "relativeTime")
                {
                    string unit = RequiredString(fields, "unit");
                    string numeric = RequiredString(fields, "numeric");
                    if (argument.Type is not (TextArgumentType.Int or TextArgumentType.Number) ||
                        unit is not ("second" or "minute" or "hour" or "day" or "week" or "month" or "year") || numeric is not ("always" or "auto"))
                        throw ContractMismatch(contract.Key.Name);
                    nodes.Add(new CompiledTextMessageNode(CompiledTextMessageNodeKind.RelativeTime, input, TextArgumentFormat.Plain, unit, numeric));
                }
                else
                {
                    if (!FunctionMatches(function, argument.Type) || !TryFormat(formatName, out TextArgumentFormat format) || !TextResourcePackValidation.IsFormatAllowed(argument.Type, format))
                        throw ContractMismatch(contract.Key.Name);
                    if (fields.ContainsKey("unit") || fields.ContainsKey("numeric")) throw Error("A scalar format contains relative-time options.");
                    nodes.Add(new CompiledTextMessageNode(CompiledTextMessageNodeKind.Format, input, format));
                }
            }
            else if (kind == "markup")
            {
                Dictionary<string, JsonElement> fields = Members(item, ["kind", "name", "attributes", "children"]);
                string name = String(fields["name"]);
                if (!TextResourcePackValidation.IsIdentifier(name) || fields["attributes"].ValueKind != JsonValueKind.Object) throw Error("A markup node is invalid.");
                var attributes = new List<CompiledTextMarkupProperty>();
                var attributeNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonProperty attribute in fields["attributes"].EnumerateObject())
                {
                    if (!attributeNames.Add(attribute.Name) || !TextResourcePackValidation.IsIdentifier(attribute.Name)) throw Error("A markup property is invalid or duplicated.");
                    attributes.Add(new CompiledTextMarkupProperty(attribute.Name, String(attribute.Value)));
                }
                attributes.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));
                nodes.Add(new CompiledTextMessageNode(CompiledTextMessageNodeKind.MarkupStart, name, attributes: attributes));
                nodes.AddRange(ReadNodes(fields["children"], contract, limits, depth + 1, ref nodeCount, ref textBytes));
                nodes.Add(new CompiledTextMessageNode(CompiledTextMessageNodeKind.MarkupEnd, name));
            }
            else throw Error("A message node kind is unsupported.");
        }
        return nodes.ToArray();
    }

    private static Dictionary<string, JsonElement> Members(JsonElement value, string[] expected)
    {
        var allowed = new HashSet<string>(expected, StringComparer.Ordinal);
        return MembersSubset(value, allowed, expected);
    }

    private static Dictionary<string, JsonElement> MembersSubset(JsonElement value, HashSet<string> allowed, string[] required)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Error("An external pack object is malformed.");
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (JsonProperty property in value.EnumerateObject())
        {
            if (!allowed.Contains(property.Name)) throw Error("The external pack contains unknown property '" + property.Name + "'.", TextResourcePackFailureReason.UnknownMember);
            if (!result.TryAdd(property.Name, property.Value)) throw Error("The external pack contains duplicate property '" + property.Name + "'.");
        }
        for (int index = 0; index < required.Length; index++) if (!result.ContainsKey(required[index])) throw Error("The external pack is missing required property '" + required[index] + "'.");
        return result;
    }

    private static TextResourcePackArgumentContract FindArgument(TextResourcePackMessageContract contract, string name)
    {
        for (int index = 0; index < contract.Arguments.Count; index++) if (contract.Arguments[index].Name == name) return contract.Arguments[index];
        throw ContractMismatch(contract.Key.Name);
    }

    private static TextResourcePlaceholderDescriptor[] Descriptors(IReadOnlyList<TextResourcePackArgumentContract> arguments)
    {
        var result = new TextResourcePlaceholderDescriptor[arguments.Count];
        for (int index = 0; index < result.Length; index++) result[index] = new TextResourcePlaceholderDescriptor(arguments[index].Name, arguments[index].Type, arguments[index].Format);
        return result;
    }

    private static string CompatibilityPattern(CompiledTextMessageNode[] nodes)
    {
        var result = new StringBuilder();
        for (int index = 0; index < nodes.Length; index++)
        {
            CompiledTextMessageNode node = nodes[index];
            if (node.Kind == CompiledTextMessageNodeKind.Text) result.Append(node.Value.Replace("{", "{{", StringComparison.Ordinal).Replace("}", "}}", StringComparison.Ordinal));
            else if (node.Kind is CompiledTextMessageNodeKind.Input or CompiledTextMessageNodeKind.Format or CompiledTextMessageNodeKind.RelativeTime) result.Append('{').Append(node.Value).Append('}');
        }
        return result.ToString();
    }

    private static bool FunctionMatches(string function, TextArgumentType type) => function switch
    {
        "string" => type == TextArgumentType.String,
        "integer" => type == TextArgumentType.Int,
        "number" => type == TextArgumentType.Number,
        "date" => type == TextArgumentType.Date,
        "time" => type == TextArgumentType.Time,
        "datetime" => type == TextArgumentType.DateTime,
        "uuid" => type == TextArgumentType.Guid,
        _ => false,
    };

    private static bool TryFormat(string value, out TextArgumentFormat format)
    {
        format = value switch
        {
            "none" => TextArgumentFormat.None, "plain" => TextArgumentFormat.Plain, "grouped" => TextArgumentFormat.Grouped,
            "fixed0" => TextArgumentFormat.Fixed0, "fixed1" => TextArgumentFormat.Fixed1, "fixed2" => TextArgumentFormat.Fixed2,
            "fixed3" => TextArgumentFormat.Fixed3, "fixed4" => TextArgumentFormat.Fixed4, "fixed5" => TextArgumentFormat.Fixed5,
            "fixed6" => TextArgumentFormat.Fixed6, "percent0" => TextArgumentFormat.Percent0, "percent1" => TextArgumentFormat.Percent1,
            "percent2" => TextArgumentFormat.Percent2, "percent3" => TextArgumentFormat.Percent3, "percent4" => TextArgumentFormat.Percent4,
            "lower" => TextArgumentFormat.Lower, "iso" => TextArgumentFormat.Iso, "short" => TextArgumentFormat.Short,
            "medium" => TextArgumentFormat.Medium, "long" => TextArgumentFormat.Long, "d" => TextArgumentFormat.D, "n" => TextArgumentFormat.N,
            _ => (TextArgumentFormat)(-1),
        };
        return (int)format >= 0;
    }

    private static string TypeName(TextArgumentType type) => type switch
    {
        TextArgumentType.String => "string", TextArgumentType.Int => "int", TextArgumentType.Number => "number", TextArgumentType.Bool => "bool",
        TextArgumentType.Date => "date", TextArgumentType.Time => "time", TextArgumentType.DateTime => "datetime", TextArgumentType.Guid => "guid",
        _ => throw Error("An argument type is unsupported."),
    };

    private static string FormatName(TextArgumentFormat format) => format.ToString() switch
    {
        "None" => "none", "Plain" => "plain", "Grouped" => "grouped", "Lower" => "lower", "Iso" => "iso",
        "Short" => "short", "Medium" => "medium", "Long" => "long", "D" => "d", "N" => "n",
        string name => char.ToLowerInvariant(name[0]) + name.Substring(1),
    };

    private static int Integer(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int result) ? result : throw Error("An external pack integer is invalid.");
    private static string String(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()! : throw Error("An external pack string is invalid.");
    private static string RequiredString(Dictionary<string, JsonElement> fields, string name) => fields.TryGetValue(name, out JsonElement value) ? String(value) : throw Error("A format node is missing '" + name + "'.");
    private static TextResourcePackException ContractMismatch(string key) => Error("Message '" + key + "' does not match its generated argument contract.", TextResourcePackFailureReason.ArgumentContractMismatch);
    private static TextResourcePackException Limit(string message) => Error(message, TextResourcePackFailureReason.LimitExceeded);
    private static TextResourcePackException Error(string message, TextResourcePackFailureReason reason = TextResourcePackFailureReason.Malformed) =>
        TextResourcePackFailure.Create(message, reason);
}
