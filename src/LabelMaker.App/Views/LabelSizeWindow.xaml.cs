using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using LabelMaker.App.Services;
using LabelMaker.Core.Editing;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Model;
using LabelMaker.Core.Units;

namespace LabelMaker.App.Views;

/// <summary>
/// Changes the working label size after a project has already been started, which
/// otherwise could only be chosen at New Project time. Optionally rescales the
/// objects and re-fits the sheet grid so printing stays coherent.
/// </summary>
public partial class LabelSizeWindow : Window
{
    private readonly DocumentSession _session;
    private readonly IReadOnlyList<LabelTemplate> _templates;
    private readonly HashSet<string> _favorites;
    private LabelTemplate? _picked;
    private bool _settingSize;
    private string? _refitNote;
    private double _widthMm;
    private double _heightMm;

    public LabelSizeWindow(DocumentSession session, Unit displayUnit, TemplateStore? templates = null, IEnumerable<string>? favorites = null)
    {
        _session = session;
        _favorites = new HashSet<string>(favorites ?? Array.Empty<string>(), StringComparer.Ordinal);
        _templates = (templates?.All ?? Array.Empty<LabelTemplate>())
            .Where(t => !t.IsRoll)
            .OrderByDescending(t => _favorites.Contains(t.Id))
            .ThenBy(t => t.Brand, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.ProductCode, StringComparer.OrdinalIgnoreCase)
            .ToList();
        InitializeComponent();

        SizeMm current = _session.ActiveLabel.SizeMm;
        _widthMm = current.Width;
        _heightMm = current.Height;

        foreach (Unit u in new[] { Unit.Mm, Unit.Cm, Unit.Inch, Unit.Point })
        {
            UnitBox.Items.Add(u.Symbol());
        }

        UnitBox.SelectedItem = displayUnit.IsPhysical() ? displayUnit.Symbol() : Unit.Mm.Symbol();
        ShowValues();

        LabelTemplate? t = _session.Project.Template;
        CurrentText.Text = t is null
            ? $"{current.Width:0.#} × {current.Height:0.#} mm — no sheet template selected."
            : $"{t.Brand} {t.ProductCode} · {t.Grid.Columns} × {t.Grid.Rows} on {t.EffectivePage.Name}\n" +
              $"label {current.Width:0.#} × {current.Height:0.#} mm";

        SheetNote.Text = t is null
            ? "There is no sheet to re-fit; the label prints on its own."
            : "Keeps the page and margins, recalculating how many labels fit across and down. " +
              "Leave it off to keep the current grid and change only the design size.";

        ShowTemplates();
    }

    private Unit SelectedUnit => (UnitBox.SelectedItem as string) switch
    {
        "cm" => Unit.Cm,
        "in" => Unit.Inch,
        "pt" => Unit.Point,
        _ => Unit.Mm,
    };

    private void ShowValues()
    {
        Unit u = SelectedUnit;
        _settingSize = true;
        WidthBox.Text = UnitConverter.FromMillimetres(_widthMm, u).ToString("0.###", CultureInfo.CurrentCulture);
        HeightBox.Text = UnitConverter.FromMillimetres(_heightMm, u).ToString("0.###", CultureInfo.CurrentCulture);
        _settingSize = false;
    }

    // ---- picking a size from a label sheet ------------------------------------------------

    /// <summary>The sheets matching the search, favourites first.</summary>
    private void ShowTemplates()
    {
        string q = TemplateSearch.Text?.Trim() ?? string.Empty;
        string compact = q.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("×", "x", StringComparison.Ordinal);
        TemplateList.ItemsSource = _templates
            .Where(t => q.Length == 0 ||
                        t.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        t.ProductCode.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        t.Brand.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        SizeKey(t).Contains(compact, StringComparison.OrdinalIgnoreCase))
            .Select(t => new TemplateRow(t, _favorites.Contains(t.Id)))
            .ToList();
    }

    private static string SizeKey(LabelTemplate t) =>
        string.Create(CultureInfo.InvariantCulture, $"{t.Grid.LabelWidthMm:0.##}x{t.Grid.LabelHeightMm:0.##}");

    private void OnTemplateSearch(object sender, TextChangedEventArgs e) => ShowTemplates();

    /// <summary>Takes the picked sheet's label size, and offers to switch to its sheet layout as well.</summary>
    private void OnTemplatePicked(object sender, SelectionChangedEventArgs e)
    {
        if (TemplateList.SelectedItem is not TemplateRow row)
        {
            return;
        }

        _picked = row.Template;
        _widthMm = row.Template.Grid.LabelWidthMm;
        _heightMm = row.Template.Grid.LabelHeightMm;
        ShowValues();
        LabelGrid g = row.Template.Grid;
        UseTemplateSheet.Content = $"Also print on the {row.Title.TrimStart('★', ' ')} sheet ({g.Columns} × {g.Rows} on {row.Template.EffectivePage.Name})";
        UseTemplateSheet.IsChecked = true;
        UpdateSheetChoice();
    }

    /// <summary>Typing a size by hand lets go of the picked sheet.</summary>
    private void OnSizeTyped(object sender, TextChangedEventArgs e)
    {
        if (_settingSize || _picked is null)
        {
            return;
        }

        _picked = null;
        TemplateList.SelectedItem = null;
        UpdateSheetChoice();
    }

    private void OnUseTemplateSheet(object sender, RoutedEventArgs e) => UpdateSheetChoice();

    private void UpdateSheetChoice()
    {
        bool picked = _picked is not null;
        UseTemplateSheet.Visibility = picked ? Visibility.Visible : Visibility.Collapsed;
        RefitSheet.Visibility = picked && UseTemplateSheet.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;
        _refitNote ??= SheetNote.Text;
        SheetNote.Text = picked && UseTemplateSheet.IsChecked == true
            ? "The project will print on that sheet: its paper, margins and number of labels across and down."
            : _refitNote;
    }

    /// <summary>One sheet in the list: brand and code, grid and paper, label size.</summary>
    private sealed record TemplateRow(LabelTemplate Template, bool IsFavorite)
    {
        public string Title => (IsFavorite ? "★  " : string.Empty) +
            (Template.Brand == CustomSheet.Brand ? Template.Name : $"{Template.Brand} {Template.ProductCode}".Trim());

        public string Subtitle => $"{Template.Grid.Columns} × {Template.Grid.Rows} per {Template.EffectivePage.Name}";

        public string SizeText => string.Create(CultureInfo.InvariantCulture, $"{Template.Grid.LabelWidthMm:0.##} × {Template.Grid.LabelHeightMm:0.##} mm");

        public override string ToString() => Title;
    }

    private bool ReadValues()
    {
        Unit u = SelectedUnit;
        if (!double.TryParse(WidthBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double w) ||
            !double.TryParse(HeightBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double h))
        {
            return false;
        }

        _widthMm = UnitConverter.ToMillimetres(w, u);
        _heightMm = UnitConverter.ToMillimetres(h, u);
        return _widthMm >= 1 && _heightMm >= 1 && _widthMm <= 2000 && _heightMm <= 2000;
    }

    private void OnUnitChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            ShowValues();
        }
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        if (!ReadValues())
        {
            MessageBox.Show(this, "Enter a width and height between 1 mm and 2000 mm.", "Label size",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var target = new SizeMm(Math.Round(_widthMm, 3), Math.Round(_heightMm, 3));
        var commands = new List<IUndoableCommand>();

        foreach (LabelDocument doc in DocumentsToChange())
        {
            SizeMm from = doc.SizeMm;
            commands.Add(new SetPropertyCommand<SizeMm>("Label size", () => doc.SizeMm, v => doc.SizeMm = v, target));

            if (ScaleObjects.IsChecked == true && from.Width > 0 && from.Height > 0)
            {
                double sx = target.Width / from.Width;
                double sy = target.Height / from.Height;
                foreach (LabelObject obj in doc.Objects)
                {
                    RectMm b = obj.Bounds;
                    commands.Add(new TransformObjectCommand(
                        obj, new RectMm(b.X * sx, b.Y * sy, b.Width * sx, b.Height * sy), obj.RotationDegrees));
                }
            }
        }

        if (_picked is { } sheet && UseTemplateSheet.IsChecked == true &&
            Math.Abs(sheet.Grid.LabelWidthMm - target.Width) < 0.01 && Math.Abs(sheet.Grid.LabelHeightMm - target.Height) < 0.01)
        {
            // The chosen sheet itself, so printing lands exactly where that sheet's labels are.
            ProjectModel project = _session.Project;
            commands.Add(new SetPropertyCommand<LabelTemplate?>(
                "Use sheet", () => project.Template, v => project.Template = v, sheet));
        }
        else if (RefitSheet.IsChecked == true && _session.Project.Template is { } template)
        {
            ProjectModel project = _session.Project;
            commands.Add(new SetPropertyCommand<LabelTemplate?>(
                "Re-fit sheet", () => project.Template, v => project.Template = v, RefitTemplate(template, target)));
        }

        // One undo step for the whole resize, however many pages and objects it touched.
        _session.Execute(new CompositeCommand("Change label size", commands));
        DialogResult = true;
    }

    private IEnumerable<LabelDocument> DocumentsToChange()
    {
        if (AllPages.IsChecked == true)
        {
            yield return _session.Project.MasterLabel;
            foreach (LabelPage page in _session.Project.Pages)
            {
                yield return page.Document;
            }
        }
        else
        {
            yield return _session.ActiveLabel;
        }
    }

    /// <summary>
    /// Keeps the page, margins and gaps, and works out how many labels of the new
    /// size fit inside the printable area. A template edited this way is marked
    /// user-defined so it is never confused with a shipped Formtec sheet.
    /// </summary>
    private static LabelTemplate RefitTemplate(LabelTemplate template, SizeMm label)
    {
        PageSize page = template.EffectivePage;
        LabelGrid g = template.Grid;

        double usableW = page.WidthMm - g.MarginLeftMm - template.PrintableMarginMm.Right;
        double usableH = page.HeightMm - g.MarginTopMm - template.PrintableMarginMm.Bottom;

        int columns = Fit(usableW, label.Width, g.HGapMm);
        int rows = Fit(usableH, label.Height, g.VGapMm);

        return template with
        {
            Id = template.Id.EndsWith("-custom", StringComparison.Ordinal) ? template.Id : template.Id + "-custom",
            Name = $"{template.Name} ({label.Width:0.#} × {label.Height:0.#} mm)",
            IsUserDefined = true,
            Grid = g with
            {
                LabelWidthMm = label.Width,
                LabelHeightMm = label.Height,
                Columns = columns,
                Rows = rows,
            },
        };
    }

    private static int Fit(double usable, double size, double gap)
    {
        if (size <= 0)
        {
            return 1;
        }

        int n = (int)Math.Floor((usable + gap) / (size + gap));
        return Math.Max(1, n);
    }
}
