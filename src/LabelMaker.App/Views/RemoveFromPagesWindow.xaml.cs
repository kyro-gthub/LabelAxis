using System.Windows;
using LabelMaker.App.Controls;
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

    public RemoveFromPagesWindow(DocumentSession session)
    {
        _session = session;
        _sourcePage = session.ActiveLabel;
        _selected = session.Selection.ToList();
        InitializeComponent();

        ObjectsText.Text = AcrossPages.SelectionCaption(_selected);

        // This page is the "Also remove it from this page" box.
        Pages.Fill(session.Project, session.ActivePagePosition, CurrentPageRow.Hidden);
        Refresh();
    }

    /// <summary>How many objects were removed (0 when the window was cancelled).</summary>
    public int Removed { get; private set; }

    private LayoutMatch Match => AcrossPages.Match(SameOrderBox, SameNameBox);

    private List<LabelObject> Matches(IEnumerable<int> positions) =>
        LayoutCopy.Pairs(_session.Project, _sourcePage, _selected, positions, AllKinds, Match).Select(p => p.Target).ToList();

    // Everything that goes: the matches on the ticked pages, and the selection itself when asked.
    private List<LabelObject> Doomed()
    {
        List<LabelObject> doomed = Matches(Pages.Ticked);
        if (ThisPageBox.IsChecked == true)
        {
            doomed.AddRange(_selected.Where(o => !o.Locked));
        }

        return doomed;
    }

    private void OnChanged(object sender, RoutedEventArgs e) => Refresh();

    private void Refresh()
    {
        Pages.Describe((position, caption) => AcrossPages.ObjectCount(caption, Matches(new[] { position }).Count));

        List<LabelObject> doomed = Doomed();
        int pages = Pages.Ticked.Count(p => Matches(new[] { p }).Count > 0) + (ThisPageBox.IsChecked == true && _selected.Any(o => !o.Locked) ? 1 : 0);
        if (doomed.Count == 0)
        {
            SummaryText.Text = AcrossPages.NothingMatches(Match);
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
}
