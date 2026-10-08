using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Navigation;
using Runic.Translations;
using Runic.Translations.Wpf;

/// <summary>WPF document adapter checks: structure, UI Automation, callback retirement and the copy projection.</summary>
internal static class DocumentTests
{
    internal static void Run()
    {
        CompiledTranslationSnapshot snapshot = DocumentFixture.CreateSnapshot();
        int checks = 0, navigations = 0;
        var renderer = new WpfDocumentRenderer(DocumentFixture.MarkupContract, _ => navigations++);
        var semantic = new Rmf2DocumentRenderer(Rmf2MarkupContract.Link(DocumentFixture.MarkupContract));
        var typed = new LocalizedDocumentContent<BackupSlots>(DocumentFixture.Backup(snapshot, "<report>.txt"));
        BoundLocalizedDocumentContent bound = typed.Bind(new BackupSlots(new InlineActionBinding(() => checks++), new InlineLinkBinding(new Uri("https://example.test/guide"))));
        var viewer = new FlowDocumentScrollViewer();

        // Structure: p, ul(li, li), p in a fresh FlowDocument with the content locale.
        renderer.SetContent(viewer, bound);
        FlowDocument document = viewer.Document;
        Require(document.Language.IetfLanguageTag == "en" && document.FlowDirection == FlowDirection.LeftToRight, "Document lost its locale or direction.");
        Require(Shape(document) == "Paragraph(Bold(Run:Before continuing)|Run:, save a copy of |Run:<report>.txt|Run:.)|" +
            "List[Disc](ListItem(Paragraph(Run:Read the |Hyperlink(Run:guide)|Run:.))|ListItem(Paragraph(UI:Button|Run: the result.)))|" +
            "Paragraph(Run:You can continue when the check finishes.)", "Unexpected document shape: " + Shape(document));
        var list = (List)document.Blocks.ElementAt(1);
        Require(list.ListItems.All(item => item.Blocks.FirstBlock.Margin == new Thickness(0)), "List item paragraphs kept paragraph spacing.");

        // Slot refs name the interactive elements for UI Automation; occurrence keys are not stable across translations.
        Hyperlink link = Descendants(document).OfType<Hyperlink>().Single();
        Button button = Descendants(document).OfType<InlineUIContainer>().Select(container => container.Child).OfType<Button>().Single();
        Require(AutomationProperties.GetAutomationId(link) == "guide" && AutomationProperties.GetAutomationId(button) == "check", "Slot refs did not become automation IDs.");

        // UI Automation: lists and items expose their roles and positions.
        var listPeer = ContentElementAutomationPeer.CreatePeerForElement(list)!;
        Require(listPeer.GetAutomationControlType() == AutomationControlType.List && listPeer.IsControlElement(), "The list is not exposed as a list.");
        var itemPeer = ContentElementAutomationPeer.CreatePeerForElement(list.ListItems.ElementAt(1))!;
        Require(itemPeer.GetAutomationControlType() == AutomationControlType.ListItem && itemPeer.GetPositionInSet() == 2 && itemPeer.GetSizeOfSet() == 2,
            "List items do not expose their position.");
        // Item names are the copy text without the native list marker, including the action label.
        string[] names = list.ListItems.Select(item => ContentElementAutomationPeer.CreatePeerForElement(item)!.GetName()).ToArray();
        Require(names.SequenceEqual(["Read the guide.", "Check the result."]), "List item names: " + string.Join(" | ", names));

        // Activation goes through the active lifetime.
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        link.RaiseEvent(new RequestNavigateEventArgs(link.NavigateUri, null) { RoutedEvent = Hyperlink.RequestNavigateEvent });
        Require(checks == 1 && navigations == 1, "Active callbacks did not fire once.");

        // A rejected render keeps the displayed document and its callbacks.
        bool rejected = false;
        try { renderer.SetContent(viewer, "backup", DocumentFixture.Backup(snapshot, "a.txt"), new Dictionary<string, MarkupBinding> { ["check"] = new InlineActionBinding(() => { }) }); }
        catch (TranslationFormatException) { rejected = true; }
        Require(rejected && ReferenceEquals(document, viewer.Document) && button.IsEnabled && link.IsEnabled, "A rejected render replaced or retired the displayed document.");

        // Replacing retires the previous callbacks: retained, detached controls are disabled and inert.
        renderer.SetContent(viewer, bound);
        Require(!ReferenceEquals(document, viewer.Document), "A successful render did not replace the document.");
        Require(!button.IsEnabled && !link.IsEnabled, "Retired controls stayed enabled.");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        link.RaiseEvent(new RequestNavigateEventArgs(link.NavigateUri, null) { RoutedEvent = Hyperlink.RequestNavigateEvent });
        Require(checks == 1 && navigations == 1, "Detached callbacks remained active.");

        // The dictionary overload renders the same document.
        string boundShape = Shape(viewer.Document);
        renderer.SetContent(viewer, "backup", typed, bound.Slots);
        Require(Shape(viewer.Document) == boundShape, "The dictionary overload rendered a different document.");

        // Clearing removes the document and retires its callbacks.
        Button cleared = Descendants(viewer.Document).OfType<InlineUIContainer>().Select(container => container.Child).OfType<Button>().Single();
        WpfDocumentRenderer.ClearContent(viewer);
        cleared.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(viewer.Document is null && checks == 1 && !cleared.IsEnabled, "Clear did not retire the document.");

        Headings(snapshot);
        EmptyBlocks();
        Copy(snapshot, renderer, semantic, bound);
        Console.WriteLine("PASS WPF document structure, UI Automation, callback retirement and copy projection.");
    }

    private static void Headings(CompiledTranslationSnapshot snapshot)
    {
        var slots = new Dictionary<string, MarkupBinding> { ["star"] = DocumentFixture.Star() };
        var viewer = new FlowDocumentScrollViewer();
        new WpfDocumentRenderer(DocumentFixture.MarkupContract, _ => { }).SetContent(viewer, "lists", DocumentFixture.Lists(snapshot), slots);
        Require(Shape(viewer.Document) == "Paragraph(Run:Appendix)|List[LowerLatin@25](ListItem(Paragraph(Run:Twenty-five))|ListItem(Paragraph(Run:Twenty-six))|" +
            "ListItem(Paragraph(Run:Twenty-seven)))|List[Disc](ListItem(Paragraph(Run:First line|LineBreak|Run:second line))|ListItem(Paragraph(Italic(Run:Only)|Run: |UI:IconHost)))",
            "Unexpected list shape: " + Shape(viewer.Document));
        var heading = (Paragraph)viewer.Document.Blocks.FirstBlock;
        var peer = ContentElementAutomationPeer.CreatePeerForElement(heading)!;
        Require(peer.GetHeadingLevel() == AutomationHeadingLevel.Level2 && peer.GetAutomationControlType() == AutomationControlType.Text &&
            peer.IsControlElement() && peer.GetName() == "Appendix", "The heading is not exposed with its level.");

        var deep = new WpfDocumentRenderer(DocumentFixture.MarkupContract, _ => { }) { HeadingBase = 9 };
        deep.SetContent(viewer, "lists", DocumentFixture.Lists(snapshot), slots);
        Require(AutomationProperties.GetHeadingLevel(viewer.Document.Blocks.FirstBlock) == AutomationHeadingLevel.Level9, "The heading base was not applied.");
        bool rejected = false;
        try { _ = new WpfDocumentRenderer(DocumentFixture.MarkupContract, _ => { }) { HeadingBase = 10 }; }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Require(rejected, "An out-of-range heading base was accepted.");

        // The theme sees every block and inline by contract name.
        var themed = new List<string>();
        new WpfDocumentRenderer(DocumentFixture.MarkupContract, _ => { }, theme: (name, element) => themed.Add(name + ":" + element.GetType().Name))
            .SetContent(viewer, "lists", DocumentFixture.Lists(snapshot), slots);
        Require(string.Join(",", themed) == "runic:h:HeadingParagraph,runic:li:DocumentListItem,runic:li:DocumentListItem,runic:li:DocumentListItem,runic:ol:DocumentList," +
            "runic:br:LineBreak,runic:li:DocumentListItem,runic:em:Italic,runic:icon:InlineUIContainer,runic:li:DocumentListItem,runic:ul:DocumentList",
            "Unexpected theme calls: " + string.Join(",", themed));

        // Native markers match the plain-text numbering, which the copy projection writes.
        Require(WpfDocumentRenderer.Number(27, "lower-alpha") == "aa" && WpfDocumentRenderer.Number(702, "upper-alpha") == "ZZ" &&
            WpfDocumentRenderer.Number(3999, "upper-roman") == "MMMCMXCIX" && WpfDocumentRenderer.Number(4000, "lower-roman") == "4000",
            "List numbering differs from the plain-text projection.");
    }

    private static void EmptyBlocks()
    {
        var none = new Dictionary<string, string>();
        var options = new Dictionary<string, string> { ["start"] = "1", ["marker"] = "decimal" };
        DocumentBlock Text(string name, string occurrence, string text) => new(name, none, [], [new InlineMarkupRun("text", text, [], none, null, true)], null, occurrence, false);
        var renderer = new WpfDocumentRenderer(DocumentFixture.MarkupContract, _ => { });
        // Empty paragraphs, headings and lists are skipped, as in plain text; an empty list item keeps its place.
        DocumentRender render = renderer.Build([
            new("runic:p", none, [], [], null, "p[1]", false), Text("runic:p", "p[2]", "A"), Text("runic:h", "h[1]", ""),
            new("runic:ul", none, [], [], null, "ul[1]", false), new("runic:ol", options, [Text("runic:li", "ol[1]/li[1]", "")], [], null, "ol[1]", false),
            Text("runic:p", "p[3]", "B"),
        ], "en");
        Require(Shape(render.Document) == "Paragraph(Run:A)|List[Decimal](ListItem(Paragraph(Run:)))|Paragraph(Run:B)", "Empty blocks: " + Shape(render.Document));
        // WPF list markers count from an int StartIndex: numbering past int.MaxValue is rejected.
        var last = new Dictionary<string, string> { ["start"] = int.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture), ["marker"] = "decimal" };
        bool rejected = false;
        try { renderer.Build([new("runic:ol", last, [Text("runic:li", "ol[1]/li[1]", "x"), Text("runic:li", "ol[1]/li[2]", "y")], [], null, "ol[1]", false)], "en"); }
        catch (TranslationFormatException) { rejected = true; }
        Require(rejected, "A list numbered past int.MaxValue was accepted.");
        Require(Shape(renderer.Build([new("runic:ol", last, [Text("runic:li", "ol[1]/li[1]", "x")], [], null, "ol[1]", false)], "en").Document) ==
            "List[Decimal@2147483647](ListItem(Paragraph(Run:x)))", "The last representable start was rejected.");
    }

    private static void Copy(CompiledTranslationSnapshot snapshot, WpfDocumentRenderer renderer, Rmf2DocumentRenderer semantic, BoundLocalizedDocumentContent bound)
    {
        var options = new Rmf2PlainTextOptions { AllowActionLabels = true };
        var viewer = new FlowDocumentScrollViewer();
        renderer.SetContent(viewer, bound);
        DocumentRender render = WpfDocumentRenderer.RenderOf(viewer)!;
        FlowDocument document = viewer.Document;

        // A whole-document selection copies the section 7 projection: action labels, no link destinations.
        string whole = render.Project(document.ContentStart, document.ContentEnd);
        Require(whole == semantic.ToPlainText(bound, options), "Whole copy differs from the plain-text projection: " + whole);
        Require(whole == "Before continuing, save a copy of <report>.txt.\n\n- Read the guide.\n- Check the result.\n\nYou can continue when the check finishes.", "Whole copy: " + whole);

        Run[] runs = Descendants(document).OfType<Run>().ToArray();
        Run save = runs.Single(run => run.Text == ", save a copy of "), read = runs.Single(run => run.Text == "Read the "), result = runs.Single(run => run.Text == " the result.");
        // Inside one leaf only the selected text is copied.
        Require(render.Project(save.ContentStart.GetPositionAtOffset(2), save.ContentStart.GetPositionAtOffset(6)) == "save", "Partial copy inside a paragraph.");
        Require(render.Project(read.ContentStart.GetPositionAtOffset(5), read.ContentEnd) == "the ", "Partial copy inside a list item kept the marker.");
        // Across list items, markers and the item separator are kept, and the action label is copied.
        string items = render.Project(read.ContentStart.GetPositionAtOffset(5), result.ContentStart.GetPositionAtOffset(4));
        Require(items == "- the guide.\n- Check the", "Copy across list items: " + items);
        // From a paragraph into a list, blocks are separated by a blank line.
        string blocks = render.Project(save.ContentStart.GetPositionAtOffset(2), read.ContentStart.GetPositionAtOffset(4));
        Require(blocks == "save a copy of <report>.txt.\n\n- Read", "Copy across blocks: " + blocks);

        // A whole list copies each marker once: native WPF markers are not part of the projection.
        var wholeList = (List)document.Blocks.ElementAt(1);
        string listCopy = render.Project(wholeList.ContentStart, wholeList.ContentEnd);
        Require(listCopy == "- Read the guide.\n- Check the result.", "Whole-list copy: " + listCopy);

        // Lists: ordered markers, continuation lines and meaningful icon text.
        var lists = new FlowDocumentScrollViewer();
        var slots = new Dictionary<string, MarkupBinding> { ["star"] = DocumentFixture.Star() };
        renderer.SetContent(lists, "lists", DocumentFixture.Lists(snapshot), slots);
        string listed = WpfDocumentRenderer.RenderOf(lists)!.Project(lists.Document.ContentStart, lists.Document.ContentEnd);
        Require(listed == semantic.ToPlainText("lists", DocumentFixture.Lists(snapshot), slots, options), "List copy differs from the plain-text projection: " + listed);
        Require(listed == "Appendix\n\ny. Twenty-five\nz. Twenty-six\naa. Twenty-seven\n\n- First line\n  second line\n- Only Star", "List copy: " + listed);

        // The copy handlers replace the text formats and drop the rich formats on the live viewer.
        var window = new Window { Content = viewer, Width = 480, Height = 320, ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000 };
        try
        {
            window.Show();
            window.UpdateLayout();
            Require(viewer.Selection is not null, "The viewer has no selection.");
            viewer.Selection!.Select(read.ContentStart.GetPositionAtOffset(5), result.ContentStart.GetPositionAtOffset(4));
            var data = new DataObject();
            foreach (string format in new[] { DataFormats.Rtf, DataFormats.Xaml, DataFormats.XamlPackage, DataFormats.UnicodeText, DataFormats.Text })
            {
                var setting = new DataObjectSettingDataEventArgs(data, format);
                viewer.RaiseEvent(setting);
                Require(setting.CommandCancelled, "The copy handler kept the " + format + " format.");
            }
            viewer.RaiseEvent(new DataObjectCopyingEventArgs(data, false));
            // The clipboard text formats use Windows line endings.
            Require(data.GetData(DataFormats.UnicodeText) as string == "- the guide.\r\n- Check the", "The copy handler did not set the projected text.");
            viewer.Selection.Select(wholeList.ContentStart, wholeList.ContentEnd);
            var listData = new DataObject();
            viewer.RaiseEvent(new DataObjectCopyingEventArgs(listData, false));
            Require(listData.GetData(DataFormats.UnicodeText) as string == "- Read the guide.\r\n- Check the result." &&
                listData.GetData(DataFormats.Text) as string == "- Read the guide.\r\n- Check the result.", "Whole-list clipboard text: " + listData.GetData(DataFormats.UnicodeText));
            // A document the application set itself is left to WPF.
            viewer.Document = new FlowDocument(new Paragraph(new Run("Own")));
            var own = new DataObjectSettingDataEventArgs(new DataObject(), DataFormats.Rtf);
            viewer.RaiseEvent(own);
            Require(!own.CommandCancelled, "The copy handler changed a document it did not render.");
        }
        finally { window.Close(); }
    }

    internal static IEnumerable<TextElement> Descendants(FlowDocument document)
    {
        var pending = new Stack<object>(document.Blocks.Reverse());
        while (pending.Count != 0)
        {
            object next = pending.Pop();
            if (next is not TextElement element) continue;
            yield return element;
            IEnumerable<object> children = element switch
            {
                List list => list.ListItems,
                ListItem item => item.Blocks,
                Paragraph paragraph => paragraph.Inlines,
                Span span => span.Inlines,
                _ => Array.Empty<object>(),
            };
            foreach (object child in children.Reverse()) pending.Push(child);
        }
    }

    private static string Shape(FlowDocument document) => string.Join("|", document.Blocks.Select(Shape));
    private static string Shape(TextElement element) => element switch
    {
        List list => "List[" + list.MarkerStyle + (list.StartIndex == 1 ? "" : "@" + list.StartIndex) + "](" + string.Join("|", list.ListItems.Select(Shape)) + ")",
        ListItem item => "ListItem(" + string.Join("|", item.Blocks.Select(Shape)) + ")",
        Paragraph paragraph => "Paragraph(" + string.Join("|", paragraph.Inlines.Select(Shape)) + ")",
        Run run => "Run:" + run.Text,
        Span span => span.GetType().Name + "(" + string.Join("|", span.Inlines.Select(Shape)) + ")",
        InlineUIContainer container => "UI:" + container.Child?.GetType().Name,
        _ => element.GetType().Name,
    };

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
