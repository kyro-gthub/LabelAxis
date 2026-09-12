using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using LabelMaker.App.ViewModels;

namespace LabelMaker.App.Views;

/// <summary>
/// The imported records, in their own resizable window. This used to be docked
/// under the canvas, where it ate a third of the design area.
/// </summary>
public partial class DataTableWindow : Window
{
    private readonly MainViewModel _vm;

    public DataTableWindow(MainViewModel vm)
    {
        _vm = vm;
        GoToRecord = new Mvvm.RelayCommand(ShowSelectedRecord);
        InitializeComponent();
        Rebuild();
        _vm.DataChanged += OnDataChanged;
        Closed += (_, _) => _vm.DataChanged -= OnDataChanged;
    }

    /// <summary>Double-clicking a row shows it on the canvas, so the grid doubles as a record picker.</summary>
    public Mvvm.RelayCommand GoToRecord { get; }

    private void OnDataChanged(object? sender, EventArgs e) => Rebuild();

    /// <summary>Builds the columns from the imported file's own headers.</summary>
    private void Rebuild()
    {
        Grid.Columns.Clear();
        Core.Data.DataSet? data = _vm.Session.Data;
        if (data is null || data.Columns.Count == 0)
        {
            Grid.ItemsSource = null;
            Empty.Visibility = Visibility.Visible;
            Summary.Text = "No records";
            return;
        }

        Empty.Visibility = Visibility.Collapsed;
        Grid.Columns.Add(new DataGridTextColumn
        {
            Header = "#",
            Binding = new Binding(nameof(Core.Data.DataRecord.SourceRowNumber)),
            Width = 52,
        });

        foreach (Core.Data.DataColumn column in data.Columns)
        {
            Grid.Columns.Add(new DataGridTextColumn
            {
                Header = column.Name,
                // DataRecord exposes a string indexer, so the cell reads straight from the row.
                Binding = new Binding($"[{column.Name}]"),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            });
        }

        Grid.ItemsSource = data.Rows;
        Summary.Text = $"{data.Rows.Count} record(s) · {data.Columns.Count} field(s)";
    }

    private void OnImport(object sender, RoutedEventArgs e) => _vm.ImportDataCommand.Execute(null);

    private void OnAddField(object sender, RoutedEventArgs e) => _vm.AddDataFieldCommand.Execute(null);

    private void OnBulkBarcode(object sender, RoutedEventArgs e) => _vm.BulkBarcodeCommand.Execute(null);

    private void OnGoToRecord(object sender, RoutedEventArgs e) => ShowSelectedRecord();

    private void ShowSelectedRecord()
    {
        if (Grid.SelectedIndex >= 0)
        {
            _vm.Session.RecordIndex = Grid.SelectedIndex;
        }
    }
}
