using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using LabelMaker.App.Services;
using LabelMaker.Core.Formtec;

namespace LabelMaker.App.Views;

/// <summary>What the user picked on the welcome window.</summary>
public enum WelcomeAction
{
    Blank,
    NewFromTemplateDialog,
    NewFromDesign,
    NewFromTemplate,
    NewThermal,
    OpenDialog,
    OpenRecent,
    ImportPublisher,
    LabelManager,
}

/// <summary>
/// Shown when LabelAxis starts: start a label from a sheet or a thermal roll, open a
/// project (recent ones listed), import a Publisher file, or browse the templates.
/// </summary>
public partial class WelcomeWindow : Window
{
    private static readonly string[] QuickSheetIds =
    {
        "formtec-gs-1065", "formtec-gs-1024", "formtec-gs-1014", "formtec-gs-1010", "formtec-gs-1001", "formtec-3100",
    };

    private readonly AppEnvironment _env;

    public WelcomeWindow(AppEnvironment env)
    {
        _env = env;
        InitializeComponent();
        ShowAtStartBox.IsChecked = env.Settings.ShowWelcomeOnStart;
        VersionText.Text = "Version " + (typeof(WelcomeWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0") +
                           (env.IsPortable ? " · portable" : string.Empty);

        env.Settings.PruneMissingRecents();
        var recent = env.Settings.RecentProjects.Select(r => new RecentRow(r.Path, r.Title, r.LastOpenedUtc)).ToList();
        RecentList.ItemsSource = recent;
        NoRecent.Visibility = recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // The last sheet used comes first, then a few common ones.
        var ids = new List<string>();
        if (env.Settings.LastTemplateId is { } last)
        {
            ids.Add(last);
        }

        ids.AddRange(QuickSheetIds.Where(id => !ids.Contains(id)));
        QuickSheets.ItemsSource = ids
            .Select(id => env.Templates.Get(id))
            .OfType<LabelTemplate>()
            .Take(6)
            .Select(t => new SheetTile(t))
            .ToList();

        Closing += (_, _) => env.Settings.ShowWelcomeOnStart = ShowAtStartBox.IsChecked == true;
        Loaded += (_, _) => (recent.Count > 0 ? (UIElement)RecentList : this).Focus();
    }

    public WelcomeAction Action { get; private set; } = WelcomeAction.Blank;

    public LabelTemplate? ChosenTemplate { get; private set; }

    public string? ProjectPath { get; private set; }

    private void Finish(WelcomeAction action)
    {
        Action = action;
        DialogResult = true;
    }

    private void OnBlank(object sender, RoutedEventArgs e) => Finish(WelcomeAction.Blank);

    private void OnNewFromTemplate(object sender, RoutedEventArgs e) => Finish(WelcomeAction.NewFromTemplateDialog);

    private void OnNewFromDesign(object sender, RoutedEventArgs e) => Finish(WelcomeAction.NewFromDesign);

    private void OnNewThermal(object sender, RoutedEventArgs e) => Finish(WelcomeAction.NewThermal);

    private void OnOpen(object sender, RoutedEventArgs e) => Finish(WelcomeAction.OpenDialog);

    private void OnPublisher(object sender, RoutedEventArgs e) => Finish(WelcomeAction.ImportPublisher);

    private void OnLabelManager(object sender, RoutedEventArgs e) => Finish(WelcomeAction.LabelManager);

    private void OnQuickSheet(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SheetTile tile })
        {
            ChosenTemplate = tile.Template;
            Finish(WelcomeAction.NewFromTemplate);
        }
    }

    private void OnRecentDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelectedRecent();

    private void OnRecentKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OpenSelectedRecent();
            e.Handled = true;
        }
    }

    private void OpenSelectedRecent()
    {
        if (RecentList.SelectedItem is RecentRow row)
        {
            ProjectPath = row.Path;
            Finish(WelcomeAction.OpenRecent);
        }
    }

    private sealed class RecentRow
    {
        public RecentRow(string path, string title, DateTimeOffset opened)
        {
            Path = path;
            Title = string.IsNullOrWhiteSpace(title) ? System.IO.Path.GetFileNameWithoutExtension(path) : title;
            Folder = System.IO.Path.GetDirectoryName(path) ?? string.Empty;
            DateTime local = opened.ToLocalTime().DateTime;
            When = local.Date == DateTime.Today ? "Today " + local.ToString("HH:mm")
                : local.Date == DateTime.Today.AddDays(-1) ? "Yesterday"
                : local.ToString("d MMM yyyy");
        }

        public string Path { get; }

        public string Title { get; }

        public string Folder { get; }

        public string When { get; }
    }

    private sealed class SheetTile
    {
        public SheetTile(LabelTemplate template)
        {
            Template = template;
            Code = string.IsNullOrWhiteSpace(template.ProductCode) ? template.Brand : template.ProductCode;
            Size = $"{template.Grid.LabelWidthMm:0.##} × {template.Grid.LabelHeightMm:0.##} mm · {template.Grid.Count}-up";
            Tooltip = template.Name;
            Preview = DrawSheet(template, 64, 88);
        }

        public LabelTemplate Template { get; }

        public string Code { get; }

        public string Size { get; }

        public string Tooltip { get; }

        public UIElement Preview { get; }

        private static UIElement DrawSheet(LabelTemplate t, double maxW, double maxH)
        {
            PageSize page = t.EffectivePage;
            double scale = Math.Min(maxW / page.WidthMm, maxH / page.HeightMm);
            var canvas = new Canvas { Width = page.WidthMm * scale, Height = page.HeightMm * scale, Background = Brushes.White };
            var slotStroke = new SolidColorBrush(Color.FromRgb(0x5B, 0x8D, 0xEF));
            slotStroke.Freeze();
            foreach (LabelSlot slot in TemplateGeometry.AllSlots(t))
            {
                var r = new Rectangle
                {
                    Width = Math.Max(0.5, slot.RectMm.Width * scale),
                    Height = Math.Max(0.5, slot.RectMm.Height * scale),
                    Stroke = slotStroke,
                    StrokeThickness = 0.5,
                };
                Canvas.SetLeft(r, slot.RectMm.X * scale);
                Canvas.SetTop(r, slot.RectMm.Y * scale);
                canvas.Children.Add(r);
            }

            return new Border
            {
                Child = canvas,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xC9, 0xD2, 0xDC)),
                BorderThickness = new Thickness(0.8),
            };
        }
    }
}
