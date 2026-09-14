using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using LabelMaker.App.ViewModels;
using LabelMaker.Core.Data;
using LabelMaker.Core.Model;

namespace LabelMaker.App.Views;

/// <summary>
/// The records behind variable-data printing, in their own window: edit cells, add and
/// delete records (all undoable), filter, and print a label for just the selected or
/// filtered records. Edits change the project's copy; Refresh reads the source again.
/// </summary>
public partial class DataTableWindow : Window
{
    private readonly MainViewModel _vm;
    private ListCollectionView? _view;
    private DataSet? _data;
    private string _filter = string.Empty;

    // Which data field each grid column shows; columns can be dragged into a different order.
    private readonly Dictionary<DataGridColumn, int> _fieldOf = new();
    private DataGridColumn? _numberColumn;

    public DataTableWindow(MainViewModel vm)
    {
        _vm = vm;
        FocusFilter = new Mvvm.RelayCommand(() => { FilterBox.Focus(); FilterBox.SelectAll(); });
        InitializeComponent();
        DataContext = vm;
        Rebuild();
        _vm.DataChanged += OnDataChanged;
        _vm.RecordsEdited += OnRecordsEdited;
        Closed += (_, _) =>
        {
            _vm.DataChanged -= OnDataChanged;
            _vm.RecordsEdited -= OnRecordsEdited;
        };
    }

    public Mvvm.RelayCommand FocusFilter { get; }

    private void OnDataChanged(object? sender, EventArgs e) => Rebuild();

    private void OnRecordsEdited(object? sender, EventArgs e)
    {
        // Cell edits update themselves through the record; added or removed rows need the view refreshed.
        if (_view is not null && _data is not null && _lastCount != _data.RowCount)
        {
            _view.Refresh();
        }

        UpdateSummary();
    }

    private int _lastCount;

    /// <summary>Builds the columns from the source's own field names.</summary>
    private void Rebuild()
    {
        Grid.Columns.Clear();
        _fieldOf.Clear();
        _data = _vm.Session.Data;
        SourceText.Text = _vm.Session.ActiveConnection?.Describe() ?? (_data is null ? "No data source" : "Typed or pasted records");
        RefreshButton.IsEnabled = _vm.CanRefreshData;

        if (_data is null || _data.Columns.Count == 0)
        {
            Grid.ItemsSource = null;
            _view = null;
            Empty.Visibility = Visibility.Visible;
            UpdateSummary();
            return;
        }

        Empty.Visibility = Visibility.Collapsed;
        _numberColumn = new DataGridTextColumn
        {
            Header = "#",
            Binding = new Binding(nameof(DataRecord.SourceRowNumber)) { Mode = BindingMode.OneWay },
            IsReadOnly = true,
            Width = 56,
            CanUserReorder = false,
        };
        Grid.Columns.Add(_numberColumn);

        foreach (DataColumn column in _data.Columns)
        {
            var gridColumn = new DataGridTextColumn
            {
                Header = new TextBlock { Text = column.Name },
                // Bound by position (names may hold dots or brackets); the record raises change
                // notifications, so an undone edit shows at once.
                Binding = new Binding($"RawValues[{column.Index}]") { Mode = BindingMode.OneWay },
                Width = DataGridLength.Auto,
                MinWidth = 90,
                MaxWidth = 360,
            };
            _fieldOf[gridColumn] = column.Index;
            Grid.Columns.Add(gridColumn);
        }

        _view = new ListCollectionView((System.Collections.IList)_data.Rows) { Filter = Matches };
        Grid.ItemsSource = _view;
        UpdateSummary();
    }

    private bool Matches(object item)
    {
        if (_filter.Length == 0 || item is not DataRecord record)
        {
            return true;
        }

        foreach (string value in record.RawValues)
        {
            if (value.Contains(_filter, StringComparison.CurrentCultureIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void OnFilterChanged(object sender, TextChangedEventArgs e)
    {
        _filter = FilterBox.Text.Trim();
        _view?.Refresh();
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        int total = _data?.RowCount ?? 0;
        _lastCount = total;
        int shown = _view?.Count ?? 0;
        int selected = Grid.SelectedItems.Count;

        Summary.Text = total == 0
            ? "No records"
            : $"{total:N0} record(s) · {_data!.Columns.Count} field(s)" +
              (_filter.Length > 0 ? $" · {shown:N0} match the filter" : string.Empty) +
              (selected > 1 ? $" · {selected:N0} selected" : string.Empty) +
              (_vm.Session.Project.PrintSettings.SelectedRecords.Count > 0
                  ? $" · printing {_vm.Session.Project.PrintSettings.SelectedRecords.Count:N0} chosen"
                  : string.Empty);

        PrintSelectedButton.Content = selected > 0 ? $"Print selected ({selected:N0})" : "Print selected";
        PrintSelectedButton.IsEnabled = selected > 0;
        PrintShownButton.Content = _filter.Length > 0 ? $"Print {shown:N0} shown" : "Print all";
        PrintShownButton.IsEnabled = shown > 0;
        DeleteButton.IsEnabled = selected > 0;
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSummary();

    // ---- editing ----------------------------------------------------------------

    private void OnCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit || e.Row.Item is not DataRecord record ||
            e.EditingElement is not TextBox box || !_fieldOf.TryGetValue(e.Column, out int column))
        {
            return;
        }

        string value = box.Text;

        // Apply after the grid has finished its own commit, through the undo stack.
        Dispatcher.BeginInvoke(() => _vm.EditRecordCell(record, column, value));
    }

    private List<DataRecord> SelectedRecords() => Grid.SelectedItems.OfType<DataRecord>().ToList();

    private void OnAddRecord(object sender, RoutedEventArgs e)
    {
        if (_data is null)
        {
            return;
        }

        int at = Grid.SelectedItem is DataRecord current ? _data.IndexOf(current) + 1 : _data.RowCount;
        if (_vm.AddRecord(at) is { } record)
        {
            if (_filter.Length > 0)
            {
                FilterBox.Text = string.Empty;
            }

            _view?.Refresh();
            Grid.SelectedItem = record;
            Grid.ScrollIntoView(record);
            DataGridColumn first = Grid.Columns.Where(c => c != _numberColumn).OrderBy(c => c.DisplayIndex).FirstOrDefault() ?? Grid.Columns[0];
            Grid.CurrentCell = new DataGridCellInfo(record, first);
            Grid.BeginEdit();
        }
    }

    private void OnDelete(object sender, RoutedEventArgs e) => DeleteSelected();

    private void DeleteSelected()
    {
        List<DataRecord> records = SelectedRecords();
        if (records.Count == 0)
        {
            return;
        }

        if (records.Count > 1 && MessageBox.Show(this, $"Delete {records.Count:N0} records? You can undo this with Ctrl+Z.",
                "LabelAxis", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
        {
            return;
        }

        _vm.DeleteRecords(records);
        _view?.Refresh();
        UpdateSummary();
    }

    private void OnGridKeyDown(object sender, KeyEventArgs e)
    {
        bool editing = e.OriginalSource is TextBox;
        if (e.Key == Key.Delete && !editing)
        {
            DeleteSelected();
            e.Handled = true;
        }
    }

    // ---- sources and printing ---------------------------------------------------------

    private void OnImport(object sender, RoutedEventArgs e) => _vm.ImportDataCommand.Execute(null);

    private void OnConnect(object sender, RoutedEventArgs e) =>
        (_vm.Session.ActiveConnection is { } c && c.Kind.IsDatabase() ? _vm.EditConnectionCommand : _vm.ConnectDatabaseCommand).Execute(null);

    private void OnRefresh(object sender, RoutedEventArgs e) => _vm.RefreshData();

    private void OnAddField(object sender, RoutedEventArgs e) => _vm.AddDataFieldCommand.Execute(null);

    private void OnGoToRecord(object sender, RoutedEventArgs e) => ShowSelectedRecord();

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Double-clicking a cell starts editing; only the row header area and '#' column jump to the record.
        if (e.OriginalSource is FrameworkElement { DataContext: DataRecord } element &&
            ItemsControl.ContainerFromElement(Grid, element) is DataGridRow &&
            Grid.CurrentColumn == _numberColumn)
        {
            ShowSelectedRecord();
        }
    }

    private void ShowSelectedRecord()
    {
        if (_data is not null && Grid.SelectedItem is DataRecord record)
        {
            _vm.Session.RecordIndex = _data.IndexOf(record);
        }
    }

    private void OnPrintSelected(object sender, RoutedEventArgs e) => PrintRecords(SelectedRecords());

    private void OnPrintShown(object sender, RoutedEventArgs e) =>
        PrintRecords(_view?.Cast<DataRecord>().ToList() ?? new List<DataRecord>());

    private void PrintRecords(IReadOnlyList<DataRecord> records)
    {
        if (_data is null || records.Count == 0)
        {
            return;
        }

        // Row numbers in table order, however the rows were selected.
        var index = new Dictionary<DataRecord, int>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < _data.RowCount; i++)
        {
            index[_data.Rows[i]] = i;
        }

        List<int> rows = records.Select(r => index.TryGetValue(r, out int i) ? i : -1).Where(i => i >= 0).ToList();
        bool all = rows.Count == _data.RowCount;
        if (all)
        {
            _vm.Session.Project.PrintSettings.SelectedRecords.Clear();
            _vm.PrintCommand.Execute(null);
        }
        else
        {
            _vm.PrintRecords(rows);
        }

        UpdateSummary();
    }
}
