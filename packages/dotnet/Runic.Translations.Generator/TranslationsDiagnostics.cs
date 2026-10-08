using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Runic.Translations.Generator;

/// <summary>
/// The diagnostics the generator reports: its own and every compiler diagnostic it forwards. Each
/// diagnostic ID has one descriptor with a specific title and a help link to its entry in
/// <c>docs/guides/translations/diagnostics.md</c>. The message is the compiler's message, and the
/// severity of each report is the compiler's, so project policies such as
/// <c>translationCompleteness</c> still choose between warnings and errors.
/// Release tracking: AnalyzerReleases.Shipped.md and AnalyzerReleases.Unshipped.md.
/// </summary>
internal static class TranslationsDiagnostics
{
    internal const string Category = "Runic.Translations";
    internal const string HelpLinkBase = "https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/diagnostics.md#";

    internal static readonly DiagnosticDescriptor UnreadableSource = new(
        "RTR0001", "Translation source is unreadable or malformed", "{0}", Category, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: HelpLinkBase + "rtr0001");
    internal static readonly DiagnosticDescriptor DuplicateInputs = new(
        "RTR0002", "Translation inputs are duplicated or ambiguous", "{0}", Category, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: HelpLinkBase + "rtr0002");
    internal static readonly DiagnosticDescriptor UnsupportedProjectSchema = new(
        "RTR0003", "Translation project schema is not supported", "{0}", Category, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: HelpLinkBase + "rtr0003");
    internal static readonly DiagnosticDescriptor InvalidLocale = new(
        "RTR0004", "Locale is invalid or not declared", "{0}", Category, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: HelpLinkBase + "rtr0004");
    internal static readonly DiagnosticDescriptor InvalidCodeName = new(
        "RTR0006", "Catalog ID or generated code name is invalid", "{0}", Category, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: HelpLinkBase + "rtr0006");
    internal static readonly DiagnosticDescriptor EmptyBaseLocale = new(
        "RTR0009", "Base locale defines no messages", "{0}", Category, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: HelpLinkBase + "rtr0009");
    internal static readonly DiagnosticDescriptor MissingTranslation = new(
        "RTR0010", "Locale lacks a translation for a base-locale message", "{0}", Category, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: HelpLinkBase + "rtr0010");
    internal static readonly DiagnosticDescriptor ExtraLocaleKey = new(
        "RTR0011", "Locale defines a message the base locale does not have", "{0}", Category, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: HelpLinkBase + "rtr0011");
    internal static readonly DiagnosticDescriptor InvalidFallback = new(
        "RTR0012", "Locale fallback is invalid", "{0}", Category, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: HelpLinkBase + "rtr0012");
    internal static readonly DiagnosticDescriptor FallbackCycle = new(
        "RTR0013", "Locale fallback does not reach the base locale", "{0}", Category, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: HelpLinkBase + "rtr0013");
    internal static readonly DiagnosticDescriptor MalformedPattern = new(
        "RTR0014", "Message pattern is malformed", "{0}", Category, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: HelpLinkBase + "rtr0014");
    internal static readonly DiagnosticDescriptor CallerInputChanged = new(
        "RTR0016", "Translation changes the message's caller inputs", "{0}", Category, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: HelpLinkBase + "rtr0016");
    internal static readonly DiagnosticDescriptor NameCollision = new(
        "RTR0018", "Generated name collides or is reserved", "{0}", Category, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: HelpLinkBase + "rtr0018");
    internal static readonly DiagnosticDescriptor InvalidStructure = new(
        "RTR0019", "Project member or source encoding is invalid", "{0}", Category, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: HelpLinkBase + "rtr0019");
    internal static readonly DiagnosticDescriptor EmptyVariant = new(
        "RTR0021", "Message has an empty variant", "{0}", Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, helpLinkUri: HelpLinkBase + "rtr0021");
    internal static readonly DiagnosticDescriptor LimitExceeded = new(
        "RTR0022", "Compiler limit exceeded", "{0}", Category, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: HelpLinkBase + "rtr0022");
    internal static readonly DiagnosticDescriptor RuntimeAbi = new(
        "RTR0024", "Referenced Runic.Translations runtime ABI is incompatible", "{0}", Category, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: HelpLinkBase + "rtr0024");
    internal static readonly DiagnosticDescriptor UnsupportedContentLocale = new(
        "RTR0031", "Built-in formatter does not support the content locale", "{0}", Category, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: HelpLinkBase + "rtr0031");
    internal static readonly DiagnosticDescriptor InvalidVariableOrFunction = new(
        "RTR0041", "MF2 variable or function is invalid", "{0}", Category, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: HelpLinkBase + "rtr0041");
    internal static readonly DiagnosticDescriptor InvalidResourceSyntax = new(
        "RTR0050", "RMF2 resource syntax is invalid", "{0}", Category, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: HelpLinkBase + "rtr0050");
    internal static readonly DiagnosticDescriptor InvalidMetadata = new(
        "RTR0051", "Message metadata is invalid or unknown", "{0}", Category, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: HelpLinkBase + "rtr0051");
    internal static readonly DiagnosticDescriptor InvalidSourceLayout = new(
        "RTR0052", "Translation source layout is invalid", "{0}", Category, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: HelpLinkBase + "rtr0052");
    internal static readonly DiagnosticDescriptor ConflictingDeclaration = new(
        "RTR0054", "Resource is declared more than once or conflicts", "{0}", Category, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: HelpLinkBase + "rtr0054");
    internal static readonly DiagnosticDescriptor InvalidMarkupContract = new(
        "RTR0060", "Markup contract is invalid", "{0}", Category, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: HelpLinkBase + "rtr0060");
    internal static readonly DiagnosticDescriptor InvalidMarkup = new(
        "RTR0061", "Inline markup is invalid", "{0}", Category, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: HelpLinkBase + "rtr0061");
    internal static readonly DiagnosticDescriptor InvalidMarkupReference = new(
        "RTR0062", "Markup slot or project markup reference is invalid", "{0}", Category, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: HelpLinkBase + "rtr0062");
    internal static readonly DiagnosticDescriptor NotExecutable = new(
        "RTR0065", "Message is not executable in the RMF2 profile", "{0}", Category, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: HelpLinkBase + "rtr0065");
    internal static readonly DiagnosticDescriptor SyntaxError = new(
        "RTR0066", "MF2 syntax error", "{0}", Category, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: HelpLinkBase + "rtr0066");
    internal static readonly DiagnosticDescriptor InvalidDataModel = new(
        "RTR0067", "MF2 data model is invalid", "{0}", Category, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: HelpLinkBase + "rtr0067");
    internal static readonly DiagnosticDescriptor ReadableSurfaceUnsupported = new(
        "RTR0068", "Referenced runtime lacks the readable C# surface", "{0}", Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, helpLinkUri: HelpLinkBase + "rtr0068");
    internal static readonly DiagnosticDescriptor ReadableNameReserved = new(
        "RTR0069", "Readable C# name is reserved or clashes", "{0}", Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, helpLinkUri: HelpLinkBase + "rtr0069");
    internal static readonly DiagnosticDescriptor DocumentContentKind = new(
        "RTR0070", "Message content kind is invalid", "{0}", Category, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: HelpLinkBase + "rtr0070");
    internal static readonly DiagnosticDescriptor DocumentVariantStructure = new(
        "RTR0071", "Variant uses another variant's document structure", "{0}", Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, helpLinkUri: HelpLinkBase + "rtr0071");
    internal static readonly DiagnosticDescriptor DocumentChildInvalid = new(
        "RTR0072", "Document element is in an invalid position", "{0}", Category, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: HelpLinkBase + "rtr0072");
    internal static readonly DiagnosticDescriptor DocumentLimitExceeded = new(
        "RTR0073", "Document exceeds a structure limit", "{0}", Category, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: HelpLinkBase + "rtr0073");
    internal static readonly DiagnosticDescriptor DocumentStructureMismatch = new(
        "RTR0074", "Translated document structure does not match the source", "{0}", Category, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: HelpLinkBase + "rtr0074");
    internal static readonly DiagnosticDescriptor DocumentBlockEmpty = new(
        "RTR0076", "Document block or list is empty", "{0}", Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, helpLinkUri: HelpLinkBase + "rtr0076");
    internal static readonly DiagnosticDescriptor DocumentHeadingLevel = new(
        "RTR0077", "Document heading skips a level", "{0}", Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, helpLinkUri: HelpLinkBase + "rtr0077");
    internal static readonly DiagnosticDescriptor DocumentLineBreakSpace = new(
        "RTR0078", "Line break between Southeast Asian characters became a space", "{0}", Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, helpLinkUri: HelpLinkBase + "rtr0078");

    private static readonly Dictionary<string, DiagnosticDescriptor> ById = Index(
        UnreadableSource, DuplicateInputs, UnsupportedProjectSchema, InvalidLocale, InvalidCodeName, EmptyBaseLocale,
        MissingTranslation, ExtraLocaleKey, InvalidFallback, FallbackCycle, MalformedPattern, CallerInputChanged,
        NameCollision, InvalidStructure, EmptyVariant, LimitExceeded, RuntimeAbi, UnsupportedContentLocale,
        InvalidVariableOrFunction, InvalidResourceSyntax, InvalidMetadata, InvalidSourceLayout, ConflictingDeclaration,
        InvalidMarkupContract, InvalidMarkup, InvalidMarkupReference, NotExecutable, SyntaxError, InvalidDataModel,
        ReadableSurfaceUnsupported, ReadableNameReserved, DocumentContentKind, DocumentVariantStructure, DocumentChildInvalid,
        DocumentLimitExceeded, DocumentStructureMismatch, DocumentBlockEmpty, DocumentHeadingLevel, DocumentLineBreakSpace);

    private static readonly object UnknownGate = new();
    private static readonly Dictionary<string, DiagnosticDescriptor> Unknown = new(System.StringComparer.Ordinal);

    /// <summary>Every tracked descriptor.</summary>
    internal static IReadOnlyCollection<DiagnosticDescriptor> All => ById.Values;

    /// <summary>Returns the descriptor for a compiler diagnostic ID.</summary>
    /// <remarks>A test keeps this table complete. An ID added to the compiler without a descriptor still
    /// reports under its own ID, with a generic title and the reference's index as help.</remarks>
    internal static DiagnosticDescriptor Get(string id)
    {
        if (ById.TryGetValue(id, out DiagnosticDescriptor? descriptor)) return descriptor;
        lock (UnknownGate)
        {
            if (!Unknown.TryGetValue(id, out descriptor))
            {
#pragma warning disable RS2008 // Untracked IDs are a test failure; this keeps a missing entry reportable.
                descriptor = new DiagnosticDescriptor(id, "Runic Translations compiler diagnostic", "{0}", Category,
                    DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: HelpLinkBase + "diagnostics");
#pragma warning restore RS2008
                Unknown.Add(id, descriptor);
            }
            return descriptor;
        }
    }


    private static Dictionary<string, DiagnosticDescriptor> Index(params DiagnosticDescriptor[] descriptors)
    {
        var index = new Dictionary<string, DiagnosticDescriptor>(System.StringComparer.Ordinal);
        foreach (DiagnosticDescriptor descriptor in descriptors) index.Add(descriptor.Id, descriptor);
        return index;
    }
}
