using Runic.Application.Views;

namespace Runic.Translations.Editor;

/// <summary>
/// The editor's window. A close the user starts (the title-bar close button, Alt+F4, the window manager) and the
/// application's own <c>RequestCloseAsync</c> ask the page first, because the unsaved work lives there
/// (<see cref="EditorViewModel.CanCloseAsync"/>). On Runic Desktop's embedded window the close waits for that answer; a
/// browser presentation (<c>--browser</c>, a fallback) and <c>serve</c> cannot keep the window open, so the user's close
/// is unguarded there (event 1053) and only <c>RequestCloseAsync</c> asks.
/// </summary>
public sealed partial class EditorWindow : RunicWindow<EditorViewModel>
{
    public EditorWindow() => GuardClose = true;

    protected override async ValueTask<bool> CanCloseAsync(CancellationToken cancellationToken) =>
        await base.CanCloseAsync(cancellationToken).ConfigureAwait(false) &&
        (DataContext is not { } editor || await editor.CanCloseAsync(cancellationToken).ConfigureAwait(false));
}
