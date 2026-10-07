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

Cross-compilation is supported through `EnableWindowsTargeting`. Runtime and
accessibility verification require Windows; Linux cannot execute WPF controls.
