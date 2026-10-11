using System;
using System.ComponentModel;
using System.Reflection;

namespace Runic.Translations.Runtime.Tests;

// Runtime plumbing that only generated catalogs and the runtime itself call stays
// public, because generated code in application assemblies calls it, but stays
// out of completion lists.
internal static class EditorBrowsableTests
{
    private static readonly Type[] HiddenTypes =
    [
        typeof(TranslationsCompatibility),
        typeof(TextMessageSelector),
        typeof(TextRelativeTimeFormatter),
        typeof(TextPatternFormatter),
        typeof(CompiledRmf2Value),
        typeof(CompiledRmf2Option),
        typeof(CompiledRmf2Annotation),
        typeof(CompiledRmf2Expression),
        typeof(CompiledRmf2Input),
        typeof(CompiledRmf2Declaration),
        typeof(CompiledRmf2Selector),
        typeof(CompiledRmf2Key),
        typeof(CompiledRmf2Node),
        typeof(CompiledRmf2Variant),
        typeof(CompiledRmf2Message),
    ];

    private static readonly (Type Type, string Member)[] HiddenMembers =
    [
        (typeof(CompiledTextMessage), nameof(CompiledTextMessage.Rmf2V5)),
        (typeof(CompiledTextMessage), nameof(CompiledTextMessage.FromRmf2)),
        (typeof(CompiledTranslationDefinition), nameof(CompiledTranslationDefinition.FromRmf2Inputs)),
        (typeof(TranslationPackContract), nameof(TranslationPackContract.CreateRmf2V5)),
        (typeof(TranslationPackMessageContract), nameof(TranslationPackMessageContract.FromRmf2Inputs)),
        (typeof(TextArgument), nameof(TextArgument.CreateRmf2)),
        (typeof(TextArgumentFormat), nameof(TextArgumentFormat.Fixed0)),
        (typeof(TextArgumentFormat), nameof(TextArgumentFormat.Fixed1)),
        (typeof(TextArgumentFormat), nameof(TextArgumentFormat.Fixed2)),
        (typeof(TextArgumentFormat), nameof(TextArgumentFormat.Fixed3)),
        (typeof(TextArgumentFormat), nameof(TextArgumentFormat.Fixed4)),
        (typeof(TextArgumentFormat), nameof(TextArgumentFormat.Fixed5)),
        (typeof(TextArgumentFormat), nameof(TextArgumentFormat.Fixed6)),
        (typeof(TextArgumentFormat), nameof(TextArgumentFormat.Percent0)),
        (typeof(TextArgumentFormat), nameof(TextArgumentFormat.Percent1)),
        (typeof(TextArgumentFormat), nameof(TextArgumentFormat.Percent2)),
        (typeof(TextArgumentFormat), nameof(TextArgumentFormat.Percent3)),
        (typeof(TextArgumentFormat), nameof(TextArgumentFormat.Percent4)),
    ];

    public static void Register(TestRunner runner) =>
        runner.Add("generated-code plumbing stays public but hidden from completion", Hidden);

    private static void Hidden()
    {
        foreach (Type type in HiddenTypes)
            Assert.True(type.IsPublic && IsHidden(type), $"{type.FullName} must stay public and carry [EditorBrowsable(Never)].");
        foreach ((Type type, string name) in HiddenMembers)
        {
            MemberInfo[] members = type.GetMember(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);
            Assert.True(members.Length > 0, $"{type.Name}.{name} must stay public.");
            foreach (MemberInfo member in members)
                Assert.True(IsHidden(member), $"{type.Name}.{name} must carry [EditorBrowsable(Never)].");
        }
        // The formats an application chooses stay visible.
        Assert.False(IsHidden(typeof(TextArgumentFormat).GetField(nameof(TextArgumentFormat.Grouped))!), "TextArgumentFormat.Grouped must stay visible.");
    }

    private static bool IsHidden(MemberInfo member) =>
        member.GetCustomAttribute<EditorBrowsableAttribute>(false)?.State == EditorBrowsableState.Never;
}
