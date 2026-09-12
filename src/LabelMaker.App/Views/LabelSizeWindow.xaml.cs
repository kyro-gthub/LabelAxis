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
    private double _widthMm;
    private double _heightMm;

    public LabelSizeWindow(DocumentSession session, Unit displayUnit)
    {
        _session = session;
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
        WidthBox.Text = UnitConverter.FromMillimetres(_widthMm, u).ToString("0.###", CultureInfo.CurrentCulture);
        HeightBox.Text = UnitConverter.FromMillimetres(_heightMm, u).ToString("0.###", CultureInfo.CurrentCulture);
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

        if (RefitSheet.IsChecked == true && _session.Project.Template is { } template)
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
