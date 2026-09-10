using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Units;

namespace LabelMaker.App.Views;

public partial class NewProjectWindow : Window
{
    private readonly TemplateStore _store;

    public NewProjectWindow(TemplateStore store)
    {
        InitializeComponent();
        _store = store;
        SearchBox.TextChanged += (_, _) => Refresh();
        Refresh();
        if (TemplateList.Items.Count > 0)
        {
            TemplateList.SelectedIndex = 0;
        }
    }

    public LabelTemplate? SelectedTemplate { get; private set; }

    private void Refresh()
    {
        var query = new TemplateQuery { Text = SearchBox.Text };
        TemplateList.ItemsSource = _store.Search(query).Select(t => new Row(t)).ToList();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TemplateList.SelectedItem is not Row row)
        {
            return;
        }

        LabelTemplate t = row.Template;
        PreviewTitle.Text = $"{t.Brand} {t.ProductCode} — {t.Name}";
        PreviewDetail.Text =
            $"Sheet {t.EffectivePage.Name} ({t.EffectivePage.WidthMm:0.#}×{t.EffectivePage.HeightMm:0.#} mm)\n" +
            $"{t.Grid.Columns} columns × {t.Grid.Rows} rows = {t.Grid.Count} labels\n" +
            $"Label {t.Grid.LabelWidthMm:0.##}×{t.Grid.LabelHeightMm:0.##} mm, gaps {t.Grid.HGapMm:0.#}/{t.Grid.VGapMm:0.#} mm\n" +
            $"Margins L{t.Grid.MarginLeftMm:0.#} T{t.Grid.MarginTopMm:0.#} mm · {t.Grid.LabelShape}";
        DrawSheetPreview(t);
    }

    private void DrawSheetPreview(LabelTemplate t)
    {
        PreviewCanvas.Children.Clear();
        PreviewCanvas.UpdateLayout();
        double cw = PreviewCanvas.ActualWidth <= 0 ? 220 : PreviewCanvas.ActualWidth;
        double ch = PreviewCanvas.ActualHeight <= 0 ? 300 : PreviewCanvas.ActualHeight;
        PageSize page = t.EffectivePage;
        double scale = Math.Min((cw - 8) / page.WidthMm, (ch - 8) / page.HeightMm);

        var sheet = new Rectangle
        {
            Width = page.WidthMm * scale,
            Height = page.HeightMm * scale,
            Stroke = Brushes.Gray,
            Fill = Brushes.White,
            StrokeThickness = 1,
        };
        Canvas.SetLeft(sheet, 4);
        Canvas.SetTop(sheet, 4);
        PreviewCanvas.Children.Add(sheet);

        foreach (LabelSlot slot in TemplateGeometry.AllSlots(t))
        {
            Shape shape = t.Grid.LabelShape == LabelShape.Ellipse
                ? new Ellipse()
                : new Rectangle { RadiusX = t.Grid.CornerRadiusMm * scale, RadiusY = t.Grid.CornerRadiusMm * scale };
            shape.Width = slot.RectMm.Width * scale;
            shape.Height = slot.RectMm.Height * scale;
            shape.Stroke = new SolidColorBrush(Color.FromRgb(0, 120, 215));
            shape.StrokeThickness = 0.8;
            shape.Fill = new SolidColorBrush(Color.FromArgb(30, 0, 120, 215));
            Canvas.SetLeft(shape, 4 + (slot.RectMm.X * scale));
            Canvas.SetTop(shape, 4 + (slot.RectMm.Y * scale));
            PreviewCanvas.Children.Add(shape);
        }
    }

    private void OnCreate(object sender, RoutedEventArgs e)
    {
        if (TemplateList.SelectedItem is Row row)
        {
            SelectedTemplate = row.Template;
            DialogResult = true;
        }
    }

    private sealed record Row(LabelTemplate Template)
    {
        public string ProductCode => Template.ProductCode;
        public string Brand => Template.Brand;
        public string Name => Template.Name;
        public string GridText => $"{Template.Grid.Columns}×{Template.Grid.Rows}";
        public string LabelText => $"{Template.Grid.LabelWidthMm:0.#}×{Template.Grid.LabelHeightMm:0.#}";
    }
}
