using System.Globalization;
using LabelMaker.App.Mvvm;
using LabelMaker.App.Services;
using LabelMaker.Core.Bulk;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Units;

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
        PrintSelectedPagesCommand = new RelayCommand(PrintSelectedPages);
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

    // ---- several pages at once -----------------------------------------------------------------

    private IReadOnlyList<PageListItem> _selectedPages = Array.Empty<PageListItem>();

    /// <summary>
    /// The pages selected in the page list with Ctrl and Shift (the page on screen is one of
    /// them). Remove, Duplicate and Print selected pages act on all of them.
    /// </summary>
    public IReadOnlyList<PageListItem> SelectedPages
    {
        get => _selectedPages;
        set
        {
            _selectedPages = value ?? Array.Empty<PageListItem>();
            RaiseAllCanExecute();
        }
    }

    public RelayCommand PrintSelectedPagesCommand { get; private set; } = null!;

    /// <summary>The selected pages still in the list, in list order; the page on screen when only it is selected.</summary>
    private List<PageListItem> PagesActedOn()
    {
        List<PageListItem> chosen = Pages.Where(p => _selectedPages.Contains(p)).ToList();
        return chosen.Count > 1 ? chosen : SelectedPage is { } current ? new List<PageListItem> { current } : chosen;
    }

    /// <summary>Asks blank pages or copies, and how many, then adds them after the page on screen.</summary>
    private void AddPagesAsked()
    {
        int current = SelectedPage is { } page ? Math.Max(0, Pages.IndexOf(page)) : 0;
        if (_shell.ShowAddPages(Pages.ToList(), current) is { } choice)
        {
            AddPages(choice);
        }
    }

    /// <summary>Adds what <paramref name="choice"/> asks for after the page on screen; returns how many pages.</summary>
    public int AddPages(AddPagesChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        int count = Math.Clamp(choice.Count, 1, MaxMadePages);
        int after = SelectedPage is { } page ? Pages.IndexOf(page) + 1 : Pages.Count;

        if (choice.Blank)
        {
            SizeMm size = _session.ActiveLabel.SizeMm;
            var documents = Enumerable.Range(0, count).Select(_ => new LabelDocument { SizeMm = size }).ToList();
            var names = Enumerable.Range(0, count).Select(i => $"Page {after + 1 + i}").ToList();
            _session.InsertPages(documents, names, count == 1 ? "Add page" : $"Add {count} pages");
            StatusText = Localization.Localizer.F("Added {0} blank page(s).", count);
            return count;
        }

        LabelDocument source = choice.SourcePageId is { } id && _session.Project.Pages.FirstOrDefault(p => p.Id == id) is { } chosen
            ? chosen.Document
            : _session.Project.MasterLabel;
        string sourceName = choice.SourcePageId is { } sid ? _session.Project.Pages.FirstOrDefault(p => p.Id == sid)?.Name ?? MasterPageName : MasterPageName;

        if (choice.CountUp && NumberedBarcodes(source) > 0)
        {
            IReadOnlyList<LabelDocument> numbered = PageMaker.Numbered(source, count);
            var numberedNames = numbered.Select(d => d.Objects.OfType<BarcodeObject>().Select(b => b.Spec.Data).FirstOrDefault(v => v.Length > 0) ?? sourceName).ToList();
            _session.InsertPages(numbered.ToList(), numberedNames, $"Make {numbered.Count} numbered copies");
            StatusText = Localization.Localizer.F("Added {0} numbered copies of “{1}”, up to {2}.", numbered.Count, sourceName, numberedNames[^1]);
            return numbered.Count;
        }

        var copies = Enumerable.Range(0, count).Select(_ =>
        {
            LabelDocument copy = source.Clone();
            copy.Id = Guid.NewGuid().ToString("N");
            return copy;
        }).ToList();
        var copyNames = Enumerable.Range(0, count).Select(i => count == 1 ? $"{sourceName} copy" : $"{sourceName} copy {i + 1}").ToList();
        _session.InsertPages(copies, copyNames, count == 1 ? "Duplicate page" : $"Add {count} copies");
        StatusText = choice.CountUp
            ? Localization.Localizer.F("Added {0} copies of “{1}”. It has no barcode with a number to count up.", count, sourceName)
            : Localization.Localizer.F("Added {0} copies of “{1}”.", count, sourceName);
        return count;
    }

    /// <summary>Duplicates every selected page, each copy right after its page.</summary>
    private void DuplicateSelectedPages()
    {
        List<PageListItem> pages = PagesActedOn();
        if (pages.Count <= 1)
        {
            _session.DuplicateActivePage();
            return;
        }

        _session.DuplicatePages(pages.Select(p => p.PageId).ToList());
        StatusText = Localization.Localizer.F("Duplicated {0} pages.", pages.Count);
    }

    /// <summary>Removes every selected page, after asking; one page always stays.</summary>
    private void RemoveSelectedPages()
    {
        List<PageListItem> pages = PagesActedOn();
        if (pages.Count <= 1)
        {
            RemoveActivePage();
            return;
        }

        if (pages.Count >= Pages.Count)
        {
            StatusText = "A project needs at least one page, so not every page can be removed.";
            return;
        }

        if (!_env.Settings.ConfirmDestructiveActions || _shell.Confirm(Localization.Localizer.F("Remove the {0} selected pages?", pages.Count)))
        {
            _session.RemovePages(pages.Select(p => p.PageId).ToList());
            StatusText = Localization.Localizer.F("Removed {0} pages. Undo (Ctrl+Z) brings them back.", pages.Count);
        }
    }

    /// <summary>Opens Print set to print just the selected pages, several to a sheet.</summary>
    private void PrintSelectedPages()
    {
        List<PageListItem> pages = PagesActedOn();
        PrintSettings settings = _session.Project.PrintSettings;
        settings.Content = PrintContent.Pages;
        settings.PagesToPrint = PagesToPrint.Chosen;
        settings.ChosenPages = pages.Select(p => Pages.IndexOf(p)).Where(i => i >= 0).ToList();
        _activeTab.HasConfiguredPrintSettings = true;
        Print(keepPageChoice: true);
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
