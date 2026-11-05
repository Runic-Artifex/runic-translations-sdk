using Acme.Widgets;
using Runic.Translations;
using Shop;

// One context for the application's catalog and the library's. ShopTextCatalog.Overlays holds the overrides of
// translations/overrides/acme.widgets/, compiled into this assembly, so Build composes without I/O.
TranslationContext translations = TranslationContext.CreateBuilder()
    .Add(ShopTextCatalog.Registration)
    .Add(WidgetsTextCatalog.Registration)
    .AddOverlays(ShopTextCatalog.Overlays)
    .Build("en");

var shop = new ShopText(translations);
var meter = new StorageMeter(translations);

// The locales every catalog shows, the French that the overrides add included: offer these in a language picker.
Console.WriteLine("Languages: " + string.Join(", ", translations.SupportedLocales.Order(StringComparer.Ordinal)));

foreach (string locale in args.Length > 0 ? args : ["en", "de", "de-CH", "fr"])
{
    // One switch for every catalog: the library's text follows the application's.
    await translations.SetLocaleAsync(locale);
    Console.WriteLine($"[{locale}] {shop.Messages.title} | {meter.Title} | {meter.Describe(0, 0, 15)} | " +
        $"{meter.Describe(3, 1.5m, 15)} | {shop.Messages.price(amount: 4.99m)}");
    foreach (TranslationCatalogStatus catalog in translations.Catalogs)
        if (catalog.LocaleResolution != LocaleResolution.Exact)
            Console.WriteLine($"  {catalog.CatalogId} shows {catalog.Locale} for {catalog.RequestedLocale} ({catalog.LocaleResolution})");
}
