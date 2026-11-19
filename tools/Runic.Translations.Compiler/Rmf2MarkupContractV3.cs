using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Runic.Translations.Compiler;

/// <summary>
/// Markup contract version 3 (D077.16): version 2 without the message entries of the default shape, with the catalog's
/// locales instead (<c>specs/translations/rmf2-execution-v2.md</c>, "Markup contract version 3").
/// </summary>
/// <remarks>
/// <para>
/// A message entry has the default shape when it has no slots, is not structured, has <c>inline</c> content without
/// skeletons, and names each of the catalog's locales, and only those, as its own content locale. Nearly every message of
/// a catalog has it, and in version 2 each repeats its key and every locale. Version 3 lists the catalog's locales once
/// (<c>"locales"</c>, in ordinal order) and omits those entries; a reader gives every message of the catalog that the
/// contract does not list the default entry for those locales. Everything else is as in version 2, member for member.
/// </para>
/// <para>
/// Generated code and packs of this release carry version 3. Version 2 stays what generated C# for an earlier runtime (RMF2
/// ABI 3 and 4) embeds, and what the caller fingerprint covers; packs written for an earlier release (<c>--contract</c>)
/// copy that release's contract as it is.
/// </para>
/// </remarks>
internal static class Rmf2MarkupContractV3
{
    internal const int Version = 3;

    /// <summary>The version 3 form of a version 2 markup contract of a catalog with the given locales.</summary>
    internal static string Compact(string contractV2, IEnumerable<string> locales)
    {
        string[] tags = locales.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        using JsonDocument document = JsonDocument.Parse(contractV2);
        JsonElement root = document.RootElement;
        if (root.GetProperty("version").GetInt32() != Rmf2ProjectV5.MarkupContractVersion)
            throw new InvalidOperationException("Only a version 2 markup contract can be compacted.");
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", Version);
            writer.WritePropertyName("contracts");
            root.GetProperty("contracts").WriteTo(writer);
            writer.WriteStartArray("locales");
            foreach (string tag in tags) writer.WriteStringValue(tag);
            writer.WriteEndArray();
            writer.WriteStartObject("messages");
            foreach (System.Text.Json.JsonProperty message in root.GetProperty("messages").EnumerateObject())
            {
                if (IsDefault(message.Value, tags)) continue;
                writer.WritePropertyName(message.Name);
                message.Value.WriteTo(writer);
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Whether a version 2 message entry has the default shape for these locales (ordinal order).</summary>
    internal static bool IsDefault(JsonElement message, IReadOnlyList<string> locales)
    {
        if (message.ValueKind != JsonValueKind.Object) return false;
        int members = 0;
        foreach (System.Text.Json.JsonProperty member in message.EnumerateObject())
        {
            members++;
            switch (member.Name)
            {
                case "slots" when member.Value.ValueKind == JsonValueKind.Object && !member.Value.EnumerateObject().Any(): break;
                case "structured" when member.Value.ValueKind == JsonValueKind.False: break;
                case "content" when member.Value.ValueKind == JsonValueKind.String && member.Value.ValueEquals("inline"): break;
                case "skeletons" when member.Value.ValueKind == JsonValueKind.Array && member.Value.GetArrayLength() == 0: break;
                case "contentLocales" when member.Value.ValueKind == JsonValueKind.Object:
                {
                    int index = 0;
                    foreach (System.Text.Json.JsonProperty locale in member.Value.EnumerateObject())
                    {
                        if (index >= locales.Count || !string.Equals(locale.Name, locales[index], StringComparison.Ordinal) ||
                            locale.Value.ValueKind != JsonValueKind.String || !locale.Value.ValueEquals(locale.Name))
                            return false;
                        index++;
                    }
                    if (index != locales.Count) return false;
                    break;
                }
                default: return false;
            }
        }
        return members == 5;
    }

    /// <summary>The default message entry of version 3 for these locales (ordinal order), as version 2 writes it.</summary>
    internal static void WriteDefault(Utf8JsonWriter writer, IReadOnlyList<string> locales)
    {
        writer.WriteStartObject();
        writer.WriteStartObject("slots");
        writer.WriteEndObject();
        writer.WriteBoolean("structured", false);
        writer.WriteStartObject("contentLocales");
        foreach (string locale in locales) writer.WriteString(locale, locale);
        writer.WriteEndObject();
        writer.WriteString("content", "inline");
        writer.WriteStartArray("skeletons");
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    /// <summary>
    /// The version 2 form of a version 2 or 3 contract for a catalog with these message keys: a version 3 contract gets the
    /// default entry of every key it does not list. A version 2 contract is returned as it is.
    /// </summary>
    internal static string Expand(string contract, IEnumerable<string> keys)
    {
        using JsonDocument document = JsonDocument.Parse(contract);
        JsonElement root = document.RootElement;
        int version = root.GetProperty("version").GetInt32();
        if (version == Rmf2ProjectV5.MarkupContractVersion) return contract;
        if (version != Version) throw new InvalidOperationException("Unsupported markup contract version " + version + ".");
        string[] locales = root.GetProperty("locales").EnumerateArray().Select(static item => item.GetString()!).ToArray();
        JsonElement listed = root.GetProperty("messages");
        var all = new SortedSet<string>(keys, StringComparer.Ordinal);
        foreach (System.Text.Json.JsonProperty message in listed.EnumerateObject()) all.Add(message.Name);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", Rmf2ProjectV5.MarkupContractVersion);
            writer.WritePropertyName("contracts");
            root.GetProperty("contracts").WriteTo(writer);
            writer.WriteStartObject("messages");
            foreach (string key in all)
            {
                writer.WritePropertyName(key);
                if (listed.TryGetProperty(key, out JsonElement entry)) entry.WriteTo(writer);
                else WriteDefault(writer, locales);
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
