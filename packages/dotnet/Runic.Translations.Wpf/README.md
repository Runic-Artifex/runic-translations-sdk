# Runic.Translations.Wpf

The bounded native RMF2 adapter targets WPF on .NET 10 for Windows. Construct a
`WpfInlineRenderer` from the generated markup contract once, then call `SetContent`
on a `TextBlock` on its UI thread. The caller supplies an `Action<Uri>` navigation
handler, typed action/link/icon slots, optional custom markup factories, and an
optional theme callback. Rendering never invokes navigation or action callbacks.

With the readable generated surface, bind a structured message's slots through
its typed slot type and pass the bound value. Use named arguments, because slots
of the same kind share a type:

```csharp
var renderer = new WpfInlineRenderer(AppTextCatalog.Rmf2MarkupContract, Navigate);
renderer.SetContent(helpBlock, text.Messages.checkout_help.Bind(new(
    guide: new InlineLinkBinding(guideUri),
    retry: new InlineActionBinding(Retry))));
```

A wrong binding kind, a missing or misspelled slot, or another message's slot
type is a compile error. The renderer still validates every runtime rule
(slot multiplicity, link schemes, icon alternate text, nesting) against the
current locale's content, including external packs. The string-key overload
`SetContent(target, key, content, slots)` remains for dynamic keys. Build one
renderer per catalog from that catalog's `Rmf2MarkupContract`: a bound value
from another catalog is detected only at runtime, by an unknown key or
mismatched slots, or not at all when that catalog has a compatible message
with the same key.

Icons use `InlineIconBinding` with a `Func<FrameworkElement>` that creates a fresh
asset for every render. Meaningful icons require a localized accessible name;
decorative icons are excluded from WPF's control/content accessibility views.
Custom factories receive validated options and child inlines; they must return a
fresh `Inline`. They are linked by canonical contract name, such as `shop:badge`.

WPF controls are not shared between messages or windows. Replacing content drops
the previous inline tree and deactivates its callbacks, even if a caller retains
a detached button. Call `ClearContent` when disposing the host. The adapter builds the replacement
before touching the existing control so a binding failure preserves its content.
The theme owns final typography and button presentation. No icon library ships.

Retired links and action buttons are also disabled, so retained detached controls
look inert.

## WPF quick start

Localize one screen with Runic Translations while the rest of the application
keeps its `.resx` and `ResourceDictionary` resources. WPF on .NET 10 for Windows.

### 1. Install

Your WPF project targets Windows:

```xml
<PropertyGroup>
  <TargetFramework>net10.0-windows</TargetFramework>
  <UseWPF>true</UseWPF>
</PropertyGroup>
```

Add the runtime and the build package, or reference the translations library
that holds your catalog (see the
[.NET quick start](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/quickstart-dotnet.md)).
Use one exact version for all of them:

```powershell
dotnet add package Runic.Translations.Wpf --version <VERSION>
dotnet add package Runic.Translations.Build --version <VERSION>
```

`Runic.Translations.Wpf` brings `Runic.Translations` with it.

### 2. Author a catalog

Put `runic.json` and one `.rmf2` file per locale in the project (the
[template](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/quickstart-dotnet.md#1-create-the-translations-library)
scaffolds them). For one screen:

```rmf2
orders {
  title = Your orders
  greeting = Hello {$name}
  count =
    .input {$count :integer}
    .match $count
    one {{One open order}}
    * {{{$count} open orders}}
  help = Need help? {#link ref=guide}Read the guide{/link}.
}
```

Add `de.rmf2` (or any locale) with the same keys. The build generates `AppText`
and `AppTextCatalog`. `AppText.Messages` is the readable surface, with members
named by the flattened key: `orders_title`, `orders_greeting(name)`,
`orders_count(count)` and `orders_help`, whose slots type is
`AppTextSlots.orders_help`.

### 3. Create one source at startup

```csharp
protected override async void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);
    ITranslationManager manager = await AppTextCatalog.CreateManagerAsync("en");
    var text = new AppText(manager);
    var renderer = new WpfInlineRenderer(AppTextCatalog.Rmf2MarkupContract, uri => Process.Start(
        new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }));
    TranslationSource.Default = new TranslationSource(manager, text.Messages, renderer);
    new MainWindow { DataContext = new MainViewModel(manager) }.Show();
}
```

Pass `text.Messages`, not `text`; the constructor rejects anything that is not a
generated readable surface. Create the source on the UI thread (it uses the
application's dispatcher, else the current thread's, and throws if there is none;
pass a `Dispatcher` otherwise). The renderer is needed only for messages with
markup. Set `Default` once at startup: replacing it after a binding used it
throws, and disposing the default source clears it. Bindings that are already
loaded keep the source they resolved, so after a reset or dispose they stop
following the new default; reload those views or name a `Source` explicitly.

If you await in `OnStartup`, keep the default context: `ConfigureAwait(false)`
would continue on a thread-pool thread, where the source cannot find a dispatcher
and `Show()` fails.

### 4. Bind the screen

```xml
<Window xmlns:rt="clr-namespace:Runic.Translations.Wpf;assembly=Runic.Translations.Wpf"
        xmlns:res="clr-namespace:MyApp.Properties" ...>
  <StackPanel>
    <!-- Plain message. -->
    <TextBlock Text="{rt:Message orders_title}" FontSize="20"/>

    <!-- Inputs: Arg0..Arg3 in the generated method's parameter order (a constant is {Binding Source=text}). -->
    <TextBlock Text="{rt:Message orders_greeting, Arg0={Binding UserName}}"/>
    <TextBlock Text="{rt:Message orders_count, Arg0={Binding OpenOrders}}"/>

    <!-- Message with markup: the generated typed slots come from the view model. -->
    <TextBlock rt:TranslationProperties.RichMessage="orders_help"
               rt:TranslationProperties.Slots="{Binding HelpSlots}"/>

    <!-- Everything else is unchanged. -->
    <Button Content="{x:Static res:Resources.Close}"/>
  </StackPanel>
</Window>
```

`{rt:Message}` works on any dependency property (`Content`, `Header`, `ToolTip`,
`Title`, ...), in styles and in templates. The slots are the generated typed
object, so a wrong binding kind or a missing slot is a compile error in the view
model:

```csharp
public AppTextSlots.orders_help HelpSlots { get; } =
    new(guide: new InlineLinkBinding(guideUri));
```

An `IReadOnlyDictionary<string, InlineMarkupBinding>` by slot ID is accepted for
dynamic use, without compile-time checks.

More than four inputs, or inputs you want checked by name, use the long form. The
names are the generated method's parameter names:

```xml
<TextBlock>
  <TextBlock.Text>
    <rt:Message Key="orders_greeting">
      <rt:MessageInput Name="name" Value="{Binding UserName}"/>
    </rt:Message>
  </TextBlock.Text>
</TextBlock>
```

For rich messages with inputs, set `rt:TranslationProperties.Arguments` to a
list in parameter order or a dictionary by parameter name. A null input becomes
empty text; other values convert with the invariant culture to the parameter
type, and a number input that is null or not convertible is an error.

Mistakes surface early. A gap in `Arg0..Arg3` or mixed input forms fails when the
XAML loads. With an explicit `Source=` (or, for a rich message, a
`rt:TranslationProperties.Source` set on the element before `RichMessage`), a
mistyped message name, a wrong input name or count, or using a plain message as
rich (or the reverse) fails then too, with the parameter names in the exception.
Without one, the catalog is inherited or the default (see step 6) and cannot be
known at load, so the load asks every live `TranslationSource`: it fails when none
has a matching message, which catches a typo in a one-catalog app, while a key
that exists only in a parent's catalog still loads. The exact check, and
everything found while binding or rendering (a missing slot, an input of the wrong
type, a throwing message), is traced like any WPF binding error: plain text shows
`[key]`, rich content keeps what it had. With no source created yet (designers,
early startup) nothing is checked at load.
A rich message whose inputs have not been set yet renders once they are.

### 5. Switch locale

```csharp
await manager.SetLocaleAsync("de");
```

Every `{rt:Message}` and `RichMessage` refreshes when the manager publishes the
new snapshot, from any thread: the source marshals to the dispatcher it was
created on, and each rich element renders on its own dispatcher. Several changes
in a row render once. The built-in manager also publishes
`ITranslationManager.RefreshAsync` (for example after external pack bytes
change), although that never raises `LocaleChanged`. A custom
`ITranslationManager` is followed through `LocaleChanged` only; implement
`ITranslationSnapshotNotifier` or call `source.Invalidate()` after refreshing it.

Nothing leaks when windows close: the manager holds only a weak reference to the
source, the source holds rich elements weakly, and WPF's own bindings do the
rest. Dispose the source only if you replace it during the application's life.

### 6. Coexist with existing resources

`{rt:Message}` is an ordinary markup extension, so it mixes with `{x:Static}`,
`{DynamicResource}` and `{StaticResource}` in one tree, and you can move a screen
at a time. Keep `.resx` for strings you have not migrated. To use a second
catalog, create another `TranslationSource` and set it on a parent element with
`rt:TranslationProperties.Source`, for example in XAML with `{x:Static}` or a
resource. Every `{rt:Message}` and `RichMessage` below that element, including
those in templates instantiated under it, uses that catalog even when the other
catalog has the same key; elsewhere `TranslationSource.Default` applies. A single
`{rt:Message Source=...}` overrides both. Style setters follow the styled element,
like any other element. Locale and culture
are separate: the source follows the manager's locale only, so set `CultureInfo` and
`FrameworkElement.Language` yourself where existing resources need them. There is
no `.resx` importer and no `FlowDocument` conversion; use `WpfDocumentRenderer`
for document messages.

### Designer

Visual Studio and Blend do not run `OnStartup`, so no default source exists.
`{rt:Message key}` then shows `[key]` and rich elements stay empty instead of
failing every view. At run time an element without any source traces a binding error and shows `[key]`. Message names
are not checked at build time yet.

## Documents

`WpfDocumentRenderer` renders RMF2 document messages (paragraphs, headings and
flat lists) into a read-only `FlowDocumentScrollViewer`:

```csharp
var documents = new WpfDocumentRenderer(AppTextCatalog.Rmf2MarkupContract, Navigate) { HeadingBase = 2 };
documents.SetContent(helpViewer, text.Messages.guide_backup(fileName: fileName).Bind(new(
    check: new InlineActionBinding(Check),
    guide: new InlineLinkBinding(guideUri))));
```

Headings are bold paragraphs that UI Automation exposes as headings at
`HeadingBase + level - 1`; lists and list items are exposed as UI Automation
lists and items with their positions. Links and actions get their slot name as
`AutomationId`. Every render builds a fresh `FlowDocument` with the content
locale's `Language` and `FlowDirection`; replacing it or calling `ClearContent`
retires its callbacks. Paragraphs and headings with no children or only empty
text, and lists with no items, are skipped.
Copying a selection puts the plain-text projection on the clipboard, with list
markers and block breaks, action labels and meaningful icon text, Windows line
endings, and without link destinations or rich formats. See the
[document adapter guide](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/rmf2.md#wpf-document-adapter).

The repository has an interactive Windows sample: a window with the bounded
example of issue #15 in a `FlowDocumentScrollViewer`, buttons to render again,
clear the document and show the clipboard text. Its source is
[`DocumentSample.cs`](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/tests/dotnet/Runic.Translations.Wpf.Tests/DocumentSample.cs).
In a clone of the repository, run it with:

```powershell
dotnet run --project tests/dotnet/Runic.Translations.Wpf.Tests -- --sample
```

Cross-compilation is supported through `EnableWindowsTargeting`. Runtime and
accessibility verification require Windows; Linux cannot execute WPF controls.
