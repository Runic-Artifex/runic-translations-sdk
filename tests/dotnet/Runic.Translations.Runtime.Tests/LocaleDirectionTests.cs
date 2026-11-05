using System;
using System.Collections.Generic;
using Runic.Translations;
using Runic.Translations.Generated;
using TUnit.Core;

namespace Runic.Translations.Runtime.Tests;

internal sealed class LocaleDirectionTests
{
    // The same table as the ESM runtime's localeDirection test (Rmf2EsmV5Tests.LocaleDirection).
    private static readonly Dictionary<string, TextDirection> Expected = new(StringComparer.Ordinal)
    {
        ["ar"] = TextDirection.RightToLeft, ["ar-EG"] = TextDirection.RightToLeft, ["AR-eg"] = TextDirection.RightToLeft, ["ar_EG"] = TextDirection.RightToLeft,
        ["he"] = TextDirection.RightToLeft, ["iw"] = TextDirection.RightToLeft, ["fa"] = TextDirection.RightToLeft, ["ur"] = TextDirection.RightToLeft,
        ["ps"] = TextDirection.RightToLeft, ["dv"] = TextDirection.RightToLeft, ["yi"] = TextDirection.RightToLeft, ["ckb"] = TextDirection.RightToLeft,
        ["sd"] = TextDirection.RightToLeft, ["ug"] = TextDirection.RightToLeft, ["ks"] = TextDirection.RightToLeft, ["pa-PK"] = TextDirection.RightToLeft,
        ["pa-Arab"] = TextDirection.RightToLeft, ["az-IR"] = TextDirection.RightToLeft, ["uz-AF"] = TextDirection.RightToLeft, ["ku-Arab"] = TextDirection.RightToLeft,
        ["en-Arab"] = TextDirection.RightToLeft, ["und-Hebr"] = TextDirection.RightToLeft, ["ar-u-nu-latn"] = TextDirection.RightToLeft,
        ["en"] = TextDirection.LeftToRight, ["en-US"] = TextDirection.LeftToRight, ["de"] = TextDirection.LeftToRight, ["ja"] = TextDirection.LeftToRight,
        ["zh-Hant-TW"] = TextDirection.LeftToRight, ["sr-Cyrl"] = TextDirection.LeftToRight, ["pa"] = TextDirection.LeftToRight, ["ku"] = TextDirection.LeftToRight,
        ["he-Latn"] = TextDirection.LeftToRight, ["sd-Deva"] = TextDirection.LeftToRight, ["ks-Deva"] = TextDirection.LeftToRight, ["ar-Latn"] = TextDirection.LeftToRight,
        ["und"] = TextDirection.LeftToRight, [""] = TextDirection.LeftToRight,
    };

    [Test, DisplayName("LocaleDirection gives a locale's script direction from pinned CLDR data")]
    public void Directions()
    {
        foreach ((string tag, TextDirection direction) in Expected)
        {
            Assert.Equal(direction, LocaleDirection.Get(tag), tag);
            Assert.Equal(direction == TextDirection.RightToLeft, LocaleDirection.IsRightToLeft(tag), tag);
        }
        foreach (string invalid in new[] { " ", "en-", "x", "en-a" })
            Assert.Throws<ArgumentException>(() => LocaleDirection.Get(invalid));
        Assert.Throws<ArgumentNullException>(() => LocaleDirection.Get(null!));
        Assert.Equal("rtl", LocaleDirection.ToHtml(TextDirection.RightToLeft));
        Assert.Equal("ltr", LocaleDirection.ToHtml(TextDirection.LeftToRight));
    }

    [Test, DisplayName("u:dir is an option of every function with four literal values and does not change formatting")]
    public void DirectionOption()
    {
        var value = new CompiledRmf2Value("string-literal", "rtl");
        var expression = new CompiledRmf2Expression(new CompiledRmf2Value("number-literal", "5", "5"), TextArgumentType.Int, "integer", [new CompiledRmf2Option("u:dir", value)]);
        var message = new CompiledRmf2Message([], [], [], [new CompiledRmf2Variant([], [new CompiledRmf2Node(expression)])]);
        // Without the catalog's isolation, an explicit u:dir still isolates.
        Assert.Equal("⁧5⁩", message.Format([], "en"));
        Assert.Throws<ArgumentException>(() => _ = new CompiledRmf2Expression(new CompiledRmf2Value("string-literal", "x"), TextArgumentType.String, "string",
            [new CompiledRmf2Option("u:dir", new CompiledRmf2Value("string-literal", "up"))]));
        foreach (string function in new[] { "string", "date", "runic:list" })
            Assert.Equal(TextArgumentType.String, Rmf2RuntimeValidation.OptionType(function, "u:dir"), function);
    }

    [Test, DisplayName("a catalog's bidi isolation survives WithOptions")]
    public void CatalogIsolation()
    {
        var catalog = new CompiledTranslationCatalog("app", "en", [new CompiledTranslationDefinition("greeting", [])],
            [new CompiledTranslationLocale("en", null, [new(0, "Hello")])]);
        Assert.True(!catalog.BidiIsolation, "off by default");
        CompiledTranslationCatalog isolated = catalog.WithBidiIsolation();
        Assert.True(isolated.BidiIsolation && ReferenceEquals(isolated, isolated.WithBidiIsolation(true)), "on");
        Assert.True(isolated.WithOptions(new TranslationOptions { MissingKey = MissingTranslationPolicy.ReturnKey }).BidiIsolation, "kept by WithOptions");
        Assert.True(!isolated.WithBidiIsolation(false).BidiIsolation, "off again");
    }
}
