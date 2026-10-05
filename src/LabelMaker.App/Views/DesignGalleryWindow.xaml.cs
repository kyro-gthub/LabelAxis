using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LabelMaker.App.Localization;
using LabelMaker.App.Rendering;
using LabelMaker.Core.Designs;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Model;
using LabelMaker.Core.Rendering;
using LabelMaker.Core.Serialization;
using LabelMaker.Core.Variables;

namespace LabelMaker.App.Views;

/// <summary>
/// "Start from a design": the ready-made label designs that come with LabelAxis, by group and
/// with a picture of each, drawn by the real renderer. The one chosen opens as a new project.
/// </summary>
public partial class DesignGalleryWindow : Window
{
    /// <summary>A card in the gallery.</summary>
    public sealed class DesignRow : System.ComponentModel.INotifyPropertyChanged
    {
        private ImageSource? _thumbnail;

        public required LabelDesign Design { get; init; }
        public required string Name { get; init; }
        public required string Group { get; init; }
        public required string SizeText { get; init; }
        public required string Description { get; init; }

        public ImageSource? Thumbnail
        {
            get => _thumbnail;
            set
            {
                _thumbnail = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Thumbnail)));
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        public override string ToString() => Name;
    }

    /// <summary>A row of the group list; <see cref="Key"/> is null for "All designs".</summary>
    public sealed record GroupRow(string? Key, string Display, int Count)
    {
        public override string ToString() => Display;
    }

    private const double ThumbWidth = 222;
    private const double ThumbHeight = 140;

    private readonly TemplateStore _store;
    private readonly List<DesignRow> _rows;

    public DesignGalleryWindow(TemplateStore store)
    {
        _store = store;
        InitializeComponent();

        _rows = DesignCatalog.BuiltIn.Select(d => new DesignRow
        {
            Design = d,
            Name = Localizer.T(d.Name),
            Group = d.Group,
            SizeText = d.Size.IsThermal
                ? Localizer.F("Thermal roll · {0} × {1} mm", Mm(d.Size.WidthMm), Mm(d.Size.HeightMm))
                : $"{d.Size.Sheet} · {Mm(d.Size.WidthMm)} × {Mm(d.Size.HeightMm)} mm",
            Description = Localizer.T(d.Description),
        }).ToList();

        var groups = new List<GroupRow> { new(null, Localizer.T("All designs"), _rows.Count) };
        groups.AddRange(DesignCatalog.Groups(DesignCatalog.BuiltIn)
            .Select(g => new GroupRow(g, Localizer.T(g), _rows.Count(r => r.Group == g))));
        GroupList.ItemsSource = groups;
        GroupList.SelectedIndex = 0;

        // Pictures are drawn a few at a time once the window is up, so it opens at once.
        Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Background, DrawNextThumbnail);
    }

    /// <summary>The design chosen, when the window closes with "Use this design".</summary>
    public LabelDesign? ChosenDesign { get; private set; }

    /// <summary>Keep {Product}, {Price}… as data fields instead of writing the sample words in.</summary>
    public bool UseDataFields => DataFieldsBox.IsChecked == true;

    private static string Mm(double value) => value.ToString("0.##", CultureInfo.CurrentCulture);

    private void Filter()
    {
        if (DesignList is null || GroupList is null)
        {
            return;
        }

        var group = GroupList.SelectedItem as GroupRow;
        string query = SearchBox.Text.Trim();
        List<DesignRow> shown = _rows.Where(r => group?.Key is null || r.Group == group.Key)
            .Where(r => query.Length == 0 || Matches(r, query))
            .ToList();
        DesignList.ItemsSource = shown;
        HeadingText.Text = group?.Display ?? string.Empty;
        NothingText.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (shown.Count > 0)
        {
            DesignList.SelectedIndex = 0;
        }

        UseButton.IsEnabled = shown.Count > 0;
    }

    // In the interface language and in English, so "price" finds a price tag in Arabic too.
    private static bool Matches(DesignRow row, string query)
    {
        IEnumerable<string> words = new[]
        {
            row.Name, row.Design.Name, row.Description, row.Design.Description, row.SizeText, row.Design.Size.Sheet,
            Localizer.T(row.Group), row.Group,
        }.Concat(row.Design.Fields);
        return query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .All(q => words.Any(w => w.Contains(q, StringComparison.CurrentCultureIgnoreCase)));
    }

    private void OnGroup(object sender, SelectionChangedEventArgs e) => Filter();

    private void OnSearch(object sender, TextChangedEventArgs e) => Filter();

    private void OnDesignSelected(object sender, SelectionChangedEventArgs e) =>
        UseButton.IsEnabled = DesignList.SelectedItem is DesignRow;

    private void OnDesignDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: DesignRow })
        {
            OnUse(sender, e);
        }
    }

    private void OnUse(object sender, RoutedEventArgs e)
    {
        if (DesignList.SelectedItem is DesignRow row)
        {
            ChosenDesign = row.Design;
            DialogResult = true;
        }
    }

    private void DrawNextThumbnail()
    {
        // The cards on show first, then the rest.
        IEnumerable<DesignRow> order = (DesignList.ItemsSource as IEnumerable<DesignRow> ?? Enumerable.Empty<DesignRow>()).Concat(_rows);
        if (order.FirstOrDefault(r => r.Thumbnail is null) is not { } next)
        {
            return;
        }

        try
        {
            next.Thumbnail = Thumbnail(next.Design, _store);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            next.Thumbnail = new DrawingImage(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 1, 1))));
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Background, DrawNextThumbnail);
    }

    /// <summary>A picture of a design as it will print, on its label shape (round labels round).</summary>
    public static BitmapSource Thumbnail(LabelDesign design, TemplateStore store, double width = ThumbWidth, double height = ThumbHeight)
    {
        ProjectDocument document = DesignBuilder.Build(design, store, new DesignBuildOptions
        {
            Measurer = Measurer,
            Placeholder = DesignPlaceholders.Make,
        });
        LabelDocument label = document.Project.MasterLabel;
        var renderer = new SheetVisualRenderer(Measurer, new WpfImageSource(document.Assets));
        DrawingVisual drawn = renderer.RenderSingleLabel(label, new ResolveContext { Variables = document.Project.Variables },
            new LabelRenderOptions(), ColorRgba.White);

        const double K = 96 / 25.4;
        double w = label.SizeMm.Width * K, h = label.SizeMm.Height * K;
        double scale = Math.Min((width - 8) / w, (height - 8) / h);
        double pw = Math.Ceiling(w * scale) + 4, ph = Math.Ceiling(h * scale) + 4;

        bool round = string.Equals(design.Size.Shape, "round", StringComparison.OrdinalIgnoreCase);
        double corner = design.Size.IsThermal ? 2.5 * K : 1.2 * K;
        Geometry outline = round
            ? new EllipseGeometry(new Rect(0, 0, w, h))
            : new RectangleGeometry(new Rect(0, 0, w, h), corner, corner);

        var card = new DrawingVisual();
        using (DrawingContext dc = card.RenderOpen())
        {
            dc.PushTransform(new TranslateTransform(2, 2));
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.PushClip(outline);
            dc.DrawDrawing(VisualTreeHelper.GetDrawing(drawn));
            dc.Pop();
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromRgb(0xC9, 0xCF, 0xD8)), 1 / scale), outline);
            dc.Pop();
            dc.Pop();
        }

        const double Sharp = 2; // drawn at twice the size, shown at half: crisp on any screen
        var bitmap = new RenderTargetBitmap((int)(pw * Sharp), (int)(ph * Sharp), 96 * Sharp, 96 * Sharp, PixelFormats.Pbgra32);
        bitmap.Render(card);
        bitmap.Freeze();
        return bitmap;
    }

    private static readonly WpfTextMeasurer Measurer = new();
}
