using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Markup;

namespace Runic.Translations.Wpf;

/// <summary>
/// Links a contract once and renders RMF2 document messages (document profile v1) as fresh <see cref="FlowDocument"/>s
/// in a read-only <see cref="FlowDocumentScrollViewer"/> on the owning UI thread.
/// </summary>
/// <remarks>
/// Paragraphs become <see cref="Paragraph"/>, headings a bold <see cref="Paragraph"/> exposed to UI Automation as a heading,
/// lists <see cref="List"/> with <see cref="List.MarkerStyle"/> and <see cref="List.StartIndex"/>, and list items <see cref="ListItem"/>.
/// Copying replaces the clipboard text with the plain-text projection of the selected blocks, with Windows line endings.
/// Empty paragraphs, headings and lists are skipped, as in the plain-text projection.
/// The rendered <see cref="FlowDocument"/> binds its font family, size and foreground to the viewer; these local values
/// override an implicit <see cref="FlowDocument"/> style, so style the viewer or use the theme callback. Headings, lists and
/// list items pick up implicit <see cref="Paragraph"/>, <see cref="List"/> and <see cref="ListItem"/> styles.
/// When the application sets <see cref="FlowDocumentScrollViewer.Document"/> itself, the previous render keeps its callbacks
/// until the next <c>SetContent</c> or <see cref="ClearContent"/> on that viewer.
/// </remarks>
public sealed class WpfDocumentRenderer
{
    private static readonly ConditionalWeakTable<FlowDocumentScrollViewer, DocumentRender> Renders = new();
    private static readonly ConditionalWeakTable<FlowDocumentScrollViewer, object> Hooked = new();
    private readonly Rmf2DocumentRenderer _semantic;
    private readonly WpfInlineBuilder _inlines;
    private readonly Action<string, TextElement>? _theme;
    private readonly int _headingBase = 2;

    /// <summary>Links immutable markup contracts and application-owned navigation, inline factories and themes.</summary>
    /// <param name="contractJson">The compiler-exported RMF2 markup contract.</param>
    /// <param name="navigate">Called when an active link is activated.</param>
    /// <param name="custom">Factories for declared custom inline contracts.</param>
    /// <param name="theme">Called with the canonical contract name for every rendered block and markup inline, for example to style <c>runic:h</c>.
    /// Plain text runs are not passed.</param>
    public WpfDocumentRenderer(string contractJson, Action<Uri> navigate,
        IReadOnlyDictionary<string, WpfMarkupFactory>? custom = null, Action<string, TextElement>? theme = null)
    {
        ArgumentNullException.ThrowIfNull(navigate);
        _semantic = new Rmf2DocumentRenderer(Rmf2MarkupContract.Link(contractJson));
        _theme = theme;
        _inlines = new WpfInlineBuilder(contractJson, navigate, custom, theme is null ? null : (name, inline) => theme(name, inline), slotAutomationIds: true);
    }

    /// <summary>
    /// The heading level of a message's level-1 heading, from 1 to 9 (default 2). The effective level is
    /// <c>HeadingBase + level − 1</c>, clamped at 9.
    /// </summary>
    public int HeadingBase
    {
        get => _headingBase;
        init => _headingBase = value is >= 1 and <= 9 ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "The heading base must be from 1 to 9.");
    }

    /// <summary>Validates typed bound content and replaces the viewer's document on its dispatcher thread.</summary>
    public void SetContent(FlowDocumentScrollViewer target, BoundLocalizedDocumentContent content)
    {
        ArgumentNullException.ThrowIfNull(target); target.Dispatcher.VerifyAccess();
        ArgumentNullException.ThrowIfNull(content);
        Replace(target, _semantic.Render(content), content.Content.Locale);
    }

    /// <summary>Validates content against the message contract and replaces the viewer's document on its dispatcher thread.</summary>
    public void SetContent(FlowDocumentScrollViewer target, string key, LocalizedDocumentContent content, IReadOnlyDictionary<string, MarkupBinding> slots)
    {
        ArgumentNullException.ThrowIfNull(target); target.Dispatcher.VerifyAccess();
        ArgumentNullException.ThrowIfNull(content);
        Replace(target, _semantic.Render(key, content, slots), content.Locale);
    }

    /// <summary>Removes the rendered document and deactivates its callbacks, including on retained detached controls.</summary>
    public static void ClearContent(FlowDocumentScrollViewer target)
    {
        ArgumentNullException.ThrowIfNull(target); target.Dispatcher.VerifyAccess();
        Retire(target);
        target.Document = null;
    }

    private static void Retire(FlowDocumentScrollViewer target)
    {
        if (Renders.TryGetValue(target, out DocumentRender? previous)) previous.Lifetime.Retire();
        Renders.Remove(target);
    }

    private void Replace(FlowDocumentScrollViewer target, IReadOnlyList<DocumentBlock> blocks, string locale)
    {
        // Build first: a binding failure must not partially replace displayed content.
        DocumentRender render = Build(blocks, locale);
        BindingOperations.SetBinding(render.Document, FlowDocument.FontFamilyProperty, new Binding(nameof(Control.FontFamily)) { Source = target });
        BindingOperations.SetBinding(render.Document, FlowDocument.FontSizeProperty, new Binding(nameof(Control.FontSize)) { Source = target });
        BindingOperations.SetBinding(render.Document, FlowDocument.ForegroundProperty, new Binding(nameof(Control.Foreground)) { Source = target });
        Retire(target);
        Renders.Add(target, render);
        if (!Hooked.TryGetValue(target, out _))
        {
            Hooked.Add(target, new object());
            DataObject.AddSettingDataHandler(target, OnSettingData);
            DataObject.AddCopyingHandler(target, OnCopying);
        }
        target.Document = render.Document;
    }

    internal DocumentRender Build(IReadOnlyList<DocumentBlock> blocks, string locale)
    {
        var lifetime = new RenderLifetime();
        var leaves = new List<DocumentLeaf>();
        var document = new FlowDocument { Language = XmlLanguage.GetLanguage(locale), FlowDirection = Direction(locale) };
        foreach (DocumentBlock block in blocks)
            if (Create(block) is Block created) document.Blocks.Add(created);
        return new DocumentRender(document, lifetime, leaves);

        Block? Create(DocumentBlock block)
        {
            // Empty blocks are skipped, as in the plain-text projection: a paragraph or heading with only empty text and a
            // list without items. List items are always rendered.
            if (block.Name is "runic:ul" or "runic:ol" ? block.Blocks.Count == 0 : block.Inlines.All(run => run.Text?.Length == 0)) return null;
            switch (block.Name)
            {
                case "runic:p":
                {
                    var paragraph = Leaf(new Paragraph(), block, null, null, out _);
                    _theme?.Invoke(block.Name, paragraph);
                    return paragraph;
                }
                case "runic:h":
                {
                    int level = Math.Min(_headingBase + int.Parse(block.Options["level"], NumberStyles.None, CultureInfo.InvariantCulture) - 1, 9);
                    var heading = new HeadingParagraph { FontWeight = FontWeights.Bold };
                    Leaf(heading, block, null, null, out heading.Text);
                    AutomationProperties.SetHeadingLevel(heading, (AutomationHeadingLevel)level);
                    _theme?.Invoke(block.Name, heading);
                    return heading;
                }
                case "runic:ul" or "runic:ol":
                {
                    bool ordered = block.Name == "runic:ol";
                    long start = ordered ? long.Parse(block.Options["start"], NumberStyles.None, CultureInfo.InvariantCulture) : 1;
                    string marker = ordered ? block.Options["marker"] : "disc";
                    // WPF numbers items from an int StartIndex; the profile allows start up to int.MaxValue.
                    if (start + block.Blocks.Count - 1 > int.MaxValue)
                        throw new TranslationFormatException("The ordered list at '" + block.Occurrence + "' numbers items past " + int.MaxValue + ", which WPF list markers cannot show.");
                    var list = new DocumentList { MarkerStyle = MarkerStyle(marker) };
                    if (ordered) list.StartIndex = (int)start;
                    for (int index = 0; index < block.Blocks.Count; index++)
                    {
                        DocumentBlock item = block.Blocks[index];
                        var listItem = new DocumentListItem();
                        AutomationProperties.SetPositionInSet(listItem, index + 1);
                        AutomationProperties.SetSizeOfSet(listItem, block.Blocks.Count);
                        // The item's paragraph has no margin of its own, so items are spaced like a list rather than like paragraphs.
                        listItem.Blocks.Add(Leaf(new Paragraph { Margin = new Thickness(0) }, item, ordered ? Number(start + index, marker) + ". " : "- ", list, out listItem.Text));
                        _theme?.Invoke(item.Name, listItem);
                        list.ListItems.Add(listItem);
                    }
                    _theme?.Invoke(block.Name, list);
                    return list;
                }
                default:
                    throw new TranslationFormatException("No WPF renderer linked for block '" + block.Name + "'.");
            }
        }

        // The leaf's copy text (with action labels and icon alternate text, without list markers) is its UI Automation name.
        Paragraph Leaf(Paragraph paragraph, DocumentBlock block, string? marker, List? list, out string text)
        {
            foreach (InlineMarkupRun run in block.Inlines) paragraph.Inlines.Add(_inlines.Create(run, locale, lifetime));
            leaves.Add(new DocumentLeaf(paragraph, marker, list));
            text = WpfInlineBuilder.CopyText(paragraph.Inlines, lifetime).Replace('\n', ' ').Trim();
            return paragraph;
        }
    }

    private static FlowDirection Direction(string locale)
    {
        try { return CultureInfo.GetCultureInfo(locale).TextInfo.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight; }
        catch (CultureNotFoundException) { return FlowDirection.LeftToRight; }
    }

    // WPF numbers lower/upper-alpha bijectively (a … z, aa) and falls back to decimal past 3999 for roman
    // numerals, like the plain-text projection (W220-003 section 7), so the native markers are used as they are.
    private static TextMarkerStyle MarkerStyle(string marker) => marker switch
    {
        "disc" => TextMarkerStyle.Disc,
        "lower-alpha" => TextMarkerStyle.LowerLatin,
        "upper-alpha" => TextMarkerStyle.UpperLatin,
        "lower-roman" => TextMarkerStyle.LowerRoman,
        "upper-roman" => TextMarkerStyle.UpperRoman,
        _ => TextMarkerStyle.Decimal,
    };

    // The plain-text list numbering of W220-003 section 7: ASCII decimal, bijective base-26 letters, and
    // subtractive roman numerals for 1 to 3999 with a decimal fallback.
    internal static string Number(long value, string marker)
    {
        switch (marker)
        {
            case "lower-alpha" or "upper-alpha":
            {
                var letters = new StringBuilder();
                for (long rest = value; rest > 0; rest = (rest - 1) / 26) letters.Insert(0, (char)('a' + (rest - 1) % 26));
                return marker == "upper-alpha" ? letters.ToString().ToUpperInvariant() : letters.ToString();
            }
            case "lower-roman" or "upper-roman" when value is >= 1 and <= 3999:
            {
                ReadOnlySpan<int> values = [1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1];
                string[] symbols = ["M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I"];
                var roman = new StringBuilder();
                long rest = value;
                for (int index = 0; index < values.Length; index++)
                    for (; rest >= values[index]; rest -= values[index]) roman.Append(symbols[index]);
                return marker == "lower-roman" ? roman.ToString().ToLowerInvariant() : roman.ToString();
            }
            default:
                return value.ToString(CultureInfo.InvariantCulture);
        }
    }

    // Copy and drag replace the text formats with the plain-text projection of the selection. The rich formats
    // (XAML, XAML package, RTF) are not offered because they would serialise each Hyperlink's NavigateUri and leak link
    // destinations that the projection leaves out. WPF has already serialised them when SettingData is raised, so
    // cancelling discards that output; the internal element subclasses would be written as their standard types anyway.
    private static void OnSettingData(object sender, DataObjectSettingDataEventArgs e)
    {
        if (Current(sender) is not null && e.Format is not null &&
            (e.Format == DataFormats.Text || e.Format == DataFormats.UnicodeText || e.Format == DataFormats.Xaml ||
             e.Format == DataFormats.XamlPackage || e.Format == DataFormats.Rtf))
            e.CancelCommand();
    }

    private static void OnCopying(object sender, DataObjectCopyingEventArgs e)
    {
        if (Current(sender) is not DocumentRender render || sender is not FlowDocumentScrollViewer viewer || viewer.Selection is not TextRange selection) return;
        // The projection uses \n; the Windows clipboard text formats use \r\n.
        string text = render.Project(selection.Start, selection.End).Replace("\n", "\r\n", StringComparison.Ordinal);
        e.DataObject.SetData(DataFormats.UnicodeText, text);
        e.DataObject.SetData(DataFormats.Text, text);
    }

    private static DocumentRender? Current(object sender) =>
        sender is FlowDocumentScrollViewer viewer && Renders.TryGetValue(viewer, out DocumentRender? render) && ReferenceEquals(viewer.Document, render.Document)
            ? render : null;

    internal static DocumentRender? RenderOf(FlowDocumentScrollViewer viewer) => Renders.TryGetValue(viewer, out DocumentRender? render) ? render : null;

    /// <summary>A heading paragraph exposed to UI Automation with its heading level.</summary>
    private sealed class HeadingParagraph : Paragraph
    {
        internal string Text = "";
        public HeadingParagraph() => SetResourceReference(StyleProperty, typeof(Paragraph));
        protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);
        private sealed class Peer(HeadingParagraph owner) : TextElementAutomationPeer(owner)
        {
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
            protected override string GetClassNameCore() => "Paragraph";
            protected override bool IsControlElementCore() => true;
            protected override string GetNameCore() => Named(owner.Text, base.GetNameCore());
            protected override AutomationHeadingLevel GetHeadingLevelCore() => AutomationProperties.GetHeadingLevel(owner);
        }
    }

    /// <summary>A list exposed to UI Automation as a list.</summary>
    private sealed class DocumentList : List
    {
        public DocumentList() => SetResourceReference(StyleProperty, typeof(List));
        protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);
        private sealed class Peer(DocumentList owner) : TextElementAutomationPeer(owner)
        {
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.List;
            protected override string GetClassNameCore() => "List";
            protected override bool IsControlElementCore() => true;
        }
    }

    /// <summary>A list item exposed to UI Automation as a list item with its position in the list.</summary>
    private sealed class DocumentListItem : ListItem
    {
        internal string Text = "";
        public DocumentListItem() => SetResourceReference(StyleProperty, typeof(ListItem));
        protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);
        private sealed class Peer(DocumentListItem owner) : TextElementAutomationPeer(owner)
        {
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;
            protected override string GetClassNameCore() => "ListItem";
            protected override bool IsControlElementCore() => true;
            protected override string GetNameCore() => Named(owner.Text, base.GetNameCore());
            protected override int GetPositionInSetCore() => AutomationProperties.GetPositionInSet(owner);
            protected override int GetSizeOfSetCore() => AutomationProperties.GetSizeOfSet(owner);
        }
    }

    // An explicit AutomationProperties.Name wins; otherwise the leaf's copy text, computed when it was built, names it.
    // TextRange.Text is not used: it includes the native list marker ("•\t").
    private static string Named(string text, string explicitName) => string.IsNullOrEmpty(explicitName) ? text : explicitName;
}

/// <summary>A leaf paragraph of a rendered document, with its plain-text list marker and list.</summary>
internal sealed record DocumentLeaf(Paragraph Paragraph, string? Marker, List? List);

/// <summary>One rendered document: its callbacks and the leaves the copy projection walks.</summary>
internal sealed class DocumentRender(FlowDocument document, RenderLifetime lifetime, IReadOnlyList<DocumentLeaf> leaves)
{
    internal FlowDocument Document { get; } = document;
    internal RenderLifetime Lifetime { get; } = lifetime;
    internal IReadOnlyList<DocumentLeaf> Leaves { get; } = leaves;

    /// <summary>
    /// The plain-text projection (W220-003 section 7) of the leaves between two positions. A selection inside one leaf
    /// copies the selected text only; a selection spanning leaves (or a whole list item) keeps list markers, a line
    /// break between items of one list and a blank line between other blocks.
    /// </summary>
    internal string Project(TextPointer start, TextPointer end)
    {
        var parts = new List<(DocumentLeaf Leaf, string Text, bool Whole)>();
        foreach (DocumentLeaf leaf in Leaves)
        {
            TextPointer from = leaf.Paragraph.ContentStart.CompareTo(start) < 0 ? start : leaf.Paragraph.ContentStart;
            TextPointer to = leaf.Paragraph.ContentEnd.CompareTo(end) > 0 ? end : leaf.Paragraph.ContentEnd;
            if (from.CompareTo(to) >= 0) continue;
            string text = WpfInlineBuilder.CopyText(leaf.Paragraph.Inlines, Lifetime, from, to);
            if (text.Length == 0 && leaf.Marker is null) continue;
            parts.Add((leaf, text, from.CompareTo(leaf.Paragraph.ContentStart) == 0 && to.CompareTo(leaf.Paragraph.ContentEnd) == 0));
        }
        if (parts.Count == 1 && !parts[0].Whole) return parts[0].Text;
        var result = new StringBuilder();
        for (int index = 0; index < parts.Count; index++)
        {
            (DocumentLeaf leaf, string text, _) = parts[index];
            if (index > 0) result.Append(leaf.List is not null && ReferenceEquals(leaf.List, parts[index - 1].Leaf.List) ? "\n" : "\n\n");
            if (leaf.Marker is null) { result.Append(text); continue; }
            string[] lines = text.Split('\n');
            for (int line = 1; line < lines.Length; line++) if (lines[line].Length != 0) lines[line] = "  " + lines[line];
            result.Append(leaf.Marker).Append(string.Join('\n', lines));
        }
        return result.ToString();
    }
}
