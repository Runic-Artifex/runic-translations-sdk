using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Runic.Translations.CommandLine.Tests.Translations;
using TUnit.Core;

namespace Runic.Translations.CommandLine.Tests;

/// <summary>Locale-first precedence between the application catalog and the framework text, overlays and the invocation culture.</summary>
internal sealed class OverlayTests
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr");
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de");

    // runic-command-line.fr.locale-v5.json is `runic-translations generate --emit-json` of a project with catalog
    // "runic-command-line", base locale fr and help.usage = Utilisation, help.options = Options, help.showHelp = Afficher
    // l'aide, diagnostics.unknownOption (input $option) = Option inconnue. ({$option}).
    private static TranslationOverlay FrenchOverlay() => TranslationOverlay.FromPacks(
        TranslationCommandTextResolver.CatalogId, new EmbeddedPacks(), [new TranslationOverlayLocale("fr")]);

    [Test, DisplayName("rank orders a request's own locale, closer parents and other languages")]
    public void Rank()
    {
        Assert.Equal(0, TranslationCommandTextResolver.Rank("de-CH", "de-CH"));
        Assert.Equal(0, TranslationCommandTextResolver.Rank("de-CH", "de-ch"));
        Assert.Equal(2, TranslationCommandTextResolver.Rank("de-CH", "de"));
        Assert.Equal(1, TranslationCommandTextResolver.Rank("en-AU", "en-001"));
        Assert.Equal(1, TranslationCommandTextResolver.Rank("zh-TW", "zh-Hant"));
        Assert.True(TranslationCommandTextResolver.Rank("en-AU", "en-001") < TranslationCommandTextResolver.Rank("en-AU", "en"), "a CLDR parent beats the language");
        Assert.Equal(int.MaxValue, TranslationCommandTextResolver.Rank("de", "en"));
        Assert.Equal(int.MaxValue, TranslationCommandTextResolver.Rank("", "en"));
    }

    [Test, DisplayName("locale-first: built-in German beats an application override that reached German only by fallback")]
    public async Task LocaleFirst()
    {
        // As with relaxed completeness: the application's German snapshot carries its English messages as fallbacks.
        ITranslationSnapshot english = await TestTextCatalog.CreateProvider().GetSnapshotAsync("en");
        var resolver = new TranslationCommandTextResolver(new RequestedAs(english, "de"));
        // help.usage is overridden only in English, so the framework's German wins.
        Assert.Equal("Aufruf", resolver.Resolve("help.usage", German, []));
        // Application keys have no framework text and keep the application's fallback.
        Assert.Equal("Greet a person", resolver.Resolve("commands.greet", German, []));

        // In German, the application's German override wins over the framework's German.
        ITranslationSnapshot german = await TestTextCatalog.CreateProvider().GetSnapshotAsync("de");
        Assert.Equal("Übersicht", new TranslationCommandTextResolver(german).Resolve("help.usage", German, []));
        // A regional request: both are German parents at the same distance, so the application overlays the framework.
        ITranslationSnapshot swiss = await TestTextCatalog.CreateProvider().GetSnapshotAsync("de-CH");
        Assert.Equal("Übersicht", new TranslationCommandTextResolver(swiss).Resolve("help.usage", CultureInfo.GetCultureInfo("de-CH"), []));
    }

    [Test, DisplayName("a context builder resolves in the invocation culture, with a French overlay of the framework text")]
    public void BuilderFollowsCulture()
    {
        TranslationContextBuilder catalogs = TranslationContext.CreateBuilder()
            .Add(TestTextCatalog.Registration)
            .Add(TranslationCommandTextResolver.CatalogRegistration)
            .AddOverlay(FrenchOverlay());
        var resolver = new TranslationCommandTextResolver(catalogs, TestTextCatalog.CatalogId);

        // French: the overlay's French beats the application's English override (help.usage = Synopsis).
        Assert.Equal("Utilisation", resolver.Resolve("help.usage", French, []));
        Assert.Equal("Option inconnue. (--bogus)", resolver.Resolve("diagnostics.unknown-option", French, ["--bogus"]));
        // A key the overlay lacks falls back along the framework's chain to English.
        Assert.Equal("Commands", resolver.Resolve("help.commands", French, []));
        // The application's own keys use its fallback locale.
        Assert.Equal("Greet a person", resolver.Resolve("commands.greet", French, []));

        // The same resolver resolves another invocation's culture.
        Assert.Equal("Übersicht", resolver.Resolve("help.usage", German, []));
        Assert.Equal("Optionen", resolver.Resolve("help.options", German, []));
        Assert.Equal("Grüßt eine Person", resolver.Resolve("commands.greet", German, []));
        Assert.Equal("Synopsis", resolver.Resolve("help.usage", CultureInfo.InvariantCulture, []));
    }

    [Test, DisplayName("a context builder without the framework catalog uses the built-in text, and an invalid culture the default")]
    public void BuilderWithoutFrameworkCatalog()
    {
        var resolver = new TranslationCommandTextResolver(TranslationContext.CreateBuilder().Add(TestTextCatalog.Registration), TestTextCatalog.CatalogId);
        Assert.Equal("Optionen", resolver.Resolve("help.options", German, []));
        Assert.Equal("Options", resolver.Resolve("help.options", French, []));
        Assert.Equal("Synopsis", resolver.Resolve("help.usage", French, []));
        var frameworkOnly = new TranslationCommandTextResolver(TranslationContext.CreateBuilder().Add(TranslationCommandTextResolver.CatalogRegistration)
            .AddOverlay(FrenchOverlay()), applicationCatalogId: null);
        Assert.Equal("Utilisation", frameworkOnly.Resolve("help.usage", French, []));
        Assert.Equal(null, frameworkOnly.Resolve("commands.greet", French, []));
    }

    [Test, DisplayName("a context-backed resolver follows the context's locale and its overlays")]
    public async Task ContextFollowsLocale()
    {
        TranslationContext context = await TranslationContext.CreateBuilder()
            .Add(TestTextCatalog.Registration)
            .Add(TranslationCommandTextResolver.CatalogRegistration)
            .AddOverlay(FrenchOverlay())
            .BuildAsync("de");
        var resolver = new TranslationCommandTextResolver(context, TestTextCatalog.CatalogId);
        Assert.Equal("Übersicht", resolver.Resolve("help.usage", German, []));
        await context.SetLocaleAsync("fr");
        Assert.Equal("Utilisation", resolver.Resolve("help.usage", French, []));
        Assert.Equal("Afficher l'aide", resolver.Resolve("help.show-help", French, []));
        Assert.Throws<ArgumentException>(() => _ = new TranslationCommandTextResolver(context, "not-registered"));
        Assert.Throws<ArgumentNullException>(() => _ = new TranslationCommandTextResolver((ITranslationContext)null!, null));
        Assert.Throws<ArgumentNullException>(() => _ = new TranslationCommandTextResolver((TranslationContextBuilder)null!, null));
        Assert.Equal("runic-command-line", TranslationCommandTextResolver.CatalogRegistration.CatalogId);
    }

    [Test, DisplayName("a culture of another language than the manager's locale keeps the locale and is reported once"), NotInParallel("trace")]
    public async Task CultureMismatchReportedOnce()
    {
        ITranslationManager manager = await TestTextCatalog.CreateManagerAsync("en");
        var resolver = new TranslationCommandTextResolver(manager);
        using var listener = new Capture();
        Trace.Listeners.Add(listener);
        try
        {
            Assert.Equal("Synopsis", resolver.Resolve("help.usage", German, []));
            Assert.Equal("Options", resolver.Resolve("help.options", German, []));
            // Same language, another region, and the invariant culture are not reported.
            Assert.Equal("Options", new TranslationCommandTextResolver(manager).Resolve("help.options", CultureInfo.GetCultureInfo("en-GB"), []));
            Assert.Equal("Options", new TranslationCommandTextResolver(manager).Resolve("help.options", CultureInfo.InvariantCulture, []));
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }

        Assert.Equal(1, listener.Count("invocation culture 'de' differs from the translation locale 'en'"), string.Join("\n", listener.Messages));
    }

    private sealed class Capture : TraceListener
    {
        private readonly List<string> _messages = [];
        internal IReadOnlyList<string> Messages { get { lock (_messages) return [.. _messages]; } }
        internal int Count(string fragment) { lock (_messages) return _messages.FindAll(item => item.Contains(fragment, StringComparison.Ordinal)).Count; }
        public override void Write(string? message) { }
        public override void WriteLine(string? message) { lock (_messages) _messages.Add(message ?? ""); }
    }

    private sealed class EmbeddedPacks : IExternalTranslationSource
    {
        public ValueTask<ExternalTranslationPack?> LoadAsync(string catalog, string locale, CancellationToken cancellationToken = default)
        {
            using Stream? stream = typeof(OverlayTests).Assembly.GetManifestResourceStream(catalog + "." + locale + ".locale-v5.json");
            if (stream is null) return ValueTask.FromResult<ExternalTranslationPack?>(null);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return ValueTask.FromResult<ExternalTranslationPack?>(new ExternalTranslationPack(buffer.ToArray()));
        }
    }

    /// <summary>A snapshot that reports another requested locale, as one composed with fallback copies would.</summary>
    private sealed class RequestedAs(ITranslationSnapshot inner, string requested) : ITranslationSnapshot
    {
        public string Catalog => inner.Catalog;
        public string Locale => requested;
        public string RequestedLocale => requested;
        public string Get(TranslationKey key) => inner.Get(key);
        public bool TryGet(TranslationKey key, out string pattern) => inner.TryGet(key, out pattern);
        public string Format(TranslationKey key, ReadOnlySpan<TextArgument> arguments) => inner.Format(key, arguments);
        public LocalizedTextContent FormatContent(TranslationKey key, ReadOnlySpan<TextArgument> arguments) => inner.FormatContent(key, arguments);
        public bool TryGetKey(string name, out TranslationKey key) => inner.TryGetKey(name, out key);
        public bool TryGetPlaceholders(TranslationKey key, out ReadOnlyMemory<TranslationPlaceholderDescriptor> placeholders) => inner.TryGetPlaceholders(key, out placeholders);
        public bool TryGetContentLocale(TranslationKey key, out string contentLocale) => inner.TryGetContentLocale(key, out contentLocale);
    }
}
