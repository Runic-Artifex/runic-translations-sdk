using System;
using System.Linq;
using TUnit.Core;

namespace Runic.Translations.Runtime.Tests;

// DefinedEnums replaces Enum.IsDefined with range checks, so NativeAOT need not keep the enums' metadata. Each check must
// accept exactly the enum's members: a new member that is not the last, or a bound left at the old last member, fails here.
internal sealed class DefinedEnumsTests
{
    private static void Check<T>(Func<T, bool> isDefined) where T : struct, Enum
    {
        int[] values = Enum.GetValues<T>().Select(value => Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture)).Order().ToArray();
        Assert.Equal(0, values[0], typeof(T).Name + " starts at 0");
        Assert.True(values.SequenceEqual(Enumerable.Range(0, values.Length)), typeof(T).Name + " is contiguous");
        foreach (T value in Enum.GetValues<T>()) Assert.True(isDefined(value), typeof(T).Name + "." + value + " is defined");
        Assert.False(isDefined((T)(object)values.Length), typeof(T).Name + " rejects the value after its last member");
        Assert.False(isDefined((T)(object)(-1)), typeof(T).Name + " rejects -1");
    }

    [Test, DisplayName("the enum range checks accept exactly the enums' members")]
    public void RangeChecksMatchMembers()
    {
        Check<CompiledTextMessageNodeKind>(DefinedEnums.IsDefined);
        Check<CompiledTextMessageSelectorKind>(DefinedEnums.IsDefined);
        Check<TextArgumentType>(DefinedEnums.IsDefined);
        Check<TranslationPackFailureReason>(DefinedEnums.IsDefined);
    }

    [Test, DisplayName("a pack exception rejects an undefined failure reason")]
    public void PackExceptionRejectsUndefinedReason()
    {
        Assert.Equal(TranslationPackFailureReason.DocumentStructureMismatch, new TranslationPackException("x", TranslationPackFailureReason.DocumentStructureMismatch).Reason);
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new TranslationPackException("x", (TranslationPackFailureReason)(-1)));
    }
}
