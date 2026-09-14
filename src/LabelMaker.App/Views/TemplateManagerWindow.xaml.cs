using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using LabelMaker.App.Mvvm;
using LabelMaker.App.Services;
using LabelMaker.Core.Formtec;
using Microsoft.Win32;

namespace LabelMaker.App.Views;

/// <summary>
/// Browses every installed label sheet, whatever the brand: categories down the
/// left, one row per sheet reading brand / code / size, and a drawn preview of the
/// actual grid on the right. It used to be a grid of raw fields next to the
/// template's JSON, which told you nothing about what the sheet looks like.
/// </summary>
public partial class TemplateManagerWindow : Window
{
    private readonly AppEnvironment _env;
    private List<Category> _categories = new();

    public TemplateManagerWindow(AppEnvironment env)
    {
        _env = env;
        UseCommand = new RelayCommand(Use);
        InitializeComponent();
        SearchBox.TextChanged += (_, _) => Refresh();
        Refresh();
    }

    /// <summary>The sheet the user picked, or null if they closed the window.</summary>
    public LabelTemplate? SelectedTemplate { get; private set; }

    public RelayCommand UseCommand { get; }

    // ---- listing ----------------------------------------------------------

    private void Refresh()
    {
        IEnumerable<LabelTemplate> source = _env.Templates.All;
        string q = SearchBox.Text?.Trim() ?? string.Empty;
        if (q.Length > 0)
        {
            source = source.Where(t =>
                t.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                t.ProductCode.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                t.Brand.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                $"{t.Grid.LabelWidthMm:0.##}x{t.Grid.LabelHeightMm:0.##}".Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        var all = source.ToList();
        _categories = new List<Category>();

        void Add(string title, string subtitle, string icon, Func<LabelTemplate, bool> match)
        {
            var hits = all.Where(match)
                .OrderBy(t => t.Brand, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.ProductCode, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (hits.Count > 0)
            {
                _categories.Add(new Category(title, subtitle, icon, hits));
            }
        }

        Add("All sheets", "Every installed template", "Icon.Gallery", _ => true);

        // One entry per brand, so other brands appear here automatically as they are imported.
        foreach (string brand in all.Select(t => t.Brand).Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(b => b, StringComparer.OrdinalIgnoreCase))
        {
            Add(brand, "Brand", "Icon.Sheet", t => string.Equals(t.Brand, brand, StringComparison.OrdinalIgnoreCase));
        }

        Add("Product labels", "Retail & commercial", "Icon.Tag",
            t => t.Grid.LabelWidthMm >= 50 && t.Grid.LabelHeightMm >= 20);
        Add("Barcode labels", "Logistics & inventory", "Icon.Barcode",
            t => t.Grid.LabelWidthMm >= 40 && t.Grid.LabelHeightMm < 30);
        Add("Small & serial", "Sequential numbering", "Icon.Sticker", t => t.Grid.LabelWidthMm < 40);
        Add("Round & special", "Shaped labels", "Icon.Ellipse", t => t.Grid.LabelShape != LabelShape.Rectangle);
        Add("My templates", "Imported or edited here", "Icon.Plus", t => t.IsUserDefined);

        Categories.ItemsSource = _categories;
        Categories.SelectedIndex = 0;
    }

    private void OnCategory(object sender, SelectionChangedEventArgs e)
    {
        if (Categories.SelectedItem is Category c)
        {
            List.ItemsSource = c.Templates.Select(t => new Row(t)).ToList();
            List.SelectedIndex = c.Templates.Count > 0 ? 0 : -1;
        }
    }

    private void OnSel(object sender, SelectionChangedEventArgs e)
    {
        if (List.SelectedItem is not Row row)
        {
            PreviewHost.Children.Clear();
            PreviewTitle.Text = string.Empty;
            PreviewDetail.Text = string.Empty;
            return;
        }

        LabelTemplate t = row.Template;
        PreviewTitle.Text = $"{t.Brand} {t.ProductCode}".Trim();
        PreviewDetail.Text =
            $"{t.Name}\n" +
            $"{t.Grid.Columns} × {t.Grid.Rows} = {t.Grid.Count} labels per {t.EffectivePage.Name} sheet\n" +
            $"label {t.Grid.LabelWidthMm:0.##} × {t.Grid.LabelHeightMm:0.##} mm · " +
            $"margins {t.Grid.MarginLeftMm:0.##} / {t.Grid.MarginTopMm:0.##} mm · " +
            $"gaps {t.Grid.HGapMm:0.##} / {t.Grid.VGapMm:0.##} mm" +
            (t.IsUserDefined ? "\nUser template" : string.Empty);

        DrawPreview(t);
    }

    /// <summary>Draws the page with its label grid to scale — the shape of the sheet, not its JSON.</summary>
    private void DrawPreview(LabelTemplate t)
    {
        PreviewHost.Children.Clear();
        PageSize page = t.EffectivePage;
        if (page.WidthMm <= 0 || page.HeightMm <= 0)
        {
            return;
        }

        double available = 250;
        double scale = Math.Min(available / page.WidthMm, 330 / page.HeightMm);

        var canvas = new Canvas
        {
            Width = page.WidthMm * scale,
            Height = page.HeightMm * scale,
            Background = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            SnapsToDevicePixels = true,
        };

        var border = new Border
        {
            BorderBrush = (Brush)FindResource("Br.BorderStrong"),
            BorderThickness = new Thickness(1),
            Child = canvas,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        LabelGrid g = t.Grid;
        var fill = (Brush)FindResource("Br.AccentSoft");
        var stroke = (Brush)FindResource("Br.Accent");

        for (int row = 0; row < g.Rows; row++)
        {
            for (int col = 0; col < g.Columns; col++)
            {
                double x = g.MarginLeftMm + (col * (g.LabelWidthMm + g.HGapMm));
                double y = g.MarginTopMm + (row * (g.LabelHeightMm + g.VGapMm));

                Shape cell = g.LabelShape == LabelShape.Ellipse
                    ? new Ellipse()
                    : new Rectangle
                    {
                        RadiusX = g.CornerRadiusMm * scale,
                        RadiusY = g.CornerRadiusMm * scale,
                    };

                cell.Width = Math.Max(1, g.LabelWidthMm * scale);
                cell.Height = Math.Max(1, g.LabelHeightMm * scale);
                cell.Fill = fill;
                cell.Stroke = stroke;
                cell.StrokeThickness = 0.6;

                Canvas.SetLeft(cell, x * scale);
                Canvas.SetTop(cell, y * scale);
                canvas.Children.Add(cell);
            }
        }

        PreviewHost.Children.Add(border);
    }

    // ---- actions ----------------------------------------------------------

    private void Use()
    {
        if (List.SelectedItem is Row row)
        {
            SelectedTemplate = row.Template;
            DialogResult = true;
        }
    }

    private void OnUse(object sender, RoutedEventArgs e) => Use();

    private void OnImport(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Template JSON|*.json", Multiselect = true };
        if (dlg.ShowDialog(this) != true)
        {
            return;
        }

        int ok = 0;
        foreach (string file in dlg.FileNames)
        {
            try
            {
                LabelTemplate t = _env.Templates.ImportJson(file);
                _env.Templates.SaveUserTemplate(t, _env.Paths.UserTemplatesDirectory);
                ok++;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"{System.IO.Path.GetFileName(file)}: {ex.Message}", "Import", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        Refresh();
        MessageBox.Show(this, $"Imported {ok} template(s).", "Import", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnImportCsv(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "CSV|*.csv" };
        if (dlg.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var added = _env.Templates.ImportCsv(File.ReadAllText(dlg.FileName));
            foreach (LabelTemplate t in added)
            {
                _env.Templates.SaveUserTemplate(t, _env.Paths.UserTemplatesDirectory);
            }

            Refresh();
            MessageBox.Show(this, $"Imported {added.Count} template(s) from CSV.", "Import", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Import", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is not Row row)
        {
            return;
        }

        var dlg = new SaveFileDialog { Filter = "Template JSON|*.json", FileName = row.Template.Id };
        if (dlg.ShowDialog(this) == true)
        {
            _env.Templates.ExportJson(row.Template.Id, dlg.FileName);
        }
    }

    private void OnExportCsv(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Filter = "CSV|*.csv", FileName = "label-templates" };
        if (dlg.ShowDialog(this) == true)
        {
            File.WriteAllText(dlg.FileName, _env.Templates.ExportCsv());
        }
    }

    private sealed record Category(string Title, string Subtitle, string IconKey, IReadOnlyList<LabelTemplate> Templates)
    {
        public int Count => Templates.Count;

        public override string ToString() => Title;    // screen readers and type-to-search
    }

    /// <summary>One sheet, read as brand / code / size rather than as raw JSON.</summary>
    private sealed record Row(LabelTemplate Template)
    {
        public string Title => $"{Template.Brand} {Template.ProductCode}".Trim();

        public string Subtitle =>
            $"{Template.Grid.Columns} × {Template.Grid.Rows} per {Template.EffectivePage.Name}" +
            (Template.IsUserDefined ? " · user template" : string.Empty);

        public string SizeText => $"{Template.Grid.LabelWidthMm:0.##} × {Template.Grid.LabelHeightMm:0.##} mm";

        public override string ToString() => Title;
    }
}
