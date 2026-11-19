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

/// <summary>Framework text with its overlays, the application catalog's own keys, and the invocation culture.</summary>
internal sealed class OverlayTests
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr");
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de");

    // runic-command-line.fr.locale-v5.json is `runic-translations generate --emit-json` of a project with catalog
    // "runic-command-line", base locale fr and help.usage = Utilisation, help.options = Options, help.showHelp = Afficher
    // l'aide, diagnostics.unknownOption (input $option) = Option inconnue. ({$option}).
    private static TranslationOverlay FrenchOverlay() => TranslationOverlay.FromPacks(
        TranslationCommandTextResolver.CatalogId, new EmbeddedPacks(), [new TranslationOverlayLocale("fr")]);

    [Test, DisplayName("framework keys follow the framework's locales, application keys the application's fallback")]
    public async Task FrameworkAndApplicationLocales()
    {
        // As with relaxed completeness: the application's German snapshot carries its English messages as fallbacks.
        ITranslationSnapshot english = await TestTextCatalog.CreateProvider().GetSnapshotAsync("en");
        var resolver = new TranslationCommandTextResolver(new RequestedAs(english, "de"));
        // The framework's German; the application's help.usage (Synopsis) has no effect.
        Assert.Equal("Aufruf", resolver.Resolve("help.usage", German, []));
        // Application keys have no framework text and keep the application's fallback.
        Assert.Equal("Greet a person", resolver.Resolve("commands.greet", German, []));

        // A regional request: the framework's German parent, and the application's German.
        ITranslationSnapshot swiss = await TestTextCatalog.CreateProvider().GetSnapshotAsync("de-CH");
        var regional = new TranslationCommandTextResolver(swiss);
        Assert.Equal("Aufruf", regional.Resolve("help.usage", CultureInfo.GetCultureInfo("de-CH"), []));
        Assert.Equal("Grüßt eine Person", regional.Resolve("commands.greet", CultureInfo.GetCultureInfo("de-CH"), []));
    }

    [Test, DisplayName("a context builder resolves in the invocation culture, with a French overlay of the framework text")]
    public void BuilderFollowsCulture()
    {
        TranslationContextBuilder catalogs = TranslationContext.CreateBuilder()
            .AddCatalog(TestTextCatalog.Registration)
            .AddCatalog(TranslationCommandTextResolver.CatalogRegistration)
            .AddOverlay(FrenchOverlay());
        var resolver = new TranslationCommandTextResolver(catalogs, TestTextCatalog.CatalogId);

        // French comes from the overlay; the application's help.usage (Synopsis) has no effect.
        Assert.Equal("Utilisation", resolver.Resolve("help.usage", French, []));
        Assert.Equal("Option inconnue. (--bogus)", resolver.Resolve("diagnostics.unknown-option", French, ["--bogus"]));
        // A key the overlay lacks falls back along the framework's chain to English.
        Assert.Equal("Commands", resolver.Resolve("help.commands", French, []));
        // The application's own keys use its fallback locale.
        Assert.Equal("Greet a person", resolver.Resolve("commands.greet", French, []));

        // The same resolver resolves another invocation's culture.
        Assert.Equal("Aufruf", resolver.Resolve("help.usage", German, []));
        Assert.Equal("Optionen", resolver.Resolve("help.options", German, []));
        Assert.Equal("Grüßt eine Person", resolver.Resolve("commands.greet", German, []));
        Assert.Equal("Usage", resolver.Resolve("help.usage", CultureInfo.InvariantCulture, []));
    }

    [Test, DisplayName("a context builder without the framework catalog uses the built-in text, and an invalid culture the default")]
    public void BuilderWithoutFrameworkCatalog()
    {
        var resolver = new TranslationCommandTextResolver(TranslationContext.CreateBuilder().AddCatalog(TestTextCatalog.Registration), TestTextCatalog.CatalogId);
        Assert.Equal("Optionen", resolver.Resolve("help.options", German, []));
        Assert.Equal("Options", resolver.Resolve("help.options", French, []));
        Assert.Equal("Usage", resolver.Resolve("help.usage", French, []));
        Assert.Equal("Greet a person", resolver.Resolve("commands.greet", French, []));
        var frameworkOnly = new TranslationCommandTextResolver(TranslationContext.CreateBuilder().AddCatalog(TranslationCommandTextResolver.CatalogRegistration)
            .AddOverlay(FrenchOverlay()), applicationCatalogId: null);
        Assert.Equal("Utilisation", frameworkOnly.Resolve("help.usage", French, []));
        Assert.Equal(null, frameworkOnly.Resolve("commands.greet", French, []));
    }

    [Test, DisplayName("a context-backed resolver follows the context's locale and its overlays")]
    public async Task ContextFollowsLocale()
    {
        TranslationContext context = await TranslationContext.CreateBuilder()
            .AddCatalog(TestTextCatalog.Registration)
            .AddCatalog(TranslationCommandTextResolver.CatalogRegistration)
            .AddOverlay(FrenchOverlay())
            .BuildAsync("de");
        var resolver = new TranslationCommandTextResolver(context, TestTextCatalog.CatalogId);
        Assert.Equal("Aufruf", resolver.Resolve("help.usage", German, []));
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
        using var standardError = new StringWriter();
        var resolver = new TranslationCommandTextResolver(manager) { WarningOutput = standardError };
        using var listener = new Capture();
        Trace.Listeners.Add(listener);
        try
        {
            Assert.Equal("Usage", resolver.Resolve("help.usage", German, []));
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
        // A shipped console app has no trace listener, so the warning also goes to standard error, once.
        string written = standardError.ToString();
        Assert.True(written.StartsWith("warning: Runic.Translations.CommandLine: the invocation culture 'de'", StringComparison.Ordinal), written);
        Assert.Equal(1, written.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Test, DisplayName("a locale the application has but runic-command-line lacks is reported once"), NotInParallel("trace")]
    public async Task MissingFrameworkLocaleReportedOnce()
    {
        ITranslationSnapshot english = await TestTextCatalog.CreateProvider().GetSnapshotAsync("en");
        using var standardError = new StringWriter();
        // The application's text is French; the built-in framework text has only en and de.
        var resolver = new TranslationCommandTextResolver(new RequestedAs(english, "fr")) { WarningOutput = standardError };
        using var listener = new Capture();
        Trace.Listeners.Add(listener);
        try
        {
            // The framework's English help.usage and help.options: it has no French.
            Assert.Equal("Usage", resolver.Resolve("help.usage", French, []));
            Assert.Equal("Options", resolver.Resolve("help.options", French, []));
            // An application key alone reports nothing: the warning is about framework text.
            Assert.Equal(null, new TranslationCommandTextResolver(new RequestedAs(english, "fr")).Resolve("commands.unknown", French, []));
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }

        Assert.Equal(1, listener.Count("the catalog 'runic-command-line' has no text for the locale 'fr'"), string.Join("\n", listener.Messages));
        string written = standardError.ToString();
        Assert.True(written.StartsWith("warning: Runic.Translations.CommandLine: the catalog 'runic-command-line' has no text for the locale 'fr'", StringComparison.Ordinal), written);
        Assert.True(written.Contains("translations/overrides/runic-command-line/fr.rmf2", StringComparison.Ordinal), written);
        Assert.Equal(1, written.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);

        // German, which both have, and a French overlay of the framework text report nothing.
        using var quiet = new StringWriter();
        ITranslationSnapshot german = await TestTextCatalog.CreateProvider().GetSnapshotAsync("de");
        Assert.Equal("Optionen", new TranslationCommandTextResolver(german) { WarningOutput = quiet }.Resolve("help.options", German, []));
        TranslationContext context = await TranslationContext.CreateBuilder()
            .AddCatalog(TestTextCatalog.Registration)
            .AddCatalog(TranslationCommandTextResolver.CatalogRegistration)
            .AddOverlay(FrenchOverlay())
            .BuildAsync("fr");
        Assert.Equal("Utilisation", new TranslationCommandTextResolver(context, TestTextCatalog.CatalogId) { WarningOutput = quiet }.Resolve("help.usage", French, []));
        Assert.Equal("", quiet.ToString());
    }

    [Test, DisplayName("AddRunicCommandLineTranslations adds the catalog to the shared context and a resolver that follows it")]
    public async Task HostingRegistration()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Microsoft.Extensions.DependencyInjection.RunicTranslationsServiceCollectionExtensions.AddRunicTranslations(services, options => options.InitialLocale = "de")
            .AddCatalog(TestTextCatalog.Registration)
            .AddOverlay(FrenchOverlay());
        Microsoft.Extensions.DependencyInjection.RunicCommandLineTranslationsServiceCollectionExtensions.AddRunicCommandLineTranslations(services, TestTextCatalog.CatalogId);
        // A second call registers nothing more.
        int count = services.Count;
        Microsoft.Extensions.DependencyInjection.RunicCommandLineTranslationsServiceCollectionExtensions.AddRunicCommandLineTranslations(services, TestTextCatalog.CatalogId);
        Assert.Equal(count, services.Count, "second registration");

        await using Microsoft.Extensions.DependencyInjection.ServiceProvider provider =
            Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
        await Microsoft.Extensions.DependencyInjection.RunicTranslationsServiceProviderExtensions.InitializeRunicTranslationsAsync(provider);
        var context = (ITranslationContext)provider.GetService(typeof(ITranslationContext))!;
        Assert.True(context.TryGetManager(TranslationCommandTextResolver.CatalogId, out _), "runic-command-line is not registered");
        var resolver = (Runic.CommandLine.ICommandTextResolver)provider.GetService(typeof(Runic.CommandLine.ICommandTextResolver))!;
        // The framework catalog supplies framework text and the application's catalog its own keys, both in the context's locale.
        Assert.Equal("Aufruf", resolver.Resolve("help.usage", German, []));
        Assert.Equal("Grüßt eine Person", resolver.Resolve("commands.greet", German, []));
        Assert.Equal("Optionen", resolver.Resolve("help.options", German, []));
        await context.SetLocaleAsync("fr");
        Assert.Equal("Afficher l'aide", resolver.Resolve("help.show-help", French, []));
        Assert.Throws<ArgumentNullException>(() =>
            Microsoft.Extensions.DependencyInjection.RunicCommandLineTranslationsServiceCollectionExtensions.AddRunicCommandLineTranslations(null!));
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
