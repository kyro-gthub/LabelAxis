using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using LabelMaker.App.Services;
using LabelMaker.Core.Bulk;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Model;

namespace LabelMaker.App.Views;

/// <summary>
/// Advanced print settings (spec §16): what to print (sheet fill / variable data
/// / standalone pages several-to-a-sheet), start position + skip mask, record or
/// page selection, scale correction, outlines/crop marks, and a live sheet/label
/// count. Printer, paper and copies still go through the system Print dialog,
/// which is the authoritative source for driver capabilities.
/// </summary>
public partial class PrintSettingsWindow : Window
{
    private readonly DocumentSession _session;
    private readonly IShellServices _shell;

    public PrintSettingsWindow(DocumentSession session, IShellServices shell)
    {
        InitializeComponent();
        _session = session;
        _shell = shell;

        LoadFrom(session.Project.PrintSettings);

        LabelTemplate? t = session.Project.Template;
        HeaderSubtitle.Text = t is null
            ? "No sheet template selected"
            : $"{t.Brand} {t.ProductCode} · {t.Grid.Columns} × {t.Grid.Rows} per {t.EffectivePage.Name}";

        foreach (var rb in new[] { ContentSheetFill, ContentVariableData, ContentPages, RecordsAll, RecordsRange, RecordsSelected })
        {
            rb.Checked += (_, _) => UpdateSummary();
        }

        foreach (var tb in new[] { StartPositionBox, SkipPositionsBox, FirstRecordBox, LastRecordBox, SelectedRecordsBox, ScaleBox })
        {
            tb.TextChanged += (_, _) => UpdateSummary();
        }

        UpdateSummary();
    }

    /// <summary>Saves what is on screen, then hands off to the full-sheet preview.</summary>
    private void OnPreview(object sender, RoutedEventArgs e)
    {
        ApplyTo(_session.Project.PrintSettings);
        _shell.ShowPrintPreview();
    }

    private void LoadFrom(PrintSettings s)
    {
        (s.Content switch
        {
            PrintContent.SheetFill => ContentSheetFill,
            PrintContent.Pages => ContentPages,
            _ => ContentVariableData,
        }).IsChecked = true;

        StartPositionBox.Text = s.StartPosition.ToString(CultureInfo.InvariantCulture);
        SkipPositionsBox.Text = string.Join(", ", s.SkipPositions);
        ScaleBox.Text = s.ScalePercent.ToString("0.#", CultureInfo.InvariantCulture);
        CopiesBox.Text = Math.Max(1, s.Copies).ToString(CultureInfo.InvariantCulture);
        LandscapeBox.IsChecked = s.Landscape;
        OutlinesBox.IsChecked = s.PrintLabelOutlines;
        CropMarksBox.IsChecked = s.PrintCropMarks;

        if (s.SelectedRecords.Count > 0)
        {
            RecordsSelected.IsChecked = true;
            SelectedRecordsBox.Text = string.Join(", ", s.SelectedRecords.Select(i => i + 1));
        }
        else if (s.FirstRecord is not null || s.LastRecord is not null)
        {
            RecordsRange.IsChecked = true;
            FirstRecordBox.Text = ((s.FirstRecord ?? 0) + 1).ToString(CultureInfo.InvariantCulture);
            LastRecordBox.Text = ((s.LastRecord ?? Math.Max(0, _session.RecordCount - 1)) + 1).ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            RecordsAll.IsChecked = true;
        }
    }

    private PrintContent SelectedContent =>
        ContentSheetFill.IsChecked == true ? PrintContent.SheetFill :
        ContentPages.IsChecked == true ? PrintContent.Pages :
        PrintContent.VariableData;

    private void ApplyTo(PrintSettings s)
    {
        s.Content = SelectedContent;
        s.StartPosition = Math.Max(1, ParseInt(StartPositionBox.Text, 1));
        s.SkipPositions = ParseIntList(SkipPositionsBox.Text);
        s.ScalePercent = Math.Clamp(ParseDouble(ScaleBox.Text, 100), 10, 400);
        s.Copies = Math.Max(1, ParseInt(CopiesBox.Text, 1));
        s.Landscape = LandscapeBox.IsChecked == true;
        s.PrintLabelOutlines = OutlinesBox.IsChecked == true;
        s.PrintCropMarks = CropMarksBox.IsChecked == true;

        s.SelectedRecords.Clear();
        s.FirstRecord = null;
        s.LastRecord = null;

        if (RecordsRange.IsChecked == true)
        {
            s.FirstRecord = Math.Max(0, ParseInt(FirstRecordBox.Text, 1) - 1);
            s.LastRecord = Math.Max(0, ParseInt(LastRecordBox.Text, 1) - 1);
        }
        else if (RecordsSelected.IsChecked == true)
        {
            s.SelectedRecords.AddRange(ParseIntList(SelectedRecordsBox.Text).Select(i => i - 1).Where(i => i >= 0));
        }
    }

    private void UpdateSummary()
    {
        try
        {
            var probe = new PrintSettings();
            ApplyTo(probe);

            LabelTemplate? template = _session.Project.Template;
            if (template is null)
            {
                SummaryText.Text = "No Formtec template selected.";
                return;
            }

            switch (probe.Content)
            {
                case PrintContent.Pages:
                {
                    var docs = probe.SelectedRecords.Count > 0
                        ? probe.SelectedRecords.Where(i => i >= 0 && i < _session.Project.Pages.Count).Select(i => _session.Project.Pages[i].Document).ToList()
                        : _session.Project.Pages.Select(p => p.Document).ToList();
                    if (docs.Count == 0)
                    {
                        SummaryText.Text = "This project has no standalone pages yet (Page > Add page).";
                        return;
                    }

                    var plan = new MultiDocumentRenderPlan
                    {
                        Template = template, Documents = docs,
                        StartPosition = probe.StartPosition, SkipPositions = new HashSet<int>(probe.SkipPositions),
                    };
                    int sheets = MultiDocumentSheetLayout.PlanSheets(plan).Count();
                    SummaryText.Text = $"{docs.Count} page(s) across {sheets} sheet(s) of {template.Brand} {template.ProductCode} ({template.Grid.Columns}×{template.Grid.Rows}).";
                    break;
                }

                default:
                {
                    var plan = LabelMaker.Core.Printing.SheetLayoutEngine.CreatePlan(_session.Project, _session.Data, probe);
                    SummaryText.Text = $"{plan.TotalLabels} label(s) across {plan.TotalSheets} sheet(s) of {template.Brand} {template.ProductCode} ({template.Grid.Columns}×{template.Grid.Rows}), {probe.ScalePercent:0.#}% scale.";
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            SummaryText.Text = "Could not compute a preview: " + ex.Message;
        }
    }

    private void OnCalibration(object sender, RoutedEventArgs e) => _shell.ShowCalibrationWizard();

    private void OnPrint(object sender, RoutedEventArgs e)
    {
        ApplyTo(_session.Project.PrintSettings);
        _session.MarkDirty();
        DialogResult = true;
    }

    private static int ParseInt(string s, int fallback) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;

    private static double ParseDouble(string s, double fallback) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;

    private static List<int> ParseIntList(string s) =>
        s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
         .Select(t => ParseInt(t, -1))
         .Where(i => i > 0)
         .ToList();
}
