using LabelMaker.Core.Model;

namespace LabelMaker.Core.Printing;

/// <summary>Which of the project's pages a print job of <see cref="PrintContent.Pages"/> puts on paper.</summary>
public static class PrintablePages
{
    /// <summary>
    /// The page list as the Pages panel shows it: the master label first when it has anything
    /// on it, then the numbered pages. <see cref="PrintSettings.PagesToPrint"/> narrows that to
    /// the page on screen or to pages ticked by hand (the master label among them if ticked).
    /// Otherwise a range or a chosen list (1-based page numbers, as in the panel) prints just
    /// those numbered pages. Each page is repeated <see cref="PrintSettings.CopiesPerPage"/>
    /// times, one after another.
    /// </summary>
    public static IReadOnlyList<LabelDocument> For(ProjectModel project, PrintSettings settings)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(settings);

        var pages = new List<LabelDocument>();
        if (settings.PagesToPrint == PagesToPrint.Current)
        {
            if (All(project).ElementAtOrDefault(settings.CurrentPage) is { } current)
            {
                pages.Add(current);
            }
        }
        else if (settings.PagesToPrint == PagesToPrint.Chosen)
        {
            var chosen = new HashSet<int>(settings.ChosenPages);
            pages.AddRange(All(project).Where((_, i) => chosen.Contains(i)));
        }
        else if (settings.SelectedRecords.Count > 0)
        {
            pages.AddRange(settings.SelectedRecords
                .Where(i => i >= 0 && i < project.Pages.Count)
                .Select(i => project.Pages[i].Document));
        }
        else if (settings.FirstRecord is not null || settings.LastRecord is not null)
        {
            int first = Math.Max(0, settings.FirstRecord ?? 0);
            int last = Math.Min(project.Pages.Count - 1, settings.LastRecord ?? project.Pages.Count - 1);
            for (int i = first; i <= last; i++)
            {
                pages.Add(project.Pages[i].Document);
            }
        }
        else
        {
            if (HasContent(project.MasterLabel))
            {
                pages.Add(project.MasterLabel);
            }

            pages.AddRange(project.Pages.Select(p => p.Document));
        }

        int copies = Math.Clamp(settings.CopiesPerPage, 1, 1000);
        return copies == 1 ? pages : pages.SelectMany(p => Enumerable.Repeat(p, copies)).ToList();
    }

    /// <summary>Every page in panel order, the master label first, whether or not it has content.</summary>
    public static IEnumerable<LabelDocument> All(ProjectModel project)
    {
        ArgumentNullException.ThrowIfNull(project);
        yield return project.MasterLabel;
        foreach (LabelPage page in project.Pages)
        {
            yield return page.Document;
        }
    }

    private static bool HasContent(LabelDocument document) =>
        document.Objects.Count > 0 || document.Background is { Enabled: true };
}
