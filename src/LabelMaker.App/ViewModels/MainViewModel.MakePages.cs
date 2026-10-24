using System.Globalization;
using LabelMaker.App.Mvvm;
using LabelMaker.Core.Bulk;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;

namespace LabelMaker.App.ViewModels;

/// <summary>
/// Pages made in one go from the label on screen: one for each record of the imported data, or
/// numbered copies whose barcodes count up. Each run is one step that undo takes back.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>More pages than this in one go would make the project slow to work with.</summary>
    private const int MaxMadePages = 2000;

    public RelayCommand PagesFromRecordsCommand { get; private set; } = null!;

    public RelayCommand NumberedPagesCommand { get; private set; } = null!;

    private void BuildMakePagesCommands()
    {
        PagesFromRecordsCommand = new RelayCommand(
            () => MakePagesFromRecords(Enumerable.Range(0, _session.Data?.RowCount ?? 0).ToList()),
            () => (_session.Data?.RowCount ?? 0) > 0);
        NumberedPagesCommand = new RelayCommand(MakeNumberedPages, () => NumberedBarcodes(_session.ActiveLabel) > 0);
    }

    /// <summary>
    /// One new page for each of these records (0-based rows), made from the label on screen with
    /// each record's values written in, after the page on screen. Printing is set to pages, so
    /// they print several to a sheet. Returns how many pages were made.
    /// </summary>
    public int MakePagesFromRecords(IReadOnlyList<int> rows)
    {
        if (_session.Data is not { RowCount: > 0 } data || rows.Count == 0)
        {
            return 0;
        }

        LabelDocument label = _session.ActiveLabel;
        var columns = new HashSet<string>(data.Columns.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
        if (!label.Objects.SelectMany(o => o.GetReferencedFields()).Any(columns.Contains))
        {
            _shell.Info("The label on screen does not use any of the records' columns yet, so every page would be the same.\n\n" +
                        "Put the columns on the label first (Records… › Add field to label), then make the pages.", "Make pages");
            return 0;
        }

        List<int> chosen = rows.Where(r => r >= 0 && r < data.RowCount).Distinct().Take(MaxMadePages).ToList();
        if (chosen.Count > 1 && !_shell.Confirm(
                Localization.Localizer.F("Make {0} pages from the label on screen, one for each record?", chosen.Count.ToString("N0", CultureInfo.CurrentCulture)),
                "Make pages"))
        {
            return 0;
        }

        string firstColumn = data.Columns[0].Name;
        var documents = new List<LabelDocument>(chosen.Count);
        var names = new List<string>(chosen.Count);
        foreach (int row in chosen)
        {
            documents.Add(PageMaker.ForRecord(label, data.Rows[row], _session.Project.Variables));
            string name = data.Rows[row].TryGetValue(firstColumn, out string? value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : $"Record {row + 1}";
            names.Add(name);
        }

        _session.InsertPages(documents, names, chosen.Count == 1 ? "Make page from record" : $"Make {chosen.Count} pages from records");
        UsePagePrinting();
        StatusText = Localization.Localizer.F("Made {0} page(s), one for each record. Printing is set to print the pages.", chosen.Count.ToString("N0", CultureInfo.CurrentCulture));
        return chosen.Count;
    }

    /// <summary>Asks how many, then makes that many copies of the page on screen with its barcodes counting up.</summary>
    private void MakeNumberedPages()
    {
        string? answer = _shell.Prompt("Numbered copies",
            "How many copies? Each one counts the page's barcodes up by one, so the copies are numbered one after another.", "10");
        if (answer is null)
        {
            return;
        }

        if (!int.TryParse(answer.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out int count) || count < 1 || count > MaxMadePages)
        {
            _shell.Warn(Localization.Localizer.F("Type a number of copies from 1 to {0}.", MaxMadePages), "Numbered copies");
            return;
        }

        MakeNumberedPages(count);
    }

    /// <summary>Makes <paramref name="count"/> numbered copies of the page on screen after it; returns how many.</summary>
    public int MakeNumberedPages(int count)
    {
        LabelDocument label = _session.ActiveLabel;
        if (count < 1 || NumberedBarcodes(label) == 0)
        {
            return 0;
        }

        IReadOnlyList<LabelDocument> documents = PageMaker.Numbered(label, Math.Min(count, MaxMadePages));

        // Each copy is named by its new number, which is also what tells the pages apart.
        var names = documents.Select(d => d.Objects.OfType<BarcodeObject>().Select(b => b.Spec.Data).FirstOrDefault(v => v.Length > 0) ?? d.Name).ToList();
        _session.InsertPages(documents, names, $"Make {documents.Count} numbered copies");
        UsePagePrinting();
        StatusText = Localization.Localizer.F("Made {0} numbered copies, up to {1}. Printing is set to print the pages.",
            documents.Count.ToString("N0", CultureInfo.CurrentCulture), names[^1]);
        return documents.Count;
    }

    /// <summary>How many barcodes on <paramref name="label"/> have a number that can count up.</summary>
    private static int NumberedBarcodes(LabelDocument label) =>
        label.Objects.OfType<BarcodeObject>().Count(b => PageMaker.CountUp(b.Spec.Data, b.Spec.Symbology, 1) is not null);

    /// <summary>The new pages print one to a label slot, several to a sheet.</summary>
    private void UsePagePrinting()
    {
        if (_session.Project.PrintSettings.Content != PrintContent.Pages)
        {
            _session.Project.PrintSettings.Content = PrintContent.Pages;
        }

        RaiseAllCanExecute();
    }
}
