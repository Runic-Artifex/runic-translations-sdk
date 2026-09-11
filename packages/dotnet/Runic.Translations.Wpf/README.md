# Runic.Translations.Wpf

The bounded native RMF2 adapter targets WPF on .NET 10 for Windows. Construct a
`WpfInlineRenderer` from the generated markup contract once, then call `SetContent`
on a `TextBlock` on its UI thread. The caller supplies an `Action<Uri>` navigation
handler, typed action/link/icon slots, optional custom markup factories, and an
optional theme callback. Rendering never invokes navigation or action callbacks.

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
