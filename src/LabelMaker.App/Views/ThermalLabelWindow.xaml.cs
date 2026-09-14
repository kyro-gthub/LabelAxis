using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using LabelMaker.Core.Formtec;

namespace LabelMaker.App.Views;

/// <summary>Defines a roll of labels for a thermal printer: size, labels across, gaps and how the printer finds each label.</summary>
public partial class ThermalLabelWindow : Window
{
    private bool _loading = true;

    public ThermalLabelWindow()
    {
        InitializeComponent();
        PresetList.ItemsSource = Presets;
        _loading = false;
        PresetList.SelectedIndex = 2;
    }

    /// <summary>The roll template, when the window closed with Create.</summary>
    public LabelTemplate? Result { get; private set; }

    /// <summary>Whether to keep the roll in the user's templates.</summary>
    public bool SaveAsTemplate => SaveTemplateBox.IsChecked == true;

    private sealed record Preset(string Title, string Hint, double Width, double Height, int Across = 1, double Gap = 3, MediaSensing Sensing = MediaSensing.Gap)
    {
        public override string ToString() => Title;    // screen readers and type-to-search
    }

    private static readonly Preset[] Presets =
    {
        new("100 × 150 mm", "Shipping label (4 × 6 in)", 100, 150),
        new("100 × 100 mm", "Carton / pallet", 100, 100),
        new("100 × 50 mm", "Product and address", 100, 50),
        new("80 × 50 mm", "Product", 80, 50),
        new("76 × 25 mm", "Barcode (3 × 1 in)", 76, 25),
        new("60 × 40 mm", "Price and product", 60, 40),
        new("58 × 40 mm", "Price and product", 58, 40),
        new("50 × 30 mm", "Barcode and price", 50, 30),
        new("50 × 25 mm", "Barcode (2 × 1 in)", 50, 25),
        new("40 × 30 mm", "Small product", 40, 30),
        new("38 × 25 mm", "Small barcode", 38, 25),
        new("32 × 25 mm × 3", "Three across", 32, 25, 3, 2),
        new("25 × 10 mm × 4", "Tiny tags, four across", 25, 10, 4, 2),
        new("80 mm continuous", "Receipt-style stock", 80, 60, 1, 0, MediaSensing.Continuous),
    };

    private void OnPreset(object sender, SelectionChangedEventArgs e)
    {
        if (PresetList.SelectedItem is not Preset p)
        {
            return;
        }

        _loading = true;
        WidthBox.Text = Format(p.Width);
        HeightBox.Text = Format(p.Height);
        AcrossBox.Text = p.Across.ToString(CultureInfo.InvariantCulture);
        RollGapBox.Text = Format(p.Gap);
        ColumnGapBox.Text = p.Across > 1 ? "2" : "0";
        SensingBox.SelectedIndex = (int)p.Sensing;
        _loading = false;
        Redraw();
    }

    private void OnChanged(object sender, TextChangedEventArgs e) => Redraw();

    private void OnSensingChanged(object sender, SelectionChangedEventArgs e) => Redraw();

    private void OnPreviewSize(object sender, SizeChangedEventArgs e) => Redraw();

    private LabelTemplate? Build(out string? problem)
    {
        problem = null;
        double w = Parse(WidthBox), h = Parse(HeightBox);
        int across = (int)Math.Round(Parse(AcrossBox, 1));
        if (w < 5 || h < 5 || w > 300 || h > 1000)
        {
            problem = "Labels must be between 5 and 300 mm wide and 5 and 1000 mm tall.";
            return null;
        }

        if (across is < 1 or > 12)
        {
            problem = "Labels across must be between 1 and 12.";
            return null;
        }

        var sensing = (MediaSensing)Math.Max(0, SensingBox.SelectedIndex);
        double rollGap = sensing == MediaSensing.Continuous ? 0 : Math.Max(0, Parse(RollGapBox, 3));
        LabelTemplate roll = LabelTemplate.CreateRoll(
            $"thermal-{w:0.#}x{h:0.#}-{across}up".Replace('.', '_').Replace(',', '_'),
            $"Thermal roll {Format(w)} × {Format(h)} mm" + (across > 1 ? $", {across} across" : string.Empty),
            w, h, across, Math.Max(0, Parse(ColumnGapBox, 2)), Math.Max(0, Parse(SideBox, 0)), rollGap, sensing,
            Math.Max(0, Parse(RadiusBox, 0)));

        if (roll.EffectivePage.WidthMm > 350)
        {
            problem = $"The roll would be {roll.EffectivePage.WidthMm:0} mm wide, wider than any label printer.";
            return null;
        }

        return roll;
    }

    private void Redraw()
    {
        if (_loading || !IsLoaded && PreviewCanvas.ActualWidth <= 0)
        {
            return;
        }

        PreviewCanvas.Children.Clear();
        LabelTemplate? roll = Build(out string? problem);
        Problem.Text = problem ?? string.Empty;
        if (roll is null)
        {
            Summary.Text = string.Empty;
            return;
        }

        double gap = roll.Roll?.GapMm ?? 0;
        double liner = roll.EffectivePage.WidthMm;
        double rowPitch = roll.Grid.LabelHeightMm + gap;
        double areaW = Math.Max(50, PreviewCanvas.ActualWidth - 40);
        double areaH = Math.Max(50, PreviewCanvas.ActualHeight - 40);
        double scale = Math.Min(areaW / liner, areaH / Math.Min(rowPitch * 3.3, rowPitch * 3 + 20));
        double left = (PreviewCanvas.ActualWidth - (liner * scale)) / 2;

        var linerRect = new Rectangle
        {
            Width = liner * scale,
            Height = PreviewCanvas.ActualHeight,
            Fill = new SolidColorBrush(Color.FromRgb(0xF4, 0xEE, 0xE1)),
        };
        Canvas.SetLeft(linerRect, left);
        PreviewCanvas.Children.Add(linerRect);

        var stroke = new SolidColorBrush(Color.FromRgb(0x5B, 0x8D, 0xEF));
        double y = 16;
        while (y < PreviewCanvas.ActualHeight)
        {
            foreach (LabelSlot slot in TemplateGeometry.AllSlots(roll))
            {
                var label = new Rectangle
                {
                    Width = slot.RectMm.Width * scale,
                    Height = slot.RectMm.Height * scale,
                    RadiusX = roll.Grid.CornerRadiusMm * scale,
                    RadiusY = roll.Grid.CornerRadiusMm * scale,
                    Fill = Brushes.White,
                    Stroke = stroke,
                    StrokeThickness = 1,
                };
                Canvas.SetLeft(label, left + (slot.RectMm.X * scale));
                Canvas.SetTop(label, y);
                PreviewCanvas.Children.Add(label);
            }

            if (roll.Roll?.Sensing == MediaSensing.BlackMark)
            {
                var mark = new Rectangle { Width = 6, Height = Math.Max(2, gap * scale), Fill = Brushes.Black, Opacity = 0.35 };
                Canvas.SetLeft(mark, left + (liner * scale) + 4);
                Canvas.SetTop(mark, y + (roll.Grid.LabelHeightMm * scale));
                PreviewCanvas.Children.Add(mark);
            }

            y += Math.Max(8, rowPitch * scale);
        }

        Summary.Text = $"Roll {roll.EffectivePage.WidthMm:0.##} mm wide. Each printed row is {roll.Grid.LabelHeightMm:0.##} mm long" +
                       (gap > 0 ? $" with a {gap:0.#} mm gap." : ".") +
                       " Set the printer's resolution, darkness and connection when you print.";
    }

    private void OnCreate(object sender, RoutedEventArgs e)
    {
        if (Build(out string? problem) is not { } roll)
        {
            Problem.Text = problem;
            return;
        }

        Result = roll;
        DialogResult = true;
    }

    private static double Parse(TextBox box, double fallback = 0) =>
        double.TryParse(box.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;

    private static string Format(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
}
