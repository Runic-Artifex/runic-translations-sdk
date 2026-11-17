namespace Runic.Translations;

/// <summary>
/// Range checks for the enums whose arguments the runtime validates. <c>Enum.IsDefined</c> reads the enum's metadata
/// through reflection, which a NativeAOT executable would have to keep; these are constant comparisons. Each enum is
/// contiguous from 0, and a new member must be its last: <c>DefinedEnumsTests</c> fails when a bound is not.
/// </summary>
internal static class DefinedEnums
{
    internal static bool IsDefined(CompiledTextMessageNodeKind value) => (uint)value <= (uint)CompiledTextMessageNodeKind.MarkupStandalone;

    internal static bool IsDefined(CompiledTextMessageSelectorKind value) => (uint)value <= (uint)CompiledTextMessageSelectorKind.OrdinalPlural;

    internal static bool IsDefined(TextArgumentType value) => (uint)value <= (uint)TextArgumentType.List;

    internal static bool IsDefined(TranslationPackFailureReason value) => (uint)value <= (uint)TranslationPackFailureReason.DocumentStructureMismatch;
}
