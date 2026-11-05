#pragma warning disable CA1822 // LibraryMessages mirrors generated instance members.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;
using Runic.Translations;
using Runic.Translations.Wpf;
using TUnit.Core;
using DisplayNameAttribute = TUnit.Core.DisplayNameAttribute;

namespace Runic.Translations.Wpf.Tests;

/// <summary>
/// Catalog sources from a shared context: <c>TranslationSource.ForCatalog</c>, <c>{rt:Message Catalog=…}</c> and
/// <c>{rt:CatalogSource}</c>. The case runs before the binding cases and stops sharing its context at the end, so their
/// live-source checks see none of its sources.
/// </summary>
internal sealed class CatalogSourceTests
{
    private const string Namespaces =
        "xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' " +
        "xmlns:rt='https://runic-artifex.eu/xaml/translations'";
    private const string GridId = "acme-grid";
    private const string Fingerprint = "sha256:0000000000000000000000000000000000000000000000000000000000000000";

    [Test, DisplayName("catalog: a library's XAML names its catalog and follows the shared context's locale"), NotInParallel(WpfSuite.Key, Order = 250)]
    public void ForCatalog()
    {
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        TranslationContext context = TranslationContext.CreateBuilder().Add(Registration(GridId, readable: true)).Add(Registration("plain", readable: false)).Build("en");
        try
        {
            Throws<InvalidOperationException>(() => TranslationSource.ForCatalog(GridId), "no context yet");
            TranslationSource.UseContext(context);
            Require(ReferenceEquals(TranslationSource.Context, context), "context");
            TranslationSource source = TranslationSource.ForCatalog(GridId);
            Require(ReferenceEquals(source, TranslationSource.ForCatalog(GridId)), "one source per catalog");
            Require(ReferenceEquals(source.Manager, context.GetManager(GridId)) && source.Messages is LibraryMessages, "the context's manager and the readable surface");
            Throws<KeyNotFoundException>(() => TranslationSource.ForCatalog("missing"), "unknown catalog");
            Throws<InvalidOperationException>(() => TranslationSource.ForCatalog("plain"), "no readable surface");

            var title = Parse<TextBlock>("<TextBlock {NS} Text=\"{rt:Message title, Catalog=acme-grid}\"/>");
            var rows = Parse<TextBlock>("<TextBlock {NS} Text=\"{rt:Message rows, Catalog=acme-grid, Arg0={Binding}}\"/>");
            rows.DataContext = 3L;
            var scope = Parse<StackPanel>("<StackPanel {NS} rt:TranslationProperties.Source='{rt:CatalogSource acme-grid}'><TextBlock Text=\"{rt:Message title}\"/></StackPanel>");
            Flush(dispatcher);
            Require(title.Text == "Grid" && rows.Text == "3 rows" && ((TextBlock)scope.Children[0]).Text == "Grid",
                "English: " + title.Text + " | " + rows.Text + " | " + ((TextBlock)scope.Children[0]).Text);

            // The hand-written provider completes at once, so the switch publishes on this thread.
            context.SetLocaleAsync("de").AsTask().GetAwaiter().GetResult();
            Flush(dispatcher);
            Require(title.Text == "Raster" && rows.Text == "3 Zeilen" && ((TextBlock)scope.Children[0]).Text == "Raster",
                "German: " + title.Text + " | " + rows.Text + " | " + ((TextBlock)scope.Children[0]).Text);

            // The named catalog is checked when the XAML loads.
            ThrowsLoad(() => Parse<TextBlock>("<TextBlock {NS} Text=\"{rt:Message titel, Catalog=acme-grid}\"/>"), "a typo in the named catalog");
            ThrowsLoad(() => Parse<TextBlock>("<TextBlock {NS} Text=\"{rt:Message rows, Catalog=acme-grid}\"/>"), "missing inputs");
            ThrowsLoad(() => Parse<TextBlock>("<TextBlock {NS} Text=\"{rt:Message title, Catalog=missing}\"/>"), "unknown catalog");
            ThrowsLoad(() => Parse<TextBlock>("<TextBlock {NS} Text=\"{rt:Message title, Catalog=acme-grid, Source={rt:CatalogSource acme-grid}}\"/>"), "Source and Catalog");

            // Another context cannot replace one whose sources bindings use; the same one can be passed again.
            TranslationSource.UseContext(context);
            Throws<InvalidOperationException>(() => TranslationSource.UseContext(TranslationContext.CreateBuilder().Add(Registration(GridId, readable: true)).Build()), "replace");
        }
        finally
        {
            TranslationSource.UseContext(null);
        }

        Require(TranslationSource.Context is null, "context cleared");
        Throws<InvalidOperationException>(() => TranslationSource.ForCatalog(GridId), "no context after clearing");
    }

    private static TranslationCatalogRegistration Registration(string id, bool readable) =>
        new(id, "en", Fingerprint, "{\"version\":2,\"contracts\":{},\"messages\":{}}",
            (_, _, _) => new Provider(id), readable ? static manager => new LibraryMessages(manager) : null);

    private static T Parse<T>(string xaml) => (T)XamlReader.Parse(xaml.Replace("{NS}", Namespaces, StringComparison.Ordinal));

    private static void Flush(Dispatcher dispatcher) => dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));

    private static void ThrowsLoad(Action action, string message)
    {
        try { action(); }
        catch (Exception exception) when (exception is XamlParseException or ArgumentException or InvalidOperationException or KeyNotFoundException) { return; }
        throw new InvalidOperationException("Did not fail to load: " + message);
    }

    private static void Throws<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ": " + message);
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    /// <summary>Mirrors a library's generated readable surface over a context manager.</summary>
    public sealed class LibraryMessages(ITranslationManager manager) : IReadableMessages
    {
        public const int ReadableNameVersion = 1;
        int IReadableMessages.ReadableNameVersion => ReadableNameVersion;
        private bool German => manager.Current.Locale == "de";
        public string title => German ? "Raster" : "Grid";
        public string rows(long count) => count + (German ? " Zeilen" : " rows");
    }

    private sealed class Provider(string catalog) : ITranslationProvider
    {
        public ValueTask<ITranslationSnapshot> GetSnapshotAsync(string requestedLocale, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ITranslationSnapshot>(new Snapshot(catalog, requestedLocale.Length == 0 ? "en" : requestedLocale));
    }

    // The snapshot of a hand-written catalog; LibraryMessages reads only its locale.
    private sealed class Snapshot(string catalog, string locale) : ITranslationSnapshot
    {
        public string Catalog => catalog;
        public string Locale => locale;
        public string Get(TranslationKey key) => throw new NotSupportedException();
        public bool TryGet(TranslationKey key, out string pattern) { pattern = ""; return false; }
        public string Format(TranslationKey key, ReadOnlySpan<TextArgument> arguments) => throw new NotSupportedException();
        public LocalizedTextContent FormatContent(TranslationKey key, ReadOnlySpan<TextArgument> arguments) => throw new NotSupportedException();
        public bool TryGetKey(string name, out TranslationKey key) { key = default; return false; }
        public bool TryGetPlaceholders(TranslationKey key, out ReadOnlyMemory<TranslationPlaceholderDescriptor> placeholders) { placeholders = default; return false; }
    }
}
