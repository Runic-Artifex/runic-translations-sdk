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
using TUnit.Core;

namespace Runic.Translations.Wpf.Tests;

/// <summary>WPF document adapter checks: structure, UI Automation, callback retirement and the copy projection.</summary>
internal sealed class DocumentTests
{
    [Test, DisplayName("document: renders p, ul(li, li), p with the content locale and unspaced list items"), NotInParallel(WpfSuite.Key, Order = 201)]
    public void Structure()
    {
        var backup = new Backup();
        FlowDocument document = backup.Document;
        Require(document.Language.IetfLanguageTag == "en" && document.FlowDirection == FlowDirection.LeftToRight, "Document lost its locale or direction.");
        Require(Shape(document) == "Paragraph(Bold(Run:Before continuing)|Run:, save a copy of |Run:<report>.txt|Run:.)|" +
            "List[Disc](ListItem(Paragraph(Run:Read the |Hyperlink(Run:guide)|Run:.))|ListItem(Paragraph(UI:Button|Run: the result.)))|" +
            "Paragraph(Run:You can continue when the check finishes.)", "Unexpected document shape: " + Shape(document));
        Require(backup.List.ListItems.All(item => item.Blocks.FirstBlock.Margin == new Thickness(0)), "List item paragraphs kept paragraph spacing.");
    }

    [Test, DisplayName("document: slot refs become UI Automation IDs"), NotInParallel(WpfSuite.Key, Order = 202)]
    public void AutomationIds()
    {
        // Slot refs name the interactive elements for UI Automation; occurrence keys are not stable across translations.
        var backup = new Backup();
        Require(AutomationProperties.GetAutomationId(backup.Link) == "guide" && AutomationProperties.GetAutomationId(backup.Button) == "check",
            "Slot refs did not become automation IDs.");
    }

    [Test, DisplayName("document: lists and list items expose their roles, positions and copy-text names"), NotInParallel(WpfSuite.Key, Order = 203)]
    public void ListAutomation()
    {
        List list = new Backup().List;
        var listPeer = ContentElementAutomationPeer.CreatePeerForElement(list)!;
        Require(listPeer.GetAutomationControlType() == AutomationControlType.List && listPeer.IsControlElement(), "The list is not exposed as a list.");
        var itemPeer = ContentElementAutomationPeer.CreatePeerForElement(list.ListItems.ElementAt(1))!;
        Require(itemPeer.GetAutomationControlType() == AutomationControlType.ListItem && itemPeer.GetPositionInSet() == 2 && itemPeer.GetSizeOfSet() == 2,
            "List items do not expose their position.");
        // Item names are the copy text without the native list marker, including the action label.
        string[] names = list.ListItems.Select(item => ContentElementAutomationPeer.CreatePeerForElement(item)!.GetName()).ToArray();
        Require(names.SequenceEqual(["Read the guide.", "Check the result."]), "List item names: " + string.Join(" | ", names));
    }

    [Test, DisplayName("document: link and action activation goes through the active lifetime"), NotInParallel(WpfSuite.Key, Order = 204)]
    public void Activation()
    {
        var backup = new Backup();
        backup.Activate();
        Require(backup.Checks == 1 && backup.Navigations == 1, "Active callbacks did not fire once.");
    }

    [Test, DisplayName("document: a rejected render keeps the displayed document and its callbacks"), NotInParallel(WpfSuite.Key, Order = 205)]
    public void RejectedRender()
    {
        var backup = new Backup();
        bool rejected = false;
        try { backup.Renderer.SetContent(backup.Viewer, "backup", DocumentFixture.Backup(Snapshot, "a.txt"), new Dictionary<string, MarkupBinding> { ["check"] = new InlineActionBinding(() => { }) }); }
        catch (TranslationFormatException) { rejected = true; }
        Require(rejected && ReferenceEquals(backup.Document, backup.Viewer.Document) && backup.Button.IsEnabled && backup.Link.IsEnabled,
            "A rejected render replaced or retired the displayed document.");
        backup.Activate();
        Require(backup.Checks == 1 && backup.Navigations == 1, "Callbacks of the kept document did not fire once.");
    }

    [Test, DisplayName("document: a replacing render retires the previous callbacks and disables their controls"), NotInParallel(WpfSuite.Key, Order = 206)]
    public void ReplaceRetires()
    {
        var backup = new Backup();
        backup.Renderer.SetContent(backup.Viewer, backup.Bound);
        Require(!ReferenceEquals(backup.Document, backup.Viewer.Document), "A successful render did not replace the document.");
        Require(!backup.Button.IsEnabled && !backup.Link.IsEnabled, "Retired controls stayed enabled.");
        backup.Activate();
        Require(backup.Checks == 0 && backup.Navigations == 0, "Detached callbacks remained active.");
    }

    [Test, DisplayName("document: the dictionary overload renders the same document"), NotInParallel(WpfSuite.Key, Order = 207)]
    public void DictionaryOverload()
    {
        var backup = new Backup();
        string boundShape = Shape(backup.Viewer.Document);
        backup.Renderer.SetContent(backup.Viewer, "backup", backup.Typed, backup.Bound.Slots);
        Require(Shape(backup.Viewer.Document) == boundShape, "The dictionary overload rendered a different document.");
    }

    [Test, DisplayName("document: clearing removes the document and retires its callbacks"), NotInParallel(WpfSuite.Key, Order = 208)]
    public void Clear()
    {
        var backup = new Backup();
        WpfDocumentRenderer.ClearContent(backup.Viewer);
        backup.Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(backup.Viewer.Document is null && backup.Checks == 0 && !backup.Button.IsEnabled, "Clear did not retire the document.");
    }

    [Test, DisplayName("document: lists render native markers and the heading is exposed with its level"), NotInParallel(WpfSuite.Key, Order = 209)]
    public void Headings()
    {
        var viewer = new FlowDocumentScrollViewer();
        new WpfDocumentRenderer(DocumentFixture.MarkupContract, _ => { }).SetContent(viewer, "lists", DocumentFixture.Lists(Snapshot), StarSlots());
        Require(Shape(viewer.Document) == "Paragraph(Run:Appendix)|List[LowerLatin@25](ListItem(Paragraph(Run:Twenty-five))|ListItem(Paragraph(Run:Twenty-six))|" +
            "ListItem(Paragraph(Run:Twenty-seven)))|List[Disc](ListItem(Paragraph(Run:First line|LineBreak|Run:second line))|ListItem(Paragraph(Italic(Run:Only)|Run: |UI:IconHost)))",
            "Unexpected list shape: " + Shape(viewer.Document));
        var heading = (Paragraph)viewer.Document.Blocks.FirstBlock;
        var peer = ContentElementAutomationPeer.CreatePeerForElement(heading)!;
        Require(peer.GetHeadingLevel() == AutomationHeadingLevel.Level2 && peer.GetAutomationControlType() == AutomationControlType.Text &&
            peer.IsControlElement() && peer.GetName() == "Appendix", "The heading is not exposed with its level.");
    }

    [Test, DisplayName("document: HeadingBase offsets heading levels and rejects values past 9"), NotInParallel(WpfSuite.Key, Order = 210)]
    public void HeadingBase()
    {
        var viewer = new FlowDocumentScrollViewer();
        var deep = new WpfDocumentRenderer(DocumentFixture.MarkupContract, _ => { }) { HeadingBase = 9 };
        deep.SetContent(viewer, "lists", DocumentFixture.Lists(Snapshot), StarSlots());
        Require(AutomationProperties.GetHeadingLevel(viewer.Document.Blocks.FirstBlock) == AutomationHeadingLevel.Level9, "The heading base was not applied.");
        bool rejected = false;
        try { _ = new WpfDocumentRenderer(DocumentFixture.MarkupContract, _ => { }) { HeadingBase = 10 }; }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Require(rejected, "An out-of-range heading base was accepted.");
    }

    [Test, DisplayName("document: the theme sees every block and inline by contract name"), NotInParallel(WpfSuite.Key, Order = 211)]
    public void Theme()
    {
        var themed = new List<string>();
        new WpfDocumentRenderer(DocumentFixture.MarkupContract, _ => { }, theme: (name, element) => themed.Add(name + ":" + element.GetType().Name))
            .SetContent(new FlowDocumentScrollViewer(), "lists", DocumentFixture.Lists(Snapshot), StarSlots());
        Require(string.Join(",", themed) == "runic:h:HeadingParagraph,runic:li:DocumentListItem,runic:li:DocumentListItem,runic:li:DocumentListItem,runic:ol:DocumentList," +
            "runic:br:LineBreak,runic:li:DocumentListItem,runic:em:Italic,runic:icon:InlineUIContainer,runic:li:DocumentListItem,runic:ul:DocumentList",
            "Unexpected theme calls: " + string.Join(",", themed));
    }

    [Test, DisplayName("document: native list numbering matches the plain-text projection"), NotInParallel(WpfSuite.Key, Order = 212)]
    public void Numbering()
    {
        // Native markers match the plain-text numbering, which the copy projection writes.
        Require(WpfDocumentRenderer.Number(27, "lower-alpha") == "aa" && WpfDocumentRenderer.Number(702, "upper-alpha") == "ZZ" &&
            WpfDocumentRenderer.Number(3999, "upper-roman") == "MMMCMXCIX" && WpfDocumentRenderer.Number(4000, "lower-roman") == "4000",
            "List numbering differs from the plain-text projection.");
    }

    [Test, DisplayName("document: empty paragraphs, headings and lists are skipped and an empty list item keeps its place"), NotInParallel(WpfSuite.Key, Order = 213)]
    public void EmptyBlocks()
    {
        var options = new Dictionary<string, string> { ["start"] = "1", ["marker"] = "decimal" };
        DocumentRender render = new WpfDocumentRenderer(DocumentFixture.MarkupContract, _ => { }).Build([
            new("runic:p", None, [], [], null, "p[1]", false), Text("runic:p", "p[2]", "A"), Text("runic:h", "h[1]", ""),
            new("runic:ul", None, [], [], null, "ul[1]", false), new("runic:ol", options, [Text("runic:li", "ol[1]/li[1]", "")], [], null, "ol[1]", false),
            Text("runic:p", "p[3]", "B"),
        ], "en");
        Require(Shape(render.Document) == "Paragraph(Run:A)|List[Decimal](ListItem(Paragraph(Run:)))|Paragraph(Run:B)", "Empty blocks: " + Shape(render.Document));
    }

    [Test, DisplayName("document: list numbering past int.MaxValue is rejected and the last representable start renders"), NotInParallel(WpfSuite.Key, Order = 214)]
    public void NumberingLimit()
    {
        // WPF list markers count from an int StartIndex: numbering past int.MaxValue is rejected.
        var renderer = new WpfDocumentRenderer(DocumentFixture.MarkupContract, _ => { });
        var last = new Dictionary<string, string> { ["start"] = int.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture), ["marker"] = "decimal" };
        bool rejected = false;
        try { renderer.Build([new("runic:ol", last, [Text("runic:li", "ol[1]/li[1]", "x"), Text("runic:li", "ol[1]/li[2]", "y")], [], null, "ol[1]", false)], "en"); }
        catch (TranslationFormatException) { rejected = true; }
        Require(rejected, "A list numbered past int.MaxValue was accepted.");
        Require(Shape(renderer.Build([new("runic:ol", last, [Text("runic:li", "ol[1]/li[1]", "x")], [], null, "ol[1]", false)], "en").Document) ==
            "List[Decimal@2147483647](ListItem(Paragraph(Run:x)))", "The last representable start was rejected.");
    }

    [Test, DisplayName("document: a link-first list item keeps its name and copies partial selections inside the link"), NotInParallel(WpfSuite.Key, Order = 215)]
    public void LinkFirst()
    {
        // A list item whose first inline is a link: the item boundary and the link's start are the same position.
        InlineMarkupRun Plain(string text) => new("text", text, [], None, null, true);
        var link = new InlineMarkupRun("runic:link", null, [Plain("guide")], new Dictionary<string, string> { ["ref"] = "guide" },
            new InlineLinkBinding(new Uri("https://example.test/guide")), false) { Occurrence = "ul[1]/li[1]/0" };
        DocumentRender render = new WpfDocumentRenderer(DocumentFixture.MarkupContract, _ => { }).Build([
            new("runic:ul", None, [new("runic:li", None, [], [link, Plain(" first.")], null, "ul[1]/li[1]", false),
                new("runic:li", None, [], [Plain("Second.")], null, "ul[1]/li[2]", false)], [], null, "ul[1]", false)], "en");
        var list = (List)render.Document.Blocks.FirstBlock;
        var first = (Paragraph)list.ListItems.First().Blocks.FirstBlock;
        Run guide = (Run)Descendants(render.Document).OfType<Hyperlink>().Single().Inlines.FirstInline;
        Require(ContentElementAutomationPeer.CreatePeerForElement(list.ListItems.First())!.GetName() == "guide first.", "Link-first item name.");
        Require(render.Project(list.ContentStart, list.ContentEnd) == "- guide first.\n- Second.", "Link-first list copy: " + render.Project(list.ContentStart, list.ContentEnd));
        Require(render.Project(first.ContentStart, first.ContentEnd) == "- guide first.", "Link-first whole item copy: " + render.Project(first.ContentStart, first.ContentEnd));
        // A selection starting inside the link's run copies from that offset.
        string across = render.Project(guide.ContentStart.GetPositionAtOffset(2), list.ContentEnd);
        Require(across == "- ide first.\n- Second.", "Copy from inside a link across items: " + across);
        string inside = render.Project(guide.ContentStart.GetPositionAtOffset(1), guide.ContentStart.GetPositionAtOffset(4));
        Require(inside == "uid", "Copy inside a link: " + inside);
        string tail = render.Project(guide.ContentStart.GetPositionAtOffset(3), first.ContentEnd);
        Require(tail == "de first.", "Copy from inside a link to the item end: " + tail);
    }

    [Test, DisplayName("document: copying whole and partial selections projects the plain text"), NotInParallel(WpfSuite.Key, Order = 216)]
    public void CopyProjection()
    {
        var backup = new Backup();
        DocumentRender render = WpfDocumentRenderer.RenderOf(backup.Viewer)!;
        FlowDocument document = backup.Document;

        // A whole-document selection copies the section 7 projection: action labels, no link destinations.
        string whole = render.Project(document.ContentStart, document.ContentEnd);
        Require(whole == Semantic.ToPlainText(backup.Bound, CopyOptions), "Whole copy differs from the plain-text projection: " + whole);
        Require(whole == "Before continuing, save a copy of <report>.txt.\n\n- Read the guide.\n- Check the result.\n\nYou can continue when the check finishes.", "Whole copy: " + whole);

        (Run save, Run read, Run result) = backup.Runs();
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
        string listCopy = render.Project(backup.List.ContentStart, backup.List.ContentEnd);
        Require(listCopy == "- Read the guide.\n- Check the result.", "Whole-list copy: " + listCopy);
    }

    [Test, DisplayName("document: list copy has ordered markers, continuation lines and meaningful icon text"), NotInParallel(WpfSuite.Key, Order = 217)]
    public void ListCopy()
    {
        var lists = new FlowDocumentScrollViewer();
        Dictionary<string, MarkupBinding> slots = StarSlots();
        new WpfDocumentRenderer(DocumentFixture.MarkupContract, _ => { }).SetContent(lists, "lists", DocumentFixture.Lists(Snapshot), slots);
        string listed = WpfDocumentRenderer.RenderOf(lists)!.Project(lists.Document.ContentStart, lists.Document.ContentEnd);
        Require(listed == Semantic.ToPlainText("lists", DocumentFixture.Lists(Snapshot), slots, CopyOptions), "List copy differs from the plain-text projection: " + listed);
        Require(listed == "Appendix\n\ny. Twenty-five\nz. Twenty-six\naa. Twenty-seven\n\n- First line\n  second line\n- Only Star", "List copy: " + listed);
    }

    [Test, DisplayName("document: copy handlers put the projected text with Windows line endings on the clipboard and leave own documents to WPF"), NotInParallel(WpfSuite.Key, Order = 218)]
    public void CopyHandlers()
    {
        // The copy handlers replace the text formats and drop the rich formats on the live viewer.
        var backup = new Backup();
        FlowDocumentScrollViewer viewer = backup.Viewer;
        (_, Run read, Run result) = backup.Runs();
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
            viewer.Selection.Select(backup.List.ContentStart, backup.List.ContentEnd);
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

    private static readonly CompiledTranslationSnapshot Snapshot = DocumentFixture.CreateSnapshot();
    private static readonly Rmf2DocumentRenderer Semantic = new(Rmf2MarkupContract.Link(DocumentFixture.MarkupContract));
    private static readonly Rmf2PlainTextOptions CopyOptions = new() { AllowActionLabels = true };
    private static readonly Dictionary<string, string> None = new();

    private static Dictionary<string, MarkupBinding> StarSlots() => new() { ["star"] = DocumentFixture.Star() };

    private static DocumentBlock Text(string name, string occurrence, string text) =>
        new(name, None, [], [new InlineMarkupRun("text", text, [], None, null, true)], null, occurrence, false);

    /// <summary>The bounded #15 example rendered through typed slots into a fresh viewer, with counted callbacks.</summary>
    private sealed class Backup
    {
        internal Backup()
        {
            Renderer = new WpfDocumentRenderer(DocumentFixture.MarkupContract, _ => Navigations++);
            Typed = new LocalizedDocumentContent<BackupSlots>(DocumentFixture.Backup(Snapshot, "<report>.txt"));
            Bound = Typed.Bind(new BackupSlots(new InlineActionBinding(() => Checks++), new InlineLinkBinding(new Uri("https://example.test/guide"))));
            Renderer.SetContent(Viewer, Bound);
            Document = Viewer.Document;
            List = (List)Document.Blocks.ElementAt(1);
            Link = Descendants(Document).OfType<Hyperlink>().Single();
            Button = Descendants(Document).OfType<InlineUIContainer>().Select(container => container.Child).OfType<Button>().Single();
        }

        internal WpfDocumentRenderer Renderer { get; }
        internal LocalizedDocumentContent<BackupSlots> Typed { get; }
        internal BoundLocalizedDocumentContent Bound { get; }
        internal FlowDocumentScrollViewer Viewer { get; } = new();
        internal FlowDocument Document { get; }
        internal List List { get; }
        internal Hyperlink Link { get; }
        internal Button Button { get; }
        internal int Checks { get; private set; }
        internal int Navigations { get; private set; }

        internal void Activate()
        {
            Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Link.RaiseEvent(new RequestNavigateEventArgs(Link.NavigateUri, null) { RoutedEvent = Hyperlink.RequestNavigateEvent });
        }

        internal (Run Save, Run Read, Run Result) Runs()
        {
            Run[] runs = Descendants(Document).OfType<Run>().ToArray();
            return (runs.Single(run => run.Text == ", save a copy of "), runs.Single(run => run.Text == "Read the "), runs.Single(run => run.Text == " the result."));
        }
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
