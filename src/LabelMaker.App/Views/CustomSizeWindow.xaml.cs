using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Printing;

namespace LabelMaker.App.Views;

/// <summary>
/// Makes (or edits) a sticker sheet of any size: the sticker's width, height and shape, the
/// paper, margins and gaps. The sheet is drawn live and saved as one of the user's own
/// templates, so it shows up in the Label manager like any bought sheet.
/// </summary>
public partial class CustomSizeWindow : Window
{
    private const string CustomPaperKey = "custom";

    private readonly TemplateStore _store;
    private readonly LabelTemplate? _editing;
    private LabelTemplate? _built;
    private bool _loading = true;

    public CustomSizeWindow(TemplateStore store, LabelTemplate? editing, bool isFavorite)
    {
        _store = store;
        _editing = editing;
        InitializeComponent();
        Title = editing is null ? "New custom sticker size" : $"Edit {editing.Name}";

        ShapeBox.Items.Add(new Choice<LabelShape>(LabelShape.Rectangle, "Rectangle"));
        ShapeBox.Items.Add(new Choice<LabelShape>(LabelShape.RoundRectangle, "Rounded corners"));
        ShapeBox.Items.Add(new Choice<LabelShape>(LabelShape.Ellipse, "Round / oval"));
        foreach (PageSize p in PrintLayoutResolver.Papers)
        {
            PaperBox.Items.Add(new Choice<string>(p.Name, $"{p.Name} ({Mm(p.WidthMm)} × {Mm(p.HeightMm)} mm)"));
        }

        PaperBox.Items.Add(new Choice<string>(CustomPaperKey, "Custom paper size…"));

        CustomSheetSpec spec = editing is null ? new CustomSheetSpec() : CustomSheet.ToSpec(editing);
        Load(spec, fixedCount: editing is not null);
        FavoriteBox.IsChecked = editing is null || isFavorite;

        _loading = false;
        Refresh();
    }

    /// <summary>The saved sheet, once Save or Save and use was pressed.</summary>
    public LabelTemplate? Result { get; private set; }

    /// <summary>True when the sheet should be used for the open project straight away.</summary>
    public bool UseNow { get; private set; }

    public bool IsFavorite => FavoriteBox.IsChecked == true;

    private void Load(CustomSheetSpec s, bool fixedCount)
    {
        NameBox.Text = s.Name;
        WidthBox.Text = Mm(s.LabelWidthMm);
        HeightBox.Text = Mm(s.LabelHeightMm);
        ShapeBox.SelectedItem = ShapeBox.Items.Cast<Choice<LabelShape>>().First(c => c.Value == s.Shape);
        RadiusBox.Text = Mm(s.CornerRadiusMm);

        Choice<string>? paper = PaperBox.Items.Cast<Choice<string>>()
            .FirstOrDefault(c => PrintLayoutResolver.Papers.Any(p => p.Name == c.Value && Same(p, s.Paper)));
        PaperBox.SelectedItem = paper ?? PaperBox.Items.Cast<Choice<string>>().Last();
        PaperWidthBox.Text = Mm(s.Paper.WidthMm);
        PaperHeightBox.Text = Mm(s.Paper.HeightMm);
        LandscapeBox.IsChecked = s.Orientation == PageOrientation.Landscape;

        TopBox.Text = Mm(s.MarginTopMm);
        BottomBox.Text = Mm(s.MarginBottomMm);
        LeftBox.Text = Mm(s.MarginLeftMm);
        RightBox.Text = Mm(s.MarginRightMm);
        HGapBox.Text = Mm(s.HGapMm);
        VGapBox.Text = Mm(s.VGapMm);
        (fixedCount ? FixedCountBox : AutoCountBox).IsChecked = true;
        (int cols, int rows) = s.Columns is { } c && s.Rows is { } r ? (c, r) : CustomSheet.Fit(s);
        ColumnsBox.Text = cols.ToString(CultureInfo.InvariantCulture);
        RowsBox.Text = rows.ToString(CultureInfo.InvariantCulture);
        CenterBox.IsChecked = s.CenterOnPaper;
    }

    private CustomSheetSpec Read()
    {
        string paperKey = (PaperBox.SelectedItem as Choice<string>)?.Value ?? "A4";
        PageSize paper = paperKey == CustomPaperKey
            ? new PageSize("Custom", Clamp(PaperWidthBox.Text, 210, 10, 2000), Clamp(PaperHeightBox.Text, 297, 10, 2000))
            : PrintLayoutResolver.Papers.First(p => p.Name == paperKey);
        bool fixedCount = FixedCountBox.IsChecked == true;
        return new CustomSheetSpec
        {
            Name = NameBox.Text,
            LabelWidthMm = Clamp(WidthBox.Text, 0, 0, 2000),
            LabelHeightMm = Clamp(HeightBox.Text, 0, 0, 2000),
            Shape = (ShapeBox.SelectedItem as Choice<LabelShape>)?.Value ?? LabelShape.Rectangle,
            CornerRadiusMm = Clamp(RadiusBox.Text, 2, 0, 500),
            Paper = paper,
            Orientation = LandscapeBox.IsChecked == true ? PageOrientation.Landscape : PageOrientation.Portrait,
            MarginTopMm = Clamp(TopBox.Text, 0, 0, 1000),
            MarginBottomMm = Clamp(BottomBox.Text, 0, 0, 1000),
            MarginLeftMm = Clamp(LeftBox.Text, 0, 0, 1000),
            MarginRightMm = Clamp(RightBox.Text, 0, 0, 1000),
            HGapMm = Clamp(HGapBox.Text, 0, 0, 1000),
            VGapMm = Clamp(VGapBox.Text, 0, 0, 1000),
            Columns = fixedCount ? (int)Clamp(ColumnsBox.Text, 1, 1, 500) : null,
            Rows = fixedCount ? (int)Clamp(RowsBox.Text, 1, 1, 500) : null,
            CenterOnPaper = CenterBox.IsChecked == true,
        };
    }

    private void OnChanged(object sender, RoutedEventArgs e) => Refresh();

    private void OnChanged(object sender, SelectionChangedEventArgs e) => Refresh();

    private void OnChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void OnPreviewSizeChanged(object sender, SizeChangedEventArgs e) => DrawPreview();

    private void Refresh()
    {
        if (_loading)
        {
            return;
        }

        CustomSheetSpec spec = Read();
        RadiusRow.Visibility = spec.Shape == LabelShape.RoundRectangle ? Visibility.Visible : Visibility.Collapsed;
        CustomPaperRow.Visibility = (PaperBox.SelectedItem as Choice<string>)?.Value == CustomPaperKey ? Visibility.Visible : Visibility.Collapsed;
        CountRow.IsEnabled = FixedCountBox.IsChecked == true;

        // With the counts fixed the right and bottom margins decide nothing.
        RightBox.IsEnabled = BottomBox.IsEnabled = AutoCountBox.IsChecked == true;
        if (AutoCountBox.IsChecked == true)
        {
            // Show what "as many as fit" works out to, ready to be fixed if wanted.
            (int cols, int rows) = CustomSheet.Fit(spec);
            ColumnsBox.Text = cols.ToString(CultureInfo.InvariantCulture);
            RowsBox.Text = rows.ToString(CultureInfo.InvariantCulture);
        }

        try
        {
            _built = CustomSheet.Build(spec, _editing?.Id ?? "custom-preview");
            LabelGrid g = _built.Grid;
            PageSize page = _built.EffectivePage;
            SummaryText.Text =
                $"{g.Columns} across × {g.Rows} down = {g.Count} stickers per sheet.\n" +
                $"Sticker {Mm(g.LabelWidthMm)} × {Mm(g.LabelHeightMm)} mm on {page.Name} ({Mm(page.WidthMm)} × {Mm(page.HeightMm)} mm). " +
                $"First sticker {Mm(g.MarginLeftMm)} mm in from the left and {Mm(g.MarginTopMm)} mm down from the top.";
            SaveButton.IsEnabled = UseButton.IsEnabled = !string.IsNullOrWhiteSpace(spec.Name);
        }
        catch (InvalidOperationException ex)
        {
            _built = null;
            SummaryText.Text = ex.Message;
            SaveButton.IsEnabled = UseButton.IsEnabled = false;
        }

        DrawPreview();
    }

    private void DrawPreview()
    {
        PreviewHost.Children.Clear();
        if (_built is null)
        {
            return;
        }

        double w = PreviewHost.ActualWidth > 20 ? PreviewHost.ActualWidth - 4 : 250;
        double h = PreviewHost.ActualHeight > 20 ? PreviewHost.ActualHeight - 4 : 340;
        PreviewHost.Children.Add(SheetPreview.Draw(this, _built, w, h));
    }

    private void OnSave(object sender, RoutedEventArgs e) => Save(useNow: false);

    private void OnSaveAndUse(object sender, RoutedEventArgs e) => Save(useNow: true);

    private void Save(bool useNow)
    {
        try
        {
            string id = _editing?.Id ?? CustomSheet.NewId(_store.Contains);
            LabelTemplate template = CustomSheet.Build(Read(), id);
            _store.SaveUserTemplate(template);
            Result = _store.Get(id) ?? template;
            UseNow = useNow;
            DialogResult = true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.IO.IOException or UnauthorizedAccessException or ArgumentException)
        {
            MessageBox.Show(this, "The sticker size could not be saved:\n\n" + ex.Message, "LabelAxis",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static bool Same(PageSize a, PageSize b) =>
        Math.Abs(a.WidthMm - b.WidthMm) < 0.05 && Math.Abs(a.HeightMm - b.HeightMm) < 0.05;

    private static double Clamp(string text, double fallback, double min, double max) =>
        Math.Clamp(double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback, min, max);

    private static string Mm(double v) => Math.Round(v, 2).ToString("0.##", CultureInfo.InvariantCulture);

    private sealed record Choice<T>(T Value, string Text)
    {
        public override string ToString() => Text;
    }
}
