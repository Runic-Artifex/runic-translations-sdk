using System;
using System.Windows;
using System.Windows.Controls;
using Runic.Translations;
using Runic.Translations.Wpf;

/// <summary>
/// The interactive Windows sample: renders the #15 example in a FlowDocumentScrollViewer.
/// Run with <c>dotnet run --project tests/dotnet/Runic.Translations.Wpf.Tests -- --sample</c>.
/// </summary>
internal static class DocumentSample
{
    internal static int Run()
    {
        CompiledTranslationSnapshot snapshot = DocumentFixture.CreateSnapshot();
        var status = new TextBlock { Margin = new Thickness(8), Text = "Tab to the link and the action, press Enter, select text and copy it." };
        var viewer = new FlowDocumentScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var renderer = new WpfDocumentRenderer(DocumentFixture.MarkupContract, uri => status.Text = "Navigate to " + uri);
        int renders = 0, checks = 0;
        void Render()
        {
            int generation = ++renders;
            var content = new LocalizedDocumentContent<BackupSlots>(DocumentFixture.Backup(snapshot, "<report>.txt"));
            renderer.SetContent(viewer, content.Bind(new BackupSlots(
                new InlineActionBinding(() => status.Text = "Check activated " + ++checks + " time(s) from render " + generation + "."),
                new InlineLinkBinding(new Uri("https://example.test/guide")))));
        }
        var again = new Button { Content = "_Render again", Margin = new Thickness(8, 0, 0, 0) };
        again.Click += (_, _) => { Render(); status.Text = "Rendered again; the previous link and action are retired."; };
        var clear = new Button { Content = "_Clear", Margin = new Thickness(8, 0, 0, 0) };
        clear.Click += (_, _) => { WpfDocumentRenderer.ClearContent(viewer); status.Text = "Cleared."; };
        var copied = new Button { Content = "Show c_lipboard", Margin = new Thickness(8, 0, 0, 0) };
        copied.Click += (_, _) => status.Text = "Clipboard: " + (Clipboard.ContainsText() ? Clipboard.GetText().Replace("\n", "⏎", StringComparison.Ordinal) : "(no text)");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(again); buttons.Children.Add(clear); buttons.Children.Add(copied);
        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Top); DockPanel.SetDock(status, Dock.Bottom);
        root.Children.Add(buttons); root.Children.Add(status); root.Children.Add(viewer);
        Render();
        var window = new Window { Title = "Runic RMF2 document sample", Width = 640, Height = 420, Content = root };
        return new Application().Run(window);
    }
}
