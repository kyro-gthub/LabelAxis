using System.IO;
using System.Globalization;
using System.Windows;
using LabelMaker.Core.Barcodes;
using LabelMaker.Export.Pdf;
using LabelMaker.Export.Svg;
using Microsoft.Win32;

namespace LabelMaker.App.Views;

public partial class BarcodeImageWindow : Window
{
    public BarcodeImageWindow()
    {
        InitializeComponent();
        SymbologyBox.ItemsSource = BarcodeEngine.SupportedSymbologies();
        SymbologyBox.SelectedItem = BarcodeSymbology.Code128;
    }

    private BarcodeSpec? BuildSpec()
    {
        if (SymbologyBox.SelectedItem is not BarcodeSymbology symbology)
        {
            return null;
        }

        double module = Parse(ModuleBox.Text, 0.33);
        double height = Parse(HeightBox.Text, 15);

        var spec = new BarcodeSpec
        {
            Symbology = symbology,
            Data = ValueBox.Text,
            ModuleWidthMm = module,
            BarHeightMm = height,
            Gs1Mode = symbology == BarcodeSymbology.GS1_128,
            HumanReadable = HriBox.IsChecked == true ? HumanReadablePosition.Below : HumanReadablePosition.None,
            Background = TransparentBox.IsChecked == true ? Core.Model.ColorRgba.Transparent : Core.Model.ColorRgba.White,
        };

        BarcodeValidationResult v = BarcodeValidator.Validate(spec, spec.Data);
        StatusText.Text = v.IsValid
            ? $"OK · {symbology.DisplayName()}"
            : "Cannot generate: " + string.Join("; ", v.Issues.Select(i => i.Message));
        return v.IsValid ? spec : null;
    }

    private void OnSaveSvg(object sender, RoutedEventArgs e)
    {
        if (BuildSpec() is not { } spec)
        {
            return;
        }

        var dlg = new SaveFileDialog { Filter = "SVG|*.svg", FileName = spec.Data };
        if (dlg.ShowDialog(this) == true)
        {
            string svg = new SvgExporter().ExportBarcode(spec, spec.Data, transparentBackground: TransparentBox.IsChecked == true);
            File.WriteAllText(dlg.FileName, svg);
            StatusText.Text = $"Wrote {dlg.FileName}";
        }
    }

    private void OnSavePdf(object sender, RoutedEventArgs e)
    {
        if (BuildSpec() is not { } spec)
        {
            return;
        }

        var dlg = new SaveFileDialog { Filter = "PDF|*.pdf", FileName = spec.Data };
        if (dlg.ShowDialog(this) == true)
        {
            File.WriteAllBytes(dlg.FileName, BarcodePdf.Render(spec, spec.Data));
            StatusText.Text = $"Wrote {dlg.FileName}";
        }
    }

    private static double Parse(string text, double fallback) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;
}
