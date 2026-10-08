using System.Windows;
using LabelMaker.App.Controls;
using LabelMaker.App.Localization;
using LabelMaker.App.Services;
using LabelMaker.Core.Editing;
using LabelMaker.Core.Model;
using LabelMaker.Core.Printing;

namespace LabelMaker.App.Views;

/// <summary>
/// Gives the text, barcodes, images and so on of other pages the place, size and look they
/// have on the page on screen — each page keeping its own words, barcode values and pictures —
/// and can add what a page is missing, all in one undo step.
/// </summary>
public partial class ApplyLayoutWindow : Window
{
    private readonly DocumentSession _session;
    private readonly LabelDocument _sourcePage;
    private readonly List<LabelObject> _selected;

    public ApplyLayoutWindow(DocumentSession session)
    {
        _session = session;
        _sourcePage = session.ActiveLabel;
        _selected = session.Selection.ToList();
        InitializeComponent();

        SelectedObjectsBox.Content = AcrossPages.SelectionCaption(_selected);
        SelectedObjectsBox.IsEnabled = _selected.Count > 0;
        AllObjectsBox.Content = Localizer.F("Every object on this page ({0})", _sourcePage.Objects.Count);
        AllObjectsBox.IsChecked = true;

        Pages.Fill(session.Project, session.ActivePagePosition, CurrentPageRow.Fixed,
            caption => Localizer.F("{0}  (this page — the one copied from)", caption));
        Refresh();
    }

    private IEnumerable<LabelObject> Sources => SelectedObjectsBox.IsChecked == true ? _selected : _sourcePage.Objects;

    private PageFormatParts Parts =>
        (PositionBox.IsChecked == true ? PageFormatParts.Position : 0) |
        (SizeBox.IsChecked == true ? PageFormatParts.Size : 0) |
        (RotationBox.IsChecked == true ? PageFormatParts.Rotation : 0) |
        (LookBox.IsChecked == true ? PageFormatParts.Look : 0) |
        (AddMissingBox.IsChecked == true ? PageFormatParts.AddMissing : 0);

    private TextStyleParts TextParts =>
        (FontPartBox.IsChecked == true ? TextStyleParts.Font : 0) |
        (SizePartBox.IsChecked == true ? TextStyleParts.Size : 0) |
        (StylePartBox.IsChecked == true ? TextStyleParts.Style : 0) |
        (ColourPartBox.IsChecked == true ? TextStyleParts.Colour : 0) |
        (AlignPartBox.IsChecked == true ? TextStyleParts.Alignment : 0) |
        (SpacingPartBox.IsChecked == true ? TextStyleParts.Spacing : 0);

    private HashSet<ObjectKind> Kinds
    {
        get
        {
            var kinds = new HashSet<ObjectKind>();
            if (TextKindBox.IsChecked == true)
            {
                kinds.Add(ObjectKind.Text);
            }

            if (BarcodeKindBox.IsChecked == true)
            {
                kinds.Add(ObjectKind.Barcode);
            }

            if (ImageKindBox.IsChecked == true)
            {
                kinds.Add(ObjectKind.Image);
            }

            if (ShapeKindBox.IsChecked == true)
            {
                kinds.Add(ObjectKind.Shape);
            }

            if (TableKindBox.IsChecked == true)
            {
                kinds.Add(ObjectKind.Table);
            }

            return kinds;
        }
    }

    private LayoutMatch Match => AcrossPages.Match(SameOrderBox, SameNameBox);

    private IReadOnlyList<(LabelObject Source, LabelObject Target)> Pairs(IEnumerable<int> positions) =>
        LayoutCopy.Pairs(_session.Project, _sourcePage, Sources, positions, Kinds, Match);

    private IReadOnlyList<(LabelObject Source, LabelDocument Page)> Missing(IEnumerable<int> positions) =>
        AddMissingBox.IsChecked == true
            ? LayoutCopy.Unmatched(_session.Project, _sourcePage, Sources, positions, Kinds, Match)
            : Array.Empty<(LabelObject, LabelDocument)>();

    private void OnChanged(object sender, RoutedEventArgs e) => Refresh();

    private void Refresh()
    {
        TextPartsPanel.IsEnabled = LookBox.IsChecked == true;
        Pages.Describe((position, caption) =>
        {
            int count = Pairs(new[] { position }).Count;
            int add = Missing(new[] { position }).Count;
            return add > 0 ? Localizer.F("{0}  ·  {1} objects  ·  {2} to add", caption, count, add) : AcrossPages.ObjectCount(caption, count);
        });

        int[] ticked = Pages.Ticked;
        int matched = Pairs(ticked).Count;
        int missing = Missing(ticked).Count;
        int pages = ticked.Count(p => Pairs(new[] { p }).Count + Missing(new[] { p }).Count > 0);

        if ((Parts & ~PageFormatParts.AddMissing) == PageFormatParts.None && missing == 0)
        {
            SummaryText.Text = Localizer.T("Tick at least one thing to copy.");
            ApplyButton.IsEnabled = false;
        }
        else if (matched + missing == 0)
        {
            SummaryText.Text = AcrossPages.NothingMatches(Match);
            ApplyButton.IsEnabled = false;
        }
        else
        {
            SummaryText.Text = missing == 0
                ? Localizer.F("Changes {0} object(s) on {1} page(s). Undo puts them back.", matched, pages)
                : Localizer.F("Changes {0} object(s) and adds {1} on {2} page(s). Undo puts them back.", matched, missing, pages);
            ApplyButton.IsEnabled = true;
        }
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        int[] ticked = Pages.Ticked;
        if (PageFormatCopy.Build(Pairs(ticked), Missing(ticked), PrintablePages.All(_session.Project).ToList(), Parts, TextParts, _session.TextMeasurer) is { } command)
        {
            _session.Execute(command);
        }

        DialogResult = true;
    }
}
