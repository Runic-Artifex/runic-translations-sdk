using System;
using System.Reflection;
using System.Runtime.Versioning;
using Runic.Translations;
using CompilerModel = Runic.Translations.Compiler;

namespace Runic.Translations.Compiler.Tests;

internal static class RuntimeContractTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("runtime compatibility versions and value carriers", CompatibilityAndValueCarriers);
        runner.Add("runtime and compiler expose their required compatibility targets", CompatibilityTargets);
    }

    private static void CompatibilityAndValueCarriers()
    {
        Assert.Equal(2, TranslationsCompatibility.CatalogSchemaVersion);
        Assert.Equal(2, TranslationsCompatibility.ResourceSchemaVersion);
        Assert.Equal(2, TranslationsCompatibility.MessageGrammarVersion);
        Assert.Equal(1, TranslationsCompatibility.RuntimeAbiVersion);

        TranslationKey key = new("app", 7, "Files.Deleted");
        Assert.Equal("app", key.Catalog);
        Assert.Equal(7, key.Id);
        Assert.Equal("Files.Deleted", key.Name);

        TextArgument count = new("count", 42L, TextArgumentFormat.Grouped);
        Assert.Equal("count", count.Name);
        Assert.Equal(TextArgumentType.Int, count.Type);
        Assert.Equal(TextArgumentFormat.Grouped, count.Format);
        Assert.True(count.TryGetValue(out long value), "The closed integer value should be readable as Int64.");
        Assert.Equal(42L, value);
        Assert.True(!count.TryGetValue(out string? _), "The closed integer value must not be readable as a string.");

        Guid identifier = new("7c9e6679-7425-40de-944b-e07fc1f90ae7");
        TextArgument guid = new("id", identifier, TextArgumentFormat.N);
        Assert.Equal(TextArgumentType.Guid, guid.Type);
        Assert.True(guid.TryGetValue(out Guid actualIdentifier), "The GUID carrier should retain its closed value.");
        Assert.Equal(identifier, actualIdentifier);

        bool invalidFormatRejected = false;
        try
        {
            _ = new TextArgument("count", 42L, TextArgumentFormat.Long);
        }
        catch (ArgumentOutOfRangeException)
        {
            invalidFormatRejected = true;
        }

        Assert.True(invalidFormatRejected, "Argument carriers must reject type/format combinations outside the portable registry.");

        TranslationOptions options = new();
        Assert.Equal(UnsupportedLocalePolicy.ParentsThenDefault, options.UnsupportedLocale);
        Assert.Equal(MissingTranslationPolicy.Throw, options.MissingKey);

        byte[] bytes = [1, 2, 3];
        ExternalTranslationPack pack = new(bytes);
        Assert.Equal(3, pack.Content.Length);
        Assert.Equal((byte)1, pack.Content.Span[0]);
    }

    private static void CompatibilityTargets()
    {
        TargetFrameworkAttribute runtime =
            typeof(TranslationsCompatibility).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()
            ?? throw new InvalidOperationException("The runtime target framework attribute is missing.");
        TargetFrameworkAttribute compiler =
            typeof(CompilerModel.TranslationCompiler).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()
            ?? throw new InvalidOperationException("The compiler target framework attribute is missing.");
        Assert.Equal(".NETCoreApp,Version=v10.0", runtime.FrameworkName);
        Assert.Equal(".NETCoreApp,Version=v10.0", compiler.FrameworkName);
    }
}
