using LabelMaker.Core.Model;

namespace LabelMaker.Core.Printing;

/// <summary>The designs a "pages" print job puts on the sheet, in order.</summary>
public static class PrintablePages
{
    /// <summary>
    /// The page list as the Pages panel shows it: the master label first when it has anything
    /// on it, then the numbered pages. A range or a chosen list (1-based page numbers, as in
    /// the panel) prints just those numbered pages. Each page is repeated
    /// <see cref="PrintSettings.CopiesPerPage"/> times, one after another.
    /// </summary>
    public static IReadOnlyList<LabelDocument> For(ProjectModel project, PrintSettings settings)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(settings);

        var pages = new List<LabelDocument>();
        if (settings.SelectedRecords.Count > 0)
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

    private static bool HasContent(LabelDocument document) =>
        document.Objects.Count > 0 || document.Background is { Enabled: true };
}
