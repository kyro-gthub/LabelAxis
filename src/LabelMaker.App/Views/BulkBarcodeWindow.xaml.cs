using System.IO;
using System.Globalization;
using System.Windows;
using LabelMaker.App.Rendering;
using LabelMaker.App.Services;
using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Bulk;
using LabelMaker.Core.Sequences;
using LabelMaker.Core.Serialization;
using LabelMaker.Export.Pdf;
using LabelMaker.Export.Svg;
using Microsoft.Win32;

namespace LabelMaker.App.Views;

public partial class BulkBarcodeWindow : Window
{
    private readonly AppEnvironment _env;
    private readonly WpfImageExporter _imageExporter;

    public BulkBarcodeWindow(AppEnvironment env)
    {
        InitializeComponent();
        _env = env;
        _imageExporter = new WpfImageExporter(new SheetVisualRenderer(new WpfTextMeasurer(), new WpfImageSource(new AssetStore())));
        SymbologyBox.ItemsSource = BarcodeEngine.SupportedSymbologies();
        SymbologyBox.SelectedItem = BarcodeSymbology.Code128;
    }

    private BulkBarcodeRequest? BuildRequest(string outputDir)
    {
        if (SymbologyBox.SelectedItem is not BarcodeSymbology symbology)
        {
            return null;
        }

        var sequence = new SequenceDefinition
        {
            Prefix = PrefixBox.Text,
            Suffix = SuffixBox.Text,
            Start = ParseLong(StartBox.Text, 1),
            End = ParseLong(EndBox.Text, 1000),
            Step = ParseLong(StepBox.Text, 1),
            Padding = (int)ParseLong(PaddingBox.Text, 6),
            Casing = SequenceCasing.Upper,
        };

        return new BulkBarcodeRequest
        {
            Sequence = sequence,
            Template = new BarcodeSpec
            {
                Symbology = symbology,
                ModuleWidthMm = ParseDouble(ModuleBox.Text, 0.33),
                BarHeightMm = ParseDouble(HeightBox.Text, 15),
                Gs1Mode = symbology == BarcodeSymbology.GS1_128,
                HumanReadable = HriBox.IsChecked == true ? HumanReadablePosition.Below : HumanReadablePosition.None,
                // SVG/PDF never paint a background rect, so this only matters for the PNG path,
                // whose renderer fills Spec.Background behind the bars unless it's transparent.
                Background = TransparentBox.IsChecked == true ? Core.Model.ColorRgba.Transparent : Core.Model.ColorRgba.White,
            },
            Format = FormatBox.SelectedIndex switch
            {
                1 => BarcodeImageFormat.Pdf,
                2 => BarcodeImageFormat.Png,
                _ => BarcodeImageFormat.Svg,
            },
            HumanReadable = HriBox.IsChecked == true,
            TransparentBackground = TransparentBox.IsChecked == true,
            FileNamePattern = string.IsNullOrWhiteSpace(FileNameBox.Text) ? "{value}" : FileNameBox.Text,
            OutputDirectory = outputDir,
        };
    }

    private void OnPreview(object sender, RoutedEventArgs e)
    {
        BulkBarcodeRequest? request = BuildRequest(string.Empty);
        if (request is null)
        {
            return;
        }

        (string first, string last, long count) = BulkBarcodeService.Preview(request);
        var errors = BulkBarcodeService.ValidateAll(request);
        StatusText.Text = errors.Count > 0
            ? $"{count:N0} items · first={first} last={last}\n\nValidation errors:\n" + string.Join('\n', errors.Take(8))
            : $"{count:N0} items · first={first} · last={last}\nAll values valid.";
    }

    private void OnGenerate(object sender, RoutedEventArgs e)
    {
        var folder = new OpenFolderDialog { Title = "Choose output folder" };
        if (folder.ShowDialog(this) != true)
        {
            return;
        }

        BulkBarcodeRequest? request = BuildRequest(folder.FolderName);
        if (request is null)
        {
            return;
        }

        var errors = BulkBarcodeService.ValidateAll(request);
        if (errors.Count > 0)
        {
            MessageBox.Show(this, "Validation failed:\n\n" + string.Join('\n', errors.Take(10)), "Bulk barcodes",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Cursor = System.Windows.Input.Cursors.Wait;
        try
        {
            var svg = new SvgExporter();
            int written = 0;
            string ext = request.Format switch
            {
                BarcodeImageFormat.Pdf => "pdf",
                BarcodeImageFormat.Png => "png",
                _ => "svg",
            };
            foreach (BulkBarcodeItem item in BulkBarcodeService.Enumerate(request))
            {
                string path = Path.Combine(folder.FolderName, item.FileName + "." + ext);
                switch (request.Format)
                {
                    case BarcodeImageFormat.Pdf:
                        File.WriteAllBytes(path, BarcodePdf.Render(item.Spec, item.Value));
                        break;
                    case BarcodeImageFormat.Png:
                        File.WriteAllBytes(path, BarcodeRasterizer.RenderPng(_imageExporter, item.Spec, item.Value, transparent: request.TransparentBackground));
                        break;
                    default:
                        File.WriteAllText(path, svg.ExportBarcode(item.Spec, item.Value, transparentBackground: request.TransparentBackground));
                        break;
                }

                written++;
            }

            StatusText.Text = $"Wrote {written:N0} files to {folder.FolderName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Bulk barcodes", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Cursor = null;
        }
    }

    private static long ParseLong(string s, long fallback) =>
        long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : fallback;

    private static double ParseDouble(string s, double fallback) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;
}
