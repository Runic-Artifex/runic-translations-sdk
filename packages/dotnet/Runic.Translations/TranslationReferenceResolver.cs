using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace Runic.Translations;

/// <summary>How a <see cref="TranslationReference"/> was displayed.</summary>
public enum TranslationReferenceOutcome
{
    /// <summary>The message was translated by the receiver's catalog. Message arguments may still have used their fallback text.</summary>
    Translated = 0,
    /// <summary>The message could not be translated and its stored fallback text is shown.</summary>
    Fallback = 1,
    /// <summary>The message could not be translated and has no fallback text.</summary>
    Failed = 2,
}

/// <summary>Why part of a reference could not be translated, or that the catalog changed since it was written.</summary>
/// <param name="Path">
/// <c>$</c> for the reference itself, followed by <c>.name</c> for each message argument on the way to the reference concerned,
/// for example <c>$.step</c>.
/// </param>
/// <param name="Reason">
/// A stable reason: <c>contract-changed</c> (informational: the catalog contract differs from the writer's, but the reference was
/// checked key by key), <c>catalog-mismatch</c>, <c>fingerprint-mismatch</c> (a version 1 reference, whose untyped arguments need
/// the writer's exact contract), <c>unknown-key</c>, <c>incompatible-arguments</c>, <c>structured-content</c> or <c>format-failed</c>.
/// </param>
public readonly record struct TranslationReferenceDiagnostic(string Path, string Reason);

/// <summary>The text shown for a reference and how it was obtained.</summary>
public sealed class TranslationReferenceResolution
{
    internal TranslationReferenceResolution(TranslationReferenceOutcome outcome, string? text, List<TranslationReferenceDiagnostic> diagnostics)
    {
        Outcome = outcome;
        Text = text;
        Diagnostics = new ReadOnlyCollection<TranslationReferenceDiagnostic>(diagnostics);
    }

    /// <summary>Whether the text is a translation, the stored fallback, or missing.</summary>
    public TranslationReferenceOutcome Outcome { get; }
    /// <summary>The plain text to show; <see langword="null"/> when <see cref="Outcome"/> is <see cref="TranslationReferenceOutcome.Failed"/>.</summary>
    public string? Text { get; }
    /// <summary>The reasons for every fallback, in evaluation order: arguments are visited in ordinal name order.</summary>
    public IReadOnlyList<TranslationReferenceDiagnostic> Diagnostics { get; }
}

/// <summary>Displays stored or transported references with the receiver's current catalog.</summary>
/// <remarks>
/// <para>
/// The policy, shared with the generated ESM <c>resolveTextReference</c>: a reference of another catalog is not translated. A
/// version 2 reference whose writer used a different contract fingerprint is still translated when its key exists and every
/// argument has exactly the name and type of the key's current inputs; a message argument fills a string input. Otherwise the
/// reference shows its own fallback text. A message argument that cannot be translated uses its fallback text; without one, the
/// message containing it cannot be translated either. A version 1 reference carries no argument types, so it is translated only
/// with the writer's exact contract fingerprint.
/// </para>
/// <para>
/// Resolution produces plain text. A message that formats to markup or document content in the snapshot's locale is not
/// translated (<c>structured-content</c>). Resolution never throws for a catalog change; it reports diagnostics instead.
/// </para>
/// </remarks>
public static class TranslationReferenceResolver
{
    /// <summary>Resolves <paramref name="reference"/> to plain text in the locale of <paramref name="snapshot"/>.</summary>
    /// <param name="snapshot">The receiver's snapshot, which must support <see cref="ITranslationSnapshot.TryGetKey"/>.</param>
    /// <param name="contractFingerprint">The receiver's generated contract fingerprint, for example <c>AppTextCatalog.ContractFingerprint</c>.</param>
    /// <param name="reference">The stored or received reference.</param>
    public static TranslationReferenceResolution Resolve(ITranslationSnapshot snapshot, string contractFingerprint, TranslationReference reference)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(contractFingerprint);
        ArgumentNullException.ThrowIfNull(reference);
        var diagnostics = new List<TranslationReferenceDiagnostic>();
        if (!string.Equals(reference.Catalog, snapshot.Catalog, StringComparison.Ordinal))
        {
            diagnostics.Add(new("$", "catalog-mismatch"));
            return Fallback(reference, diagnostics);
        }
        if (!string.Equals(reference.ContractFingerprint, contractFingerprint, StringComparison.Ordinal))
        {
            if (reference.Version == 1)
            {
                diagnostics.Add(new("$", "fingerprint-mismatch"));
                return Fallback(reference, diagnostics);
            }
            diagnostics.Add(new("$", "contract-changed"));
        }
        string? text = Translate(snapshot, reference, "$", diagnostics);
        return text is null ? Fallback(reference, diagnostics) : new(TranslationReferenceOutcome.Translated, text, diagnostics);
    }

    private static TranslationReferenceResolution Fallback(TranslationReference reference, List<TranslationReferenceDiagnostic> diagnostics) =>
        reference.FallbackText is { } fallback
            ? new(TranslationReferenceOutcome.Fallback, fallback, diagnostics)
            : new(TranslationReferenceOutcome.Failed, null, diagnostics);

    private static string? Translate(ITranslationSnapshot snapshot, TranslationReference reference, string path, List<TranslationReferenceDiagnostic> diagnostics)
    {
        // Keys are matched exactly, as the generated ESM transport contract does: no dotted aliases and no locale extras.
        if (!snapshot.TryGetKey(reference.Key, out TranslationKey key) || key.Id == CompiledTranslationCatalog.DynamicKeyId ||
            !string.Equals(key.Name, reference.Key, StringComparison.Ordinal) ||
            !snapshot.TryGetPlaceholders(key, out ReadOnlyMemory<TranslationPlaceholderDescriptor> placeholders))
        {
            diagnostics.Add(new(path, "unknown-key"));
            return null;
        }

        ReadOnlySpan<TranslationPlaceholderDescriptor> inputs = placeholders.Span;
        bool compatible = inputs.Length == reference.Arguments.Count;
        for (int index = 0; compatible && index < inputs.Length; index++)
            compatible = reference.Arguments.TryGetValue(inputs[index].Name, out TranslationReferenceArgument argument) &&
                Compatible(reference.Version, inputs[index], argument);
        if (!compatible)
        {
            diagnostics.Add(new(path, "incompatible-arguments"));
            return null;
        }

        var order = new TranslationPlaceholderDescriptor[inputs.Length];
        inputs.CopyTo(order);
        Array.Sort(order, static (left, right) => string.CompareOrdinal(left.Name, right.Name));
        var arguments = new TextArgument[order.Length];
        for (int index = 0; index < order.Length; index++)
        {
            TranslationPlaceholderDescriptor input = order[index];
            TranslationReferenceArgument argument = reference.Arguments[input.Name];
            if (argument.Message is { } message)
            {
                string? text = Translate(snapshot, message, path + "." + input.Name, diagnostics) ?? message.FallbackText;
                if (text is null) return null;
                arguments[index] = TextArgument.CreateRmf2(input.Name, new TextArgument("_", text));
            }
            else arguments[index] = TextArgument.CreateRmf2(input.Name, Carrier(input, argument));
        }

        try
        {
            return snapshot.Format(key, arguments);
        }
        catch (TranslationFormatException)
        {
            // A message whose locale variant contains markup formats only as content; plain content is still text.
        }
        catch (Exception exception) when (exception is TranslationNotFoundException or ArgumentException or FormatException or NotSupportedException)
        {
            diagnostics.Add(new(path, "format-failed"));
            return null;
        }

        LocalizedTextContent content;
        try { content = snapshot.FormatContent(key, arguments); }
        catch (Exception exception) when (exception is TranslationFormatException or TranslationNotFoundException or ArgumentException or FormatException or NotSupportedException)
        {
            diagnostics.Add(new(path, "format-failed"));
            return null;
        }
        var builder = new StringBuilder();
        foreach (LocalizedTextContentNode node in content.Nodes.Span)
        {
            if (node.Kind != LocalizedTextContentNodeKind.Text)
            {
                diagnostics.Add(new(path, "structured-content"));
                return null;
            }
            builder.Append(node.Value);
        }
        return builder.ToString();
    }

    private static bool Compatible(int version, TranslationPlaceholderDescriptor input, TranslationReferenceArgument argument)
    {
        if (version != 1) return argument.Type == input.Type && (argument.Message is null || input.Type == TextArgumentType.String);
        // Version 1 carries strings and booleans only; the receiver's type decides how a string is read.
        if (input.Type is TextArgumentType.Bool or TextArgumentType.List) return argument.Type == input.Type;
        if (argument.Type != TextArgumentType.String) return false;
        try { _ = new TranslationReferenceArgument(input.Type, argument.Value); return true; }
        catch (ArgumentException) { return false; }
    }

    private static TextArgument Carrier(TranslationPlaceholderDescriptor input, TranslationReferenceArgument argument)
    {
        string value = argument.Value;
        CultureInfo invariant = CultureInfo.InvariantCulture;
        return input.Type switch
        {
            TextArgumentType.String => new TextArgument("_", value),
            TextArgumentType.Int => new TextArgument("_", long.Parse(value, NumberStyles.AllowLeadingSign, invariant), input.Format),
            TextArgumentType.Number => new TextArgument("_", decimal.Parse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, invariant), input.Format),
            TextArgumentType.Bool => new TextArgument("_", value == "true", input.Format),
            TextArgumentType.Date => new TextArgument("_", DateOnly.ParseExact(value, "yyyy-MM-dd", invariant), input.Format),
            TextArgumentType.Time => new TextArgument("_", TimeOnly.ParseExact(value, "HH:mm:ss", invariant), input.Format),
            TextArgumentType.DateTime => new TextArgument("_", DateTimeOffset.ParseExact(value, "yyyy-MM-dd'T'HH:mm:ss'Z'", invariant,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal), input.Format),
            TextArgumentType.Guid => new TextArgument("_", Guid.ParseExact(value, "D"), input.Format),
            TextArgumentType.List => new TextArgument("_", argument.Items!),
            _ => throw new ArgumentOutOfRangeException(nameof(input)),
        };
    }
}
