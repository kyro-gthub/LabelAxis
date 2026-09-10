using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using LabelMaker.App.Services;
using LabelMaker.Core.Sequences;

namespace LabelMaker.App.Views;

public partial class SequenceManagerWindow : Window
{
    private readonly DocumentSession _session;
    private readonly ObservableCollection<SequenceDefinition> _items;

    public SequenceManagerWindow(DocumentSession session)
    {
        InitializeComponent();
        _session = session;
        _items = new ObservableCollection<SequenceDefinition>(session.Project.Sequences);
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
        _session.Project.Sequences.Clear();
        _session.Project.Sequences.AddRange(_items);
        _session.RebuildSearchIndex();
        _session.MarkDirty();
        base.OnClosed(e);
    }
}
