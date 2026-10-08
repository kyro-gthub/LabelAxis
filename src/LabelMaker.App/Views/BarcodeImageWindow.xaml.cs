using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using LabelMaker.App.Rendering;
using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Diagnostics;
using LabelMaker.Core.Serialization;
using LabelMaker.Export.Pdf;
using LabelMaker.Export.Svg;
using Microsoft.Win32;

namespace LabelMaker.App.Views;

public partial class BarcodeImageWindow : Window
{
    private readonly WpfImageExporter _imageExporter =
        new(new SheetVisualRenderer(new WpfTextMeasurer(), new WpfImageSource(new AssetStore())));

    public BarcodeImageWindow()
    {
        InitializeComponent();
        SymbologyBox.ItemsSource = BarcodeEngine.SupportedSymbologies();
        SymbologyBox.SelectedItem = BarcodeSymbology.Code128;

        // Redraw the preview whenever anything that changes the symbol changes.
        SymbologyBox.SelectionChanged += (_, _) => UpdatePreview();
        foreach (TextBox box in new[] { ValueBox, ModuleBox, HeightBox })
        {
            box.TextChanged += (_, _) => UpdatePreview();
        }

        HriBox.Click += (_, _) => UpdatePreview();
        TransparentBox.Click += (_, _) => UpdatePreview();
        Loaded += (_, _) => UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (!IsLoaded)
        {
            return;
        }

        PreviewImage.Source = null;
        if (BuildSpec() is not { } spec)
        {
            return;
        }

        try
        {
            // The preview is at screen size, so transparency is shown against the panel.
            byte[] png = BarcodeRasterizer.RenderPng(_imageExporter, spec, spec.Data, dpi: 96, transparent: TransparentBox.IsChecked == true);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = new MemoryStream(png);
            bitmap.EndInit();
            bitmap.Freeze();
            PreviewImage.Source = bitmap;
        }
        catch (Exception ex) when (Faults.IsReportable(ex))
        {
            StatusText.Text = "Cannot preview: " + ex.Message;
        }
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

    /// <summary>A PNG picture at the chosen resolution, the symbol at its real printed size.</summary>
    private void OnSavePng(object sender, RoutedEventArgs e)
    {
        if (BuildSpec() is not { } spec)
        {
            return;
        }

        int dpi = DpiBox.SelectedItem is ComboBoxItem { Tag: string tag } && int.TryParse(tag, out int chosen) ? chosen : 300;
        var dlg = new SaveFileDialog { Filter = "PNG picture|*.png", FileName = SafeFileName(spec.Data), DefaultExt = ".png" };
        if (dlg.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            byte[] png = BarcodeRasterizer.RenderPng(_imageExporter, spec, spec.Data, dpi, transparent: TransparentBox.IsChecked == true);
            File.WriteAllBytes(dlg.FileName, png);
            StatusText.Text = $"Wrote {dlg.FileName}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusText.Text = "Cannot save the picture: " + ex.Message;
        }
    }

    /// <summary>The barcode's value as a file name, without the characters Windows does not allow.</summary>
    private static string SafeFileName(string value)
    {
        string name = string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        return name.Length == 0 ? "barcode" : name.Length > 80 ? name[..80] : name;
    }

    private void OnSaveSvg(object sender, RoutedEventArgs e)
    {
        if (BuildSpec() is not { } spec)
        {
            return;
        }

        var dlg = new SaveFileDialog { Filter = "SVG|*.svg", FileName = SafeFileName(spec.Data) };
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

        var dlg = new SaveFileDialog { Filter = "PDF|*.pdf", FileName = SafeFileName(spec.Data) };
        if (dlg.ShowDialog(this) == true)
        {
            File.WriteAllBytes(dlg.FileName, BarcodePdf.Render(spec, spec.Data));
            StatusText.Text = $"Wrote {dlg.FileName}";
        }
    }

    private static double Parse(string text, double fallback) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;
}
