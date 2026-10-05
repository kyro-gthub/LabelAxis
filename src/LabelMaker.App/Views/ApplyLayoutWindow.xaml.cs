using System.Windows;
using System.Windows.Controls;
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
    private readonly List<(CheckBox Box, int Position, string Caption)> _pages = new();

    public ApplyLayoutWindow(DocumentSession session)
    {
        _session = session;
        _sourcePage = session.ActiveLabel;
        _selected = session.Selection.ToList();
        InitializeComponent();

        SelectedObjectsBox.Content = _selected.Count == 1
            ? Localizer.F("The selected object ({0})", Describe(_selected[0]))
            : Localizer.F("The {0} selected objects", _selected.Count);
        SelectedObjectsBox.IsEnabled = _selected.Count > 0;
        AllObjectsBox.Content = Localizer.F("Every object on this page ({0})", _sourcePage.Objects.Count);
        AllObjectsBox.IsChecked = true;

        int current = _session.IsEditingMaster ? 0 : _session.Project.Pages.FindIndex(p => p.Id == _session.ActivePageId) + 1;
        ProjectModel project = session.Project;
        for (int i = 0; i <= project.Pages.Count; i++)
        {
            string caption = $"{i + 1}.  {(i == 0 ? project.MasterLabel.Name : project.Pages[i - 1].Name)}";
            var box = new CheckBox { IsChecked = i != current, IsEnabled = i != current, Margin = new Thickness(0, 3, 0, 3) };
            if (i == current)
            {
                caption = Localizer.F("{0}  (this page — the one copied from)", caption);
            }

            box.Click += OnChanged;
            PageList.Children.Add(box);
            _pages.Add((box, i, caption));
        }

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

    private LayoutMatch Match =>
        SameOrderBox.IsChecked == true ? LayoutMatch.SameOrder
        : SameNameBox.IsChecked == true ? LayoutMatch.SameName
        : LayoutMatch.NameThenOrder;

    private IReadOnlyList<(LabelObject Source, LabelObject Target)> Pairs(IEnumerable<int> positions) =>
        LayoutCopy.Pairs(_session.Project, _sourcePage, Sources, positions, Kinds, Match);

    private IReadOnlyList<(LabelObject Source, LabelDocument Page)> Missing(IEnumerable<int> positions) =>
        AddMissingBox.IsChecked == true
            ? LayoutCopy.Unmatched(_session.Project, _sourcePage, Sources, positions, Kinds, Match)
            : Array.Empty<(LabelObject, LabelDocument)>();

    private void OnChanged(object sender, RoutedEventArgs e) => Refresh();

    private void OnTickAll(object sender, RoutedEventArgs e) => TickAll(true);

    private void OnUntickAll(object sender, RoutedEventArgs e) => TickAll(false);

    private void TickAll(bool ticked)
    {
        foreach ((CheckBox box, _, _) in _pages.Where(p => p.Box.IsEnabled))
        {
            box.IsChecked = ticked;
        }

        Refresh();
    }

    private void Refresh()
    {
        TextPartsPanel.IsEnabled = LookBox.IsChecked == true;
        foreach ((CheckBox box, int position, string caption) in _pages)
        {
            if (!box.IsEnabled)
            {
                box.Content = caption;
                continue;
            }

            int count = Pairs(new[] { position }).Count;
            int add = Missing(new[] { position }).Count;
            box.Content = add > 0 ? Localizer.F("{0}  ·  {1} objects  ·  {2} to add", caption, count, add)
                : count == 1 ? Localizer.F("{0}  ·  1 object", caption)
                : Localizer.F("{0}  ·  {1} objects", caption, count);
        }

        int[] ticked = _pages.Where(p => p.Box.IsChecked == true && p.Box.IsEnabled).Select(p => p.Position).ToArray();
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
            SummaryText.Text = Localizer.T(Match == LayoutMatch.SameName
                ? "Nothing on the ticked pages has the same name. Try matching by order."
                : "Nothing to match on the ticked pages.");
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
        int[] ticked = _pages.Where(p => p.Box.IsChecked == true && p.Box.IsEnabled).Select(p => p.Position).ToArray();
        if (PageFormatCopy.Build(Pairs(ticked), Missing(ticked), PrintablePages.All(_session.Project).ToList(), Parts, TextParts, _session.TextMeasurer) is { } command)
        {
            _session.Execute(command);
        }

        DialogResult = true;
    }

    private static string Describe(LabelObject o) =>
        string.IsNullOrWhiteSpace(o.Name) ? Localizer.T(o.Kind.ToString()) : $"“{o.Name.Trim()}”";
}
