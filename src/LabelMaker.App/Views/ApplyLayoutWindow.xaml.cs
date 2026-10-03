using System.Windows;
using System.Windows.Controls;
using LabelMaker.App.Services;
using LabelMaker.Core.Editing;
using LabelMaker.Core.Model;

namespace LabelMaker.App.Views;

/// <summary>
/// Moves the text, barcodes, images and so on of other pages to where they are on the page on
/// screen, so a run of similar pages lines up, in one undo step.
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
            ? $"The selected object ({Describe(_selected[0])})"
            : $"The {_selected.Count} selected objects";
        SelectedObjectsBox.IsEnabled = _selected.Count > 0;
        AllObjectsBox.Content = $"Every object on this page ({_sourcePage.Objects.Count})";
        (_selected.Count > 0 ? SelectedObjectsBox : AllObjectsBox).IsChecked = true;

        int current = _session.IsEditingMaster ? 0 : _session.Project.Pages.FindIndex(p => p.Id == _session.ActivePageId) + 1;
        ProjectModel project = session.Project;
        for (int i = 0; i <= project.Pages.Count; i++)
        {
            string caption = i == 0 ? $"★  {project.MasterLabel.Name}" : $"{i}.  {project.Pages[i - 1].Name}";
            var box = new CheckBox { IsChecked = i != current, IsEnabled = i != current, Margin = new Thickness(0, 3, 0, 3) };
            if (i == current)
            {
                caption += "  (this page — the one copied from)";
            }

            box.Click += OnChanged;
            PageList.Children.Add(box);
            _pages.Add((box, i, caption));
        }

        Refresh();
    }

    private IEnumerable<LabelObject> Sources => SelectedObjectsBox.IsChecked == true ? _selected : _sourcePage.Objects;

    private LayoutParts Parts =>
        (PositionBox.IsChecked == true ? LayoutParts.Position : 0) |
        (SizeBox.IsChecked == true ? LayoutParts.Size : 0) |
        (RotationBox.IsChecked == true ? LayoutParts.Rotation : 0);

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

    private LayoutMatch Match => SameOrderBox.IsChecked == true ? LayoutMatch.SameOrder : LayoutMatch.SameName;

    private IReadOnlyList<(LabelObject Source, LabelObject Target)> Pairs(IEnumerable<int> positions) =>
        LayoutCopy.Pairs(_session.Project, _sourcePage, Sources, positions, Kinds, Match);

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
        foreach ((CheckBox box, int position, string caption) in _pages)
        {
            if (!box.IsEnabled)
            {
                box.Content = caption;
                continue;
            }

            int count = Pairs(new[] { position }).Count;
            box.Content = $"{caption}  ·  {(count == 1 ? "1 object" : $"{count} objects")}";
        }

        int[] ticked = _pages.Where(p => p.Box.IsChecked == true && p.Box.IsEnabled).Select(p => p.Position).ToArray();
        int objects = Pairs(ticked).Count;
        int pages = ticked.Count(p => Pairs(new[] { p }).Count > 0);

        if (Parts == LayoutParts.None)
        {
            SummaryText.Text = "Tick at least one thing to copy.";
            ApplyButton.IsEnabled = false;
        }
        else if (objects == 0)
        {
            SummaryText.Text = Match == LayoutMatch.SameName
                ? "Nothing on the ticked pages has the same name. Try matching by order."
                : "Nothing to match on the ticked pages.";
            ApplyButton.IsEnabled = false;
        }
        else
        {
            SummaryText.Text = $"Moves {objects} object(s) on {pages} page(s). Undo puts them back.";
            ApplyButton.IsEnabled = true;
        }
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        int[] ticked = _pages.Where(p => p.Box.IsChecked == true && p.Box.IsEnabled).Select(p => p.Position).ToArray();
        if (LayoutCopy.Build(Pairs(ticked), Parts, _session.TextMeasurer) is { } command)
        {
            _session.Execute(command);
        }

        DialogResult = true;
    }

    private static string Describe(LabelObject o) =>
        string.IsNullOrWhiteSpace(o.Name) ? o.Kind.ToString().ToLowerInvariant() : $"“{o.Name.Trim()}”";
}
