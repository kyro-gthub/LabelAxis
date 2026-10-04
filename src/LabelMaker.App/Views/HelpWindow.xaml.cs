using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using LabelMaker.Core.Help;

namespace LabelMaker.App.Views;

/// <summary>
/// The help guide: topics grouped down the left (or search results while searching), the page on
/// the right with headings, steps, tips, tables, keys and links to other pages, and Back/Forward.
/// </summary>
public partial class HelpWindow : Window
{
    private readonly HelpLibrary _library;
    private readonly List<HelpTopic> _back = new();
    private readonly List<HelpTopic> _forward = new();
    private HelpTopic? _current;
    private bool _syncingList;

    public HelpWindow(HelpLibrary library)
    {
        _library = library;
        InitializeComponent();
        ShowTopics(string.Empty);
        UpdateButtons();
        PreviewKeyDown += OnKeys;
    }

    /// <summary>Opened while a dialog was up (so it stays usable from that dialog).</summary>
    public bool OpenedDuringDialog { get; init; }

    public HelpTopic? Current => _current;

    /// <summary>Shows <paramref name="topic"/>, remembering the page before it for Back.</summary>
    public void Navigate(HelpTopic? topic) => Navigate(topic, remember: true);

    private void Navigate(HelpTopic? topic, bool remember)
    {
        if (topic is null || ReferenceEquals(topic, _current))
        {
            return;
        }

        if (remember && _current is not null)
        {
            _back.Add(_current);
            _forward.Clear();
        }

        _current = topic;
        PageViewer.Document = Render(topic);

        // A page not yet translated reads left to right, also in an Arabic or Kurdish window.
        PageViewer.Document.FlowDirection = LabelMaker.Core.Rendering.Bidi.HasRightToLeft(topic.Title + topic.Category)
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
        CrumbText.Text = topic.Category == topic.Title ? topic.Title : $"{topic.Category}  ›  {topic.Title}";
        Title = $"{topic.Title} — LabelAxis Help";
        SelectInList(topic);
        UpdateButtons();
    }

    // ---- topic list and search ----------------------------------------------------------

    private void ShowTopics(string query)
    {
        IReadOnlyList<HelpTopic> found = _library.Search(query);
        var rows = found.Select(t => new TopicRow(t, query.Trim().Length == 0 ? string.Empty : HelpLibrary.Snippet(t, query))).ToList();
        var view = new ListCollectionView(rows);
        if (query.Trim().Length == 0)
        {
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(TopicRow.Category)));
        }

        _syncingList = true;
        TopicList.ItemsSource = view;
        _syncingList = false;
        NoResults.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoResults.Text = $"Nothing found for “{query.Trim()}”. Try fewer or other words.";
        if (_current is not null)
        {
            SelectInList(_current);
        }
    }

    private void OnSearch(object sender, TextChangedEventArgs e) => ShowTopics(SearchBox.Text);

    private void OnTopicSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncingList && TopicList.SelectedItem is TopicRow row)
        {
            Navigate(row.Topic);
        }
    }

    private void SelectInList(HelpTopic topic)
    {
        _syncingList = true;
        TopicList.SelectedItem = TopicList.Items.OfType<TopicRow>().FirstOrDefault(r => ReferenceEquals(r.Topic, topic));
        if (TopicList.SelectedItem is { } item)
        {
            TopicList.ScrollIntoView(item);
        }

        _syncingList = false;
    }

    // ---- back / forward -----------------------------------------------------------------

    private void OnBack(object sender, RoutedEventArgs e) => GoBack();

    private void OnForward(object sender, RoutedEventArgs e) => GoForward();

    private void GoBack()
    {
        if (_back.Count == 0 || _current is null)
        {
            return;
        }

        _forward.Add(_current);
        HelpTopic previous = _back[^1];
        _back.RemoveAt(_back.Count - 1);
        Navigate(previous, remember: false);
    }

    private void GoForward()
    {
        if (_forward.Count == 0 || _current is null)
        {
            return;
        }

        _back.Add(_current);
        HelpTopic next = _forward[^1];
        _forward.RemoveAt(_forward.Count - 1);
        Navigate(next, remember: false);
    }

    private void UpdateButtons()
    {
        BackButton.IsEnabled = _back.Count > 0;
        ForwardButton.IsEnabled = _forward.Count > 0;
    }

    private void OnKeys(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Alt && e.SystemKey is Key.Left or Key.Right)
        {
            if (e.SystemKey == Key.Left)
            {
                GoBack();
            }
            else
            {
                GoForward();
            }

            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close();
        }
        else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
    }

    private void OnShortcuts(object sender, RoutedEventArgs e) => Navigate(_library.Find("shortcuts"));

    // ---- rendering ------------------------------------------------------------------------

    private FlowDocument Render(HelpTopic topic)
    {
        var doc = new FlowDocument
        {
            PagePadding = new Thickness(36, 26, 36, 36),
            FontSize = 13.5,
            LineHeight = 21,
            TextAlignment = TextAlignment.Left,
            ColumnWidth = double.PositiveInfinity,
        };
        doc.SetResourceReference(FlowDocument.FontFamilyProperty, "Font.Ui");
        doc.SetResourceReference(FlowDocument.ForegroundProperty, "Br.Text");
        doc.SetResourceReference(FlowDocument.BackgroundProperty, "Br.Surface");

        foreach (HelpBlock block in topic.Blocks)
        {
            doc.Blocks.Add(block switch
            {
                HelpHeading h => Heading(h),
                HelpParagraph p => Para(p.Inlines, new Thickness(0, 0, 0, 10)),
                HelpList l => ListOf(l),
                HelpCallout c => Callout(c),
                HelpTable t => TableOf(t),
                _ => new Paragraph(),
            });
        }

        return doc;
    }

    private Paragraph Heading(HelpHeading h)
    {
        var p = new Paragraph(new Run(h.Text))
        {
            FontSize = h.Level == 1 ? 24 : h.Level == 2 ? 16.5 : 14,
            Margin = h.Level == 1 ? new Thickness(0, 0, 0, 14) : new Thickness(0, 14, 0, 6),
            LineHeight = h.Level == 1 ? 30 : 22,
        };
        p.SetResourceReference(TextElement.FontFamilyProperty, h.Level == 1 ? "Font.UiExtraBold" : "Font.UiSemiBold");
        if (h.Level > 1)
        {
            p.SetResourceReference(TextElement.ForegroundProperty, "Br.Text");
        }

        return p;
    }

    private Paragraph Para(IReadOnlyList<HelpInline> inlines, Thickness margin)
    {
        var p = new Paragraph { Margin = margin };
        foreach (Inline inline in Inlines(inlines))
        {
            p.Inlines.Add(inline);
        }

        return p;
    }

    private List ListOf(HelpList l)
    {
        var list = new List
        {
            MarkerStyle = l.Numbered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(22, 0, 0, 0),
        };
        for (int i = 0; i < l.Items.Count; i++)
        {
            Paragraph text = Para(l.Items[i], new Thickness(0, 0, 0, 4));
            if (l.LevelOf(i) == 0 || list.ListItems.LastListItem is not { } parent)
            {
                list.ListItems.Add(new ListItem(text));
                continue;
            }

            // A sub-point goes in a smaller list inside the item above it.
            if (parent.Blocks.LastBlock is not List sub)
            {
                sub = new List { MarkerStyle = TextMarkerStyle.Circle, Margin = new Thickness(0, 0, 0, 4), Padding = new Thickness(18, 0, 0, 0) };
                parent.Blocks.Add(sub);
            }

            sub.ListItems.Add(new ListItem(text));
        }

        return list;
    }

    private Section Callout(HelpCallout c)
    {
        string accentKey = c.Kind == "Important" ? "Br.Danger" : "Br.Accent";
        var label = new Run(c.Kind + ": ");
        label.SetResourceReference(TextElement.FontFamilyProperty, "Font.UiSemiBold");
        label.SetResourceReference(TextElement.ForegroundProperty, accentKey);
        Paragraph p = Para(c.Inlines, new Thickness(0));
        p.Inlines.InsertBefore(p.Inlines.FirstInline, label);

        var section = new Section(p)
        {
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 4, 0, 14),
            BorderThickness = new Thickness(3, 0, 0, 0),
        };
        section.SetResourceReference(Block.BorderBrushProperty, accentKey);
        section.SetResourceReference(TextElement.BackgroundProperty, "Br.AccentSoft");
        return section;
    }

    private Table TableOf(HelpTable t)
    {
        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 4, 0, 14), BorderThickness = new Thickness(1) };
        table.SetResourceReference(Block.BorderBrushProperty, "Br.Border");
        int columns = t.Rows.Max(r => r.Count);
        for (int i = 0; i < columns; i++)
        {
            table.Columns.Add(new TableColumn { Width = i == 0 && columns == 2 ? new GridLength(0.42, GridUnitType.Star) : new GridLength(1, GridUnitType.Star) });
        }

        var group = new TableRowGroup();
        for (int r = 0; r < t.Rows.Count; r++)
        {
            var row = new TableRow();
            if (r == 0)
            {
                row.SetResourceReference(TextElement.BackgroundProperty, "Br.Bg");
                row.SetResourceReference(TextElement.FontFamilyProperty, "Font.UiSemiBold");
            }

            foreach (IReadOnlyList<HelpInline> cell in t.Rows[r])
            {
                var tc = new TableCell(Para(cell, new Thickness(0))) { Padding = new Thickness(10, 6, 10, 6), BorderThickness = new Thickness(0, 0, 0, 1) };
                tc.SetResourceReference(TableCell.BorderBrushProperty, "Br.Border");
                row.Cells.Add(tc);
            }

            group.Rows.Add(row);
        }

        table.RowGroups.Add(group);
        return table;
    }

    private IEnumerable<Inline> Inlines(IReadOnlyList<HelpInline> inlines)
    {
        foreach (HelpInline inline in inlines)
        {
            switch (inline)
            {
                case HelpBold b:
                {
                    var bold = new Run(b.Text);
                    bold.SetResourceReference(TextElement.FontFamilyProperty, "Font.UiSemiBold");
                    yield return bold;
                    break;
                }

                case HelpKey k:
                    yield return KeyBox(k.Text);
                    break;

                case HelpLink link:
                {
                    HelpTopic? target = _library.Find(link.TopicId);
                    var run = new Run(link.Text.Length > 0 ? link.Text : target?.Title ?? link.TopicId);
                    if (target is null)
                    {
                        yield return run;
                        break;
                    }

                    var hyperlink = new Hyperlink(run) { TextDecorations = null, Cursor = Cursors.Hand, ToolTip = target.Title };
                    hyperlink.SetResourceReference(TextElement.ForegroundProperty, "Br.Accent");
                    hyperlink.Click += (_, _) => Navigate(target);
                    yield return hyperlink;
                    break;
                }

                default:
                    yield return new Run(inline.Text);
                    break;
            }
        }
    }

    /// <summary>A key or menu name in a small box, like a keyboard key.</summary>
    private static InlineUIContainer KeyBox(string text)
    {
        var label = new TextBlock { Text = text, FontSize = 12, Margin = new Thickness(0) };
        label.SetResourceReference(TextBlock.FontFamilyProperty, "Font.UiMedium");
        label.SetResourceReference(TextBlock.ForegroundProperty, "Br.Text");
        var box = new Border
        {
            Child = label,
            CornerRadius = new CornerRadius(5),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(5, 0, 5, 1),
            Margin = new Thickness(1, 0, 1, -4),
        };
        box.SetResourceReference(Border.BackgroundProperty, "Br.Bg");
        box.SetResourceReference(Border.BorderBrushProperty, "Br.BorderStrong");
        return new InlineUIContainer(box) { BaselineAlignment = BaselineAlignment.Center };
    }

    /// <summary>A topic in the list; with a snippet of the matching text while searching.</summary>
    private sealed record TopicRow(HelpTopic Topic, string Snippet)
    {
        public string Title => Topic.Title;

        public string Category => Topic.Category;

        public bool HasSnippet => Snippet.Length > 0;

        public override string ToString() => Title;
    }
}
