using System;
using System.Collections.Generic;
using System.Linq;

namespace Runic.Translations.Compiler;

/// <summary>One readable C# identifier and the RMF2 input name or slot ID it stands for.</summary>
internal readonly record struct Rmf2ReadableNameV1(string Source, string Identifier);

/// <summary>The readable C# names of one canonical message under readable-name policy version 1.</summary>
internal sealed record Rmf2ReadableMessageV1(Rmf2MessageContractV5 Contract, string Member,
    IReadOnlyList<Rmf2ReadableNameV1> Inputs, IReadOnlyList<Rmf2ReadableNameV1> Slots);

// Readable C# name policy version 1 (specs/translations/rmf2-project-v5.md). A message's
// names depend only on its own key, input names, slot IDs, the class name and a fixed
// reserved list, so adding or removing another message never renames, adds or drops a
// member. Identifiers are returned without the '@' prefix: escaping is an emission
// detail outside the versioned policy.
internal static class Rmf2ReadableNamesV1
{
    internal const int Version = 1;
    internal const string DiagnosticId = "RTR0069";

    // Members every class inherits from System.Object.
    private static readonly string[] ObjectMembers =
        ["Equals", "GetHashCode", "ToString", "GetType", "MemberwiseClone", "Finalize", "ReferenceEquals"];

    // Facade members: the version constant and the facade's field.
    private static readonly string[] FacadeMembers = ["ReadableNameVersion", "__text"];

    /// <summary>False when the class name equals a member the readable facade adds to the generated class.</summary>
    internal static bool SupportsClassName(string className) => className is not ("Messages" or "__readable");

    internal static string MessagesTypeName(string className) => className + "Messages";
    internal static string SlotsTypeName(string className) => className + "Slots";

    /// <summary>An ASCII identifier is used verbatim; any other name uses its generated-name v1 encoding.</summary>
    internal static string Identifier(string name) =>
        TranslationCompiler.IsIdentifier(name) ? name : Rmf2GeneratedNamesV1.Identifier(name);

    /// <summary>Resolves a canonical message's readable names, or explains why the message is left out.</summary>
    internal static bool TryCreate(string className, Rmf2MessageContractV5 contract,
        out Rmf2ReadableMessageV1? message, out string? problem)
    {
        ArgumentException.ThrowIfNullOrEmpty(className);
        ArgumentNullException.ThrowIfNull(contract);
        message = null;
        string key = contract.Key;
        if (ObjectMembers.Contains(key, StringComparer.Ordinal) || FacadeMembers.Contains(key, StringComparer.Ordinal) ||
            key == className || key == MessagesTypeName(className) || key == SlotsTypeName(className))
        {
            problem = "Readable C# name '" + key + "' is reserved; message '" + key + "' is only available through its encoded member.";
            return false;
        }
        Rmf2ReadableNameV1[] inputs = contract.Inputs.Select(input => new Rmf2ReadableNameV1(input.Name, Identifier(input.Name))).ToArray();
        Rmf2ReadableNameV1[] slots = contract.Slots.Keys.Order(StringComparer.Ordinal)
            .Select(slot => new Rmf2ReadableNameV1(slot, Identifier(slot))).ToArray();
        foreach (Rmf2ReadableNameV1 slot in slots)
        {
            if (ObjectMembers.Contains(slot.Identifier, StringComparer.Ordinal) || slot.Identifier == key)
            {
                problem = "Readable C# slot name '" + slot.Identifier + "' is reserved in message '" + key + "'; the message is only available through its encoded member.";
                return false;
            }
        }
        if (Clash(inputs) is { } input)
        {
            problem = "Readable C# parameter name '" + input + "' clashes with the encoded name of another input of message '" + key + "'; the message is only available through its encoded member.";
            return false;
        }
        if (Clash(slots) is { } slotName)
        {
            problem = "Readable C# slot name '" + slotName + "' clashes with the encoded name of another slot of message '" + key + "'; the message is only available through its encoded member.";
            return false;
        }
        problem = null;
        message = new(contract, key, inputs, slots);
        return true;
    }

    // Input parameters and slot properties are separate C# scopes. Within one scope, a
    // verbatim name can only coincide with the encoded fallback of a non-identifier name.
    private static string? Clash(Rmf2ReadableNameV1[] names)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Rmf2ReadableNameV1 name in names)
            if (!seen.Add(name.Identifier)) return name.Identifier;
        return null;
    }
}
