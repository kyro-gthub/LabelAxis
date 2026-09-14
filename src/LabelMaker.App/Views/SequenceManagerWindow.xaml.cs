using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using LabelMaker.App.Services;
using LabelMaker.Core.Sequences;

namespace LabelMaker.App.Views;

public partial class SequenceManagerWindow : Window
{
    private readonly DocumentSession _session;
    private readonly ObservableCollection<SequenceDefinition> _items;
    private readonly string _openedWith;

    public SequenceManagerWindow(DocumentSession session)
    {
        InitializeComponent();
        _session = session;
        _items = new ObservableCollection<SequenceDefinition>(session.Project.Sequences);
        _openedWith = Fingerprint(_items);
        Grid.ItemsSource = _items;
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        var def = new SequenceDefinition { Name = $"Sequence {_items.Count + 1}", Prefix = "SN", Start = 1, End = 1000, Padding = 6 };
        _items.Add(def);
        Grid.SelectedItem = def;
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is SequenceDefinition def)
        {
            _items.Remove(def);
        }
    }

    // The edit is committed after this event returns, so refresh the preview once it has been.
    private void OnRowEditEnding(object? sender, DataGridRowEditEndingEventArgs e) =>
        Dispatcher.BeginInvoke(() => OnSelectionChanged(this, null!), System.Windows.Threading.DispatcherPriority.Background);

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Keep a value still being typed when the window is closed.
        Grid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
        base.OnClosing(e);
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Grid.SelectedItem is SequenceDefinition def)
        {
            (string first, string last, long count) = SequenceGenerator.Preview(def);
            PreviewText.Text = count > 0
                ? $"{count:N0} values · first = {first} · last = {last}"
                : "This sequence produces no values (check start / end / step).";
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        // Only a real change makes the project unsaved; looking at the list must not.
        // The grid edits the definitions in place, so compare their contents, not references.
        string after = Fingerprint(_items);
        if (after != _openedWith)
        {
            _session.Project.Sequences.Clear();
            _session.Project.Sequences.AddRange(_items);
            _session.RebuildSearchIndex();
            _session.MarkDirty();
        }

        base.OnClosed(e);
    }

    private static string Fingerprint(IEnumerable<SequenceDefinition> items) =>
        JsonSerializer.Serialize(items.ToList());
}
