using System.Windows;
using System.Windows.Controls;
using LabelMaker.App.Localization;
using LabelMaker.App.Services;
using LabelMaker.Core.Editing;
using LabelMaker.Core.Model;

namespace LabelMaker.App.Views;

/// <summary>
/// Takes the selected objects — a barcode, a named text box, a logo — off every ticked page:
/// each page loses the object that matches it by name or by order, all in one undo step.
/// </summary>
public partial class RemoveFromPagesWindow : Window
{
    private static readonly IReadOnlySet<ObjectKind> AllKinds = Enum.GetValues<ObjectKind>().ToHashSet();

    private readonly DocumentSession _session;
    private readonly LabelDocument _sourcePage;
    private readonly List<LabelObject> _selected;
    private readonly List<(CheckBox Box, int Position, string Caption)> _pages = new();

    public RemoveFromPagesWindow(DocumentSession session)
    {
        _session = session;
        _sourcePage = session.ActiveLabel;
        _selected = session.Selection.ToList();
        InitializeComponent();

        ObjectsText.Text = _selected.Count == 1
            ? Localizer.F("The selected object ({0})", Describe(_selected[0]))
            : Localizer.F("The {0} selected objects", _selected.Count);

        int current = _session.IsEditingMaster ? 0 : _session.Project.Pages.FindIndex(p => p.Id == _session.ActivePageId) + 1;
        ProjectModel project = session.Project;
        for (int i = 0; i <= project.Pages.Count; i++)
        {
            if (i == current)
            {
                continue; // this page is the "Also remove it from this page" box
            }

            string caption = $"{i + 1}.  {(i == 0 ? project.MasterLabel.Name : project.Pages[i - 1].Name)}";
            var box = new CheckBox { IsChecked = true, Margin = new Thickness(0, 3, 0, 3) };
            box.Click += OnChanged;
            PageList.Children.Add(box);
            _pages.Add((box, i, caption));
        }

        Refresh();
    }

    private LayoutMatch Match =>
        SameOrderBox.IsChecked == true ? LayoutMatch.SameOrder
        : SameNameBox.IsChecked == true ? LayoutMatch.SameName
        : LayoutMatch.NameThenOrder;

    private List<LabelObject> Matches(IEnumerable<int> positions) =>
        LayoutCopy.Pairs(_session.Project, _sourcePage, _selected, positions, AllKinds, Match).Select(p => p.Target).ToList();

    private int[] Ticked => _pages.Where(p => p.Box.IsChecked == true).Select(p => p.Position).ToArray();

    // Everything that goes: the matches on the ticked pages, and the selection itself when asked.
    private List<LabelObject> Doomed()
    {
        List<LabelObject> doomed = Matches(Ticked);
        if (ThisPageBox.IsChecked == true)
        {
            doomed.AddRange(_selected.Where(o => !o.Locked));
        }

        return doomed;
    }

    private void OnChanged(object sender, RoutedEventArgs e) => Refresh();

    private void OnTickAll(object sender, RoutedEventArgs e) => TickAll(true);

    private void OnUntickAll(object sender, RoutedEventArgs e) => TickAll(false);

    private void TickAll(bool ticked)
    {
        foreach ((CheckBox box, _, _) in _pages)
        {
            box.IsChecked = ticked;
        }

        Refresh();
    }

    private void Refresh()
    {
        foreach ((CheckBox box, int position, string caption) in _pages)
        {
            int count = Matches(new[] { position }).Count;
            box.Content = count == 1 ? Localizer.F("{0}  ·  1 object", caption) : Localizer.F("{0}  ·  {1} objects", caption, count);
        }

        List<LabelObject> doomed = Doomed();
        int pages = Ticked.Count(p => Matches(new[] { p }).Count > 0) + (ThisPageBox.IsChecked == true && _selected.Any(o => !o.Locked) ? 1 : 0);
        if (doomed.Count == 0)
        {
            SummaryText.Text = Localizer.T(Match == LayoutMatch.SameName
                ? "Nothing on the ticked pages has the same name. Try matching by order."
                : "Nothing to match on the ticked pages.");
            RemoveButton.IsEnabled = false;
        }
        else
        {
            SummaryText.Text = Localizer.F("Removes {0} object(s) from {1} page(s). Undo puts them back.", doomed.Count, pages);
            RemoveButton.IsEnabled = true;
        }
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        List<LabelObject> doomed = Doomed();
        if (LayoutCopy.Remove(_session.Project, doomed) is { } command)
        {
            _session.Execute(command);
            _session.SetSelection(_session.Selection.Where(o => _session.ActiveLabel.Objects.Contains(o)).ToList());
            Removed = doomed.Count;
        }

        DialogResult = true;
    }

    /// <summary>How many objects were removed (0 when the window was cancelled).</summary>
    public int Removed { get; private set; }

    private static string Describe(LabelObject o) =>
        string.IsNullOrWhiteSpace(o.Name) ? Localizer.T(o.Kind.ToString()) : $"“{o.Name.Trim()}”";
}
