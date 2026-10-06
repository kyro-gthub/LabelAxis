using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Printing;
using LabelMaker.Core.Units;

namespace LabelMaker.App.Views;

public partial class NewProjectWindow : Window
{
    private readonly TemplateStore _store;
    private readonly HashSet<string> _favorites;
    private readonly IList<string>? _savedFavorites;

    /// <param name="favorites">The starred sheets; a custom size starred here is added to it.</param>
    /// <param name="mine">The user's own designs, listed first among the designs.</param>
    public NewProjectWindow(TemplateStore store, IList<string>? favorites = null, bool showDesigns = true,
        LabelMaker.Core.Designs.MyDesigns? mine = null)
    {
        InitializeComponent();
        _store = store;
        _savedFavorites = favorites;
        _favorites = new HashSet<string>(favorites ?? Array.Empty<string>(), StringComparer.Ordinal);
        SearchBox.TextChanged += (_, _) => Refresh();
        Refresh();

        // The first preview is drawn before layout has sized the canvas; draw it again at its real size.
        PreviewCanvas.SizeChanged += (_, _) => { if (TemplateList.SelectedItem is Row row) { DrawSheetPreview(row.Template); } };
        Gallery.Store = store;
        Gallery.Mine = mine;
        Gallery.SelectionChanged += (_, _) => DesignCreateButton.IsEnabled = Gallery.SelectedDesign is not null;
        Gallery.DesignActivated += (_, _) => OnCreateFromDesign(this, new RoutedEventArgs());
        DesignCreateButton.IsEnabled = Gallery.SelectedDesign is not null;
        ShowTab(showDesigns);
    }

    public LabelTemplate? SelectedTemplate { get; private set; }

    /// <summary>The ready-made design chosen instead of a blank sheet, or null.</summary>
    public LabelMaker.Core.Designs.LabelDesign? ChosenDesign { get; private set; }

    /// <summary>With <see cref="ChosenDesign"/>: keep its data fields instead of its sample words.</summary>
    public bool UseDataFields => Gallery.UseDataFields;

    private void OnTab(object sender, RoutedEventArgs e) => ShowTab(DesignsTab.IsChecked == true);

    private void ShowTab(bool designs)
    {
        DesignsTab.IsChecked = designs;
        SheetsTab.IsChecked = !designs;
        Gallery.Visibility = designs ? Visibility.Visible : Visibility.Collapsed;
        DesignButtons.Visibility = Gallery.Visibility;
        SheetsPanel.Visibility = designs ? Visibility.Collapsed : Visibility.Visible;
        DesignCreateButton.IsDefault = designs;
        SheetCreateButton.IsDefault = !designs;
        if (!designs)
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () => SearchBox.Focus());
        }
    }

    private void OnCreateFromDesign(object sender, RoutedEventArgs e)
    {
        if (Gallery.SelectedDesign is { } design)
        {
            ChosenDesign = design;
            DialogResult = true;
        }
    }

    /// <summary>True when a custom size was made here (the settings then need saving).</summary>
    public bool FavoritesChanged { get; private set; }

    /// <summary>A label of any size: made in the custom size window, kept with the user's own sheets.</summary>
    private void OnCustomSize(object sender, RoutedEventArgs e)
    {
        var win = new CustomSizeWindow(_store, editing: null, isFavorite: true) { Owner = this };
        if (win.ShowDialog() != true || win.Result is not { } saved)
        {
            return;
        }

        if (win.IsFavorite && _favorites.Add(saved.Id))
        {
            _savedFavorites?.Add(saved.Id);
            FavoritesChanged = true;
        }

        if (win.UseNow)
        {
            SelectedTemplate = saved;
            DialogResult = true;
            return;
        }

        // Saved only: shown and picked in the list, ready for Create.
        SearchBox.Text = string.Empty;
        Refresh();
        if (TemplateList.Items.Cast<Row>().FirstOrDefault(r => r.Template.Id == saved.Id) is { } row)
        {
            TemplateList.SelectedItem = row;
            TemplateList.ScrollIntoView(row);
        }
    }

    private void Refresh()
    {
        var query = new TemplateQuery { Text = SearchBox.Text };
        // Favourites (starred in the Label manager) come first.
        List<Row> rows = _store.Search(query)
            .Select(t => new Row(t, _favorites.Contains(t.Id)))
            .OrderByDescending(r => r.IsFavorite)
            .ToList();
        TemplateList.ItemsSource = rows;

        bool empty = rows.Count == 0;
        NoMatches.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        NoMatches.Text = empty ? $"No label sheets match “{SearchBox.Text}”." : string.Empty;
        if (empty)
        {
            ShowPreview(null);
        }
        else
        {
            TemplateList.SelectedIndex = 0;
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ShowPreview((TemplateList.SelectedItem as Row)?.Template);

    private void ShowPreview(LabelTemplate? t)
    {
        if (t is null)
        {
            PreviewTitle.Text = string.Empty;
            PreviewDetail.Text = string.Empty;
            PreviewCanvas.Children.Clear();
            return;
        }

        PreviewTitle.Text = Row.Title(t);
        SheetMeasurements m = SheetMeasurements.From(t);
        string Mm(double v) => SheetMeasurements.Mm(v);
        PreviewDetail.Text =
            $"Sheet {t.EffectivePage.Name} ({Mm(t.EffectivePage.WidthMm)} × {Mm(t.EffectivePage.HeightMm)} mm)\n" +
            $"{t.Grid.Columns} columns × {t.Grid.Rows} rows = {t.Grid.Count} labels\n" +
            $"Label {Mm(m.LabelWidthMm)} × {Mm(m.LabelHeightMm)} mm · {t.Grid.LabelShape}\n" +
            $"Margins top {Mm(m.MarginTopMm)}, left {Mm(m.MarginLeftMm)}, right {Mm(m.MarginRightMm)}, bottom {Mm(m.MarginBottomMm)} mm\n" +
            (m.HGapMm > 0.005 || m.VGapMm > 0.005
                ? $"Gaps {Mm(m.HGapMm)} across, {Mm(m.VGapMm)} down"
                : "No gaps between labels");
        DrawSheetPreview(t);
    }

    private void DrawSheetPreview(LabelTemplate t)
    {
        PreviewCanvas.Children.Clear();
        double cw = PreviewCanvas.ActualWidth <= 0 ? 220 : PreviewCanvas.ActualWidth;
        double ch = PreviewCanvas.ActualHeight <= 0 ? 300 : PreviewCanvas.ActualHeight;
        PageSize page = t.EffectivePage;
        double scale = Math.Max(0.01, Math.Min((cw - 8) / page.WidthMm, (ch - 8) / page.HeightMm));
        double left = (cw - (page.WidthMm * scale)) / 2;

        var sheet = new Rectangle
        {
            Width = page.WidthMm * scale,
            Height = page.HeightMm * scale,
            Stroke = Brushes.Gray,
            Fill = Brushes.White,
            StrokeThickness = 1,
        };
        Canvas.SetLeft(sheet, left);
        Canvas.SetTop(sheet, 4);
        PreviewCanvas.Children.Add(sheet);

        var stroke = new SolidColorBrush(Color.FromRgb(0, 120, 215));
        var fill = new SolidColorBrush(Color.FromArgb(30, 0, 120, 215));
        stroke.Freeze();
        fill.Freeze();
        foreach (LabelSlot slot in TemplateGeometry.AllSlots(t))
        {
            Shape shape = t.Grid.LabelShape == LabelShape.Ellipse
                ? new Ellipse()
                : new Rectangle { RadiusX = t.Grid.CornerRadiusMm * scale, RadiusY = t.Grid.CornerRadiusMm * scale };
            shape.Width = slot.RectMm.Width * scale;
            shape.Height = slot.RectMm.Height * scale;
            shape.Stroke = stroke;
            shape.StrokeThickness = 0.8;
            shape.Fill = fill;
            Canvas.SetLeft(shape, left + (slot.RectMm.X * scale));
            Canvas.SetTop(shape, 4 + (slot.RectMm.Y * scale));
            PreviewCanvas.Children.Add(shape);
        }
    }

    private void OnCreate(object sender, RoutedEventArgs e)
    {
        // A double-click on the header or the scrollbar is not a choice of sheet.
        if (e is System.Windows.Input.MouseButtonEventArgs mouse &&
            ItemsControl.ContainerFromElement(TemplateList, mouse.OriginalSource as DependencyObject) is null)
        {
            return;
        }

        if (TemplateList.SelectedItem is Row row)
        {
            SelectedTemplate = row.Template;
            DialogResult = true;
        }
    }

    /// <summary>One row of the sheet list; public so WPF bindings can read it.</summary>
    public sealed class Row(LabelTemplate template, bool isFavorite = false)
    {
        public LabelTemplate Template { get; } = template;

        public bool IsFavorite { get; } = isFavorite;

        public string ProductCode => (IsFavorite ? "★ " : string.Empty) + Template.ProductCode;

        public string Brand => Template.Brand;

        /// <summary>The name without the brand and code that the other columns already show.</summary>
        public string Name => Describe(Template);

        public string GridText => $"{Template.Grid.Columns} × {Template.Grid.Rows}";

        public string LabelText => $"{SheetMeasurements.Mm(Template.Grid.LabelWidthMm)} × {SheetMeasurements.Mm(Template.Grid.LabelHeightMm)}";

        /// <summary>"Formtec GS-1065 — Name", without repeating the code when the name already is it.</summary>
        public static string Title(LabelTemplate t)
        {
            string code = $"{t.Brand} {t.ProductCode}".Trim();
            string rest = Describe(t);
            return rest.Length == 0 ? code : $"{code} — {rest}";
        }

        private static string Describe(LabelTemplate t)
        {
            string name = t.Name.Trim();
            foreach (string prefix in new[] { $"{t.Brand} {t.ProductCode}".Trim(), t.ProductCode })
            {
                if (prefix.Length > 0 && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    name = name[prefix.Length..].TrimStart(' ', '—', '-', '–', ':', '·', ',');
                    break;
                }
            }

            return name;
        }

        public override string ToString() => Title(Template);
    }
}
