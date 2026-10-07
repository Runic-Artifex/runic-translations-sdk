using System;
using System.Threading.Tasks;
using Example.ReadableConsumer;
using Runic.Translations;

// Readable accessors: one member per canonical key, the same names as ESM.
ITranslationManager manager = await ShopTextCatalog.CreateManagerAsync("en").ConfigureAwait(false);
var text = new ShopText(manager);
var renderer = new Rmf2InlineRenderer(ShopTextCatalog.Rmf2MarkupContract);
int retries = 0;

// Typed slots: bind every slot with named arguments. A wrong kind, a missing or
// misspelled slot, or another message's slot type does not compile.
BoundLocalizedTextContent help = text.Messages.checkout_help.Bind(new(
    guide: new InlineLinkBinding(new Uri("https://example.test/guide")),
    retry: new InlineActionBinding(() => retries++)));
BoundLocalizedTextContent rating = text.Messages.checkout_rating.Bind(new(
    reviews: new InlineLinkBinding(new Uri("https://example.test/reviews")),
    star: new InlineIconBinding(new object(), Decorative: false, AccessibleName: locale => locale == "de" ? "5 Sterne" : "5 stars")));

Require(text.Messages.greeting(name: "Ada") == "Hello Ada", "greeting");
// `user-name` is not a C# identifier, so that parameter keeps its encoded name.
Require(text.Messages.profile_badge(r_757365722d6e616d65: "ada") == "Signed in as ada", "badge");
Require(renderer.ToPlainText(help, allowActionLabels: true) == "Read the guide or try again.", "help");
Require(renderer.ToPlainText(rating, annotateLinkDestinations: true) == "Rated 5 stars by our customers (https://example.test/reviews).", "rating");

// The same bound values render the German content; its help text omits the
// conditional retry slot (min: 0), which stays a required argument in C#.
await manager.SetLocaleAsync("de").ConfigureAwait(false);
help = text.Messages.checkout_help.Bind(new(
    guide: new InlineLinkBinding(new Uri("https://example.test/anleitung")),
    retry: new InlineActionBinding(() => retries++)));
Require(text.Messages.greeting(name: "Ada") == "Hallo Ada", "greeting de");
Require(renderer.ToPlainText(help) == "Lies die Anleitung.", "help de");
Require(renderer.ToPlainText(text.Messages.checkout_rating.Bind(new(
    reviews: new InlineLinkBinding(new Uri("https://example.test/reviews")),
    star: new InlineIconBinding(new object(), false, locale => locale == "de" ? "5 Sterne" : "5 stars")))) == "Mit 5 Sterne bewertet von unseren Kunden.", "rating de");
Require(retries == 0, "rendering never activates actions");

// The encoded members stay the stable machine-facing contract.
Require(text.r_6772656574696e67(r_6e616d65: "Ada") == text.Messages.greeting(name: "Ada"), "encoded");
Console.WriteLine("PASS readable C# surface and typed slots");
return 0;

static void Require(bool condition, string step)
{
    if (!condition) throw new InvalidOperationException("Readable consumer failed at " + step + ".");
}
