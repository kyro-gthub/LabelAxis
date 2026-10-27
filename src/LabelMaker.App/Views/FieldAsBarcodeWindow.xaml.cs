using System.Windows;
using LabelMaker.App.Localization;
using LabelMaker.Core.Barcodes;

namespace LabelMaker.App.Views;

/// <summary>
/// Asked when a column of barcode numbers is added to the label: as a barcode (of the type
/// chosen, the likely one picked already) or as plain text. Says how many values the chosen
/// type cannot hold, so a wrong type shows before the barcodes print as red outlines.
/// </summary>
public partial class FieldAsBarcodeWindow : Window
{
    private readonly IReadOnlyList<string> _values;

    public FieldAsBarcodeWindow(string field, IReadOnlyList<string> values, BarcodeSymbology suggested)
    {
        _values = values;
        InitializeComponent();
        IntroText.Text = Localizer.F("“{0}” holds barcodes. It can go on the label as a barcode that scans, or as its text.", field);

        var types = BarcodeEngine.SupportedSymbologies().ToList();
        if (!types.Contains(suggested))
        {
            types.Insert(0, suggested);
        }

        var rows = types
            .OrderBy(t => t == suggested ? 0 : 1)
            .Select(t => new TypeRow(t, t == suggested ? Localizer.F("{0} (suggested)", t.DisplayName()) : t.DisplayName()))
            .ToList();
        TypeBox.ItemsSource = rows;
        TypeBox.SelectedIndex = 0;
        Refresh();
    }

    /// <summary>The barcode type chosen, or null for text. Read after the window closes with Add.</summary>
    public BarcodeSymbology? Choice => BarcodeBox.IsChecked == true && TypeBox.SelectedItem is TypeRow row ? row.Symbology : null;

    private void OnChanged(object sender, RoutedEventArgs e) => Refresh();

    private void Refresh()
    {
        if (!IsInitialized)
        {
            return;
        }

        TypeBox.IsEnabled = BarcodeBox.IsChecked == true;
        FitText.Visibility = BarcodeBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (TypeBox.SelectedItem is not TypeRow row)
        {
            return;
        }

        int unfit = BarcodeColumns.Unfit(row.Symbology, _values);
        FitText.Text = unfit == 0
            ? Localizer.T("Every value fits this type.")
            : Localizer.F("{0} of the values do not fit this type; those labels would show a red outline instead of bars.", unfit);
        FitText.SetResourceReference(ForegroundProperty, unfit == 0 ? "Br.TextMuted" : "Br.Danger");
    }

    private void OnAdd(object sender, RoutedEventArgs e) => DialogResult = true;

    private sealed record TypeRow(BarcodeSymbology Symbology, string Display);
}
