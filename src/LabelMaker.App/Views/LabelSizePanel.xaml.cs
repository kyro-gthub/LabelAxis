using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using LabelMaker.App.Localization;
using LabelMaker.App.Services;
using LabelMaker.Core.Editing;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Localization;
using LabelMaker.Core.Model;
using LabelMaker.Core.Units;

namespace LabelMaker.App.Views;

/// <summary>
/// The Label manager's label size: changes the size of the label after a project has been
/// started, optionally scaling the objects and re-fitting the sheet grid so printing stays
/// coherent, in one undo step. (It was a window of its own, "Label size", next to the manager.)
/// </summary>
public partial class LabelSizePanel : UserControl
{
    private DocumentSession? _session;
    private double _widthMm;
    private double _heightMm;

    // The unit the boxes are in, so switching units keeps the size typed.
    private Unit _shownUnit = Unit.Mm;

    public LabelSizePanel() => InitializeComponent();

    /// <summary>Raised once the new size is applied.</summary>
    public event EventHandler? Applied;

    /// <summary>Shows the size of the label in <paramref name="session"/>, in <paramref name="displayUnit"/>.</summary>
    public void Show(DocumentSession session, Unit displayUnit)
    {
        _session = session;
        SizeMm current = session.ActiveLabel.SizeMm;
        _widthMm = current.Width;
        _heightMm = current.Height;

        UnitBox.Items.Clear();
        foreach (Unit u in new[] { Unit.Mm, Unit.Cm, Unit.Inch, Unit.Point })
        {
            UnitBox.Items.Add(u.Symbol());
        }

        UnitBox.SelectedItem = displayUnit.IsPhysical() ? displayUnit.Symbol() : Unit.Mm.Symbol();
        ShowValues();

        LabelTemplate? t = session.Project.Template;
        CurrentText.Text = t is null
            ? Localizer.F("Now {0} × {1} mm, with no label sheet.", Mm(current.Width), Mm(current.Height))
            : Localizer.F("Now {0} × {1} mm, on the {2} {3} sheet.", Mm(current.Width), Mm(current.Height), t.Brand, t.ProductCode);
        SheetNote.Text = Localizer.T(t is null
            ? "There is no sheet to re-fit; the label prints on its own."
            : "Keeps the page and margins, recalculating how many labels fit across and down. Leave it off to keep the current grid and change only the design size.");
        RefitSheet.IsEnabled = t is not null;
    }

    private static string Mm(double value) => value.ToString("0.#", CultureInfo.CurrentCulture);

    private Unit SelectedUnit => (UnitBox.SelectedItem as string) switch
    {
        "cm" => Unit.Cm,
        "in" => Unit.Inch,
        "pt" => Unit.Point,
        _ => Unit.Mm,
    };

    private void ShowValues()
    {
        _shownUnit = SelectedUnit;
        WidthBox.Text = UnitConverter.FromMillimetres(_widthMm, _shownUnit).ToString("0.###", CultureInfo.CurrentCulture);
        HeightBox.Text = UnitConverter.FromMillimetres(_heightMm, _shownUnit).ToString("0.###", CultureInfo.CurrentCulture);
    }

    private void OnUnitChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            ReadValues();
            ShowValues();
        }
    }

    /// <summary>The size typed, kept in millimetres; false when a box does not hold a number.</summary>
    private bool ReadValues()
    {
        if (!double.TryParse(NativeDigits.ToAscii(WidthBox.Text), NumberStyles.Float, CultureInfo.CurrentCulture, out double w) ||
            !double.TryParse(NativeDigits.ToAscii(HeightBox.Text), NumberStyles.Float, CultureInfo.CurrentCulture, out double h))
        {
            return false;
        }

        _widthMm = UnitConverter.ToMillimetres(w, _shownUnit);
        _heightMm = UnitConverter.ToMillimetres(h, _shownUnit);
        return true;
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        if (_session is null)
        {
            return;
        }

        bool fits = ReadValues() && _widthMm >= 1 && _heightMm >= 1 && _widthMm <= 2000 && _heightMm <= 2000;
        if (!fits)
        {
            MessageBox.Show(Window.GetWindow(this)!, Localizer.T("Enter a width and height between 1 mm and 2000 mm."), Localizer.T("Label size"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var target = new SizeMm(Math.Round(_widthMm, 3), Math.Round(_heightMm, 3));
        var commands = new List<IUndoableCommand>();
        foreach (LabelDocument doc in DocumentsToChange(_session))
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
        Applied?.Invoke(this, EventArgs.Empty);
    }

    private IEnumerable<LabelDocument> DocumentsToChange(DocumentSession session)
    {
        if (AllPages.IsChecked == true)
        {
            yield return session.Project.MasterLabel;
            foreach (LabelPage page in session.Project.Pages)
            {
                yield return page.Document;
            }
        }
        else
        {
            yield return session.ActiveLabel;
        }
    }

    /// <summary>
    /// Keeps the page, margins and gaps, and works out how many labels of the new
    /// size fit inside the printable area. A template edited this way is marked
    /// user-defined so it is never confused with a shipped sheet.
    /// </summary>
    internal static LabelTemplate RefitTemplate(LabelTemplate template, SizeMm label)
    {
        PageSize page = template.EffectivePage;
        LabelGrid g = template.Grid;

        double usableW = page.WidthMm - g.MarginLeftMm - template.PrintableMarginMm.Right;
        double usableH = page.HeightMm - g.MarginTopMm - template.PrintableMarginMm.Bottom;

        return template with
        {
            Id = template.Id.EndsWith("-custom", StringComparison.Ordinal) ? template.Id : template.Id + "-custom",
            Name = $"{template.Name} ({label.Width:0.#} × {label.Height:0.#} mm)",
            IsUserDefined = true,
            Grid = g with
            {
                LabelWidthMm = label.Width,
                LabelHeightMm = label.Height,
                Columns = Fit(usableW, label.Width, g.HGapMm),
                Rows = Fit(usableH, label.Height, g.VGapMm),
            },
        };
    }

    private static int Fit(double usable, double size, double gap) =>
        size <= 0 ? 1 : Math.Max(1, (int)Math.Floor((usable + gap) / (size + gap)));
}
